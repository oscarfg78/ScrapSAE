using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Core.Services;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.Services;
using ScrapSAE.Infrastructure.Services;

namespace ScrapSAE.Desktop.ViewModels;

public class FlashlySyncViewModel : ViewModelBase
{
    private readonly IFlashlySyncService _syncService;
    private readonly IFlashlyProductValidator _validator;
    private readonly ApiClient? _apiClient;

    private bool _isSyncing;
    private double _progressPercent;
    private string _statusMessage = "Listo para sincronizar productos con Flashly";
    private bool _selectAll = true;
    private string? _supplierName;
    private string _logText = string.Empty;

    public ObservableCollection<FlashlySyncItemViewModel> Items { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();

    public bool IsSyncing
    {
        get => _isSyncing;
        private set
        {
            if (SetField(ref _isSyncing, value))
            {
                (StartSyncCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                (ValidateAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ToggleSelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    public bool SelectAll
    {
        get => _selectAll;
        set
        {
            if (SetField(ref _selectAll, value))
            {
                ExecuteToggleSelectAll(value);
            }
        }
    }

    public string? SupplierName
    {
        get => _supplierName;
        set => SetField(ref _supplierName, value);
    }

    public int TotalCount => Items.Count;
    public int SelectedCount => Items.Count(i => i.IsSelected);
    public int ValidCount => Items.Count(i => i.IsValid);
    public int InvalidCount => Items.Count(i => !i.IsValid);
    public int SyncedCount => Items.Count(i => i.SyncStatus == "Sincronizado");
    public int FailedCount => Items.Count(i => i.SyncStatus == "Error");

    public ICommand ToggleSelectAllCommand { get; }
    public ICommand ValidateAllCommand { get; }
    public ICommand StartSyncCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand CopyLogCommand { get; }

    public Action? CloseAction { get; set; }
    public ICommand CloseCommand { get; }

    public FlashlySyncViewModel(
        IEnumerable<StagingProduct>? stagingProducts,
        IEnumerable<FlashlyProductSyncPayload>? payloads = null,
        IFlashlySyncService? syncService = null,
        IFlashlyProductValidator? validator = null,
        ApiClient? apiClient = null,
        string? defaultSupplierName = null)
    {
        _validator = validator ?? new FlashlyProductValidator();
        _syncService = syncService ?? new DummyFlashlySyncService();
        _apiClient = apiClient;
        _supplierName = defaultSupplierName;

        if (stagingProducts != null)
        {
            foreach (var p in stagingProducts)
            {
                var payload = FlashlyProductMapper.ToFlashlyPayload(p);
                if (!string.IsNullOrWhiteSpace(_supplierName) && string.IsNullOrWhiteSpace(payload.SupplierName))
                {
                    payload.SupplierName = _supplierName;
                }
                var itemVm = new FlashlySyncItemViewModel(payload, _validator, p);
                RegisterItemEvents(itemVm);
                Items.Add(itemVm);
            }
        }

        if (payloads != null)
        {
            foreach (var payload in payloads)
            {
                if (!string.IsNullOrWhiteSpace(_supplierName) && string.IsNullOrWhiteSpace(payload.SupplierName))
                {
                    payload.SupplierName = _supplierName;
                }
                var itemVm = new FlashlySyncItemViewModel(payload, _validator, payload);
                RegisterItemEvents(itemVm);
                Items.Add(itemVm);
            }
        }

        ToggleSelectAllCommand = new RelayCommand(() => SelectAll = !SelectAll, () => !IsSyncing);
        ValidateAllCommand = new RelayCommand(ExecuteValidateAll, () => !IsSyncing);
        StartSyncCommand = new AsyncCommand(ExecuteStartSyncAsync, () => !IsSyncing && Items.Any(i => i.IsSelected && i.IsValid));
        ExportReportCommand = new AsyncCommand(ExecuteExportReportAsync, () => Items.Count > 0);
        CopyLogCommand = new RelayCommand(ExecuteCopyLog);
        CloseCommand = new RelayCommand(() => CloseAction?.Invoke());

        UpdateCounts();
    }

    private void RegisterItemEvents(FlashlySyncItemViewModel item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FlashlySyncItemViewModel.IsSelected) ||
                e.PropertyName == nameof(FlashlySyncItemViewModel.SyncStatus) ||
                e.PropertyName == nameof(FlashlySyncItemViewModel.IsValid))
            {
                UpdateCounts();
            }
        };
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ValidCount));
        OnPropertyChanged(nameof(InvalidCount));
        OnPropertyChanged(nameof(SyncedCount));
        OnPropertyChanged(nameof(FailedCount));
        (StartSyncCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteToggleSelectAll(bool select)
    {
        foreach (var item in Items)
        {
            item.IsSelected = select;
        }
        UpdateCounts();
    }

    private void ExecuteValidateAll()
    {
        foreach (var item in Items)
        {
            item.Revalidate(_validator);
        }
        UpdateCounts();
        StatusMessage = $"Validación completa: {ValidCount} válidos, {InvalidCount} con errores.";
    }

    private void AppendLog(string message)
    {
        LogMessages.Add(message);
        LogText = string.Join(Environment.NewLine, LogMessages);
    }

    private void ExecuteCopyLog()
    {
        if (!string.IsNullOrWhiteSpace(LogText))
        {
            Clipboard.SetText(LogText);
            StatusMessage = "Logs copiados al portapapeles.";
        }
    }

    private async Task ExecuteStartSyncAsync()
    {
        var targetItems = Items.Where(i => i.IsSelected && i.IsValid).ToList();
        if (targetItems.Count == 0)
        {
            StatusMessage = "No hay productos válidos seleccionados para sincronizar.";
            return;
        }

        IsSyncing = true;
        ProgressPercent = 0;
        LogMessages.Clear();
        LogText = string.Empty;

        int totalToSync = targetItems.Count;
        int completed = 0;
        int successCount = 0;
        int failCount = 0;

        AppendLog($"Iniciando envío de {totalToSync} registro(s) a Flashly API...");

        for (int i = 0; i < totalToSync; i++)
        {
            var item = targetItems[i];
            var current = i + 1;
            var skuLabel = string.IsNullOrWhiteSpace(item.SourceSku) ? $"Producto {current}" : item.SourceSku;

            item.SyncStatus = "Sincronizando...";
            item.SyncErrorMessage = null;

            var startMsg = $"[{current}/{totalToSync}] Enviando {skuLabel}...";
            StatusMessage = startMsg;
            AppendLog(startMsg);

            try
            {
                bool isSuccess = false;
                string? errorDetails = null;

                if (_apiClient != null)
                {
                    if (item.OriginalProduct is StagingProduct sp)
                    {
                        sp.Brand = item.Payload.SupplierName ?? sp.Brand;
                        var result = await _apiClient.SendToOnlineStoreAsync(sp.Id);
                        if (result.Success)
                        {
                            isSuccess = true;
                            sp.FlashlySyncStatus = "synced";
                            sp.FlashlySyncedAt = DateTime.UtcNow;
                        }
                        else
                        {
                            isSuccess = false;
                            errorDetails = result.Message ?? "Error al enviar a la API de la tienda en línea";
                            sp.FlashlySyncStatus = "error";
                        }
                    }
                    else
                    {
                        var result = await _apiClient.SendFlashlyPayloadAsync(item.Payload);
                        if (result.Success)
                        {
                            isSuccess = true;
                        }
                        else
                        {
                            isSuccess = false;
                            errorDetails = result.Message ?? "Error al enviar el payload a Flashly";
                        }
                    }
                }
                else if (_syncService != null && _syncService is not DummyFlashlySyncService)
                {
                    var batchResult = await _syncService.SyncPayloadsAsync(new[] { item.Payload });
                    if (batchResult.Success && (batchResult.Errors == null || batchResult.Errors.Count == 0))
                    {
                        isSuccess = true;
                    }
                    else
                    {
                        isSuccess = false;
                        errorDetails = batchResult.Errors?.FirstOrDefault()?.Error ?? batchResult.Message ?? "Error en sincronización";
                    }
                }
                else
                {
                    isSuccess = false;
                    errorDetails = "Servidor API de ScrapSAE no disponible. Inicia el backend API primero.";
                }

                if (isSuccess)
                {
                    item.SyncStatus = "Sincronizado";
                    successCount++;
                    AppendLog($"[{current}/{totalToSync}] OK {skuLabel}");
                }
                else
                {
                    item.SyncStatus = "Error";
                    item.SyncErrorMessage = errorDetails;
                    failCount++;
                    AppendLog($"[{current}/{totalToSync}] ERROR {skuLabel}: {errorDetails}");
                }
            }
            catch (Exception ex)
            {
                item.SyncStatus = "Error";
                item.SyncErrorMessage = ex.Message;
                failCount++;
                AppendLog($"[{current}/{totalToSync}] EXCEPCIÓN {skuLabel}: {ex.Message}");
            }

            completed++;
            ProgressPercent = (double)completed / totalToSync * 100;
            UpdateCounts();
        }

        IsSyncing = false;
        var endMsg = $"Finalizado. Enviados: {successCount}. Fallidos: {failCount}.";
        StatusMessage = endMsg;
        AppendLog(endMsg);
    }

    private async Task ExecuteExportReportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            FileName = $"Flashly_Sync_Report_{DateTime.Now:yyyyMMdd_HHmm}"
        };

        if (dialog.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext == ".json")
        {
            var data = Items.Select(i => new
            {
                i.SourceSku,
                i.Name,
                i.PurchasePrice,
                i.Currency,
                i.IsValid,
                ValidationErrors = i.ValidationErrors,
                i.SyncStatus,
                i.SyncErrorMessage,
                Payload = i.Payload
            });
            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
        }
        else
        {
            var lines = new List<string> { "SKU,Nombre,Precio,Moneda,Estado Validación,Errores Validación,Estado Sync,Error Sync" };
            foreach (var item in Items)
            {
                lines.Add($"\"{item.SourceSku}\",\"{item.Name}\",{item.PurchasePrice},\"{item.Currency}\",\"{item.ValidationStatusLabel}\",\"{item.ValidationErrors}\",\"{item.SyncStatus}\",\"{item.SyncErrorMessage}\"");
            }
            await System.IO.File.WriteAllLinesAsync(dialog.FileName, lines);
        }

        StatusMessage = $"Reporte exportado correctamente: {dialog.FileName}";
    }
}

public class DummyFlashlySyncService : IFlashlySyncService
{
    public Task<FlashlySyncResult> SyncProductsAsync(IEnumerable<StagingProduct> products, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FlashlySyncResult
        {
            Success = true,
            Created = products.Count(),
            Message = "Modo simulación (sin endpoint API configurado)."
        });
    }

    public Task<FlashlySyncResult> SyncPayloadsAsync(IEnumerable<FlashlyProductSyncPayload> payloads, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FlashlySyncResult
        {
            Success = true,
            Created = payloads.Count(),
            Message = "Modo simulación (sin endpoint API configurado)."
        });
    }
}
