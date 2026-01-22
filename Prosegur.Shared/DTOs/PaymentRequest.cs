namespace Prosegur.Shared.DTOs;

public record PaymentRequest
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "usd";
    public string? Description { get; init; }
}
