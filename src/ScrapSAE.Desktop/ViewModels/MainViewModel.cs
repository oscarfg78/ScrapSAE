using System.Collections.ObjectModel;
using System.Windows.Threading;
using ScrapSAE.Core.Entities;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.Models;
using ScrapSAE.Desktop.Services;

namespace ScrapSAE.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _saeTimer;
    private SiteProfile? _selectedSite;
    private StagingProduct? _selectedStagingProduct;
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

    public MainViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        _saeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(_saeScheduleMinutes) };
        _saeTimer.Tick += async (_, _) => await SendPendingToSaeAsync();

        LoadAllCommand = new AsyncCommand(LoadAllAsync);
        CreateSiteCommand = new AsyncCommand(CreateSiteAsync);
        UpdateSiteCommand = new AsyncCommand(UpdateSiteAsync);
        DeleteSiteCommand = new AsyncCommand(DeleteSiteAsync);

        CreateStagingCommand = new AsyncCommand(CreateStagingAsync);
        UpdateStagingCommand = new AsyncCommand(UpdateStagingAsync);
        DeleteStagingCommand = new AsyncCommand(DeleteStagingAsync);

        CreateCategoryCommand = new AsyncCommand(CreateCategoryAsync);
        UpdateCategoryCommand = new AsyncCommand(UpdateCategoryAsync);
        DeleteCategoryCommand = new AsyncCommand(DeleteCategoryAsync);

        CreateSyncLogCommand = new AsyncCommand(CreateSyncLogAsync);
        UpdateSyncLogCommand = new AsyncCommand(UpdateSyncLogAsync);
        DeleteSyncLogCommand = new AsyncCommand(DeleteSyncLogAsync);

        CreateReportCommand = new AsyncCommand(CreateReportAsync);
        UpdateReportCommand = new AsyncCommand(UpdateReportAsync);
        DeleteReportCommand = new AsyncCommand(DeleteReportAsync);

        RunScrapingCommand = new AsyncCommand(RunScrapingAsync, () => SelectedSite != null);
        SendSelectedToSaeCommand = new AsyncCommand(SendSelectedToSaeAsync, () => SelectedStagingProduct != null);
        SendPendingToSaeCommand = new AsyncCommand(SendPendingToSaeAsync);

        LoadSettingsCommand = new AsyncCommand(LoadSettingsAsync);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync);
        RunDiagnosticsCommand = new AsyncCommand(RunDiagnosticsAsync);
        TestBackendCommand = new AsyncCommand(TestBackendAsync);
    }

    public ObservableCollection<SiteProfile> Sites { get; } = new();
    public ObservableCollection<StagingProduct> StagingProducts { get; } = new();
    public ObservableCollection<CategoryMapping> CategoryMappings { get; } = new();
    public ObservableCollection<SyncLog> SyncLogs { get; } = new();
    public ObservableCollection<ExecutionReport> ExecutionReports { get; } = new();

    public SiteProfile? SelectedSite
    {
        get => _selectedSite;
        set
        {
            if (SetField(ref _selectedSite, value))
            {
                ((AsyncCommand)RunScrapingCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public StagingProduct? SelectedStagingProduct
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

            StagingProducts.Clear();
            foreach (var item in await _apiClient.GetStagingProductsAsync())
            {
                StagingProducts.Add(item);
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

            ExecutionReports.Clear();
            foreach (var item in await _apiClient.GetExecutionReportsAsync())
            {
                ExecutionReports.Add(item);
            }

            StatusMessage = "Listo";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar datos: {ex.Message}";
        }
    }

    private async Task CreateSiteAsync()
    {
        var site = SelectedSite ?? new SiteProfile { Name = "Nuevo", BaseUrl = "https://", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var created = await _apiClient.CreateSiteAsync(site);
        if (created != null)
        {
            Sites.Add(created);
            SelectedSite = created;
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
    }

    private async Task CreateStagingAsync()
    {
        var product = SelectedStagingProduct ?? new StagingProduct
        {
            SiteId = SelectedSite?.Id ?? Guid.Empty,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var created = await _apiClient.CreateStagingProductAsync(product);
        if (created != null)
        {
            StagingProducts.Add(created);
            SelectedStagingProduct = created;
        }
    }

    private async Task UpdateStagingAsync()
    {
        if (SelectedStagingProduct == null)
        {
            return;
        }

        var updated = await _apiClient.UpdateStagingProductAsync(SelectedStagingProduct.Id, SelectedStagingProduct);
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

        await _apiClient.DeleteStagingProductAsync(SelectedStagingProduct.Id);
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
        ScrapeResult = await _apiClient.RunScrapingAsync(SelectedSite.Id);
        StatusMessage = "Scraping finalizado.";
        await LoadAllAsync();
    }

    private async Task SendSelectedToSaeAsync()
    {
        if (SelectedStagingProduct == null)
        {
            return;
        }

        var ok = await _apiClient.SendToSaeAsync(SelectedStagingProduct.Id);
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
}
