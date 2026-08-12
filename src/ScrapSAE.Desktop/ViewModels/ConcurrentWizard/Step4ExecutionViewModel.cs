using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Desktop.Infrastructure;

namespace ScrapSAE.Desktop.ViewModels.ConcurrentWizard;

// ─────────────────────────────────────────────────────────────────────────────
// Card de producto para el live results grid
// ─────────────────────────────────────────────────────────────────────────────

public class ConsolidatedProductCard
{
    public string Sku { get; set; } = string.Empty;
    public string? Title { get; set; }
    public decimal SupplierCost { get; set; }
    public decimal? RetailPrice { get; set; }
    public string? FirstImageUrl { get; set; }
    public ConsolidatedStatus Status { get; set; }
    public string? WarningMessage { get; set; }
    public bool HasWarning => !string.IsNullOrEmpty(WarningMessage);
    public string StatusLabel => Status == ConsolidatedStatus.Matched ? "✓ Encontrado" : "✗ No encontrado";
    public string PriceDisplay => RetailPrice.HasValue ? $"${RetailPrice:N2}" : "—";
    public string CostDisplay  => $"${SupplierCost:N2}";
    
    // Referencia al resultado original completo para la modal de detalles
    public ConsolidatedProductResult? FullResult { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Step 4 — Execution Monitoring
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel del Step 4: monitoreo en tiempo real de la ejecución del scraping.
/// Suscribe al IObservable{ScrapingProgressEvent} con throttle de 100ms
/// y actualiza las colecciones en el hilo UI via Dispatcher.
/// </summary>
public sealed class Step4ExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly IConcurrentScrapingEngine _engine;
    private readonly IWizardSessionRepository  _sessionRepository;
    private readonly ConcurrentWizardSession   _session;
    private readonly ScrapSAE.Desktop.Services.ApiClient _apiClient;
    private IDisposable? _progressSubscription;

    // Estado de ejecución
    private bool _isRunning;
    private bool _isPaused;
    private string _statusMessage = "Listo para iniciar";
    private int _processedCount;
    private int _successCount;
    private int _skippedCount;
    private int _totalRows;
    private double _progressPercent;
    private string _elapsedTime = "00:00:00";
    private System.Timers.Timer? _elapsedTimer;
    private DateTime _startedAt;

    private int _retryIndex = 1;
    public int RetryIndex
    {
        get => _retryIndex;
        set => SetField(ref _retryIndex, value);
    }

    public Step4ExecutionViewModel(
        IConcurrentScrapingEngine engine,
        IWizardSessionRepository sessionRepository,
        ConcurrentWizardSession session,
        ScrapSAE.Desktop.Services.ApiClient apiClient)
    {
        _engine            = engine;
        _sessionRepository = sessionRepository;
        _session           = session;
        _apiClient         = apiClient;
        _totalRows         = session.TotalExcelRows - (session.LastCompletedRowIndex + 1);

        LiveResults = new ObservableCollection<ConsolidatedProductCard>();

        StartCommand   = new AsyncCommand(ExecuteStartAsync,   () => !IsRunning);
        PauseCommand  = new RelayCommand(() => _engine.Pause(), () => IsRunning && !IsPaused);
        ResumeCommand = new RelayCommand(ExecuteResume, () => IsRunning && IsPaused);
        StopCommand   = new AsyncCommand(ExecuteStopAsync, () => IsRunning);
        ExportCommand = new AsyncCommand(ExecuteExportAsync, () => LiveResults.Count > 0);
        SaveToDatabaseCommand = new AsyncCommand(ExecuteSaveToDatabaseAsync, () => LiveResults.Count > 0);
        OpenFlashlySyncCommand = new RelayCommand(ExecuteOpenFlashlySync, () => LiveResults.Count > 0);
        RetryFromIndexCommand = new AsyncCommand(ExecuteRetryFromIndexAsync, () => !IsRunning || IsPaused);
        ViewDetailsCommand = new RelayCommand<ConsolidatedProductCard>(ExecuteViewDetails);
        
        LiveResults.CollectionChanged += (_, _) => 
        {
            (ExportCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (SaveToDatabaseCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (OpenFlashlySyncCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };
    }

    // ── Observables / Properties ──────────────────────────────────────────────

    public ObservableCollection<ConsolidatedProductCard> LiveResults { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetField(ref _isRunning, value);
            (StartCommand  as AsyncCommand)?.RaiseCanExecuteChanged();
            (StopCommand   as AsyncCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (PauseCommand  as RelayCommand)?.RaiseCanExecuteChanged();
            (ResumeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            SetField(ref _isPaused, value);
            (PauseCommand  as RelayCommand)?.RaiseCanExecuteChanged();
            (ResumeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public int ProcessedCount
    {
        get => _processedCount;
        private set => SetField(ref _processedCount, value);
    }

    public int SuccessCount
    {
        get => _successCount;
        private set => SetField(ref _successCount, value);
    }

    public int SkippedCount
    {
        get => _skippedCount;
        private set => SetField(ref _skippedCount, value);
    }

    public int TotalRows
    {
        get => _totalRows;
        private set => SetField(ref _totalRows, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public string ElapsedTime
    {
        get => _elapsedTime;
        private set => SetField(ref _elapsedTime, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand StartCommand  { get; }
    public ICommand PauseCommand  { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand   { get; }
    public ICommand ExportCommand { get; }
    public ICommand SaveToDatabaseCommand { get; }
    public ICommand OpenFlashlySyncCommand { get; }
    public ICommand RetryFromIndexCommand { get; }
    public ICommand ViewDetailsCommand { get; }

    // ── Execution Control ────────────────────────────────────────────────────

    private async Task ExecuteStartAsync()
    {
        IsRunning  = true;
        IsPaused   = false;
        _startedAt = DateTime.Now;

        // Timer para elapsed time display
        _elapsedTimer = new System.Timers.Timer(1000);
        _elapsedTimer.Elapsed += (_, _) =>
        {
            var elapsed = DateTime.Now - _startedAt;
            Application.Current.Dispatcher.Invoke(() =>
                ElapsedTime = elapsed.ToString(@"hh\:mm\:ss"));
        };
        _elapsedTimer.Start();

        // Suscribir al progress stream con throttle de 100ms
        _progressSubscription = _engine.Progress
            .Sample(TimeSpan.FromMilliseconds(100))
            .Subscribe(evt => Application.Current.Dispatcher.Invoke(() => HandleProgressEvent(evt)));

        try
        {
            await _engine.StartAsync(_session);
        }
        finally
        {
            _elapsedTimer?.Stop();
            IsRunning = false;
        }
    }

    private void ExecutePause()
    {
        _engine.Pause();
        IsPaused      = true;
        StatusMessage = "⏸ Ejecución pausada";
    }

    private void ExecuteResume()
    {
        _engine.Resume();
        IsPaused      = false;
        StatusMessage = "▶ Reanudando...";
    }

    private async Task ExecuteStopAsync()
    {
        await _engine.StopAsync();
        IsRunning     = false;
        IsPaused      = false;
        StatusMessage = "⏹ Ejecución detenida";
    }

    private async Task ExecuteExportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "CSV|*.csv|JSON|*.json",
            FileName = $"scraping_{_session.SessionId[..8]}_{DateTime.Now:yyyyMMdd_HHmm}"
        };

        if (dialog.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext == ".json")
        {
            var (_, results) = await _sessionRepository.LoadAsync(_session.SessionId);
            var json = System.Text.Json.JsonSerializer.Serialize(results,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
        }
        else
        {
            // CSV básico
            var lines = new List<string>
            {
                "SKU,Costo Proveedor,Precio Venta,Título,Imagen,Estado"
            };
            foreach (var card in LiveResults)
            {
                lines.Add($"\"{card.Sku}\",{card.SupplierCost},{card.RetailPrice?.ToString() ?? ""},\"{card.Title}\",\"{card.FirstImageUrl}\",{card.StatusLabel}");
            }
            await System.IO.File.WriteAllLinesAsync(dialog.FileName, lines);
        }

        StatusMessage = $"✓ Exportado: {dialog.FileName}";
    }

    private async Task ExecuteRetryFromIndexAsync()
    {
        if (RetryIndex < 1 || RetryIndex > TotalRows)
        {
            MessageBox.Show($"El índice debe estar entre 1 y {TotalRows}", "Índice inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"¿Estás seguro de que deseas reintentar desde el registro {RetryIndex}?\nSe eliminarán de la pantalla todos los registros posteriores a este índice.", "Reintentar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        // Limpiar resultados posteriores
        // En _sessionRepository o memory, se sobrescribirán cuando el engine los vuelva a procesar.
        // Pero en la UI (LiveResults) debemos removerlos.
        var itemsToRemove = LiveResults.Skip(RetryIndex - 1).ToList();
        foreach (var item in itemsToRemove)
        {
            LiveResults.Remove(item);
        }

        _session.LastCompletedRowIndex = RetryIndex - 1; // Para que empiece desde RetryIndex
        await _sessionRepository.SaveAsync(_session);
        await _sessionRepository.TruncateResultsAsync(_session.SessionId, _session.LastCompletedRowIndex);

        ProcessedCount = _session.LastCompletedRowIndex;
        // Asignar estadisticas aproximadas (o reiniciarlas)
        SuccessCount = LiveResults.Count(x => x.Status == ConsolidatedStatus.Matched);
        SkippedCount = ProcessedCount - SuccessCount;
        
        // Si estaba detenido, iniciarlo
        if (!IsRunning)
        {
            await ExecuteStartAsync();
        }
        else if (IsPaused)
        {
            ExecuteResume();
        }
    }

    private void ExecuteViewDetails(ConsolidatedProductCard card)
    {
        if (card?.FullResult == null)
        {
            MessageBox.Show("Los detalles completos no están disponibles para este registro.", "Detalles no disponibles", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ScrapSAE.Desktop.Views.ConcurrentWizard.ProductDetailsWindow(card.FullResult);
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    // ── Progress Handler ─────────────────────────────────────────────────────

    private void HandleProgressEvent(ScrapingProgressEvent evt)
    {
        ProcessedCount  = evt.ProcessedCount;
        SuccessCount    = evt.SuccessCount;
        SkippedCount    = evt.SkippedCount;
        ProgressPercent = evt.ProgressPercent;

        switch (evt.EventType)
        {
            case ProgressEventType.RowCompleted when evt.Result != null:
                if (evt.Result.Status == ConsolidatedStatus.Matched)
                {
                    LiveResults.Add(new ConsolidatedProductCard
                    {
                        Sku           = evt.Result.Sku,
                        Title         = evt.Result.Title,
                        SupplierCost  = evt.Result.SupplierCost,
                        RetailPrice   = evt.Result.RetailPrice,
                        FirstImageUrl = evt.Result.ImageUrls.FirstOrDefault(),
                        Status        = evt.Result.Status,
                        WarningMessage = evt.Result.WarningMessage,
                        FullResult    = evt.Result
                    });
                }
                StatusMessage = $"Procesando: {evt.Sku} — {evt.ProcessedCount}/{evt.TotalRows}";
                break;

            case ProgressEventType.RowSkipped:
                StatusMessage = $"[SKIP] {evt.Sku}: {evt.Message}";
                break;

            case ProgressEventType.ExecutionPaused:
                StatusMessage = "⏸ Pausado";
                break;

            case ProgressEventType.ExecutionFinished:
                StatusMessage = $"✓ Completado: {evt.SuccessCount} encontrados, {evt.SkippedCount} omitidos";
                break;

            case ProgressEventType.ExecutionStopped:
                StatusMessage = $"⏹ Detenido: {evt.ProcessedCount} procesados";
                break;
        }
    }

    // ── Save To Database ─────────────────────────────────────────────────────

    private async Task ExecuteSaveToDatabaseAsync()
    {
        if (_session.TargetSiteId == null)
        {
            MessageBox.Show("No se seleccionó un Proveedor Destino en el Paso 1.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var matchedResults = LiveResults.Where(r => r.Status == ConsolidatedStatus.Matched && r.FullResult != null).ToList();
        if (matchedResults.Count == 0)
        {
            MessageBox.Show("No hay productos encontrados para guardar.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusMessage = "Guardando en base de datos...";
        int savedCount = 0;
        int errorCount = 0;

        foreach (var card in matchedResults)
        {
            var r = card.FullResult!;
            var stagingProduct = new ScrapSAE.Core.Entities.StagingProduct
            {
                SiteId = _session.TargetSiteId.Value,
                Brand = _session.TargetSiteName ?? string.Empty,
                SkuSource = r.Sku,
                SkuSae = r.Sku,
                Status = "pending",
                RawData = System.Text.Json.JsonSerializer.Serialize(r),
            };

            if (r.OptionalAttributes.TryGetValue("UrlOrigen", out var urlOrigen))
            {
                stagingProduct.SourceUrl = urlOrigen;
            }

            if (r.OptionalAttributes.TryGetValue("Categoria", out var category))
            {
                stagingProduct.Category = category;
            }

            try
            {
                await _apiClient.UpsertStagingProductAsync(stagingProduct);
                savedCount++;
            }
            catch (Exception)
            {
                errorCount++;
                // log or capture
            }
        }

        StatusMessage = $"Guardado completado: {savedCount} guardados, {errorCount} errores.";
        MessageBox.Show(StatusMessage, "Guardar en Base de Datos", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExecuteOpenFlashlySync()
    {
        var matchedCards = LiveResults
            .Where(r => r.Status == ConsolidatedStatus.Matched && r.FullResult != null)
            .ToList();

        if (matchedCards.Count == 0)
        {
            MessageBox.Show("No hay productos vigentes para sincronizar con Flashly.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var payloads = matchedCards
            .Select(c => ScrapSAE.Infrastructure.Services.FlashlyProductMapper.ToFlashlyPayload(c.FullResult!, _session.TargetSiteName))
            .ToList();

        var vm = new FlashlySyncViewModel(
            stagingProducts: null,
            payloads: payloads,
            apiClient: _apiClient,
            defaultSupplierName: _session.TargetSiteName);

        var window = new ScrapSAE.Desktop.Views.FlashlySyncWindow(vm)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _progressSubscription?.Dispose();
        _elapsedTimer?.Dispose();
    }
}
