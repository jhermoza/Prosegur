using Prosegur.Shared.DTOs;

namespace Prosegur.WPF.Services;

public interface IPaymentService
{
    Task<PaymentStatusResponse> CreatePaymentAsync(PaymentRequest request);
    Task<PaymentStatusResponse> GetPaymentStatusAsync(string paymentId);
}
