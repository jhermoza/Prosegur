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
        var apiKey = configuration["Stripe:SecretKey"];
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Stripe:SecretKey not configured");
        
        StripeConfiguration.ApiKey = apiKey;
        _paymentIntentService = new PaymentIntentService();
        _paymentStore = new ConcurrentDictionary<string, PaymentStatusResponse>();
    }

    public async Task<PaymentStatusResponse> CreatePaymentIntentAsync(
        PaymentRequest request, 
        string idempotencyKey)
    {
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
        
        // Verify availability before returning
        await Task.Delay(100);
        paymentIntent = await _paymentIntentService.GetAsync(paymentIntent.Id);
        
        var response = MapToResponse(paymentIntent, request.Amount);
        _paymentStore.TryAdd(paymentIntent.Id, response);
        return response;
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId)
    {
        if (_paymentStore.TryGetValue(paymentId, out var cachedResponse))
        {
            // If already in terminal state, don't update from Stripe
            if (cachedResponse.Status is "APPROVED" or "DECLINED" or "FAILED")
                return cachedResponse;

            // Only update if PENDING
            var paymentIntent = await _paymentIntentService.GetAsync(paymentId);
            var updatedResponse = MapToResponse(paymentIntent, cachedResponse.Amount);
            _paymentStore.TryUpdate(paymentId, updatedResponse, cachedResponse);
            return updatedResponse;
        }
        throw new KeyNotFoundException($"Payment {paymentId} not found");
    }

    public async Task<PaymentStatusResponse> ConfirmPaymentAsync(string paymentId, bool shouldSucceed)
    {
        if (!_paymentStore.TryGetValue(paymentId, out var existing))
            throw new KeyNotFoundException($"Payment {paymentId} not found");

        try
        {
            // Select payment method based on desired outcome
            var paymentMethodId = shouldSucceed ? "pm_card_visa" : "pm_card_chargeDeclined";
            
            await _paymentIntentService.UpdateAsync(paymentId, new PaymentIntentUpdateOptions
            {
                PaymentMethod = paymentMethodId
            });
            
            var paymentIntent = await _paymentIntentService.ConfirmAsync(paymentId, 
                new PaymentIntentConfirmOptions { PaymentMethod = paymentMethodId });

            // If successful and requires capture, capture it
            if (shouldSucceed && paymentIntent.Status == "requires_capture")
                paymentIntent = await _paymentIntentService.CaptureAsync(paymentId);

            var response = MapToResponse(paymentIntent, existing.Amount);
            _paymentStore.TryUpdate(paymentId, response, existing);
            return response;
        }
        catch (StripeException ex)
        {
            // Stripe declined the card
            var errorResponse = existing with
            {
                Status = "DECLINED",
                Message = ex.StripeError?.Message ?? ex.Message,
                UpdatedAt = DateTime.UtcNow
            };
            _paymentStore.TryUpdate(paymentId, errorResponse, existing);
            return errorResponse;
        }
    }

    public async Task<PaymentStatusResponse> CancelPaymentAsync(string paymentId)
    {
        var paymentIntent = await _paymentIntentService.CancelAsync(paymentId);
        
        if (!_paymentStore.TryGetValue(paymentId, out var existing))
            throw new KeyNotFoundException($"Payment {paymentId} not found");

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
}
