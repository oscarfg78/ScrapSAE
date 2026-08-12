using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Desktop.Infrastructure;

namespace ScrapSAE.Desktop.ViewModels.ConcurrentWizard;

// ─────────────────────────────────────────────────────────────────────────────
// Step 1 — Excel Ingestion
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel del Step 1: carga el Excel, muestra preview y requiere mapeo de columnas.
/// </summary>
public sealed class Step1ExcelIngestionViewModel : ViewModelBase
{
    private readonly IExcelIngestionService _excelService;
    private readonly ScrapSAE.Desktop.Services.ApiClient _apiClient;

    private string _filePath = string.Empty;
    private string[] _columnHeaders = [];
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private int? _skuColumnIndex;
    private int? _costColumnIndex;
    private int? _marginColumnIndex;
    private DataTable? _previewTable;
    
    private ObservableCollection<ScrapSAE.Core.Entities.SiteProfile> _availableSites = new();
    private ScrapSAE.Core.Entities.SiteProfile? _selectedSite;

    public Step1ExcelIngestionViewModel(IExcelIngestionService excelService, ScrapSAE.Desktop.Services.ApiClient apiClient)
    {
        _excelService = excelService;
        _apiClient = apiClient;
        PreviewRows = new ObservableCollection<string[]>();
        LoadFileCommand = new AsyncCommand(ExecuteLoadFileAsync);
        _ = LoadSitesAsync();
    }

    private async Task LoadSitesAsync()
    {
        try
        {
            var sites = await _apiClient.GetSitesAsync();
            AvailableSites = new ObservableCollection<ScrapSAE.Core.Entities.SiteProfile>(sites.OrderBy(s => s.Name));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error cargando proveedores: {ex.Message}";
        }
    }

    public async Task InitializeFromSessionAsync(ScrapSAE.Core.DTOs.ConcurrentWizardSession session)
    {
        // Wait for sites to load if they are not loaded yet
        if (AvailableSites.Count == 0)
        {
            await LoadSitesAsync();
        }

        if (session.TargetSiteId.HasValue)
        {
            SelectedSite = AvailableSites.FirstOrDefault(s => s.Id == session.TargetSiteId.Value);
        }

        if (!string.IsNullOrEmpty(session.ExcelFilePath) && System.IO.File.Exists(session.ExcelFilePath))
        {
            await LoadFileFromPathAsync(session.ExcelFilePath);

            // Restore column mappings
            SkuColumnIndex = session.ColumnMapping.SkuColumnIndex;
            CostColumnIndex = session.ColumnMapping.CostoColumnIndex;
            MarginColumnIndex = session.ColumnMapping.MarginColumnIndex;
            CategoryColumnIndex = session.ColumnMapping.CategoryColumnIndex;
        }
    }

    // ── Properties ───────────────────────────────────────────────────────────

    public ObservableCollection<ScrapSAE.Core.Entities.SiteProfile> AvailableSites
    {
        get => _availableSites;
        private set => SetField(ref _availableSites, value);
    }

    public ScrapSAE.Core.Entities.SiteProfile? SelectedSite
    {
        get => _selectedSite;
        set { SetField(ref _selectedSite, value); Validate(); }
    }

    public string FilePath
    {
        get => _filePath;
        private set { SetField(ref _filePath, value); OnPropertyChanged(nameof(HasFile)); }
    }

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    public string[] ColumnHeaders
    {
        get => _columnHeaders;
        private set { SetField(ref _columnHeaders, value); }
    }

    public DataView? PreviewView => _previewTable?.DefaultView;

    public ObservableCollection<string[]> PreviewRows { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set { SetField(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>Índice (base 0) de la columna SKU seleccionada por el usuario.</summary>
    public int? SkuColumnIndex
    {
        get => _skuColumnIndex;
        set { SetField(ref _skuColumnIndex, value); OnPropertyChanged(nameof(CanContinue)); }
    }

    /// <summary>Índice (base 0) de la columna Costo del Proveedor seleccionada.</summary>
    public int? CostColumnIndex
    {
        get => _costColumnIndex;
        set { SetField(ref _costColumnIndex, value); OnPropertyChanged(nameof(CanContinue)); }
    }

    /// <summary>Índice (base 0) opcional de la columna Margen de Ganancia %.</summary>
    public int? MarginColumnIndex
    {
        get => _marginColumnIndex;
        set => SetField(ref _marginColumnIndex, value);
    }

    private int? _categoryColumnIndex;
    /// <summary>Índice (base 0) opcional de la columna Categoría.</summary>
    public int? CategoryColumnIndex
    {
        get => _categoryColumnIndex;
        set => SetField(ref _categoryColumnIndex, value);
    }

    /// <summary>El usuario puede continuar cuando ambas columnas obligatorias están mapeadas y hay un proveedor seleccionado.</summary>
    public bool CanContinue =>
        SkuColumnIndex.HasValue &&
        CostColumnIndex.HasValue &&
        SkuColumnIndex != CostColumnIndex &&
        HasFile &&
        SelectedSite != null &&
        !IsLoading;

    private void Validate()
    {
        OnPropertyChanged(nameof(CanContinue));
    }

    public int TotalRowCount { get; private set; }

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand LoadFileCommand { get; }

    // ── Methods ──────────────────────────────────────────────────────────────

    private async Task ExecuteLoadFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Archivos de Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar Lista de Precios / Productos"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFileFromPathAsync(dialog.FileName);
        }
    }

    public async Task LoadFileFromPathAsync(string path)
    {
        FilePath = path;
        StatusMessage = "Leyendo vista previa del Excel...";
        ErrorMessage = string.Empty;
        IsLoading = true;

        try
        {
            var preview = await _excelService.PreviewAsync(FilePath);
            if (!string.IsNullOrEmpty(preview.ErrorMessage))
            {
                ErrorMessage = preview.ErrorMessage;
                return;
            }

            ColumnHeaders = preview.ColumnHeaders;
            TotalRowCount = preview.TotalRowCount;

            // Construir DataTable dinámico para WPF DataGrid
            var dt = new DataTable();
            foreach (var header in preview.ColumnHeaders)
                dt.Columns.Add(string.IsNullOrWhiteSpace(header) ? "Columna" : header, typeof(string));

            foreach (var row in preview.PreviewRows)
            {
                var dr = dt.NewRow();
                for (int i = 0; i < Math.Min(row.Length, dt.Columns.Count); i++)
                    dr[i] = row[i];
                dt.Rows.Add(dr);
            }

            _previewTable = dt;
            OnPropertyChanged(nameof(PreviewView));

            StatusMessage = $"Archivo cargado: {TotalRowCount} filas, {preview.ColumnHeaders.Length} columnas";

            AutoDetectColumns(preview.ColumnHeaders);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al leer el archivo: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanContinue));
        }
    }

    private void AutoDetectColumns(string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].ToLowerInvariant();
            if (SkuColumnIndex == null && (h.Contains("sku") || h == "código" || h == "codigo" || h == "clave"))
                SkuColumnIndex = i;
            if (CostColumnIndex == null && (h.Contains("costo") || h.Contains("precio prov") || h.Contains("cost")))
                CostColumnIndex = i;
            if (MarginColumnIndex == null && (h.Contains("margen") || h.Contains("ganancia") || h.Contains("profit") || h.Contains("margin")))
                MarginColumnIndex = i;
            if (CategoryColumnIndex == null && (h.Contains("categoría") || h.Contains("categoria") || h.Contains("category") || h == "cat"))
                CategoryColumnIndex = i;
        }
    }

    /// <summary>Construye el ExcelColumnMapping con las selecciones del usuario.</summary>
    public ExcelColumnMapping BuildMapping() => new()
    {
        SkuColumnIndex   = SkuColumnIndex!.Value,
        CostoColumnIndex = CostColumnIndex!.Value,
        MarginColumnIndex = MarginColumnIndex,
        CategoryColumnIndex = CategoryColumnIndex
    };
}
