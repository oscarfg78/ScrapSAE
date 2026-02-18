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
    private string _targetSystem = "Flashly";
    private string _onlineStoreName = string.Empty;
    private string _onlineStoreBaseUrl = string.Empty;
    private string _onlineStoreApiKey = string.Empty;
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
    
    private string _searchText = string.Empty;
    private bool _isSendProgressVisible;
    private bool _isSendProgressCompleted;
    private string _sendProgressTitle = "Envio a SAE";
    private string _sendProgressStatus = string.Empty;
    private string _sendProgressText = string.Empty;
    private double _sendProgressValue;
    private double _sendProgressMaximum = 1;
    private bool _showApartadosOnly;
    private bool _rescrapeManualLoginEnabled;
    private bool _showRescrapeConfirmLoginButton;
    private bool _showRescrapeControlButtons;
    private Guid? _currentRescrapeJobId;
    private string _currentRescrapeJobStatus = string.Empty;
    private HashSet<Guid> _currentRescrapeSiteIds = new();
    private string? _sendProgressLogFilePath;
    public System.ComponentModel.ICollectionView StagingProductsView { get; private set; }

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
        SendCheckedToSaeCommand = new AsyncCommand(() => SafeExecuteAsync(SendCheckedToSaeAsync, "Enviar seleccionados a SAE"), () => SelectedForSaeCount > 0);
        SendPendingToSaeCommand = new AsyncCommand(() => SafeExecuteAsync(SendPendingToSaeAsync, "Enviar pendientes a SAE"));
        SendPendingToOnlineStoreCommand = new AsyncCommand(() => SafeExecuteAsync(SendPendingToOnlineStoreAsync, "Enviar pendientes a tienda en línea"));
        SendSelectedToOnlineStoreCommand = new AsyncCommand(() => SafeExecuteAsync(SendSelectedToOnlineStoreAsync, "Enviar seleccionados a tienda en línea"));
        RescrapeSelectedOnlineStoreCommand = new AsyncCommand(() => SafeExecuteAsync(RescrapeSelectedOnlineStoreAsync, "Rescrapear seleccionados de tienda en línea"));
        SaveSelectedOnlineStoreRecordCommand = new AsyncCommand(() => SafeExecuteAsync(SaveSelectedOnlineStoreRecordAsync, "Guardar cambios en registro"));
        DeleteSelectedOnlineStoreRecordsCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteSelectedOnlineStoreRecordsAsync, "Eliminar registros seleccionados"));
        MarkSelectedAsApartadoCommand = new AsyncCommand(() => SafeExecuteAsync(MarkSelectedAsApartadoAsync, "Marcar apartados"));
        UnmarkSelectedAsApartadoCommand = new AsyncCommand(() => SafeExecuteAsync(UnmarkSelectedAsApartadoAsync, "Quitar apartado"));

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
        ConfirmRescrapeLoginCommand = new AsyncCommand(() => SafeExecuteAsync(ConfirmRescrapeLoginAsync, "Confirmar Login Manual (Rescrape)"));
        PauseRescrapeCommand = new AsyncCommand(() => SafeExecuteAsync(PauseRescrapeAsync, "Pausar Rescrape"), CanPauseRescrape);
        ResumeRescrapeCommand = new AsyncCommand(() => SafeExecuteAsync(ResumeRescrapeAsync, "Reanudar Rescrape"), CanResumeRescrape);
        CancelRescrapeCommand = new AsyncCommand(() => SafeExecuteAsync(CancelRescrapeAsync, "Cancelar Rescrape"), CanCancelRescrape);

        _logTimer.Start();
        _statusTimer.Start();
        _liveLogTimer.Start();

        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitApplicationCommand = new RelayCommand(ExitApplication);
        NavigateCommand = new RelayCommand<string>(NavigateToTab);
        CloseSendProgressCommand = new RelayCommand(CloseSendProgressModal);
        ShowSendProgressCommand = new RelayCommand(ShowSendProgressModal);
        CopySendProgressCommand = new RelayCommand(CopySendProgressToClipboard);
        
        // Initialize Collection View for filtering
        StagingProductsView = System.Windows.Data.CollectionViewSource.GetDefaultView(StagingProducts);
        StagingProductsView.Filter = FilterStagingProducts;

        PerformSearchCommand = new RelayCommand<string>(PerformSearch);
    }
    
    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public bool IsSendProgressVisible
    {
        get => _isSendProgressVisible;
        set
        {
            if (SetField(ref _isSendProgressVisible, value))
            {
                OnPropertyChanged(nameof(ShowOpenRescrapeProgressButton));
            }
        }
    }

    public bool IsSendProgressCompleted
    {
        get => _isSendProgressCompleted;
        set => SetField(ref _isSendProgressCompleted, value);
    }

    public string SendProgressTitle
    {
        get => _sendProgressTitle;
        set => SetField(ref _sendProgressTitle, value);
    }

    public string SendProgressStatus
    {
        get => _sendProgressStatus;
        set => SetField(ref _sendProgressStatus, value);
    }

    public string SendProgressText
    {
        get => _sendProgressText;
        set => SetField(ref _sendProgressText, value);
    }

    public double SendProgressValue
    {
        get => _sendProgressValue;
        set => SetField(ref _sendProgressValue, value);
    }

    public double SendProgressMaximum
    {
        get => _sendProgressMaximum;
        set => SetField(ref _sendProgressMaximum, value);
    }

    public RelayCommand<string> PerformSearchCommand { get; }

    public int SelectedForSaeCount => StagingProducts.Count(p => p.IsSelected);
    public int OnlineStorePendingCount => StagingProducts.Count(IsPendingForOnlineStore);
    public int OnlineStoreApartadosCount => StagingProducts.Count(p => p.IsApartado);
    public int OnlineStoreSelectedCount => OnlineStoreProducts.Count(p => p.IsSelected);
    public IEnumerable<StagingProductUi> OnlineStoreProducts => StagingProducts.Where(FilterOnlineStoreProducts);

    public bool ShowApartadosOnly
    {
        get => _showApartadosOnly;
        set
        {
            if (SetField(ref _showApartadosOnly, value))
            {
                OnPropertyChanged(nameof(OnlineStoreProducts));
                OnPropertyChanged(nameof(OnlineStoreSelectedCount));
            }
        }
    }

    public bool RescrapeManualLoginEnabled
    {
        get => _rescrapeManualLoginEnabled;
        set => SetField(ref _rescrapeManualLoginEnabled, value);
    }

    public bool ShowRescrapeConfirmLoginButton
    {
        get => _showRescrapeConfirmLoginButton;
        set => SetField(ref _showRescrapeConfirmLoginButton, value);
    }

    public bool ShowRescrapeControlButtons
    {
        get => _showRescrapeControlButtons;
        set
        {
            if (SetField(ref _showRescrapeControlButtons, value))
            {
                OnPropertyChanged(nameof(ShowOpenRescrapeProgressButton));
            }
        }
    }

    public bool ShowOpenRescrapeProgressButton =>
        !IsSendProgressVisible &&
        _currentRescrapeJobId.HasValue &&
        ShowRescrapeControlButtons;

    private bool FilterOnlineStoreProducts(StagingProductUi p)
    {
        if (ShowApartadosOnly)
        {
            return p.IsApartado;
        }

        return IsPendingForOnlineStore(p);
    }

    private static bool IsPendingForOnlineStore(StagingProductUi p)
    {
        return string.Equals(p.Status, "validated", StringComparison.OrdinalIgnoreCase)
            && !p.IsApartado
            && !string.Equals(p.FlashlySyncStatus, "synced", StringComparison.OrdinalIgnoreCase);
    }

    private void PerformSearch(string query)
    {
        SearchText = query; // Ensure property is updated if coming from command parameter
        StagingProductsView.Refresh();
    }
    
    private bool FilterStagingProducts(object obj)
    {
        if (obj is not StagingProductUi product) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        var search = _searchText.ToLower();
        
        // Search in Display Fields
        if ((product.Title?.ToLower().Contains(search) == true) ||
            (product.Sku?.ToLower().Contains(search) == true) ||
            (product.Description?.ToLower().Contains(search) == true))
        {
            return true;
        }

        // Search in Raw JSON (Deep Search)
        if (product.Product?.AIProcessedJson != null && 
            product.Product.AIProcessedJson.ToLower().Contains(search))
        {
            return true;
        }

        return false;
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
    public ObservableCollection<string> SendProgressLogs { get; } = new();
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

    public string TargetSystem
    {
        get => _targetSystem;
        set => SetField(ref _targetSystem, value);
    }

    public string OnlineStoreName
    {
        get => _onlineStoreName;
        set => SetField(ref _onlineStoreName, value);
    }

    public string OnlineStoreBaseUrl
    {
        get => _onlineStoreBaseUrl;
        set => SetField(ref _onlineStoreBaseUrl, value);
    }

    public string OnlineStoreApiKey
    {
        get => _onlineStoreApiKey;
        set => SetField(ref _onlineStoreApiKey, value);
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
    public AsyncCommand SendCheckedToSaeCommand { get; }
    public AsyncCommand SendPendingToSaeCommand { get; }
    public AsyncCommand SendPendingToOnlineStoreCommand { get; }
    public AsyncCommand SendSelectedToOnlineStoreCommand { get; }
    public AsyncCommand RescrapeSelectedOnlineStoreCommand { get; }
    public AsyncCommand SaveSelectedOnlineStoreRecordCommand { get; }
    public AsyncCommand DeleteSelectedOnlineStoreRecordsCommand { get; }
    public AsyncCommand MarkSelectedAsApartadoCommand { get; }
    public AsyncCommand UnmarkSelectedAsApartadoCommand { get; }
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
    public AsyncCommand ConfirmRescrapeLoginCommand { get; }
    public AsyncCommand PauseRescrapeCommand { get; }
    public AsyncCommand ResumeRescrapeCommand { get; }
    public AsyncCommand CancelRescrapeCommand { get; }
    
    public RelayCommand ShowWindowCommand { get; }
    public RelayCommand ExitApplicationCommand { get; }
    public RelayCommand<string> NavigateCommand { get; }
    public RelayCommand CloseSendProgressCommand { get; }
    public RelayCommand ShowSendProgressCommand { get; }
    public RelayCommand CopySendProgressCommand { get; }

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

            ResetStagingProducts(await _apiClient.GetStagingProductsAsync());
            OnPropertyChanged(nameof(SelectedForSaeCount));
            OnPropertyChanged(nameof(OnlineStorePendingCount));
            OnPropertyChanged(nameof(OnlineStoreApartadosCount));
            OnPropertyChanged(nameof(OnlineStoreSelectedCount));
            OnPropertyChanged(nameof(OnlineStoreProducts));
            SendCheckedToSaeCommand.RaiseCanExecuteChanged();

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
            var uiModel = CreateStagingProductUi(created);
            StagingProducts.Add(uiModel);
            SelectedStagingProduct = uiModel;
            OnPropertyChanged(nameof(SelectedForSaeCount));
            OnPropertyChanged(nameof(OnlineStorePendingCount));
            OnPropertyChanged(nameof(OnlineStoreApartadosCount));
            OnPropertyChanged(nameof(OnlineStoreSelectedCount));
            OnPropertyChanged(nameof(OnlineStoreProducts));
            SendCheckedToSaeCommand.RaiseCanExecuteChanged();
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
        SelectedStagingProduct.PropertyChanged -= OnStagingProductPropertyChanged;
        StagingProducts.Remove(SelectedStagingProduct);
        SelectedStagingProduct = null;
        OnPropertyChanged(nameof(SelectedForSaeCount));
        OnPropertyChanged(nameof(OnlineStorePendingCount));
        OnPropertyChanged(nameof(OnlineStoreApartadosCount));
        OnPropertyChanged(nameof(OnlineStoreSelectedCount));
        OnPropertyChanged(nameof(OnlineStoreProducts));
        SendCheckedToSaeCommand.RaiseCanExecuteChanged();
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

    private async Task ConfirmRescrapeLoginAsync()
    {
        if (_currentRescrapeSiteIds.Count == 0)
        {
            AppendSendProgressLog("No hay sitios activos para confirmar login manual.");
            return;
        }

        var confirmed = 0;
        foreach (var siteId in _currentRescrapeSiteIds)
        {
            try
            {
                await _apiClient.ConfirmLoginAsync(siteId);
                confirmed++;
                AppendSendProgressLog($"Login manual confirmado para sitio {siteId}.");
            }
            catch (Exception ex)
            {
                AppendSendProgressLog($"Error confirmando login manual para sitio {siteId}: {ex.Message}");
                AppLogger.Error($"ConfirmRescrapeLoginAsync failed for site {siteId}", ex);
            }
        }

        if (confirmed > 0)
        {
            SendProgressStatus = $"Login manual confirmado para {confirmed} sitio(s). Esperando reanudación del rescrape...";
        }
    }

    private bool CanPauseRescrape()
    {
        if (!_currentRescrapeJobId.HasValue || !ShowRescrapeControlButtons)
        {
            return false;
        }

        return string.Equals(_currentRescrapeJobStatus, "queued", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(_currentRescrapeJobStatus, "running", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanResumeRescrape()
    {
        if (!_currentRescrapeJobId.HasValue || !ShowRescrapeControlButtons)
        {
            return false;
        }

        return string.Equals(_currentRescrapeJobStatus, "paused", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanCancelRescrape()
    {
        if (!_currentRescrapeJobId.HasValue || !ShowRescrapeControlButtons)
        {
            return false;
        }

        return !string.Equals(_currentRescrapeJobStatus, "completed", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(_currentRescrapeJobStatus, "completed_with_errors", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(_currentRescrapeJobStatus, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshRescrapeControlCommands()
    {
        PauseRescrapeCommand.RaiseCanExecuteChanged();
        ResumeRescrapeCommand.RaiseCanExecuteChanged();
        CancelRescrapeCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ShowOpenRescrapeProgressButton));
    }

    private async Task PauseRescrapeAsync()
    {
        if (!_currentRescrapeJobId.HasValue)
        {
            return;
        }

        var ok = await _apiClient.PauseRescrapeAsync(_currentRescrapeJobId.Value);
        AppendSendProgressLog(ok
            ? "Solicitud de pausa enviada."
            : "No se pudo pausar el job.");
        if (ok)
        {
            _currentRescrapeJobStatus = "paused";
            RefreshRescrapeControlCommands();
        }
    }

    private async Task ResumeRescrapeAsync()
    {
        if (!_currentRescrapeJobId.HasValue)
        {
            return;
        }

        var ok = await _apiClient.ResumeRescrapeAsync(_currentRescrapeJobId.Value);
        AppendSendProgressLog(ok
            ? "Solicitud de reanudación enviada."
            : "No se pudo reanudar el job.");
        if (ok)
        {
            _currentRescrapeJobStatus = "queued";
            RefreshRescrapeControlCommands();
        }
    }

    private async Task CancelRescrapeAsync()
    {
        if (!_currentRescrapeJobId.HasValue)
        {
            return;
        }

        var ok = await _apiClient.CancelRescrapeAsync(_currentRescrapeJobId.Value);
        AppendSendProgressLog(ok
            ? "Solicitud de cancelación enviada."
            : "No se pudo cancelar el job.");
        if (ok)
        {
            _currentRescrapeJobStatus = "cancelled";
            RefreshRescrapeControlCommands();
        }
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
            var response = await _apiClient.InspectUrlsAsync(SelectedSite.Id, urls);
            
            if (response != null)
            {
                StatusMessage = $"Inspección completada. Extraídos {response.SuccessCount} de {urls.Count}.";
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

        await SendProductsWithProgressAsync(new List<StagingProductUi> { SelectedStagingProduct }, "Envio seleccionado a SAE");
    }

    private async Task SendCheckedToSaeAsync()
    {
        var selected = StagingProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay productos seleccionados.";
            return;
        }

        await SendProductsWithProgressAsync(selected, "Envio de seleccionados a SAE");
    }

    private async Task SendPendingToSaeAsync()
    {
        var pending = StagingProducts
            .Where(p => !p.Product.ExcludeFromSae && string.Equals(p.Product.Status, "validated", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pending.Count == 0)
        {
            StatusMessage = "No hay registros pendientes por enviar.";
            return;
        }

        await SendProductsWithProgressAsync(pending, "Envio de pendientes a SAE");
    }

    private async Task SendPendingToOnlineStoreAsync()
    {
        var pending = OnlineStoreProducts
            .Where(p => !p.IsApartado)
            .Where(p => string.Equals(p.Status, "validated", StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.Equals(p.FlashlySyncStatus, "synced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pending.Count == 0)
        {
            StatusMessage = "No hay productos pendientes por enviar a tienda en línea.";
            return;
        }

        await SendOnlineStoreProductsWithProgressAsync(pending, "Envio de pendientes a tienda en linea");
    }

    private async Task SendSelectedToOnlineStoreAsync()
    {
        var selected = OnlineStoreProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay productos seleccionados para enviar.";
            return;
        }

        await SendOnlineStoreProductsWithProgressAsync(selected, "Envio de seleccionados a tienda en linea");
    }

    private async Task RescrapeSelectedOnlineStoreAsync()
    {
        var selected = OnlineStoreProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay productos seleccionados para rescrapear.";
            return;
        }

        var productIds = selected.Select(p => p.Product.Id).Distinct().ToList();
        var labelsById = selected
            .GroupBy(p => p.Product.Id)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return string.IsNullOrWhiteSpace(first.Sku)
                        ? (!string.IsNullOrWhiteSpace(first.Product.SkuSource)
                            ? first.Product.SkuSource!
                            : first.Product.Id.ToString())
                        : first.Sku;
                });

        IsSendProgressVisible = true;
        IsSendProgressCompleted = false;
        SendProgressTitle = "Rescrape de seleccionados";
        ClearSendProgressLogs();
        _currentRescrapeSiteIds = selected.Select(s => s.Product.SiteId).Where(id => id != Guid.Empty).Distinct().ToHashSet();
        ShowRescrapeConfirmLoginButton = RescrapeManualLoginEnabled && _currentRescrapeSiteIds.Count > 0;
        ShowRescrapeControlButtons = true;
        _currentRescrapeJobId = null;
        _currentRescrapeJobStatus = "queued";
        RefreshRescrapeControlCommands();
        SendProgressMaximum = Math.Max(1, productIds.Count);
        SendProgressValue = 0;
        SendProgressStatus = $"Encolando rescrape de {productIds.Count} producto(s)...";

        var queued = await _apiClient.QueueRescrapeAsync(productIds, RescrapeManualLoginEnabled);
        if (queued == null)
        {
            SendProgressStatus = "No se pudo crear el job de rescrape.";
            IsSendProgressCompleted = true;
            ShowRescrapeConfirmLoginButton = false;
            ShowRescrapeControlButtons = false;
            _currentRescrapeSiteIds.Clear();
            _currentRescrapeJobId = null;
            _currentRescrapeJobStatus = string.Empty;
            RefreshRescrapeControlCommands();
            return;
        }

        _currentRescrapeJobId = queued.JobId;
        _currentRescrapeJobStatus = "queued";
        RefreshRescrapeControlCommands();

        StartSendProgressLogFile("rescrape", queued.JobId);
        AppendSendProgressLog($"Job creado: {queued.JobId}");
        AppendSendProgressLog($"Opciones: manualLogin={RescrapeManualLoginEnabled}");
        if (RescrapeManualLoginEnabled)
        {
            AppendSendProgressLog("Login manual habilitado: inicia sesión en el navegador emergente.");
            AppendSendProgressLog("Cuando termine el login, usa el botón 'Confirmar Login Manual' para liberar el flujo.");
        }

        var doneStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "completed",
            "completed_with_errors",
            "cancelled"
        };
        var seenItemTransitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenLogIds = new HashSet<Guid>();
        var previousProcessed = -1;
        var lastProgressAt = DateTime.UtcNow;
        var statusFetchWarningShown = false;
        var itemFetchWarningShown = false;
        var logFetchWarningShown = false;

        while (true)
        {
            await Task.Delay(2000);
            RescrapeJobStatusResponse? status;
            try
            {
                status = await _apiClient.GetRescrapeStatusAsync(queued.JobId);
                if (statusFetchWarningShown)
                {
                    AppendSendProgressLog("Conexion con estado de rescrape restablecida.");
                    statusFetchWarningShown = false;
                }
            }
            catch (Exception ex)
            {
                if (!statusFetchWarningShown)
                {
                    AppendSendProgressLog($"No se pudo leer estado de rescrape: {ex.Message}");
                    statusFetchWarningShown = true;
                }
                continue;
            }

            if (status == null)
            {
                SendProgressStatus = "Job de rescrape no encontrado.";
                IsSendProgressCompleted = true;
                ShowRescrapeConfirmLoginButton = false;
                ShowRescrapeControlButtons = false;
                _currentRescrapeSiteIds.Clear();
                _currentRescrapeJobId = null;
                _currentRescrapeJobStatus = string.Empty;
                RefreshRescrapeControlCommands();
                break;
            }

            _currentRescrapeJobStatus = status.Status;
            RefreshRescrapeControlCommands();

            SendProgressMaximum = Math.Max(1, status.TotalItems);
            SendProgressValue = Math.Min(status.ProcessedItems, SendProgressMaximum);
            var items = new List<RescrapeJobItemResponse>();
            try
            {
                items = await _apiClient.GetRescrapeItemsAsync(queued.JobId);
                if (itemFetchWarningShown)
                {
                    AppendSendProgressLog("Lectura de items de rescrape restablecida.");
                    itemFetchWarningShown = false;
                }
            }
            catch (Exception ex)
            {
                if (!itemFetchWarningShown)
                {
                    AppendSendProgressLog($"No se pudieron leer items de rescrape: {ex.Message}");
                    itemFetchWarningShown = true;
                }
            }

            var running = items.Count(i => string.Equals(i.Status, "running", StringComparison.OrdinalIgnoreCase));
            var pending = items.Count(i => string.Equals(i.Status, "pending", StringComparison.OrdinalIgnoreCase));
            var stage = BuildRescrapeStageText(items, labelsById);
            SendProgressStatus =
                $"Estado: {status.Status}. Procesados {status.ProcessedItems}/{status.TotalItems}. Running: {running}. Pendientes: {pending}. Etapa: {stage}";

            if (status.ProcessedItems > previousProcessed)
            {
                previousProcessed = status.ProcessedItems;
                lastProgressAt = DateTime.UtcNow;
            }
            else if (string.Equals(status.Status, "running", StringComparison.OrdinalIgnoreCase) &&
                     DateTime.UtcNow - lastProgressAt > TimeSpan.FromMinutes(2))
            {
                AppendSendProgressLog("Sin avance en 2+ minutos. Revisa sesión manual, conectividad y logs del job.");
                lastProgressAt = DateTime.UtcNow;
            }

            foreach (var item in items.OrderBy(i => i.UpdatedAt))
            {
                var transitionKey = $"{item.ItemId}|{item.Status}|{item.Changed}|{item.ErrorMessage}|{item.ResultJson}";
                if (!seenItemTransitions.Add(transitionKey))
                {
                    continue;
                }

                var marker = item.Status.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
                    ? "OK"
                    : item.Status.ToUpperInvariant();
                var label = labelsById.TryGetValue(item.StagingProductId, out var value)
                    ? value
                    : item.StagingProductId.ToString();
                var changed = item.Changed ? " (cambió)" : string.Empty;
                AppendSendProgressLog($"{marker} {label}{changed}");
                if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                {
                    AppendSendProgressLog($"  {item.ErrorMessage}");
                }
            }

            List<RescrapeJobLogResponse> logs;
            try
            {
                logs = await _apiClient.GetRescrapeLogsAsync(queued.JobId, 300);
                if (logFetchWarningShown)
                {
                    AppendSendProgressLog("Lectura de bitacora de rescrape restablecida.");
                    logFetchWarningShown = false;
                }
            }
            catch (Exception ex)
            {
                logs = new List<RescrapeJobLogResponse>();
                if (!logFetchWarningShown)
                {
                    AppendSendProgressLog($"No se pudo leer bitacora de rescrape: {ex.Message}");
                    AppendSendProgressLog("Continuando con estado basico del job sin bitacora persistida.");
                    logFetchWarningShown = true;
                }
            }

            foreach (var log in logs.OrderBy(l => l.CreatedAt))
            {
                if (!seenLogIds.Add(log.LogId))
                {
                    continue;
                }

                var level = string.IsNullOrWhiteSpace(log.Level) ? "INFO" : log.Level.ToUpperInvariant();
                AppendSendProgressLog($"[{log.CreatedAt:HH:mm:ss}] [{level}] {log.Message}");
            }

            if (!doneStates.Contains(status.Status))
            {
                continue;
            }

            SendProgressStatus = $"Finalizado. Exitosos: {status.SuccessItems}. Fallidos: {status.FailedItems}. Omitidos: {status.SkippedItems}.";
            if (!string.IsNullOrWhiteSpace(_sendProgressLogFilePath))
            {
                AppendSendProgressLog($"Log guardado en: {_sendProgressLogFilePath}");
            }
            IsSendProgressCompleted = true;
            ShowRescrapeConfirmLoginButton = false;
            ShowRescrapeControlButtons = false;
            _currentRescrapeSiteIds.Clear();
            _currentRescrapeJobId = null;
            _currentRescrapeJobStatus = string.Empty;
            RefreshRescrapeControlCommands();
            StatusMessage = SendProgressStatus;
            break;
        }

        await RefreshStagingProductsAsync();
    }

    private static string BuildRescrapeStageText(
        IReadOnlyList<RescrapeJobItemResponse> items,
        IReadOnlyDictionary<Guid, string> labelsById)
    {
        var runningEntries = items
            .Where(i => string.Equals(i.Status, "running", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(i =>
            {
                var label = labelsById.TryGetValue(i.StagingProductId, out var value)
                    ? value
                    : i.StagingProductId.ToString();
                var detail = string.IsNullOrWhiteSpace(i.ErrorMessage)
                    ? "Procesando..."
                    : i.ErrorMessage;
                return $"{label}: {detail}";
            })
            .ToList();

        if (runningEntries.Count > 0)
        {
            return string.Join(" | ", runningEntries);
        }

        if (items.Any(i => string.Equals(i.Status, "pending", StringComparison.OrdinalIgnoreCase)))
        {
            return "Esperando siguiente item en cola.";
        }

        return "Sin item activo reportado.";
    }

    private async Task SendOnlineStoreProductsWithProgressAsync(List<StagingProductUi> products, string title)
    {
        IsSendProgressVisible = true;
        IsSendProgressCompleted = false;
        SendProgressTitle = title;
        ClearSendProgressLogs();
        SendProgressMaximum = Math.Max(1, products.Count);
        SendProgressValue = 0;
        SendProgressStatus = $"Preparando envio de {products.Count} registro(s)...";

        var sent = 0;
        var failed = 0;
        for (int i = 0; i < products.Count; i++)
        {
            var product = products[i];
            var current = i + 1;
            var label = string.IsNullOrWhiteSpace(product.Sku) ? product.Product.Id.ToString() : product.Sku;
            var start = $"[{current}/{products.Count}] Enviando {label}...";
            AppendSendProgressLog(start);
            SendProgressStatus = start;

            var result = await _apiClient.SendToOnlineStoreAsync(product.Product.Id);
            if (result.Success)
            {
                sent++;
                AppendSendProgressLog($"[{current}/{products.Count}] OK {label}");
            }
            else
            {
                failed++;
                AppendSendProgressLog($"[{current}/{products.Count}] ERROR {label}");
                AppendSendErrorDetails(current, products.Count, result);
            }

            SendProgressValue = current;
        }

        SendProgressStatus = $"Finalizado. Enviados: {sent}. Fallidos: {failed}.";
        IsSendProgressCompleted = true;
        StatusMessage = SendProgressStatus;
        await RefreshStagingProductsAsync();
    }

    private async Task SaveSelectedOnlineStoreRecordAsync()
    {
        if (SelectedStagingProduct == null)
        {
            StatusMessage = "Selecciona un registro para guardar cambios.";
            return;
        }

        var updated = await _apiClient.UpdateStagingProductAsync(SelectedStagingProduct.Product.Id, SelectedStagingProduct.Product);
        StatusMessage = updated != null ? "Registro actualizado." : "No se pudo actualizar el registro.";
        await RefreshStagingProductsAsync();
    }

    private async Task DeleteSelectedOnlineStoreRecordsAsync()
    {
        var selected = OnlineStoreProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay registros seleccionados para eliminar.";
            return;
        }

        foreach (var item in selected)
        {
            await _apiClient.DeleteStagingProductAsync(item.Product.Id);
        }

        StatusMessage = $"Registros eliminados: {selected.Count}.";
        await RefreshStagingProductsAsync();
    }

    private async Task MarkSelectedAsApartadoAsync()
    {
        var selected = OnlineStoreProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay registros seleccionados para apartar.";
            return;
        }

        foreach (var item in selected)
        {
            item.IsApartado = true;
            await _apiClient.UpdateStagingProductAsync(item.Product.Id, item.Product);
        }

        StatusMessage = $"Registros apartados: {selected.Count}.";
        await RefreshStagingProductsAsync();
    }

    private async Task UnmarkSelectedAsApartadoAsync()
    {
        var selected = OnlineStoreProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay registros seleccionados para quitar apartado.";
            return;
        }

        foreach (var item in selected)
        {
            item.IsApartado = false;
            await _apiClient.UpdateStagingProductAsync(item.Product.Id, item.Product);
        }

        StatusMessage = $"Registros reactivados: {selected.Count}.";
        await RefreshStagingProductsAsync();
    }

    private async Task SendProductsWithProgressAsync(List<StagingProductUi> products, string title)
    {
        IsSendProgressVisible = true;
        IsSendProgressCompleted = false;
        SendProgressTitle = title;
        ClearSendProgressLogs();
        SendProgressMaximum = Math.Max(1, products.Count);
        SendProgressValue = 0;
        SendProgressStatus = $"Preparando envio de {products.Count} registro(s)...";

        int sent = 0;
        int failed = 0;
        for (int i = 0; i < products.Count; i++)
        {
            var product = products[i];
            var current = i + 1;
            var sku = string.IsNullOrWhiteSpace(product.Sku) ? product.Product.SkuSource : product.Sku;
            var label = string.IsNullOrWhiteSpace(sku) ? product.Product.Id.ToString() : sku;

            var startMessage = $"[{current}/{products.Count}] Enviando {label}...";
            AppendSendProgressLog(startMessage);
            SendProgressStatus = startMessage;

            var result = await _apiClient.SendToSaeAsync(product.Product.Id);
            if (result.Success)
            {
                sent++;
                AppendSendProgressLog($"[{current}/{products.Count}] OK {label}");
            }
            else
            {
                failed++;
                AppendSendProgressLog($"[{current}/{products.Count}] ERROR {label}");
                AppendSendErrorDetails(current, products.Count, result);
            }

            SendProgressValue = current;
        }

        SendProgressStatus = $"Finalizado. Enviados: {sent}. Fallidos: {failed}.";
        IsSendProgressCompleted = true;
        StatusMessage = SendProgressStatus;
        await LoadAllAsync();
    }

    private void AppendSendErrorDetails(int current, int total, ApiOperationResult result)
    {
        if (result.StatusCode.HasValue)
        {
            AppendSendProgressLog($"[{current}/{total}]   HTTP {result.StatusCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            var lines = result.Message
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));
            foreach (var line in lines)
            {
                AppendSendProgressLog($"[{current}/{total}]   {line}");
            }
        }
    }

    private void ClearSendProgressLogs()
    {
        SendProgressLogs.Clear();
        SendProgressText = string.Empty;
        _sendProgressLogFilePath = null;
    }

    private void CloseSendProgressModal()
    {
        if (_currentRescrapeJobId.HasValue &&
            ShowRescrapeControlButtons &&
            !IsSendProgressCompleted)
        {
            IsSendProgressVisible = false;
            StatusMessage = "Rescrape activo en segundo plano. Usa 'Ver progreso rescrape' para volver al detalle.";
            return;
        }

        IsSendProgressVisible = false;
        ShowRescrapeConfirmLoginButton = false;
        ShowRescrapeControlButtons = false;
        _currentRescrapeJobId = null;
        _currentRescrapeJobStatus = string.Empty;
        _currentRescrapeSiteIds.Clear();
        RefreshRescrapeControlCommands();
    }

    private void ShowSendProgressModal()
    {
        if (!ShowOpenRescrapeProgressButton)
        {
            return;
        }

        IsSendProgressVisible = true;
        StatusMessage = "Mostrando progreso de rescrape en ejecucion.";
    }

    private void StartSendProgressLogFile(string prefix, Guid? jobId = null)
    {
        try
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScrapSAE",
                "execution-logs");
            Directory.CreateDirectory(baseDir);

            var fileName = jobId.HasValue
                ? $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{prefix}_{jobId.Value}.log"
                : $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{prefix}.log";
            _sendProgressLogFilePath = Path.Combine(baseDir, fileName);
            File.WriteAllText(_sendProgressLogFilePath, $"Inicio: {DateTime.UtcNow:O}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _sendProgressLogFilePath = null;
            AppLogger.Error("No se pudo crear archivo de log de progreso.", ex);
        }
    }

    private void AppendSendProgressLog(string line)
    {
        SendProgressLogs.Add(line);
        SendProgressText = string.IsNullOrEmpty(SendProgressText)
            ? line
            : $"{SendProgressText}{Environment.NewLine}{line}";

        if (!string.IsNullOrWhiteSpace(_sendProgressLogFilePath))
        {
            try
            {
                File.AppendAllText(_sendProgressLogFilePath, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("No se pudo escribir línea en log de progreso.", ex);
            }
        }
    }

    private void CopySendProgressToClipboard()
    {
        if (string.IsNullOrWhiteSpace(SendProgressText))
        {
            return;
        }

        Clipboard.SetText(SendProgressText);
        StatusMessage = "Log de envío copiado al portapapeles.";
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
            TargetSystem = settings.TargetSystem ?? "Flashly";
            OnlineStoreName = settings.OnlineStoreName ?? string.Empty;
            OnlineStoreBaseUrl = settings.OnlineStoreBaseUrl ?? string.Empty;
            OnlineStoreApiKey = settings.OnlineStoreApiKey ?? string.Empty;
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
                TargetSystem = TargetSystem,
                OnlineStoreName = OnlineStoreName,
                OnlineStoreBaseUrl = OnlineStoreBaseUrl,
                OnlineStoreApiKey = OnlineStoreApiKey,
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
            ResetStagingProducts(products);
            OnPropertyChanged(nameof(SelectedForSaeCount));
            OnPropertyChanged(nameof(OnlineStorePendingCount));
            OnPropertyChanged(nameof(OnlineStoreApartadosCount));
            OnPropertyChanged(nameof(OnlineStoreSelectedCount));
            OnPropertyChanged(nameof(OnlineStoreProducts));
            SendCheckedToSaeCommand.RaiseCanExecuteChanged();
        });
    }

    private StagingProductUi CreateStagingProductUi(StagingProduct item)
    {
        var ui = new StagingProductUi(item);
        ui.PropertyChanged += OnStagingProductPropertyChanged;
        return ui;
    }

    private void OnStagingProductPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StagingProductUi.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedForSaeCount));
            OnPropertyChanged(nameof(OnlineStoreSelectedCount));
            SendCheckedToSaeCommand.RaiseCanExecuteChanged();
        }

        if (e.PropertyName == nameof(StagingProductUi.IsApartado))
        {
            OnPropertyChanged(nameof(OnlineStorePendingCount));
            OnPropertyChanged(nameof(OnlineStoreApartadosCount));
            OnPropertyChanged(nameof(OnlineStoreProducts));
        }
    }

    private void ResetStagingProducts(IEnumerable<StagingProduct> products)
    {
        foreach (var existing in StagingProducts)
        {
            existing.PropertyChanged -= OnStagingProductPropertyChanged;
        }

        StagingProducts.Clear();
        foreach (var item in products)
        {
            StagingProducts.Add(CreateStagingProductUi(item));
        }

        OnPropertyChanged(nameof(OnlineStorePendingCount));
        OnPropertyChanged(nameof(OnlineStoreApartadosCount));
        OnPropertyChanged(nameof(OnlineStoreSelectedCount));
        OnPropertyChanged(nameof(OnlineStoreProducts));
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

