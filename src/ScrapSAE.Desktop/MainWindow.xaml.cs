using System.Windows;
using System.Windows.Controls;
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Don't close, just hide to tray
        e.Cancel = true;
        this.Hide();
        base.OnClosing(e);
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
