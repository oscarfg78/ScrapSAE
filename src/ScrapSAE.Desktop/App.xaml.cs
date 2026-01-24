using System.Windows;
using Microsoft.Extensions.Configuration;
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

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var baseUrl = configuration["Api:BaseUrl"] ?? "https://localhost:5001";
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
