using System.Windows;
using ScrapSAE.Desktop.ViewModels.ConcurrentWizard;

namespace ScrapSAE.Desktop.Views.ConcurrentWizard;

public partial class ConcurrentProviderWizardWindow : Window
{
    public ConcurrentProviderWizardWindow(ConcurrentProviderWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
