using Prosegur.Shared.DTOs;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Prosegur.WPF.Services;

public class PaymentService : IPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public PaymentService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("PaymentAPI");
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    public async Task<PaymentStatusResponse> CreatePaymentAsync(PaymentRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero", nameof(request));
        }

        try
        {
            var idempotencyKey = Guid.NewGuid().ToString();
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/payments")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);

            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var paymentResponse = await response.Content.ReadFromJsonAsync<PaymentStatusResponse>(_jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize payment response");

            return paymentResponse;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to create payment: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("Request timeout. Please check your connection.", ex);
        }
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            throw new ArgumentException("Payment ID cannot be null or empty", nameof(paymentId));
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<PaymentStatusResponse>(
                $"api/payments/{paymentId}", 
                _jsonOptions
            );

            if (response == null)
            {
                throw new InvalidOperationException($"Payment {paymentId} not found");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get payment status: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException("Request timeout. Please check your connection.", ex);
        }
    }
}
