using System.Windows;
using ScrapSAE.Desktop.Services;
using ScrapSAE.Desktop.ViewModels;

namespace ScrapSAE.Desktop.Views;

public partial class ProviderWizardView : Window
{
    private readonly ProviderWizardViewModel _vm;

    public ProviderWizardView(ApiClient apiClient)
    {
        InitializeComponent();
        _vm = new ProviderWizardViewModel(apiClient);
        DataContext = _vm;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        _ = CancelAndCloseAsync();
    }

    private async Task CancelAndCloseAsync()
    {
        // ViewModel handles temp site cleanup
        var cancelCmd = _vm.CancelCommand;
        if (cancelCmd.CanExecute(null))
        {
            cancelCmd.Execute(null);
        }
        // Small delay to let async cleanup start
        await Task.Delay(200);
        DialogResult = false;
        Close();
    }

    private void OnCloseSuccessClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>The SiteProfile created by the wizard (null if cancelled or not saved).</summary>
    public Core.Entities.SiteProfile? CreatedSite => _vm.CreatedSite;
}
