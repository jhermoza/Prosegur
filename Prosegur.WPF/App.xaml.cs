using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prosegur.WPF.Services;
using Prosegur.WPF.ViewModels;
using System.Windows;

namespace Prosegur.WPF;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddHttpClient("PaymentAPI", client =>
                {
                    client.BaseAddress = new Uri("http://localhost:5000/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                });

                services.AddSingleton<IPaymentService, PaymentService>();
                services.AddSingleton<MainViewModel>();
                
                services.AddSingleton<MainWindow>(provider =>
                {
                    var viewModel = provider.GetRequiredService<MainViewModel>();
                    return new MainWindow { DataContext = viewModel };
                });
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await _host.StartAsync();
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error starting application:\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(-1);
            return;
        }
        
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var viewModel = _host.Services.GetService<MainViewModel>();
        viewModel?.Cleanup();

        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        
        base.OnExit(e);
    }
}
