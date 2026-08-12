using System.Windows;
using ScrapSAE.Desktop.ViewModels;

namespace ScrapSAE.Desktop.Views;

public partial class FlashlySyncWindow : Window
{
    public FlashlySyncWindow(FlashlySyncViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = () => Close();
    }
}
