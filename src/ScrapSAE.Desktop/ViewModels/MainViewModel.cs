using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.Models;
using ScrapSAE.Desktop.Services;

namespace ScrapSAE.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _saeTimer;
    private readonly DispatcherTimer _logTimer;
    private readonly DispatcherTimer _statusTimer;
    private SiteProfile? _selectedSite;
    private StagingProductUi? _selectedStagingProduct;
    private CategoryMapping? _selectedCategoryMapping;
    private SyncLog? _selectedSyncLog;
    private ExecutionReport? _selectedExecutionReport;
    private string _statusMessage = "Listo";
    private ScrapeRunResult? _scrapeResult;
    private bool _saeScheduleEnabled;
    private int _saeScheduleMinutes = 30;
    private string _supabaseUrl = string.Empty;
    private string _supabaseServiceKey = string.Empty;
    private string _saeSdkPath = string.Empty;
    private string _saeUser = string.Empty;
    private string _saePassword = string.Empty;
    private string _saeDbHost = string.Empty;
    private string _saeDbPath = string.Empty;
    private string _saeDbUser = string.Empty;
    private string _saeDbPassword = string.Empty;
    private int _saeDbPort = 3050;
    private string _saeDbCharset = "ISO8859_1";
    private int _saeDbDialect = 3;
    private string _saeDefaultLineCode = "LINEA";
    private string _backendStatus = "Sin validar";
    private string _supabaseStatus = "Sin validar";
    private string _saeStatus = "Sin validar";
    private string _databaseStatus = "Sin validar";
    private int? _supabaseSampleCount;
    private DiagnosticsResult? _diagnosticsResult;
    private bool _hasSites;
    private bool _manualLoginEnabled;
    private bool _headlessEnabled = true;
    private bool _isScraping;
    private string _scrapeStatusText = "Idle";
    private int _selectedTabIndex;
    private string _selectorAnalysisResult = string.Empty;
    private string _scrapingMode = "Tradicional";
    private bool _isFamiliesMode;
    
    // Nuevas propiedades para consola en tiempo real y opciones avanzadas
    private bool _keepBrowserOpen;
    private bool _useScreenshotFallback;
    private string _learnedUrlsText = string.Empty;
    private readonly DispatcherTimer _liveLogTimer;
    private DateTime _lastLogTimestamp = DateTime.UtcNow.AddDays(-1);

    public MainViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        _saeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(_saeScheduleMinutes) };
        _saeTimer.Tick += async (_, _) => await SendPendingToSaeAsync();
        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _logTimer.Tick += async (_, _) => await RefreshLogsAsync();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await RefreshScrapeStatusAsync();
        _liveLogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveLogTimer.Tick += async (_, _) => await RefreshLiveLogsAsync();

        LoadAllCommand = new AsyncCommand(() => SafeExecuteAsync(LoadAllAsync, "Cargar datos"));
        CreateSiteCommand = new AsyncCommand(() => SafeExecuteAsync(CreateSiteAsync, "Crear proveedor"));
        UpdateSiteCommand = new AsyncCommand(() => SafeExecuteAsync(UpdateSiteAsync, "Actualizar proveedor"));
        DeleteSiteCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteSiteAsync, "Eliminar proveedor"));

        CreateStagingCommand = new AsyncCommand(() => SafeExecuteAsync(CreateStagingAsync, "Crear staging"));
        UpdateStagingCommand = new AsyncCommand(() => SafeExecuteAsync(UpdateStagingAsync, "Actualizar staging"));
        DeleteStagingCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteStagingAsync, "Eliminar staging"));

        CreateCategoryCommand = new AsyncCommand(() => SafeExecuteAsync(CreateCategoryAsync, "Crear categoría"));
        UpdateCategoryCommand = new AsyncCommand(() => SafeExecuteAsync(UpdateCategoryAsync, "Actualizar categoría"));
        DeleteCategoryCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteCategoryAsync, "Eliminar categoría"));

        CreateSyncLogCommand = new AsyncCommand(() => SafeExecuteAsync(CreateSyncLogAsync, "Crear log"));
        UpdateSyncLogCommand = new AsyncCommand(() => SafeExecuteAsync(UpdateSyncLogAsync, "Actualizar log"));
        DeleteSyncLogCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteSyncLogAsync, "Eliminar log"));

        CreateReportCommand = new AsyncCommand(() => SafeExecuteAsync(CreateReportAsync, "Crear reporte"));
        UpdateReportCommand = new AsyncCommand(() => SafeExecuteAsync(UpdateReportAsync, "Actualizar reporte"));
        DeleteReportCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteReportAsync, "Eliminar reporte"));

        RunScrapingCommand = new AsyncCommand(() => SafeExecuteAsync(RunScrapingAsync, "Ejecutar scraping"), () => SelectedSite != null);
        SendSelectedToSaeCommand = new AsyncCommand(() => SafeExecuteAsync(SendSelectedToSaeAsync, "Enviar seleccionado a SAE"), () => SelectedStagingProduct != null);
        SendPendingToSaeCommand = new AsyncCommand(() => SafeExecuteAsync(SendPendingToSaeAsync, "Enviar pendientes a SAE"));

        LoadSettingsCommand = new AsyncCommand(() => SafeExecuteAsync(LoadSettingsAsync, "Cargar configuración"));
        SaveSettingsCommand = new AsyncCommand(() => SafeExecuteAsync(SaveSettingsAsync, "Guardar configuración"));
        RunDiagnosticsCommand = new AsyncCommand(() => SafeExecuteAsync(RunDiagnosticsAsync, "Ejecutar diagnóstico"));
        TestBackendCommand = new AsyncCommand(() => SafeExecuteAsync(TestBackendAsync, "Probar backend"));
        ExitCommand = new RelayCommand(() => Application.Current.Shutdown());
        RefreshLogsCommand = new AsyncCommand(() => SafeExecuteAsync(RefreshLogsAsync, "Refrescar logs"));
        RefreshAppLogsCommand = new AsyncCommand(() => SafeExecuteAsync(RefreshAppLogsAsync, "Refrescar logs app"));
        PauseScrapingCommand = new AsyncCommand(() => SafeExecuteAsync(PauseScrapingAsync, "Pausar scraping"), () => SelectedSite != null);
        ResumeScrapingCommand = new AsyncCommand(() => SafeExecuteAsync(ResumeScrapingAsync, "Reanudar scraping"), () => SelectedSite != null);
        StopScrapingCommand = new AsyncCommand(() => SafeExecuteAsync(StopScrapingAsync, "Detener scraping"), () => SelectedSite != null);
        AnalyzeSelectorsCommand = new AsyncCommand(() => SafeExecuteAsync(AnalyzeSelectorsAsync, "Analizar selectores"));
        InspectUrlsCommand = new AsyncCommand(() => SafeExecuteAsync(InspectUrlsAsync, "Inspeccionar URLs"), () => SelectedSite != null);
        LoadLearnedUrlsCommand = new AsyncCommand(() => SafeExecuteAsync(LoadLearnedUrlsAsync, "Cargar URLs"), () => SelectedSite != null);
        SaveLearnedUrlsCommand = new AsyncCommand(() => SafeExecuteAsync(SaveLearnedUrlsAsync, "Guardar URLs"), () => SelectedSite != null);
        ConfirmLoginCommand = new AsyncCommand(() => SafeExecuteAsync(ConfirmLoginAsync, "Confirmar Login"), () => SelectedSite != null);

        _logTimer.Start();
        _statusTimer.Start();
        _liveLogTimer.Start();

        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitApplicationCommand = new RelayCommand(ExitApplication);
        NavigateCommand = new RelayCommand<string>(NavigateToTab);
    }

    private void ShowWindow()
    {
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Show();
            window.Activate();
        }
    }

    private void ExitApplication()
    {
        Application.Current.Shutdown();
    }

    private void NavigateToTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out int index))
        {
            SelectedTabIndex = index;
            ShowWindow();
        }
    }


    public ObservableCollection<SiteProfile> Sites { get; } = new();
    public ObservableCollection<StagingProductUi> StagingProducts { get; } = new();
    public ObservableCollection<CategoryMapping> CategoryMappings { get; } = new();
    public ObservableCollection<SyncLog> SyncLogs { get; } = new();
    public ObservableCollection<SyncLog> RecentSyncLogs { get; } = new();
    public ObservableCollection<ExecutionReport> ExecutionReports { get; } = new();
    public ObservableCollection<string> AppLogs { get; } = new();
    public ObservableCollection<string> LiveLogs { get; } = new();
    public string AppLogPath => AppLogger.GetLogPath();


    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public SiteProfile? SelectedSite
    {
        get => _selectedSite;
        set
        {
            if (SetField(ref _selectedSite, value))
            {
                ((AsyncCommand)RunScrapingCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)PauseScrapingCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ResumeScrapingCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)StopScrapingCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ConfirmLoginCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)InspectUrlsCommand).RaiseCanExecuteChanged();
                UpdateRecentSyncLogs();
                _ = SafeExecuteAsync(RefreshScrapeStatusAsync, "Estado scraping");
            }
        }
    }

    public StagingProductUi? SelectedStagingProduct
    {
        get => _selectedStagingProduct;
        set
        {
            if (SetField(ref _selectedStagingProduct, value))
            {
                ((AsyncCommand)SendSelectedToSaeCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public CategoryMapping? SelectedCategoryMapping
    {
        get => _selectedCategoryMapping;
        set => SetField(ref _selectedCategoryMapping, value);
    }

    public SyncLog? SelectedSyncLog
    {
        get => _selectedSyncLog;
        set => SetField(ref _selectedSyncLog, value);
    }

    public ExecutionReport? SelectedExecutionReport
    {
        get => _selectedExecutionReport;
        set => SetField(ref _selectedExecutionReport, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ScrapeRunResult? ScrapeResult
    {
        get => _scrapeResult;
        set => SetField(ref _scrapeResult, value);
    }

    public string SupabaseUrl
    {
        get => _supabaseUrl;
        set => SetField(ref _supabaseUrl, value);
    }

    public string SupabaseServiceKey
    {
        get => _supabaseServiceKey;
        set => SetField(ref _supabaseServiceKey, value);
    }

    public string SaeSdkPath
    {
        get => _saeSdkPath;
        set => SetField(ref _saeSdkPath, value);
    }

    public string SaeUser
    {
        get => _saeUser;
        set => SetField(ref _saeUser, value);
    }

    public string SaePassword
    {
        get => _saePassword;
        set => SetField(ref _saePassword, value);
    }

    public string SaeDbHost
    {
        get => _saeDbHost;
        set => SetField(ref _saeDbHost, value);
    }

    public string SaeDbPath
    {
        get => _saeDbPath;
        set => SetField(ref _saeDbPath, value);
    }

    public string SaeDbUser
    {
        get => _saeDbUser;
        set => SetField(ref _saeDbUser, value);
    }

    public string SaeDbPassword
    {
        get => _saeDbPassword;
        set => SetField(ref _saeDbPassword, value);
    }

    public int SaeDbPort
    {
        get => _saeDbPort;
        set => SetField(ref _saeDbPort, value);
    }

    public string SaeDbCharset
    {
        get => _saeDbCharset;
        set => SetField(ref _saeDbCharset, value);
    }

    public int SaeDbDialect
    {
        get => _saeDbDialect;
        set => SetField(ref _saeDbDialect, value);
    }

    public string SaeDefaultLineCode
    {
        get => _saeDefaultLineCode;
        set => SetField(ref _saeDefaultLineCode, value);
    }

    public string BackendStatus
    {
        get => _backendStatus;
        set => SetField(ref _backendStatus, value);
    }

    public string SupabaseStatus
    {
        get => _supabaseStatus;
        set => SetField(ref _supabaseStatus, value);
    }

    public string SaeStatus
    {
        get => _saeStatus;
        set => SetField(ref _saeStatus, value);
    }

    public string DatabaseStatus
    {
        get => _databaseStatus;
        set => SetField(ref _databaseStatus, value);
    }

    public int? SupabaseSampleCount
    {
        get => _supabaseSampleCount;
        set => SetField(ref _supabaseSampleCount, value);
    }

    public DiagnosticsResult? DiagnosticsResult
    {
        get => _diagnosticsResult;
        set => SetField(ref _diagnosticsResult, value);
    }

    public bool HasSites
    {
        get => _hasSites;
        private set
        {
            if (SetField(ref _hasSites, value))
            {
                OnPropertyChanged(nameof(NoSites));
            }
        }
    }

    public bool NoSites => !HasSites;

    public bool ManualLoginEnabled
    {
        get => _manualLoginEnabled;
        set
        {
            if (SetField(ref _manualLoginEnabled, value) && value)
            {
                HeadlessEnabled = false;
            }
        }
    }

    public bool HeadlessEnabled
    {
        get => _headlessEnabled;
        set => SetField(ref _headlessEnabled, value);
    }

    public string ScrapingMode
    {
        get => _scrapingMode;
        set
        {
            if (SetField(ref _scrapingMode, value))
            {
                IsFamiliesMode = value == "Familias (Festo)";
            }
        }
    }

    public bool IsFamiliesMode
    {
        get => _isFamiliesMode;
        set => SetField(ref _isFamiliesMode, value);
    }

    public bool IsScraping
    {
        get => _isScraping;
        set
        {
            if (SetField(ref _isScraping, value))
            {
                if (value)
                {
                    // Iniciar scraping - limpiar y activar timers
                    LiveLogs.Clear();
                    _lastLogTimestamp = DateTime.UtcNow.AddSeconds(-5);
                    if (!_logTimer.IsEnabled)
                    {
                        _logTimer.Start();
                    }
                    if (!_statusTimer.IsEnabled)
                    {
                        _statusTimer.Start();
                    }
                    if (!_liveLogTimer.IsEnabled)
                    {
                        _liveLogTimer.Start();
                    }
                }
                else
                {
                    _logTimer.Stop();
                    _statusTimer.Stop();
                    _liveLogTimer.Stop();
                }
            }
        }
    }


    public string ScrapeStatusText
    {
        get => _scrapeStatusText;
        set => SetField(ref _scrapeStatusText, value);
    }

    public string SelectorAnalysisResult
    {
        get => _selectorAnalysisResult;
        set => SetField(ref _selectorAnalysisResult, value);
    }

    public bool KeepBrowserOpen
    {
        get => _keepBrowserOpen;
        set => SetField(ref _keepBrowserOpen, value);
    }

    public bool UseScreenshotFallback
    {
        get => _useScreenshotFallback;
        set => SetField(ref _useScreenshotFallback, value);
    }

    public string LearnedUrlsText
    {
        get => _learnedUrlsText;
        set => SetField(ref _learnedUrlsText, value);
    }


    public bool SaeScheduleEnabled
    {
        get => _saeScheduleEnabled;
        set
        {
            if (SetField(ref _saeScheduleEnabled, value))
            {
                UpdateSaeTimer();
            }
        }
    }

    public int SaeScheduleMinutes
    {
        get => _saeScheduleMinutes;
        set
        {
            if (SetField(ref _saeScheduleMinutes, value))
            {
                _saeTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, _saeScheduleMinutes));
            }
        }
    }

    public AsyncCommand LoadAllCommand { get; }
    public AsyncCommand CreateSiteCommand { get; }
    public AsyncCommand UpdateSiteCommand { get; }
    public AsyncCommand DeleteSiteCommand { get; }

    public AsyncCommand CreateStagingCommand { get; }
    public AsyncCommand UpdateStagingCommand { get; }
    public AsyncCommand DeleteStagingCommand { get; }

    public AsyncCommand CreateCategoryCommand { get; }
    public AsyncCommand UpdateCategoryCommand { get; }
    public AsyncCommand DeleteCategoryCommand { get; }

    public AsyncCommand CreateSyncLogCommand { get; }
    public AsyncCommand UpdateSyncLogCommand { get; }
    public AsyncCommand DeleteSyncLogCommand { get; }

    public AsyncCommand CreateReportCommand { get; }
    public AsyncCommand UpdateReportCommand { get; }
    public AsyncCommand DeleteReportCommand { get; }

    public AsyncCommand RunScrapingCommand { get; }
    public AsyncCommand SendSelectedToSaeCommand { get; }
    public AsyncCommand SendPendingToSaeCommand { get; }
    public AsyncCommand LoadSettingsCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand RunDiagnosticsCommand { get; }
    public AsyncCommand TestBackendCommand { get; }
    public RelayCommand ExitCommand { get; }
    public AsyncCommand RefreshLogsCommand { get; }
    public AsyncCommand RefreshAppLogsCommand { get; }
    public AsyncCommand PauseScrapingCommand { get; }
    public AsyncCommand ResumeScrapingCommand { get; }
    public AsyncCommand StopScrapingCommand { get; }
    public AsyncCommand AnalyzeSelectorsCommand { get; }
    public AsyncCommand InspectUrlsCommand { get; }
    public AsyncCommand LoadLearnedUrlsCommand { get; }
    public AsyncCommand SaveLearnedUrlsCommand { get; }
    public AsyncCommand ConfirmLoginCommand { get; }
    
    public RelayCommand ShowWindowCommand { get; }
    public RelayCommand ExitApplicationCommand { get; }
    public RelayCommand<string> NavigateCommand { get; }

    public async Task LoadAllAsync()

    {
        try
        {
            StatusMessage = "Cargando datos...";
            Sites.Clear();
            foreach (var site in await _apiClient.GetSitesAsync())
            {
                Sites.Add(site);
            }
            HasSites = Sites.Count > 0;
            AppLogger.Info($"Sites loaded: {Sites.Count}");

            StagingProducts.Clear();
            foreach (var item in await _apiClient.GetStagingProductsAsync())
            {
                StagingProducts.Add(new StagingProductUi(item));
            }

            CategoryMappings.Clear();
            foreach (var item in await _apiClient.GetCategoryMappingsAsync())
            {
                CategoryMappings.Add(item);
            }

            SyncLogs.Clear();
            foreach (var item in await _apiClient.GetSyncLogsAsync())
            {
                SyncLogs.Add(item);
            }
            UpdateRecentSyncLogs();

            ExecutionReports.Clear();
            foreach (var item in await _apiClient.GetExecutionReportsAsync())
            {
                ExecutionReports.Add(item);
            }

            StatusMessage = "Listo";
            await RefreshAppLogsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar datos: {ex.Message}";
            HasSites = false;
            AppLogger.Error("LoadAllAsync failed.", ex);
        }
    }

    private async Task CreateSiteAsync()
    {
        AppLogger.Info("CreateSite clicked.");
        var site = SelectedSite ?? new SiteProfile { Name = "Nuevo", BaseUrl = "https://", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var created = await _apiClient.CreateSiteAsync(site);
        if (created != null)
        {
            Sites.Add(created);
            SelectedSite = created;
            HasSites = Sites.Count > 0;
            AppLogger.Info($"Site created: {created.Name} ({created.Id}).");
        }
    }

    private async Task UpdateSiteAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateSiteAsync(SelectedSite.Id, SelectedSite);
        if (updated != null)
        {
            StatusMessage = "Proveedor actualizado.";
        }
    }

    private async Task DeleteSiteAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        await _apiClient.DeleteSiteAsync(SelectedSite.Id);
        Sites.Remove(SelectedSite);
        SelectedSite = null;
        HasSites = Sites.Count > 0;
    }

    private async Task CreateStagingAsync()
    {
        var product = SelectedStagingProduct?.Product ?? new StagingProduct
        {
            SiteId = SelectedSite?.Id ?? Guid.Empty,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var created = await _apiClient.CreateStagingProductAsync(product);
        if (created != null)
        {
            var uiModel = new StagingProductUi(created);
            StagingProducts.Add(uiModel);
            SelectedStagingProduct = uiModel;
        }
    }

    private async Task UpdateStagingAsync()
    {
        if (SelectedStagingProduct == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateStagingProductAsync(SelectedStagingProduct.Product.Id, SelectedStagingProduct.Product);
        if (updated != null)
        {
            StatusMessage = "Producto staging actualizado.";
        }
    }

    private async Task DeleteStagingAsync()
    {
        if (SelectedStagingProduct == null)
        {
            return;
        }

        await _apiClient.DeleteStagingProductAsync(SelectedStagingProduct.Product.Id);
        StagingProducts.Remove(SelectedStagingProduct);
        SelectedStagingProduct = null;
    }

    private async Task CreateCategoryAsync()
    {
        var mapping = SelectedCategoryMapping ?? new CategoryMapping { SaeLineCode = "LINEA", CreatedAt = DateTime.UtcNow };
        var created = await _apiClient.CreateCategoryMappingAsync(mapping);
        if (created != null)
        {
            CategoryMappings.Add(created);
            SelectedCategoryMapping = created;
        }
    }

    private async Task UpdateCategoryAsync()
    {
        if (SelectedCategoryMapping == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateCategoryMappingAsync(SelectedCategoryMapping.Id, SelectedCategoryMapping);
        if (updated != null)
        {
            StatusMessage = "Mapeo actualizado.";
        }
    }

    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategoryMapping == null)
        {
            return;
        }

        await _apiClient.DeleteCategoryMappingAsync(SelectedCategoryMapping.Id);
        CategoryMappings.Remove(SelectedCategoryMapping);
        SelectedCategoryMapping = null;
    }

    private async Task CreateSyncLogAsync()
    {
        var log = SelectedSyncLog ?? new SyncLog { OperationType = "manual", Status = "success", CreatedAt = DateTime.UtcNow };
        var created = await _apiClient.CreateSyncLogAsync(log);
        if (created != null)
        {
            SyncLogs.Add(created);
            SelectedSyncLog = created;
        }
    }

    private async Task UpdateSyncLogAsync()
    {
        if (SelectedSyncLog == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateSyncLogAsync(SelectedSyncLog.Id, SelectedSyncLog);
        if (updated != null)
        {
            StatusMessage = "Log actualizado.";
        }
    }

    private async Task DeleteSyncLogAsync()
    {
        if (SelectedSyncLog == null)
        {
            return;
        }

        await _apiClient.DeleteSyncLogAsync(SelectedSyncLog.Id);
        SyncLogs.Remove(SelectedSyncLog);
        SelectedSyncLog = null;
    }

    private async Task CreateReportAsync()
    {
        var report = SelectedExecutionReport ?? new ExecutionReport { ExecutionDate = DateTime.UtcNow.Date, CreatedAt = DateTime.UtcNow };
        var created = await _apiClient.CreateExecutionReportAsync(report);
        if (created != null)
        {
            ExecutionReports.Add(created);
            SelectedExecutionReport = created;
        }
    }

    private async Task UpdateReportAsync()
    {
        if (SelectedExecutionReport == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateExecutionReportAsync(SelectedExecutionReport.Id, SelectedExecutionReport);
        if (updated != null)
        {
            StatusMessage = "Reporte actualizado.";
        }
    }

    private async Task DeleteReportAsync()
    {
        if (SelectedExecutionReport == null)
        {
            return;
        }

        await _apiClient.DeleteExecutionReportAsync(SelectedExecutionReport.Id);
        ExecutionReports.Remove(SelectedExecutionReport);
        SelectedExecutionReport = null;
    }

    private async Task RunScrapingAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        StatusMessage = "Ejecutando scraping...";
        IsScraping = true;
        _lastLogTimestamp = DateTime.UtcNow.AddSeconds(-2);
        LiveLogs.Add($"[{DateTime.Now:HH:mm:ss}] → Iniciando sesión de scraping...");
        try
        {
            await RefreshLogsAsync();
            await RefreshScrapeStatusAsync();
            ScrapeResult = await _apiClient.RunScrapingAsync(
                SelectedSite.Id, 
                ManualLoginEnabled, 
                HeadlessEnabled,
                KeepBrowserOpen,
                UseScreenshotFallback,
                ScrapingMode);
            await RefreshLogsAsync();
            await RefreshScrapeStatusAsync();
        }
        finally
        {
            IsScraping = false;
        }
        StatusMessage = "Scraping finalizado.";
        await LoadAllAsync();
    }


    private async Task PauseScrapingAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        await _apiClient.PauseScrapingAsync(SelectedSite.Id);
        await RefreshScrapeStatusAsync();
    }

    private async Task ResumeScrapingAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        await _apiClient.ResumeScrapingAsync(SelectedSite.Id);
        await RefreshScrapeStatusAsync();
    }

    private async Task StopScrapingAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        await _apiClient.StopScrapingAsync(SelectedSite.Id);
        await RefreshScrapeStatusAsync();
        IsScraping = false;
    }

    private async Task ConfirmLoginAsync()
    {
        if (SelectedSite == null) return;
        
        await _apiClient.ConfirmLoginAsync(SelectedSite.Id);
        StatusMessage = "Login confirmado. Scraping debería continuar.";
        AppLogger.Info($"Login confirmed for site {SelectedSite.Name}");
    }

    private async Task InspectUrlsAsync()
    {
        if (SelectedSite == null || string.IsNullOrWhiteSpace(LearnedUrlsText)) return;
        
        var urls = LearnedUrlsText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(u => u.Trim())
                                  .Where(u => u.StartsWith("http"))
                                  .ToList();
                                  
        if (urls.Count == 0)
        {
            StatusMessage = "No hay URLs válidas para inspeccionar.";
            return;
        }
        
        StatusMessage = $"Inspeccionando {urls.Count} URLs...";
        IsScraping = true;
        _lastLogTimestamp = DateTime.UtcNow.AddSeconds(-2);
        LiveLogs.Add($"[{DateTime.Now:HH:mm:ss}] → Iniciando inspección de {urls.Count} URLs...");
        try
        {
            var results = await _apiClient.InspectUrlsAsync(SelectedSite.Id, urls);
            
            if (results != null)
            {
                StatusMessage = $"Inspección completada. Extraídos {results.Count(r => r.Success)} de {urls.Count}.";
                await RefreshStagingProductsAsync();
                await LoadLearnedUrlsAsync();
            }
        }
        finally
        {
            IsScraping = false;
        }
    }

    private async Task SendSelectedToSaeAsync()
    {
        if (SelectedStagingProduct == null)
        {
            return;
        }

        var ok = await _apiClient.SendToSaeAsync(SelectedStagingProduct.Product.Id);
        StatusMessage = ok ? "Envío a SAE realizado." : "SAE SDK no configurado.";
        await LoadAllAsync();
    }

    private async Task SendPendingToSaeAsync()
    {
        var summary = await _apiClient.SendPendingToSaeAsync();
        if (summary != null)
        {
            StatusMessage = $"SAE: enviados {summary.Sent} de {summary.Total}.";
        }

        await LoadAllAsync();
    }

    private void UpdateSaeTimer()
    {
        if (_saeScheduleEnabled)
        {
            _saeTimer.Start();
        }
        else
        {
            _saeTimer.Stop();
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _apiClient.GetSettingsAsync();
            if (settings == null)
            {
                StatusMessage = "No hay configuración guardada.";
                return;
            }

            SupabaseUrl = settings.SupabaseUrl ?? string.Empty;
            SupabaseServiceKey = settings.SupabaseServiceKey ?? string.Empty;
            SaeSdkPath = settings.SaeSdkPath ?? string.Empty;
            SaeUser = settings.SaeUser ?? string.Empty;
            SaePassword = settings.SaePassword ?? string.Empty;
            SaeDbHost = settings.SaeDbHost ?? string.Empty;
            SaeDbPath = settings.SaeDbPath ?? string.Empty;
            SaeDbUser = settings.SaeDbUser ?? string.Empty;
            SaeDbPassword = settings.SaeDbPassword ?? string.Empty;
            SaeDbPort = settings.SaeDbPort ?? 3050;
            SaeDbCharset = settings.SaeDbCharset ?? "ISO8859_1";
            SaeDbDialect = settings.SaeDbDialect ?? 3;
            SaeDefaultLineCode = settings.SaeDefaultLineCode ?? "LINEA";
            StatusMessage = "Configuración cargada.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar configuración: {ex.Message}";
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AppSettingsDto
            {
                SupabaseUrl = SupabaseUrl,
                SupabaseServiceKey = SupabaseServiceKey,
                SaeSdkPath = SaeSdkPath,
                SaeUser = SaeUser,
                SaePassword = SaePassword,
                SaeDbHost = SaeDbHost,
                SaeDbPath = SaeDbPath,
                SaeDbUser = SaeDbUser,
                SaeDbPassword = SaeDbPassword,
                SaeDbPort = SaeDbPort,
                SaeDbCharset = SaeDbCharset,
                SaeDbDialect = SaeDbDialect,
                SaeDefaultLineCode = SaeDefaultLineCode
            };

            await _apiClient.SaveSettingsAsync(settings);
            StatusMessage = "Configuración guardada. Reinicia el backend si estaba corriendo.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar configuración: {ex.Message}";
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        try
        {
            DiagnosticsResult = await _apiClient.GetDiagnosticsAsync();
            if (DiagnosticsResult == null)
            {
                StatusMessage = "No se pudo obtener diagnóstico.";
                return;
            }

            BackendStatus = DiagnosticsResult.BackendOk ? "OK" : "Error";
            SupabaseStatus = DiagnosticsResult.SupabaseOk ? "OK" : "Error";
            SaeStatus = DiagnosticsResult.SaeSdkOk ? "OK" : "Error";
            SupabaseSampleCount = DiagnosticsResult.SupabaseSampleCount;
            DatabaseStatus = DiagnosticsResult.SupabaseOk
                ? $"OK ({SupabaseSampleCount ?? 0} registros leídos)"
                : "Error";

            StatusMessage = "Diagnóstico completado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al diagnosticar: {ex.Message}";
        }
    }

    private async Task TestBackendAsync()
    {
        try
        {
            var ok = await _apiClient.TestBackendAsync();
            BackendStatus = ok ? "OK" : "Error";
            StatusMessage = ok ? "Backend accesible." : "Backend no responde.";
        }
        catch (Exception ex)
        {
            BackendStatus = "Error";
            StatusMessage = $"Error al conectar backend: {ex.Message}";
        }
    }

    private async Task RefreshLogsAsync()
    {
        try
        {
            var logs = await _apiClient.GetSyncLogsAsync();
            SyncLogs.Clear();
            foreach (var item in logs)
            {
                SyncLogs.Add(item);
            }
            UpdateRecentSyncLogs();
            await RefreshAppLogsAsync();
        }
        catch
        {
            // Ignore log refresh errors to avoid UI flicker.
        }
    }

    private async Task RefreshScrapeStatusAsync()
    {
        try
        {
            if (SelectedSite == null)
            {
                ScrapeStatusText = "Idle";
                IsScraping = false;
                return;
            }

            var status = await _apiClient.GetScrapeStatusAsync(SelectedSite.Id);
            if (status == null)
            {
                ScrapeStatusText = "Idle";
                IsScraping = false;
                return;
            }

            ScrapeStatusText = $"{status.State} - {status.Message}";
            IsScraping = status.State == ScrapSAE.Core.Interfaces.ScrapeRunState.Running ||
                         status.State == ScrapSAE.Core.Interfaces.ScrapeRunState.Paused;
        }
        catch
        {
            // Ignore status errors.
        }
    }

    private async Task AnalyzeSelectorsAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona capturas para análisis",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var images = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            var bytes = await File.ReadAllBytesAsync(file);
            images.Add(Convert.ToBase64String(bytes));
        }

        var request = new SelectorAnalysisRequest
        {
            Url = SelectedSite?.BaseUrl,
            HtmlSnippet = SelectedStagingProduct?.Product.RawData,
            ImagesBase64 = images,
            Notes = "Identificar prefijos de clase y selectores robustos."
        };

        var result = await _apiClient.AnalyzeSelectorsAsync(request);
        if (result != null)
        {
            SelectorAnalysisResult = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            AppLogger.Info($"Selector analysis result: {SelectorAnalysisResult}");
            await ApplySelectorSuggestionAsync(result);
        }
    }

    private async Task ApplySelectorSuggestionAsync(SelectorSuggestion suggestion)
    {
        if (SelectedSite == null)
        {
            return;
        }

        var json = SelectedSite.Selectors switch
        {
            JsonElement element => element.GetRawText(),
            string text => text,
            _ => "{}"
        };

        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(suggestion.ProductListClassPrefix)) dict["productListClassPrefix"] = suggestion.ProductListClassPrefix!;
        if (!string.IsNullOrWhiteSpace(suggestion.ProductCardClassPrefix)) dict["productCardClassPrefix"] = suggestion.ProductCardClassPrefix!;
        if (!string.IsNullOrWhiteSpace(suggestion.DetailButtonText)) dict["detailButtonText"] = suggestion.DetailButtonText!;
        if (!string.IsNullOrWhiteSpace(suggestion.DetailButtonClassPrefix)) dict["detailButtonClassPrefix"] = suggestion.DetailButtonClassPrefix!;
        if (!string.IsNullOrWhiteSpace(suggestion.TitleSelector)) dict["titleSelector"] = suggestion.TitleSelector!;
        if (!string.IsNullOrWhiteSpace(suggestion.PriceSelector)) dict["priceSelector"] = suggestion.PriceSelector!;
        if (!string.IsNullOrWhiteSpace(suggestion.SkuSelector)) dict["skuSelector"] = suggestion.SkuSelector!;
        if (!string.IsNullOrWhiteSpace(suggestion.ImageSelector)) dict["imageSelector"] = suggestion.ImageSelector!;
        if (!string.IsNullOrWhiteSpace(suggestion.NextPageSelector)) dict["nextPageSelector"] = suggestion.NextPageSelector!;

        SelectedSite.Selectors = JsonSerializer.Serialize(dict);
        await UpdateSiteAsync();
    }

    private async Task RefreshAppLogsAsync()
    {
        try
        {
            var lines = await AppLogger.ReadLatestAsync(400);
            AppLogs.Clear();
            foreach (var line in lines)
            {
                AppLogs.Add(line);
            }
        }
        catch
        {
            // Ignore app log refresh errors.
        }
    }

    private void UpdateRecentSyncLogs()
    {
        RecentSyncLogs.Clear();
        var logs = SyncLogs.AsEnumerable()
            .Where(log =>
                string.Equals(log.OperationType, "scrape", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(log.OperationType, "scrape-step", StringComparison.OrdinalIgnoreCase));
        if (SelectedSite != null)
        {
            logs = logs.Where(log => log.SiteId == SelectedSite.Id);
        }

        foreach (var log in logs
            .OrderByDescending(log => log.CreatedAt)
            .Take(50))
        {
            RecentSyncLogs.Add(log);
        }
    }

    private async Task SafeExecuteAsync(Func<Task> action, string operationName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error en {operationName}: {ex.Message}";
            AppLogger.Error($"Operation failed: {operationName}", ex);
        }
    }

    private async Task RefreshStagingProductsAsync()
    {
        var products = await _apiClient.GetStagingProductsAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            StagingProducts.Clear();
            foreach (var item in products)
            {
                StagingProducts.Add(new StagingProductUi(item));
            }
        });
    }

    private async Task RefreshLiveLogsAsync()
    {
        if (SelectedSite == null) return;
        
        var recentlyActive = (DateTime.UtcNow - _lastLogTimestamp).TotalSeconds < 15;
        if (!IsScraping && !recentlyActive) return;
        
        try
        {
            var logs = await _apiClient.GetSyncLogsAsync();
            var recentLogs = logs
                .Where(l => l.SiteId == SelectedSite.Id && l.CreatedAt > _lastLogTimestamp.AddSeconds(-30))
                .OrderBy(l => l.CreatedAt)
                .ToList();
            
            foreach (var log in recentLogs)
            {
                var timestamp = log.CreatedAt.ToString("HH:mm:ss");
                var statusIcon = log.Status switch
                {
                    "success" => "✓",
                    "error" => "✗",
                    "warning" => "⚠",
                    _ => "→"
                };
                var logLine = $"[{timestamp}] {statusIcon} {log.Message}";
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LiveLogs.Add(logLine);
                    // Limitar a 200 entradas
                    while (LiveLogs.Count > 200)
                        LiveLogs.RemoveAt(0);
                });
                
                if (log.CreatedAt > _lastLogTimestamp)
                    _lastLogTimestamp = log.CreatedAt;
            }
        }
        catch
        {
            // Ignorar errores de refresh silenciosamente
        }
    }

    private async Task LoadLearnedUrlsAsync()
    {
        if (SelectedSite == null) return;
        
        try
        {
            var patterns = await _apiClient.GetLearnedPatternsAsync(SelectedSite.Id);
            if (patterns != null)
            {
                var urls = new List<string>();
                if (patterns.ExampleProductUrls != null)
                    urls.AddRange(patterns.ExampleProductUrls);
                if (patterns.ExampleListingUrls != null)
                    urls.AddRange(patterns.ExampleListingUrls);
                
                LearnedUrlsText = string.Join("\n", urls);
            }
        }
        catch
        {
            LearnedUrlsText = "Error al cargar URLs";
        }
    }

    private async Task SaveLearnedUrlsAsync()
    {
        if (SelectedSite == null || string.IsNullOrWhiteSpace(LearnedUrlsText)) return;
        
        try
        {
            var urls = LearnedUrlsText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => !string.IsNullOrEmpty(u))
                .ToList();
            
            await _apiClient.LearnUrlsAsync(SelectedSite.Id, urls);
            StatusMessage = $"Guardadas {urls.Count} URLs de ejemplo";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error guardando URLs: {ex.Message}";
        }
    }
}

