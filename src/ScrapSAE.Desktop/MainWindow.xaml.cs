using System.Windows;
using ScrapSAE.Desktop.ViewModels;

namespace ScrapSAE.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.LoadAllCommand.CanExecute(null))
        {
            viewModel.LoadAllCommand.Execute(null);
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement element)
        {
            if (int.TryParse(element.Tag?.ToString(), out var index))
            {
                viewModel.SelectedTabIndex = index;
            }
        }
    }
}
