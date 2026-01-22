namespace Prosegur.Shared.DTOs;

public record PaymentStatusResponse
{
    public string PaymentId { get; init; } = string.Empty;
    public string Status { get; init; } = "PENDING";
    public string? Message { get; init; }
    public decimal Amount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
