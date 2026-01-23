using Microsoft.AspNetCore.Mvc;
using Prosegur.Backend.Services;
using Prosegur.Shared.DTOs;

namespace Prosegur.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IStripeService _stripeService;

    public PaymentsController(IStripeService stripeService)
    {
        _stripeService = stripeService;
    }

    [HttpPost]
    public async Task<ActionResult<PaymentStatusResponse>> CreatePayment([FromBody] PaymentRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { error = "Amount must be greater than zero" });
        }

        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return BadRequest(new { error = "Currency is required" });
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault() 
            ?? Guid.NewGuid().ToString();

        try
        {
            var response = await _stripeService.CreatePaymentIntentAsync(request, idempotencyKey);
            return CreatedAtAction(nameof(GetPaymentStatus), new { paymentId = response.PaymentId }, response);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{paymentId}")]
    public async Task<ActionResult<PaymentStatusResponse>> GetPaymentStatus(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return BadRequest(new { error = "Payment ID is required" });
        }

        try
        {
            var response = await _stripeService.GetPaymentStatusAsync(paymentId);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Payment {paymentId} not found" });
        }
    }

    [HttpPost("{paymentId}/confirm")]
    public async Task<ActionResult<PaymentStatusResponse>> ConfirmPayment(
        string paymentId, 
        [FromQuery] bool shouldSucceed = true)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return BadRequest(new { error = "Payment ID is required" });
        }

        try
        {
            var response = await _stripeService.ConfirmPaymentAsync(paymentId, shouldSucceed);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Payment {paymentId} not found" });
        }
    }

    [HttpPost("{paymentId}/cancel")]
    public async Task<ActionResult<PaymentStatusResponse>> CancelPayment(string paymentId)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return BadRequest(new { error = "Payment ID is required" });
        }

        try
        {
            var response = await _stripeService.CancelPaymentAsync(paymentId);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Payment {paymentId} not found" });
        }
    }

    [HttpGet("pending")]
    public ActionResult<IEnumerable<PaymentStatusResponse>> GetPendingPayments()
    {
        return Ok(_stripeService.GetPendingPayments());
    }
}
