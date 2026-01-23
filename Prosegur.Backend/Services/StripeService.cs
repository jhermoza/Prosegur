using Prosegur.Shared.DTOs;
using Stripe;
using System.Collections.Concurrent;

namespace Prosegur.Backend.Services;

public class StripeService : IStripeService
{
    private readonly PaymentIntentService _paymentIntentService;
    private readonly ConcurrentDictionary<string, PaymentStatusResponse> _paymentStore;

    public StripeService(IConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var apiKey = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Stripe:SecretKey not configured in appsettings.json");
        }
        
        StripeConfiguration.ApiKey = apiKey;
        _paymentIntentService = new PaymentIntentService();
        _paymentStore = new ConcurrentDictionary<string, PaymentStatusResponse>();
    }

    public async Task<PaymentStatusResponse> CreatePaymentIntentAsync(
        PaymentRequest request, 
        string idempotencyKey)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required", nameof(idempotencyKey));
        }

        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(request.Amount * 100),
            Currency = request.Currency.ToLower(),
            Description = request.Description ?? "POS Payment",
            CaptureMethod = "manual",
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never"
            }
        };

        var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
        var paymentIntent = await _paymentIntentService.CreateAsync(options, requestOptions);
        
        await Task.Delay(100);
        paymentIntent = await _paymentIntentService.GetAsync(paymentIntent.Id);
        
        var response = MapToResponse(paymentIntent, request.Amount);
        _paymentStore.TryAdd(paymentIntent.Id, response);
        return response;
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment ID cannot be null or empty", nameof(paymentId));
        }

        if (_paymentStore.TryGetValue(paymentId, out var cachedResponse))
        {
            if (cachedResponse.Status is "APPROVED" or "DECLINED" or "FAILED")
            {
                return cachedResponse;
            }

            var paymentIntent = await _paymentIntentService.GetAsync(paymentId);
            var updatedResponse = MapToResponse(paymentIntent, cachedResponse.Amount);
            _paymentStore.TryUpdate(paymentId, updatedResponse, cachedResponse);
            return updatedResponse;
        }
        
        throw new KeyNotFoundException($"Payment {paymentId} not found");
    }

    public async Task<PaymentStatusResponse> ConfirmPaymentAsync(string paymentId, bool shouldSucceed)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment ID cannot be null or empty", nameof(paymentId));
        }

        if (!_paymentStore.TryGetValue(paymentId, out var existing))
        {
            throw new KeyNotFoundException($"Payment {paymentId} not found");
        }

        // Validate payment is in a confirmable state
        if (IsTerminalState(existing.Status))
        {
            return existing; // Already processed, return current state
        }

        // Optimistic locking: mark as processing to prevent concurrent modifications
        var processingPayment = existing with 
        { 
            Status = "PROCESSING",
            UpdatedAt = DateTime.UtcNow 
        };
        
        if (!_paymentStore.TryUpdate(paymentId, processingPayment, existing))
        {
            throw new InvalidOperationException("Payment is already being processed by another request");
        }

        try
        {
            var paymentMethodId = shouldSucceed ? "pm_card_visa" : "pm_card_chargeDeclined";
            
            await _paymentIntentService.UpdateAsync(paymentId, new PaymentIntentUpdateOptions
            {
                PaymentMethod = paymentMethodId
            });
            
            var paymentIntent = await _paymentIntentService.ConfirmAsync(paymentId, 
                new PaymentIntentConfirmOptions { PaymentMethod = paymentMethodId });

            if (shouldSucceed && paymentIntent.Status == "requires_capture")
            {
                paymentIntent = await _paymentIntentService.CaptureAsync(paymentId);
            }

            var response = MapToResponse(paymentIntent, existing.Amount);
            
            // For declined payments, ensure status is DECLINED regardless of Stripe's response
            if (!shouldSucceed && response.Status != "APPROVED")
            {
                response = response with { Status = "DECLINED" };
            }
            
            _paymentStore.TryUpdate(paymentId, response, processingPayment);
            return response;
        }
        catch (StripeException ex)
        {
            var errorResponse = processingPayment with
            {
                Status = "DECLINED",
                Message = ex.StripeError?.Message ?? ex.Message,
                UpdatedAt = DateTime.UtcNow
            };
            _paymentStore.TryUpdate(paymentId, errorResponse, processingPayment);
            return errorResponse;
        }
    }

    public async Task<PaymentStatusResponse> CancelPaymentAsync(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment ID cannot be null or empty", nameof(paymentId));
        }

        if (!_paymentStore.TryGetValue(paymentId, out var existing))
        {
            throw new KeyNotFoundException($"Payment {paymentId} not found");
        }

        var paymentIntent = await _paymentIntentService.CancelAsync(paymentId);
        var response = MapToResponse(paymentIntent, existing.Amount);
        _paymentStore.TryUpdate(paymentId, response, existing);
        return response;
    }

    public IEnumerable<PaymentStatusResponse> GetPendingPayments()
    {
        return _paymentStore.Values
            .Where(p => p.Status == "PENDING")
            .OrderByDescending(p => p.CreatedAt);
    }

    private PaymentStatusResponse MapToResponse(PaymentIntent paymentIntent, decimal amount)
    {
        var status = paymentIntent.Status switch
        {
            "succeeded" => "APPROVED",
            "canceled" => "FAILED",
            "payment_failed" => "DECLINED",           // Pago fallido explícito
            "requires_payment_method" => "PENDING",   // Esperando método de pago (normal al crear)
            "requires_confirmation" => "PENDING",
            "requires_action" => "PENDING",
            "requires_capture" => "PENDING",
            "processing" => "PENDING",
            _ => "PENDING"
        };

        return new PaymentStatusResponse
        {
            PaymentId = paymentIntent.Id,
            Status = status,
            Amount = amount,
            Message = paymentIntent.CancellationReason ?? paymentIntent.Status,
            CreatedAt = paymentIntent.Created,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static bool IsTerminalState(string status)
    {
        return status is "APPROVED" or "DECLINED" or "CANCELED" or "ERROR" or "FAILED";
    }
}
