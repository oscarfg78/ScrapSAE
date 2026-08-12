using System.Linq;
using System.Text.Json;
using System.Windows;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Desktop.Views.ConcurrentWizard;

public partial class ProductDetailsWindow : Window
{
    public ProductDetailsWindow(ConsolidatedProductResult result)
    {
        InitializeComponent();
        
        // Wrap the result in an anonymous type or use an inner viewmodel to provide formatted properties
        DataContext = new
        {
            Title = result.Title,
            Sku = result.Sku,
            HasWarning = !string.IsNullOrEmpty(result.WarningMessage),
            WarningMessage = result.WarningMessage,
            FirstImageUrl = result.ImageUrls?.FirstOrDefault(),
            SupplierCost = result.SupplierCost,
            RetailPrice = result.RetailPrice,
            Description = result.Description,
            AttributesJson = result.OptionalAttributes != null && result.OptionalAttributes.Count > 0 
                ? JsonSerializer.Serialize(result.OptionalAttributes, new JsonSerializerOptions { WriteIndented = true })
                : "{}"
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
