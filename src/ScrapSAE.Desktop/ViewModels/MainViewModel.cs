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
    private static readonly JsonSerializerOptions SiteJsonOptions = new() { WriteIndented = true };
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
    private bool _headlessEnabled = false;
    private bool _isScraping;
    private bool _isLiveMonitoringEnabled = true;
    private string _scrapeStatusText = "Idle";
    private int _selectedTabIndex;
    private string _selectorAnalysisResult = string.Empty;
    private string _scrapingMode = "Tradicional";
    private bool _isFamiliesMode;

    // Nuevas propiedades para consola en tiempo real y opciones avanzadas
    private bool _useAI = true;
    private bool _keepBrowserOpen;
    private bool _useScreenshotFallback;
    private string _learnedUrlsText = string.Empty;
    private readonly DispatcherTimer _liveLogTimer;
    private DateTime _lastLogTimestamp = DateTime.UtcNow.AddDays(-1);

    // Granular phase tracking
    private string _scrapingPhaseText = "Inactivo";
    private string _scrapingPhaseColor = "#6B7280";  // gray
    private bool _logFilterErrorsOnly;

    private string _searchText = string.Empty;
    private bool _isSendProgressVisible;
    private bool _isSendProgressCompleted;
    private string _sendProgressTitle = "Envio a SAE";
    private string _sendProgressStatus = string.Empty;
    private string _sendProgressText = string.Empty;
    private double _sendProgressValue;
    private double _sendProgressMaximum = 1;
    private bool _showApartadosOnly;
    private string _onlineStoreViewFilter = "Validados";
    private bool _isOnlineStoreDetailVisible;
    private StagingProductUi? _onlineStoreDetailProduct;
    private bool _rescrapeManualLoginEnabled;
    private bool _showRescrapeConfirmLoginButton;
    private bool _showRescrapeControlButtons;
    private Guid? _currentRescrapeJobId;
    private string _currentRescrapeJobStatus = string.Empty;
    private HashSet<Guid> _currentRescrapeSiteIds = new();
    private string? _sendProgressLogFilePath;
    private Guid? _siteFormId;
    private string _siteFormName = string.Empty;
    private string _siteFormBaseUrl = "https://";
    private string _siteFormLoginUrl = string.Empty;
    private string _siteFormCronExpression = string.Empty;
    private bool _siteFormRequiresLogin;
    private bool _siteFormIsActive = true;
    private string _siteFormMaxProductsPerScrape = "0";
    private string _siteFormCredentialsEncrypted = string.Empty;
    private string _siteFormSelectorsJson = "{}";
    private string _siteFormSecondarySelectorsJson = "{}";
    private string _siteFormStrategiesJson = "[]";
    private string _siteFormStatusMessage = "Selecciona un proveedor o crea uno nuevo.";
    private string _siteSearchText = string.Empty;
    public System.ComponentModel.ICollectionView StagingProductsView { get; private set; }
    public System.ComponentModel.ICollectionView SitesView { get; private set; }

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
        CreateSiteCommand = new AsyncCommand(() => SafeExecuteAsync(PrepareNewSiteAsync, "Nuevo proveedor"));
        LaunchWizardCommand = new RelayCommand(LaunchWizard);
        UpdateSiteCommand = new AsyncCommand(() => SafeExecuteAsync(SaveSiteAsync, "Guardar proveedor"));
        DeleteSiteCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteSiteAsync, "Eliminar proveedor"), () => SelectedSite != null);

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
        SaveOnlineStoreDetailRecordCommand = new AsyncCommand(
            () => SafeExecuteAsync(SaveOnlineStoreDetailRecordAsync, "Guardar cambios desde detalle"),
            () => OnlineStoreDetailProduct != null);
        DeleteSelectedOnlineStoreRecordsCommand = new AsyncCommand(() => SafeExecuteAsync(DeleteSelectedOnlineStoreRecordsAsync, "Eliminar registros seleccionados"));
        ValidateSelectedOnlineStoreRecordsCommand = new AsyncCommand(
            () => SafeExecuteAsync(ValidateSelectedOnlineStoreRecordsAsync, "Validar seleccionados de tienda en línea"),
            () => OnlineStoreProducts.Any(p => p.IsSelected));
        ValidateOnlineStoreDetailRecordCommand = new AsyncCommand(
            () => SafeExecuteAsync(ValidateOnlineStoreDetailRecordAsync, "Validar registro desde detalle"),
            () => OnlineStoreDetailProduct != null &&
                  !string.Equals(OnlineStoreDetailProduct.Status, "validated", StringComparison.OrdinalIgnoreCase));
        MarkSelectedAsApartadoCommand = new AsyncCommand(() => SafeExecuteAsync(MarkSelectedAsApartadoAsync, "Marcar apartados"));
        UnmarkSelectedAsApartadoCommand = new AsyncCommand(() => SafeExecuteAsync(UnmarkSelectedAsApartadoAsync, "Quitar apartado"));
        OpenOnlineStoreDetailDialogCommand = new RelayCommand(OpenOnlineStoreDetailDialog, () => SelectedStagingProduct != null);
        CloseOnlineStoreDetailDialogCommand = new RelayCommand(CloseOnlineStoreDetailDialog);

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
        ResetSiteFormCommand = new RelayCommand(ResetSiteFormFromSelection);
        ClearSiteSearchCommand = new RelayCommand(ClearSiteSearch, () => !string.IsNullOrWhiteSpace(SiteSearchText));
        
        // Initialize Collection View for filtering
        SitesView = System.Windows.Data.CollectionViewSource.GetDefaultView(Sites);
        SitesView.Filter = FilterSites;

        StagingProductsView = System.Windows.Data.CollectionViewSource.GetDefaultView(StagingProducts);
        StagingProductsView.Filter = FilterStagingProducts;

        PerformSearchCommand = new RelayCommand<string>(PerformSearch);
    }
    
    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public string SiteSearchText
    {
        get => _siteSearchText;
        set
        {
            if (SetField(ref _siteSearchText, value))
            {
                SitesView.Refresh();
                ClearSiteSearchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SiteFormName
    {
        get => _siteFormName;
        set => SetField(ref _siteFormName, value);
    }

    public string SiteFormBaseUrl
    {
        get => _siteFormBaseUrl;
        set => SetField(ref _siteFormBaseUrl, value);
    }

    public string SiteFormLoginUrl
    {
        get => _siteFormLoginUrl;
        set => SetField(ref _siteFormLoginUrl, value);
    }

    public string SiteFormCronExpression
    {
        get => _siteFormCronExpression;
        set => SetField(ref _siteFormCronExpression, value);
    }

    public bool SiteFormRequiresLogin
    {
        get => _siteFormRequiresLogin;
        set => SetField(ref _siteFormRequiresLogin, value);
    }

    public bool SiteFormIsActive
    {
        get => _siteFormIsActive;
        set => SetField(ref _siteFormIsActive, value);
    }

    public bool UseAI
    {
        get => _useAI;
        set => SetField(ref _useAI, value);
    }

    public void PromptAIEfficiencyWarning()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var result = MessageBox.Show(
                "La Inteligencia Artificial no está aportando información adicional relevante en la extracción de los últimos productos.\n\n¿Desea desactivar el uso de IA durante esta ejecución para optimizar recursos?",
                "No es necesario que se siga usando IA",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                UseAI = false;
                StatusMessage = "Uso de IA desactivado dinámicamente.";
            }
        });
    }

    public string SiteFormMaxProductsPerScrape
    {
        get => _siteFormMaxProductsPerScrape;
        set => SetField(ref _siteFormMaxProductsPerScrape, value);
    }

    public string SiteFormCredentialsEncrypted
    {
        get => _siteFormCredentialsEncrypted;
        set => SetField(ref _siteFormCredentialsEncrypted, value);
    }

    public string SiteFormSelectorsJson
    {
        get => _siteFormSelectorsJson;
        set => SetField(ref _siteFormSelectorsJson, value);
    }

    public string SiteFormSecondarySelectorsJson
    {
        get => _siteFormSecondarySelectorsJson;
        set => SetField(ref _siteFormSecondarySelectorsJson, value);
    }

    public string SiteFormStrategiesJson
    {
        get => _siteFormStrategiesJson;
        set => SetField(ref _siteFormStrategiesJson, value);
    }

    public string SiteFormStatusMessage
    {
        get => _siteFormStatusMessage;
        set => SetField(ref _siteFormStatusMessage, value);
    }

    public string SiteFormTitle => _siteFormId.HasValue ? "Editar proveedor" : "Nuevo proveedor";

    public string SiteSaveButtonText => _siteFormId.HasValue ? "Guardar cambios" : "Crear proveedor";

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
    public int OnlineStoreVisibleCount => OnlineStoreProducts.Count();
    public IEnumerable<StagingProductUi> OnlineStoreProducts => StagingProducts.Where(FilterOnlineStoreProducts);
    public IReadOnlyList<string> OnlineStoreViewFilterOptions { get; } = new[] { "Todos", "Validados", "Pendientes" };

    public string OnlineStoreViewFilter
    {
        get => _onlineStoreViewFilter;
        set
        {
            if (SetField(ref _onlineStoreViewFilter, value))
            {
                RaiseOnlineStoreViewChanged();
            }
        }
    }

    public bool ShowApartadosOnly
    {
        get => _showApartadosOnly;
        set
        {
            if (SetField(ref _showApartadosOnly, value))
            {
                RaiseOnlineStoreViewChanged();
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

    public bool IsOnlineStoreDetailVisible
    {
        get => _isOnlineStoreDetailVisible;
        set => SetField(ref _isOnlineStoreDetailVisible, value);
    }

    public StagingProductUi? OnlineStoreDetailProduct
    {
        get => _onlineStoreDetailProduct;
        set
        {
            if (SetField(ref _onlineStoreDetailProduct, value))
            {
                ValidateOnlineStoreDetailRecordCommand.RaiseCanExecuteChanged();
                SaveOnlineStoreDetailRecordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool FilterOnlineStoreProducts(StagingProductUi p)
    {
        if (ShowApartadosOnly)
        {
            return p.IsApartado;
        }

        if (p.IsApartado)
        {
            return false;
        }

        return OnlineStoreViewFilter switch
        {
            "Todos" => true,
            "Pendientes" => string.Equals(p.Status, "pending", StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(p.Status, "validated", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool IsPendingForOnlineStore(StagingProductUi p)
    {
        return !p.IsApartado
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
                DeleteSiteCommand.RaiseCanExecuteChanged();
                UpdateRecentSyncLogs();
                PopulateSiteForm(value);
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
                OpenOnlineStoreDetailDialogCommand.RaiseCanExecuteChanged();
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

    public bool IsLiveMonitoringEnabled
    {
        get => _isLiveMonitoringEnabled;
        set
        {
            if (SetField(ref _isLiveMonitoringEnabled, value))
            {
                if (value)
                {
                    _statusTimer.Start();
                    _logTimer.Start();
                    _liveLogTimer.Start();
                    StatusMessage = "Monitoreo en tiempo real ACTIVADO.";
                }
                else
                {
                    _statusTimer.Stop();
                    _logTimer.Stop();
                    _liveLogTimer.Stop();
                    StatusMessage = "Monitoreo en tiempo real DESACTIVADO (Peticiones en segundo plano pausadas).";
                }
            }
        }
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

    /// <summary>Descripción de la fase granular de scraping (Descubrimiento, Paginación, Extracción, etc.)</summary>
    public string ScrapingPhaseText
    {
        get => _scrapingPhaseText;
        set => SetField(ref _scrapingPhaseText, value);
    }

    /// <summary>Color hex para el badge de fase (#10B981 verde, #2563EB azul, #D97706 amarillo, #6B7280 gris)</summary>
    public string ScrapingPhaseColor
    {
        get => _scrapingPhaseColor;
        set => SetField(ref _scrapingPhaseColor, value);
    }

    /// <summary>Cuando es true, solo se muestran logs de nivel error/warn en la consola en tiempo real.</summary>
    public bool LogFilterErrorsOnly
    {
        get => _logFilterErrorsOnly;
        set
        {
            SetField(ref _logFilterErrorsOnly, value);
            // Trigger re-filter
            OnPropertyChanged(nameof(FilteredLiveLogs));
        }
    }

    /// <summary>Vista filtrada de LiveLogs: solo errores si LogFilterErrorsOnly está activo.</summary>
    public IEnumerable<string> FilteredLiveLogs =>
        LogFilterErrorsOnly
            ? LiveLogs.Where(l => l.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
                               || l.Contains("[WARN]", StringComparison.OrdinalIgnoreCase)
                               || l.Contains("error", StringComparison.OrdinalIgnoreCase))
            : LiveLogs;

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
    public AsyncCommand SaveOnlineStoreDetailRecordCommand { get; }
    public AsyncCommand DeleteSelectedOnlineStoreRecordsCommand { get; }
    public AsyncCommand ValidateSelectedOnlineStoreRecordsCommand { get; }
    public AsyncCommand ValidateOnlineStoreDetailRecordCommand { get; }
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
    public RelayCommand LaunchWizardCommand { get; }
    
    public RelayCommand ShowWindowCommand { get; }
    public RelayCommand ExitApplicationCommand { get; }
    public RelayCommand<string> NavigateCommand { get; }
    public RelayCommand OpenOnlineStoreDetailDialogCommand { get; }
    public RelayCommand CloseOnlineStoreDetailDialogCommand { get; }
    public RelayCommand CloseSendProgressCommand { get; }
    public RelayCommand ShowSendProgressCommand { get; }
    public RelayCommand CopySendProgressCommand { get; }
    public RelayCommand ResetSiteFormCommand { get; }
    public RelayCommand ClearSiteSearchCommand { get; }

    public async Task LoadAllAsync()

    {
        try
        {
            StatusMessage = "Cargando datos...";
            var selectedSiteId = SelectedSite?.Id;
            Sites.Clear();
            var rawSites = await _apiClient.GetSitesAsync();
            var uniqueSites = rawSites
                .Where(s => !s.Name.StartsWith("[TEMP]", StringComparison.OrdinalIgnoreCase))
                .DistinctBy(s => (s.Name ?? string.Empty).Trim().ToLowerInvariant());

            foreach (var site in uniqueSites)
            {
                Sites.Add(site);
            }
            HasSites = Sites.Count > 0;
            SitesView.Refresh();
            SelectedSite = selectedSiteId.HasValue
                ? Sites.FirstOrDefault(s => s.Id == selectedSiteId.Value) ?? Sites.FirstOrDefault()
                : Sites.FirstOrDefault();
            if (SelectedSite == null)
            {
                PopulateSiteForm(null);
            }

            AppLogger.Info($"Sites loaded: {Sites.Count}");

            ResetStagingProducts(await _apiClient.GetStagingProductsAsync());
            OnPropertyChanged(nameof(SelectedForSaeCount));
            RaiseOnlineStoreDataChanged();
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

    private Task PrepareNewSiteAsync()
    {
        SelectedSite = null;
        PopulateSiteForm(null);
        SiteFormStatusMessage = "Completa el formulario para crear un proveedor.";
        StatusMessage = "Formulario listo para nuevo proveedor.";
        return Task.CompletedTask;
    }

    private async Task SaveSiteAsync()
    {
        if (!TryBuildSiteFromForm(out var payload, out var validationMessage))
        {
            SiteFormStatusMessage = validationMessage;
            StatusMessage = validationMessage;
            return;
        }

        if (_siteFormId.HasValue)
        {
            var updated = await _apiClient.UpdateSiteAsync(_siteFormId.Value, payload);
            if (updated == null)
            {
                SiteFormStatusMessage = "No se pudo actualizar el proveedor.";
                StatusMessage = SiteFormStatusMessage;
                return;
            }

            ReplaceSiteInCollection(updated);
            SelectedSite = updated;
            SiteFormStatusMessage = $"Proveedor \"{updated.Name}\" actualizado.";
        }
        else
        {
            var created = await _apiClient.CreateSiteAsync(payload);
            if (created == null)
            {
                SiteFormStatusMessage = "No se pudo crear el proveedor.";
                StatusMessage = SiteFormStatusMessage;
                return;
            }

            Sites.Add(created);
            SelectedSite = created;
            SiteFormStatusMessage = $"Proveedor \"{created.Name}\" creado.";
            AppLogger.Info($"Site created: {created.Name} ({created.Id}).");
        }

        HasSites = Sites.Count > 0;
        SitesView.Refresh();
        StatusMessage = SiteFormStatusMessage;
    }

    private async Task DeleteSiteAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        var toDelete = SelectedSite;
        var confirmation = MessageBox.Show(
            $"Se eliminará el proveedor \"{toDelete.Name}\". Esta acción no se puede deshacer.",
            "Eliminar proveedor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        await _apiClient.DeleteSiteAsync(toDelete.Id);
        var removedIndex = Sites.IndexOf(toDelete);
        Sites.Remove(toDelete);

        HasSites = Sites.Count > 0;
        SitesView.Refresh();

        if (Sites.Count == 0)
        {
            SelectedSite = null;
            PopulateSiteForm(null);
            SiteFormStatusMessage = "Proveedor eliminado. Ya puedes crear uno nuevo.";
            StatusMessage = SiteFormStatusMessage;
            return;
        }

        var nextIndex = Math.Min(Math.Max(removedIndex, 0), Sites.Count - 1);
        SelectedSite = Sites[nextIndex];
        SiteFormStatusMessage = $"Proveedor \"{toDelete.Name}\" eliminado.";
        StatusMessage = SiteFormStatusMessage;
    }

    private async Task PersistSelectedSiteAsync()
    {
        if (SelectedSite == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateSiteAsync(SelectedSite.Id, SelectedSite);
        if (updated == null)
        {
            return;
        }

        ReplaceSiteInCollection(updated);
        SelectedSite = updated;
        StatusMessage = "Proveedor actualizado.";
    }

    private void LaunchWizard()
    {
        var wizard = new Views.ProviderWizardView(_apiClient);
        if (wizard.ShowDialog() == true && wizard.CreatedSite != null)
        {
            ReplaceSiteInCollection(wizard.CreatedSite);
            SelectedSite = wizard.CreatedSite;
            StatusMessage = $"Proveedor \"{wizard.CreatedSite.Name}\" creado mediante el Wizard.";
            OnPropertyChanged(nameof(SitesView));
        }
        else
        {
            StatusMessage = "Wizard cancelado.";
        }
    }

    // ── Concurrent Scraping Wizard ────────────────────────────────────────────

    private Infrastructure.RelayCommand? _launchConcurrentWizardCommand;

    /// <summary>
    /// Abre el Concurrent Scraping Wizard (nuevo, no modifica el wizard original).
    /// Único punto de modificación en archivos existentes para esta feature.
    /// </summary>
    public Infrastructure.RelayCommand LaunchConcurrentWizardCommand =>
        _launchConcurrentWizardCommand ??= new Infrastructure.RelayCommand(LaunchConcurrentWizard);

    private async void LaunchConcurrentWizard()
    {
        try
        {
            StatusMessage = "Abriendo Wizard de Scraping Concurrente...";

            // Crear servicios
            var baseUrl       = _apiClient.BaseUrl;
            var httpClient    = new System.Net.Http.HttpClient();
            var excelService  = new ScrapSAE.Infrastructure.Services.ExcelIngestionService();
            var selectorSvc   = new ScrapSAE.Infrastructure.Services.AiSelectorDiscoveryService(httpClient, baseUrl);
            var consolidator  = new ScrapSAE.Infrastructure.Services.ProductDataConsolidator();
            var sessionRepo   = new ScrapSAE.Infrastructure.Services.WizardSessionRepository();
            var engine        = new ScrapSAE.Infrastructure.Services.ConcurrentScrapingEngine(excelService, consolidator, sessionRepo);

            var vm = new ViewModels.ConcurrentWizard.ConcurrentProviderWizardViewModel(
                excelService, selectorSvc, engine, sessionRepo, _apiClient);

            // Verificar si hay sesiones guardadas y ofrecer resume
            var savedSessions = await sessionRepo.ListSavedSessionsAsync();
            if (savedSessions.Count > 0)
            {
                var latest = savedSessions[0];
                var promptMessage = latest.LastCompletedRowIndex < 0
                    ? $"Se encontró una sesión en configuración previa: \"{latest.Name}\"\n\n¿Deseas reanudar la configuración donde la dejaste?"
                    : $"Se encontró una sesión guardada: \"{latest.Name}\"\n({latest.LastCompletedRowIndex + 1}/{latest.TotalExcelRows} filas completadas)\n\n¿Deseas continuar la ejecución desde el punto guardado?";

                var resume = System.Windows.MessageBox.Show(
                    promptMessage,
                    "Sesión guardada encontrada",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (resume == System.Windows.MessageBoxResult.Yes)
                {
                    var (session, results) = await sessionRepo.LoadAsync(latest.SessionId);
                    if (session != null)
                        vm.RestoreSession(session, results);
                }
            }

            var window = new Views.ConcurrentWizard.ConcurrentProviderWizardWindow(vm);
            if (System.Windows.Application.Current?.MainWindow != null &&
                System.Windows.Application.Current.MainWindow != window)
            {
                window.Owner = System.Windows.Application.Current.MainWindow;
            }

            window.ShowDialog();

            StatusMessage = "Wizard Concurrente cerrado.";
            await engine.DisposeAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al abrir el Wizard Concurrente: {ex.Message}";
            System.Windows.MessageBox.Show(
                $"No se pudo abrir el Wizard Concurrente:\n\n{ex.Message}\n\nDetalles:\n{ex}",
                "Error al abrir Wizard",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void ReplaceSiteInCollection(SiteProfile site)
    {
        if (site == null || site.Name.StartsWith("[TEMP]", StringComparison.OrdinalIgnoreCase)) return;

        var index = Sites.ToList().FindIndex(s => s.Id == site.Id || 
            (!string.IsNullOrEmpty(s.BaseUrl) && s.BaseUrl.Equals(site.BaseUrl, StringComparison.OrdinalIgnoreCase)) ||
            s.Name.Equals(site.Name, StringComparison.OrdinalIgnoreCase));
        
        if (index >= 0)
        {
            Sites[index] = site;
            return;
        }

        Sites.Add(site);
    }

    private void ResetSiteFormFromSelection()
    {
        PopulateSiteForm(SelectedSite);
        SiteFormStatusMessage = SelectedSite == null
            ? "Formulario restablecido para nuevo proveedor."
            : $"Se descartaron cambios. Editando \"{SelectedSite.Name}\".";
    }

    private void ClearSiteSearch()
    {
        SiteSearchText = string.Empty;
    }

    private bool FilterSites(object obj)
    {
        if (obj is not SiteProfile site)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SiteSearchText))
        {
            return true;
        }

        var query = SiteSearchText.Trim();
        return site.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || site.BaseUrl.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(site.LoginUrl) && site.LoginUrl.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void PopulateSiteForm(SiteProfile? site)
    {
        _siteFormId = site?.Id;
        OnPropertyChanged(nameof(SiteFormTitle));
        OnPropertyChanged(nameof(SiteSaveButtonText));

        SiteFormName = site?.Name ?? string.Empty;
        SiteFormBaseUrl = site?.BaseUrl ?? "https://";
        SiteFormLoginUrl = site?.LoginUrl ?? string.Empty;
        SiteFormCronExpression = site?.CronExpression ?? string.Empty;
        SiteFormRequiresLogin = site?.RequiresLogin ?? false;
        SiteFormIsActive = site?.IsActive ?? true;
        SiteFormMaxProductsPerScrape = (site?.MaxProductsPerScrape ?? 0).ToString();
        SiteFormCredentialsEncrypted = site?.CredentialsEncrypted ?? string.Empty;
        SiteFormSelectorsJson = SerializeFormJson(site?.Selectors, "{}");
        SiteFormSecondarySelectorsJson = JsonSerializer.Serialize(site?.SecondarySelectors ?? new Dictionary<string, List<string>>(), SiteJsonOptions);
        SiteFormStrategiesJson = JsonSerializer.Serialize(site?.Strategies ?? new List<ScrapingStrategyDefinition>(), SiteJsonOptions);

        if (site == null)
        {
            SiteFormStatusMessage = "Completa el formulario para crear un proveedor.";
        }
        else
        {
            SiteFormStatusMessage = $"Editando proveedor: {site.Name}";
        }
    }

    private bool TryBuildSiteFromForm(out SiteProfile payload, out string validationMessage)
    {
        payload = new SiteProfile();

        if (string.IsNullOrWhiteSpace(SiteFormName))
        {
            validationMessage = "El nombre del proveedor es obligatorio.";
            return false;
        }

        if (!TryNormalizeHttpUrl(SiteFormBaseUrl, out var baseUrl))
        {
            validationMessage = "La URL base debe ser una URL http/https válida.";
            return false;
        }

        string loginUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(SiteFormLoginUrl) && !TryNormalizeHttpUrl(SiteFormLoginUrl, out loginUrl))
        {
            validationMessage = "La URL de login debe ser una URL http/https válida.";
            return false;
        }

        if (!int.TryParse(SiteFormMaxProductsPerScrape, out var maxProductsPerScrape) || maxProductsPerScrape < 0)
        {
            validationMessage = "Máx. productos por scrape debe ser un entero mayor o igual a 0.";
            return false;
        }

        if (!TryNormalizeJson(SiteFormSelectorsJson, JsonValueKind.Object, out var selectorsJson, out validationMessage))
        {
            return false;
        }

        if (!TryNormalizeJson(SiteFormSecondarySelectorsJson, JsonValueKind.Object, out var secondarySelectorsJson, out validationMessage))
        {
            return false;
        }

        if (!TryNormalizeJson(SiteFormStrategiesJson, JsonValueKind.Array, out var strategiesJson, out validationMessage))
        {
            return false;
        }

        Dictionary<string, List<string>> secondarySelectors;
        List<ScrapingStrategyDefinition> strategies;
        try
        {
            secondarySelectors = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(secondarySelectorsJson) ?? new Dictionary<string, List<string>>();
            strategies = JsonSerializer.Deserialize<List<ScrapingStrategyDefinition>>(strategiesJson) ?? new List<ScrapingStrategyDefinition>();
        }
        catch (JsonException ex)
        {
            validationMessage = $"No se pudieron interpretar opciones avanzadas: {ex.Message}";
            return false;
        }

        SiteFormSelectorsJson = selectorsJson;
        SiteFormSecondarySelectorsJson = secondarySelectorsJson;
        SiteFormStrategiesJson = strategiesJson;

        var current = _siteFormId.HasValue ? Sites.FirstOrDefault(s => s.Id == _siteFormId.Value) : null;
        payload = new SiteProfile
        {
            Id = _siteFormId ?? Guid.NewGuid(),
            Name = SiteFormName.Trim(),
            BaseUrl = baseUrl,
            LoginUrl = string.IsNullOrWhiteSpace(loginUrl) ? null : loginUrl,
            Selectors = selectorsJson,
            CronExpression = string.IsNullOrWhiteSpace(SiteFormCronExpression) ? null : SiteFormCronExpression.Trim(),
            RequiresLogin = SiteFormRequiresLogin,
            CredentialsEncrypted = string.IsNullOrWhiteSpace(SiteFormCredentialsEncrypted) ? null : SiteFormCredentialsEncrypted.Trim(),
            IsActive = SiteFormIsActive,
            MaxProductsPerScrape = maxProductsPerScrape,
            CreatedAt = current?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SecondarySelectors = secondarySelectors,
            Strategies = strategies
        };

        validationMessage = string.Empty;
        return true;
    }

    private static bool TryNormalizeHttpUrl(string? rawUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static bool TryNormalizeJson(string input, JsonValueKind expectedKind, out string normalizedJson, out string errorMessage)
    {
        var fallback = expectedKind == JsonValueKind.Array ? "[]" : "{}";
        var candidate = string.IsNullOrWhiteSpace(input) ? fallback : input.Trim();

        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != expectedKind)
            {
                normalizedJson = candidate;
                errorMessage = expectedKind == JsonValueKind.Array
                    ? "El JSON debe ser un arreglo."
                    : "El JSON debe ser un objeto.";
                return false;
            }

            normalizedJson = JsonSerializer.Serialize(doc.RootElement, SiteJsonOptions);
            errorMessage = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            normalizedJson = candidate;
            errorMessage = $"JSON inválido: {ex.Message}";
            return false;
        }
    }

    private static string SerializeFormJson(object? rawValue, string fallback)
    {
        try
        {
            if (rawValue == null)
            {
                return fallback;
            }

            var element = rawValue switch
            {
                JsonElement jsonElement => jsonElement.Clone(),
                string text => JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? fallback : text).RootElement.Clone(),
                _ => JsonSerializer.SerializeToElement(rawValue)
            };

            return JsonSerializer.Serialize(element, SiteJsonOptions);
        }
        catch
        {
            return fallback;
        }
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
            RaiseOnlineStoreDataChanged();
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
        RaiseOnlineStoreDataChanged();
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
        var pending = StagingProducts
            .Where(p => !p.IsApartado)
            .Where(p => !string.Equals(p.FlashlySyncStatus, "synced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pending.Count == 0)
        {
            StatusMessage = "No hay productos pendientes por enviar a tienda en línea.";
            return;
        }

        if (!ConfirmOnlineStoreSend(pending, "pendientes"))
        {
            StatusMessage = "Envío a tienda en línea cancelado por el usuario.";
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

        if (!ConfirmOnlineStoreSend(selected, "seleccionados"))
        {
            StatusMessage = "Envío a tienda en línea cancelado por el usuario.";
            return;
        }

        await SendOnlineStoreProductsWithProgressAsync(selected, "Envio de seleccionados a tienda en linea");
    }

    private static bool ConfirmOnlineStoreSend(IReadOnlyCollection<StagingProductUi> products, string sourceLabel)
    {
        var nonValidated = products.Count(p => !string.Equals(p.Status, "validated", StringComparison.OrdinalIgnoreCase));
        var validationWarning = nonValidated > 0
            ? $"\nIncluye {nonValidated} registro(s) no validado(s)."
            : "\nTodos los registros están validados.";

        var firstConfirmation = MessageBox.Show(
            $"Se enviarán {products.Count} registro(s) a tienda en línea ({sourceLabel}).{validationWarning}\n\n¿Deseas continuar?",
            "Confirmar envío a tienda en línea",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (firstConfirmation != MessageBoxResult.Yes)
        {
            return false;
        }

        var secondConfirmation = MessageBox.Show(
            "Confirmación final: este envío puede crear/actualizar productos en la tienda en línea.\n\n¿Confirmas ejecutar el envío?",
            "Confirmación final",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return secondConfirmation == MessageBoxResult.Yes;
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

    private async Task SaveOnlineStoreDetailRecordAsync()
    {
        if (OnlineStoreDetailProduct == null)
        {
            StatusMessage = "No hay registro abierto en el detalle.";
            return;
        }

        var productId = OnlineStoreDetailProduct.Product.Id;
        var updated = await _apiClient.UpdateStagingProductAsync(productId, OnlineStoreDetailProduct.Product);
        if (updated == null)
        {
            StatusMessage = "No se pudo guardar el registro desde detalle.";
            return;
        }

        await RefreshStagingProductsAsync();
        var refreshed = StagingProducts.FirstOrDefault(p => p.Product.Id == productId);
        SelectedStagingProduct = refreshed;
        OnlineStoreDetailProduct = refreshed;
        StatusMessage = "Registro guardado desde detalle.";
    }

    private void OpenOnlineStoreDetailDialog()
    {
        if (SelectedStagingProduct == null)
        {
            StatusMessage = "Selecciona un registro para ver el detalle.";
            return;
        }

        OnlineStoreDetailProduct = SelectedStagingProduct;
        IsOnlineStoreDetailVisible = true;
    }

    private void CloseOnlineStoreDetailDialog()
    {
        IsOnlineStoreDetailVisible = false;
        OnlineStoreDetailProduct = null;
    }

    private async Task ValidateOnlineStoreDetailRecordAsync()
    {
        if (OnlineStoreDetailProduct == null)
        {
            StatusMessage = "No hay registro abierto en el detalle.";
            return;
        }

        var productId = OnlineStoreDetailProduct.Product.Id;
        await ValidateProductsAsValidatedAsync(new List<StagingProductUi> { OnlineStoreDetailProduct }, "desde detalle");

        var refreshed = StagingProducts.FirstOrDefault(p => p.Product.Id == productId);
        SelectedStagingProduct = refreshed;
        OnlineStoreDetailProduct = refreshed;

        if (refreshed == null)
        {
            IsOnlineStoreDetailVisible = false;
        }
    }

    private async Task ValidateSelectedOnlineStoreRecordsAsync()
    {
        var selected = OnlineStoreProducts.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No hay registros seleccionados para validar.";
            return;
        }

        await ValidateProductsAsValidatedAsync(selected, "seleccionados");
    }

    private async Task ValidateProductsAsValidatedAsync(List<StagingProductUi> products, string sourceLabel)
    {
        var toValidate = products
            .GroupBy(p => p.Product.Id)
            .Select(g => g.First())
            .Where(p => !string.Equals(p.Product.Status, "validated", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toValidate.Count == 0)
        {
            StatusMessage = "Todos los registros seleccionados ya están validados.";
            return;
        }

        var confirmation = MessageBox.Show(
            $"Se marcarán como validados {toValidate.Count} registro(s) ({sourceLabel}).\n\n¿Deseas continuar?",
            "Validar registros",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusMessage = "Validación cancelada por el usuario.";
            return;
        }

        var validated = 0;
        var failed = 0;
        foreach (var item in toValidate)
        {
            item.Product.Status = "validated";
            var updated = await _apiClient.UpdateStagingProductAsync(item.Product.Id, item.Product);
            if (updated != null)
            {
                validated++;
            }
            else
            {
                failed++;
            }
        }

        await RefreshStagingProductsAsync();
        StatusMessage = failed == 0
            ? $"Registros validados: {validated}."
            : $"Registros validados: {validated}. Fallidos: {failed}.";
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
        if (!IsLiveMonitoringEnabled) return;
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
        if (!IsLiveMonitoringEnabled) return;
        try
        {
            if (SelectedSite == null)
            {
                ScrapeStatusText = "Idle";
                ScrapingPhaseText = "Inactivo";
                ScrapingPhaseColor = "#6B7280";
                IsScraping = false;
                return;
            }

            var status = await _apiClient.GetScrapeStatusAsync(SelectedSite.Id);
            if (status == null)
            {
                ScrapeStatusText = "Idle";
                ScrapingPhaseText = "Inactivo";
                ScrapingPhaseColor = "#6B7280";
                IsScraping = false;
                return;
            }

            ScrapeStatusText = $"{status.State} - {status.Message}";
            IsScraping = status.State == ScrapSAE.Core.Interfaces.ScrapeRunState.Running ||
                         status.State == ScrapSAE.Core.Interfaces.ScrapeRunState.Paused;

            // Update granular phase indicator based on message keywords
            if (!IsScraping)
            {
                ScrapingPhaseText = "Completado / En espera";
                ScrapingPhaseColor = "#6B7280";
            }
            else
            {
                var msg = (status.Message ?? string.Empty).ToLowerInvariant();
                if (msg.Contains("descubr") || msg.Contains("discovery") || msg.Contains("catálogo"))
                {
                    ScrapingPhaseText = "🔍 Fase 1: Descubrimiento de catálogo";
                    ScrapingPhaseColor = "#10B981"; // green
                }
                else if (msg.Contains("paginac") || msg.Contains("página") || msg.Contains("navegan"))
                {
                    ScrapingPhaseText = "📄 Fase 2: Resolución de paginación";
                    ScrapingPhaseColor = "#D97706"; // amber
                }
                else if (msg.Contains("extray") || msg.Contains("extracc") || msg.Contains("producto"))
                {
                    ScrapingPhaseText = "📦 Fase 3: Extracción de productos";
                    ScrapingPhaseColor = "#2563EB"; // blue
                }
                else if (msg.Contains("error") || msg.Contains("fallback"))
                {
                    ScrapingPhaseText = "⚠️ Intentando modo alternativo";
                    ScrapingPhaseColor = "#DC2626"; // red
                }
                else
                {
                    ScrapingPhaseText = "⚙️ Procesando...";
                    ScrapingPhaseColor = "#7C3AED"; // purple
                }
            }
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
        PopulateSiteForm(SelectedSite);
        await PersistSelectedSiteAsync();
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
            RaiseOnlineStoreDataChanged();
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
            OnPropertyChanged(nameof(OnlineStoreVisibleCount));
            SendCheckedToSaeCommand.RaiseCanExecuteChanged();
            ValidateSelectedOnlineStoreRecordsCommand.RaiseCanExecuteChanged();
        }

        if (e.PropertyName == nameof(StagingProductUi.IsApartado))
        {
            RaiseOnlineStoreDataChanged();
        }
    }

    private void ResetStagingProducts(IEnumerable<StagingProduct> products)
    {
        var selectedId = SelectedStagingProduct?.Product.Id;
        var detailId = OnlineStoreDetailProduct?.Product.Id;

        foreach (var existing in StagingProducts)
        {
            existing.PropertyChanged -= OnStagingProductPropertyChanged;
        }

        StagingProducts.Clear();
        foreach (var item in products.OrderByDescending(p => p.CreatedAt))
        {
            StagingProducts.Add(CreateStagingProductUi(item));
        }

        SelectedStagingProduct = selectedId.HasValue
            ? StagingProducts.FirstOrDefault(p => p.Product.Id == selectedId.Value)
            : null;

        if (detailId.HasValue)
        {
            OnlineStoreDetailProduct = StagingProducts.FirstOrDefault(p => p.Product.Id == detailId.Value);
            if (IsOnlineStoreDetailVisible && OnlineStoreDetailProduct == null)
            {
                IsOnlineStoreDetailVisible = false;
            }
        }
        else
        {
            OnlineStoreDetailProduct = null;
        }

        RaiseOnlineStoreDataChanged();
    }

    private void RaiseOnlineStoreDataChanged()
    {
        OnPropertyChanged(nameof(OnlineStorePendingCount));
        OnPropertyChanged(nameof(OnlineStoreApartadosCount));
        RaiseOnlineStoreViewChanged();
    }

    private void RaiseOnlineStoreViewChanged()
    {
        OnPropertyChanged(nameof(OnlineStoreProducts));
        OnPropertyChanged(nameof(OnlineStoreSelectedCount));
        OnPropertyChanged(nameof(OnlineStoreVisibleCount));
        ValidateSelectedOnlineStoreRecordsCommand.RaiseCanExecuteChanged();
    }

    private async Task RefreshLiveLogsAsync()
    {
        if (!IsLiveMonitoringEnabled || SelectedSite == null) return;
        
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

