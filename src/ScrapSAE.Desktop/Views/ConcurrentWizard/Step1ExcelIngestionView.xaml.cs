using System.Windows;
using System.Windows.Controls;
using ScrapSAE.Desktop.ViewModels.ConcurrentWizard;

namespace ScrapSAE.Desktop.Views.ConcurrentWizard;

public partial class Step1ExcelIngestionView : UserControl
{
    public Step1ExcelIngestionView()
    {
        InitializeComponent();
    }

    private void Border_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Border_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var file = files[0];
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".xlsx" || ext == ".xls")
                {
                    if (DataContext is Step1ExcelIngestionViewModel vm)
                    {
                        await vm.LoadFileFromPathAsync(file);
                    }
                }
            }
        }
    }
}
