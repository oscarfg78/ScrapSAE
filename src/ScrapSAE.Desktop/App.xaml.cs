using System.Windows;
using Microsoft.Extensions.Configuration;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.Services;
using ScrapSAE.Desktop.ViewModels;

namespace ScrapSAE.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
        };

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var baseUrl = configuration["Api:BaseUrl"] ?? "http://localhost:5244";
        var apiClient = new ApiClient(baseUrl);
        var viewModel = new MainViewModel(apiClient);

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        window.Show();
        _ = viewModel.LoadAllAsync();
    }
}
