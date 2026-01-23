using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prosegur.Shared.DTOs;
using Prosegur.WPF.Services;
using System.Windows;

namespace Prosegur.WPF.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPaymentService _paymentService;
    private CancellationTokenSource? _pollCancellationTokenSource;

    [ObservableProperty]
    private decimal _amount = 10.00m;

    [ObservableProperty]
    private string _paymentId = string.Empty;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private string _statusMessage = "Enter amount and click Process Payment";

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private bool _isCompleted = false;

    [ObservableProperty]
    private DateTime? _createdAt;

    [ObservableProperty]
    private DateTime? _updatedAt;

    public MainViewModel(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [RelayCommand(CanExecute = nameof(CanProcessPayment))]
    private async Task ProcessPaymentAsync()
    {
        try
        {
            ResetState();
            IsProcessing = true;
            Status = "PENDING";
            StatusMessage = "Creating payment...";

            var request = new PaymentRequest
            {
                Amount = Amount,
                Currency = "usd",
                Description = $"POS Payment - {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };

            var response = await _paymentService.CreatePaymentAsync(request);

            if (string.IsNullOrEmpty(response.PaymentId))
            {
                throw new InvalidOperationException("Payment creation failed: Invalid payment ID");
            }

            PaymentId = response.PaymentId;
            Status = response.Status;
            CreatedAt = response.CreatedAt;
            UpdatedAt = response.UpdatedAt;
            StatusMessage = "Payment created. Waiting for approval...";

            await StartPollingAsync(response.PaymentId);
        }
        catch (Exception ex)
        {
            Status = "ERROR";
            StatusMessage = $"Error: {ex.Message}";
            IsProcessing = false;
            IsCompleted = true;

            MessageBox.Show(
                $"Failed to process payment: {ex.Message}",
                "Payment Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private bool CanProcessPayment()
    {
        return Amount > 0 && !IsProcessing;
    }

    [RelayCommand]
    private void ResetPayment()
    {
        if (_pollCancellationTokenSource != null)
        {
            _pollCancellationTokenSource.Cancel();
            _pollCancellationTokenSource.Dispose();
            _pollCancellationTokenSource = null;
        }

        ResetState();
        StatusMessage = "Ready for new payment";
    }

    private async Task StartPollingAsync(string paymentId)
    {
        if (string.IsNullOrEmpty(paymentId))
        {
            throw new ArgumentException("Payment ID cannot be null or empty", nameof(paymentId));
        }

        _pollCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _pollCancellationTokenSource.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                var statusResponse = await _paymentService.GetPaymentStatusAsync(paymentId);

                Status = statusResponse.Status;
                UpdatedAt = statusResponse.UpdatedAt;
                StatusMessage = GetStatusMessage(statusResponse.Status);

                if (IsTerminalState(statusResponse.Status))
                {
                    IsProcessing = false;
                    IsCompleted = true;
                    ShowPaymentResultNotification(statusResponse.Status);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Polling canceled";
        }
        catch (Exception ex)
        {
            Status = "ERROR";
            StatusMessage = $"Polling error: {ex.Message}";
            IsProcessing = false;
            IsCompleted = true;
        }
        finally
        {
            if (_pollCancellationTokenSource != null)
            {
                _pollCancellationTokenSource.Dispose();
                _pollCancellationTokenSource = null;
            }
        }
    }

    private bool IsTerminalState(string status)
    {
        return status is "APPROVED" or "DECLINED" or "CANCELED" or "ERROR" or "FAILED";
    }

    private string GetStatusMessage(string status)
    {
        return status switch
        {
            "PENDING" => "⏳ Waiting for approval from simulation dashboard...",
            "APPROVED" => "✅ Payment approved successfully!",
            "DECLINED" => "❌ Payment was declined.",
            "CANCELED" => "⚠️ Payment was canceled.",
            "FAILED" => "❌ Payment failed.",
            "ERROR" => "❌ An error occurred.",
            _ => $"Status: {status}"
        };
    }

    private void ShowPaymentResultNotification(string status)
    {
        var (title, icon) = status switch
        {
            "APPROVED" => ("Payment Successful", MessageBoxImage.Information),
            "DECLINED" => ("Payment Declined", MessageBoxImage.Warning),
            "CANCELED" => ("Payment Canceled", MessageBoxImage.Warning),
            _ => ("Payment Failed", MessageBoxImage.Error)
        };

        MessageBox.Show(GetStatusMessage(status), title, MessageBoxButton.OK, icon);
    }

    private void ResetState()
    {
        PaymentId = string.Empty;
        Status = "Ready";
        StatusMessage = "Enter amount and click Process Payment";
        IsProcessing = false;
        IsCompleted = false;
        CreatedAt = null;
        UpdatedAt = null;
    }

    public void Cleanup()
    {
        if (_pollCancellationTokenSource != null)
        {
            _pollCancellationTokenSource.Cancel();
            _pollCancellationTokenSource.Dispose();
            _pollCancellationTokenSource = null;
        }
    }
}
