using System.Linq;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Services;
using ScrapSAE.Desktop.Infrastructure;

namespace ScrapSAE.Desktop.ViewModels;

public class FlashlySyncItemViewModel : ViewModelBase
{
    private bool _isSelected = true;
    private string _syncStatus = "Pendiente";
    private string? _syncErrorMessage;

    public FlashlyProductSyncPayload Payload { get; }
    public FlashlyProductValidationResult Validation { get; private set; }
    public object? OriginalProduct { get; }

    public FlashlySyncItemViewModel(FlashlyProductSyncPayload payload, IFlashlyProductValidator validator, object? originalProduct = null)
    {
        Payload = payload ?? new FlashlyProductSyncPayload();
        Validation = validator.Validate(Payload);
        OriginalProduct = originalProduct;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string SourceSku => Payload.SourceSku;
    public string Name => Payload.Name;
    public decimal PurchasePrice => Payload.PurchasePrice;
    public string Currency => Payload.Currency;
    public string CategorySummary => Payload.Categories != null && Payload.Categories.Count > 0 
        ? string.Join(", ", Payload.Categories) 
        : "Sin categoría";
    public string ImageUrl => Payload.ImageUrls?.FirstOrDefault() ?? string.Empty;

    public bool IsValid => Validation.IsValid;
    public string ValidationStatusLabel => IsValid ? "✓ Válido" : "✗ Inválido";
    public string ValidationErrors => Validation.Summary;

    public string SyncStatus
    {
        get => _syncStatus;
        set
        {
            if (SetField(ref _syncStatus, value))
            {
                OnPropertyChanged(nameof(SyncStatusLabel));
            }
        }
    }

    public string? SyncErrorMessage
    {
        get => _syncErrorMessage;
        set
        {
            if (SetField(ref _syncErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasSyncError));
            }
        }
    }

    public bool HasSyncError => !string.IsNullOrEmpty(SyncErrorMessage);
    public string SyncStatusLabel => SyncStatus;

    public void Revalidate(IFlashlyProductValidator validator)
    {
        Validation = validator.Validate(Payload);
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationStatusLabel));
        OnPropertyChanged(nameof(ValidationErrors));
    }
}
