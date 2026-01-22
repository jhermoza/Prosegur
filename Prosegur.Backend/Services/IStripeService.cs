using Prosegur.Shared.DTOs;

namespace Prosegur.Backend.Services;

public interface IStripeService
{
    Task<PaymentStatusResponse> CreatePaymentIntentAsync(PaymentRequest request, string idempotencyKey);
    Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId);
    Task<PaymentStatusResponse> ConfirmPaymentAsync(string paymentId, bool shouldSucceed);
    Task<PaymentStatusResponse> CancelPaymentAsync(string paymentId);
    IEnumerable<PaymentStatusResponse> GetPendingPayments();
}
