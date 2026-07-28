using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.Models;
using ScrapSAE.Desktop.Services;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Desktop.ViewModels;

/// <summary>
/// Configuración editable del proveedor durante el wizard (Paso 3)
/// </summary>
public class WizardSiteConfig
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string StrategyType { get; set; } = "Generic";

    // Selector primario del contenedor de productos
    public string ProductContainerSelector { get; set; } = string.Empty;
    // Selector de cada tarjeta de producto
    public string ProductCardSelector { get; set; } = string.Empty;

    // Selectores de campos clave
    public string SkuSelector { get; set; } = string.Empty;
    public string NameSelector { get; set; } = string.Empty;
    public string ImageSelector { get; set; } = string.Empty;
    public string PriceSelector { get; set; } = string.Empty;
    public string CharacteristicsSelector { get; set; } = string.Empty;

    // Estrategias habilitadas
    public bool UseDirectStrategy { get; set; } = true;
    public bool UseListStrategy { get; set; } = false;
    public bool UseFamiliesStrategy { get; set; } = false;

    // Additional Execution Config
    public int MaxProductsTest { get; set; } = 5;
    public string AuthUsername { get; set; } = string.Empty;
    public string AuthPassword { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel del wizard de creación de proveedores.
/// Gestiona el flujo multi-paso: URL → Análisis IA → Revisión → Test de scrape → Confirmación.
/// </summary>
public sealed class ProviderWizardViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    // ─── Step State ────────────────────────────────────────────────────────────
    private int _currentStep = 1;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;
    private CancellationTokenSource? _cts;

    // ─── Step 1 ────────────────────────────────────────────────────────────────
    private string _url = string.Empty;
    private string _productDetailUrl = string.Empty;
    private ObservableCollection<string> _discoveredCandidateUrls = new();
    private string _brandOverride = string.Empty;

    // ─── Step 2 ────────────────────────────────────────────────────────────────
    private PageAnalysisResult? _analysisResult;

    // ─── Step 3 ────────────────────────────────────────────────────────────────
    private WizardSiteConfig _wizardConfig = new();
    private string _configValidationMessage = string.Empty;

    // ─── Step 4 ────────────────────────────────────────────────────────────────
    private ObservableCollection<WizardScrapePreviewProduct> _previewProducts = new();
    private int _totalProductsFound;
    private Guid? _tempSiteId;

    // ─── Step 5 ────────────────────────────────────────────────────────────────
    private int _skuCoverage;
    private int _nameCoverage;
    private int _imageCoverage;
    private int _priceCoverage;
    private bool _wasSuccessful;

    // ─── Result ────────────────────────────────────────────────────────────────
    /// <summary>Site creado al final del wizard (null si se canceló).</summary>
    public SiteProfile? CreatedSite { get; private set; }
    public bool WasSuccessful
    {
        get => _wasSuccessful;
        private set
        {
            if (SetField(ref _wasSuccessful, value))
            {
                (SaveProviderCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────────────────

    public ProviderWizardViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;

        AnalyzeCommand = new AsyncCommand(ExecuteAnalyzeAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(Url));
        GoToConfigCommand = new AsyncCommand(ExecuteGoToConfigAsync, () => !IsBusy && AnalysisResult != null);
        RunTestScrapeCommand = new AsyncCommand(ExecuteRunTestScrapeAsync, () => !IsBusy && string.IsNullOrEmpty(ConfigValidationMessage));
        GoBackToConfigCommand = new RelayCommand(GoBackToConfig);
        SaveProviderCommand = new AsyncCommand(ExecuteSaveProviderAsync, () => !IsBusy && WasSuccessful == false);
        CancelCommand = new AsyncCommand(ExecuteCancelAsync);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Observable Properties
    // ──────────────────────────────────────────────────────────────────────────

    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            SetField(ref _currentStep, value);
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(IsStep4));
            OnPropertyChanged(nameof(IsStep5));
            OnPropertyChanged(nameof(Step1Completed));
            OnPropertyChanged(nameof(Step2Completed));
            OnPropertyChanged(nameof(Step3Completed));
            OnPropertyChanged(nameof(Step4Completed));
        }
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep5 => CurrentStep == 5;
    public bool Step1Completed => CurrentStep > 1;
    public bool Step2Completed => CurrentStep > 2;
    public bool Step3Completed => CurrentStep > 3;
    public bool Step4Completed => CurrentStep > 4;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetField(ref _isBusy, value);
            (AnalyzeCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (GoToConfigCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (RunTestScrapeCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            (SaveProviderCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            SetField(ref _errorMessage, value);
            HasError = !string.IsNullOrEmpty(value);
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetField(ref _hasError, value);
    }

    // Step 1
    public string Url
    {
        get => _url;
        set
        {
            SetField(ref _url, value);
            ((AsyncCommand)AnalyzeCommand).RaiseCanExecuteChanged();
        }
    }

    public string ProductDetailUrl
    {
        get => _productDetailUrl;
        set => SetField(ref _productDetailUrl, value);
    }

    public string BrandOverride
    {
        get => _brandOverride;
        set => SetField(ref _brandOverride, value);
    }

    // Step 2
    public PageAnalysisResult? AnalysisResult
    {
        get => _analysisResult;
        private set
        {
            SetField(ref _analysisResult, value);
            (GoToConfigCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    // Step 3
    public WizardSiteConfig WizardConfig
    {
        get => _wizardConfig;
        set => SetField(ref _wizardConfig, value);
    }

    public string ConfigValidationMessage
    {
        get => _configValidationMessage;
        private set
        {
            SetField(ref _configValidationMessage, value);
            (RunTestScrapeCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> DiscoveredCandidateUrls
    {
        get => _discoveredCandidateUrls;
        private set => SetField(ref _discoveredCandidateUrls, value);
    }

    // Step 4
    public ObservableCollection<WizardScrapePreviewProduct> PreviewProducts
    {
        get => _previewProducts;
        private set => SetField(ref _previewProducts, value);
    }

    public int TotalProductsFound
    {
        get => _totalProductsFound;
        private set => SetField(ref _totalProductsFound, value);
    }

    public string PreviewSummary => TotalProductsFound > PreviewProducts.Count
        ? $"Mostrando {PreviewProducts.Count} de {TotalProductsFound} productos encontrados"
        : $"{PreviewProducts.Count} productos encontrados";

    // Step 5 coverage stats
    public int SkuCoverage { get => _skuCoverage; private set => SetField(ref _skuCoverage, value); }
    public int NameCoverage { get => _nameCoverage; private set => SetField(ref _nameCoverage, value); }
    public int ImageCoverage { get => _imageCoverage; private set => SetField(ref _imageCoverage, value); }
    public int PriceCoverage { get => _priceCoverage; private set => SetField(ref _priceCoverage, value); }

    // ──────────────────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────────────────

    public ICommand AnalyzeCommand { get; }
    public ICommand GoToConfigCommand { get; }
    public ICommand RunTestScrapeCommand { get; }
    public ICommand GoBackToConfigCommand { get; }
    public ICommand SaveProviderCommand { get; }
    public ICommand CancelCommand { get; }

    // ──────────────────────────────────────────────────────────────────────────
    // Step 1 → 2: Analyze URL
    // ──────────────────────────────────────────────────────────────────────────

    private async Task ExecuteAnalyzeAsync()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = "La URL debe comenzar con http:// o https://";
            return;
        }

        ErrorMessage = string.Empty;
        IsBusy = true;
        StatusMessage = "Analizando estructura del catálogo... (esto puede tardar hasta 90 segundos)";
        CurrentStep = 2;
        AnalysisResult = null;

        _cts = new CancellationTokenSource();

        try
        {
            var result = await _apiClient.AnalyzePageAsync(Url, string.IsNullOrWhiteSpace(ProductDetailUrl) ? null : ProductDetailUrl.Trim());
            if (result == null)
            {
                ErrorMessage = "No se pudo analizar la página. Verifica la URL e intenta de nuevo.";
                CurrentStep = 1;
                return;
            }

            if (result.CandidateUrls != null && result.CandidateUrls.Any())
            {
                DiscoveredCandidateUrls.Clear();
                foreach (var url in result.CandidateUrls)
                {
                    DiscoveredCandidateUrls.Add(url);
                }
            }

            if (!string.IsNullOrWhiteSpace(result.CandidateDetailUrl) && string.IsNullOrWhiteSpace(ProductDetailUrl))
            {
                ProductDetailUrl = result.CandidateDetailUrl;
            }

            AnalysisResult = result;
            StatusMessage = result.IsProductCatalog
                ? $"✓ Catálogo detectado: {result.DetectedFields.Count} campos analizados"
                : "⚠ La página no parece ser un catálogo de productos. Puedes continuar manualmente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error en el análisis: {ex.Message}";
            CurrentStep = 1;
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Step 2 → 3: Populate Config
    // ──────────────────────────────────────────────────────────────────────────

    private Task ExecuteGoToConfigAsync()
    {
        if (AnalysisResult != null)
        {
            PopulateConfigFromAnalysis(AnalysisResult);
        }
        CurrentStep = 3;
        return Task.CompletedTask;
    }

    private string GetBestSelectorString(DualSelector? descriptor)
    {
        if (descriptor == null) return string.Empty;
        if (string.IsNullOrWhiteSpace(descriptor.Css) && string.IsNullOrWhiteSpace(descriptor.XPath)) return string.Empty;
        
        var parts = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(descriptor.Css))
        {
            parts.Add($"css={descriptor.Css}");
        }
        
        if (!string.IsNullOrWhiteSpace(descriptor.XPath))
        {
            parts.Add($"xpath={descriptor.XPath}");
        }
        
        return string.Join(", ", parts);
    }

    private void PopulateConfigFromAnalysis(PageAnalysisResult result)
    {
        // Derive a clean name from the URL domain
        var domain = Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", "").Split('.')[0]
            : "Proveedor";
        var name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(domain);

        WizardConfig = new WizardSiteConfig
        {
            Name = name,
            BaseUrl = Url,
            StrategyType = result.StrategyType,
            ProductContainerSelector = GetBestSelectorString(result.ProductContainerSelector),
            ProductCardSelector = GetBestSelectorString(result.ProductCardSelector),
            SkuSelector = GetBestSelectorString(result.SkuSelector),
            NameSelector = GetBestSelectorString(result.NameSelector),
            ImageSelector = GetBestSelectorString(result.ImageSelector),
            PriceSelector = GetBestSelectorString(result.PriceSelector),
            CharacteristicsSelector = GetBestSelectorString(result.CharacteristicsSelector),
        };

        // Configure strategies from recommendations
        foreach (var strategy in result.RecommendedStrategies.OrderBy(s => s.Priority))
        {
            switch (strategy.StrategyName.ToLowerInvariant())
            {
                case "direct":
                    WizardConfig.UseDirectStrategy = true;
                    break;
                case "list":
                    WizardConfig.UseListStrategy = true;
                    break;
                case "families":
                    WizardConfig.UseFamiliesStrategy = true;
                    break;
            }
        }

        ValidateConfig();
    }

    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(WizardConfig.Name))
        {
            ConfigValidationMessage = "El nombre del proveedor es obligatorio.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(WizardConfig.StrategyType) &&
            (WizardConfig.StrategyType.Equals("Shopify", StringComparison.OrdinalIgnoreCase) ||
             WizardConfig.StrategyType.Equals("ShopifyApi", StringComparison.OrdinalIgnoreCase)))
        {
            ConfigValidationMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(WizardConfig.ProductCardSelector)
            && string.IsNullOrWhiteSpace(WizardConfig.ProductContainerSelector)
            && string.IsNullOrWhiteSpace(WizardConfig.NameSelector))
        {
            ConfigValidationMessage = "Se requiere al menos un selector (contenedor, tarjeta o nombre).";
            return;
        }
        ConfigValidationMessage = string.Empty;
    }

    public void NotifyConfigChanged()
    {
        ValidateConfig();
        OnPropertyChanged(nameof(WizardConfig));
    }

    private void GoBackToConfig()
    {
        ErrorMessage = string.Empty;
        CurrentStep = 3;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Step 3 → 4: Run Test Scrape
    // ──────────────────────────────────────────────────────────────────────────

    private async Task ExecuteRunTestScrapeAsync()
    {
        ValidateConfig();
        if (!string.IsNullOrEmpty(ConfigValidationMessage))
        {
            return;
        }

        ErrorMessage = string.Empty;
        IsBusy = true;
        StatusMessage = "Ejecutando scrape de prueba...";
        CurrentStep = 4;
        PreviewProducts.Clear();
        TotalProductsFound = 0;

        _cts = new CancellationTokenSource();

        try
        {
            var req = new ExtractionExecutionRequest
            {
                RunId = Guid.NewGuid().ToString(),
                IsDemo = true,
                ProductLimit = WizardConfig.MaxProductsTest,
                ProviderConfig = new ProviderConfigurationSnapshot
                {
                    CatalogUrl = WizardConfig.BaseUrl,
                    DetailUrl = ProductDetailUrl,
                    AuthParameters = new Dictionary<string, string>
                    {
                        { "username", WizardConfig.AuthUsername },
                        { "password", WizardConfig.AuthPassword }
                    },
                    Selectors = new SiteSelectors
                    {
                        ProductListSelector = WizardConfig.ProductContainerSelector,
                        ProductCardClassPrefix = WizardConfig.ProductCardSelector,
                        ProductLinkSelector = WizardConfig.ProductCardSelector, // Added this mapping
                        SkuSelector = WizardConfig.SkuSelector,
                        TitleSelector = WizardConfig.NameSelector,
                        ImageSelector = WizardConfig.ImageSelector,
                        PriceSelector = WizardConfig.PriceSelector,
                        CharacteristicsSelector = WizardConfig.CharacteristicsSelector
                    }
                }
            };

            var scrapeResult = await _apiClient.RunDemoScrapingAsync(req);

            TotalProductsFound = scrapeResult?.Products.Count ?? 0;
            var products = scrapeResult?.Products.Take(WizardConfig.MaxProductsTest).ToList() ?? new List<ReconciledProduct>();

            foreach (var sp in products)
            {
                var preview = new WizardScrapePreviewProduct
                {
                    Sku = sp.Sku,
                    Name = sp.Title ?? sp.Sku,
                    ImageUrl = sp.ImageUrl ?? sp.ImageUrls.FirstOrDefault(),
                    Price = sp.Price?.ToString("F2"),
                    CharacteristicsCount = sp.FieldProvenance.Count, // Approximation for preview
                    SourceUrl = sp.SourceUrl
                };

                if (!string.IsNullOrWhiteSpace(preview.Sku)) preview.FoundFields.Add("SKU");
                else preview.MissingFields.Add("SKU");

                if (!string.IsNullOrWhiteSpace(preview.Name)) preview.FoundFields.Add("Nombre");
                else preview.MissingFields.Add("Nombre");

                if (!string.IsNullOrWhiteSpace(preview.ImageUrl)) preview.FoundFields.Add("Imagen");
                else preview.MissingFields.Add("Imagen");

                if (!string.IsNullOrWhiteSpace(preview.Price)) preview.FoundFields.Add("Precio");
                else preview.MissingFields.Add("Precio");

                PreviewProducts.Add(preview);
            }

            OnPropertyChanged(nameof(PreviewSummary));

            if (PreviewProducts.Count == 0 && string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = "No se encontraron productos. Revisa los selectores en el paso anterior.";
                return;
            }

            // Calculate coverage stats for step 5
            if (PreviewProducts.Count > 0)
            {
                SkuCoverage = PreviewProducts.Count(p => p.FoundFields.Contains("SKU")) * 100 / PreviewProducts.Count;
                NameCoverage = PreviewProducts.Count(p => p.FoundFields.Contains("Nombre")) * 100 / PreviewProducts.Count;
                ImageCoverage = PreviewProducts.Count(p => p.FoundFields.Contains("Imagen")) * 100 / PreviewProducts.Count;
                PriceCoverage = PreviewProducts.Count(p => p.FoundFields.Contains("Precio")) * 100 / PreviewProducts.Count;
            }

            CurrentStep = 5;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error inesperado: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Step 5: Save Provider
    // ──────────────────────────────────────────────────────────────────────────

    private async Task ExecuteSaveProviderAsync()
    {
        IsBusy = true;
        StatusMessage = "Guardando proveedor...";
        ErrorMessage = string.Empty;

        try
        {
            if (_tempSiteId.HasValue)
            {
                // Update the temp site: remove [TEMP] prefix and finalize settings
                var finalSite = BuildSiteProfile(WizardConfig.Name);
                finalSite.Id = _tempSiteId.Value;
                finalSite.CreatedAt = DateTime.UtcNow;

                var updated = await _apiClient.UpdateSiteAsync(_tempSiteId.Value, finalSite);
                if (updated == null)
                {
                    ErrorMessage = "No se pudo guardar el proveedor. Intenta de nuevo.";
                    return;
                }
                CreatedSite = updated;
            }
            else
            {
                // No temp site, create fresh
                var newSite = BuildSiteProfile(WizardConfig.Name);
                var created = await _apiClient.CreateSiteAsync(newSite);
                if (created == null)
                {
                    ErrorMessage = "No se pudo crear el proveedor. Intenta de nuevo.";
                    return;
                }
                CreatedSite = created;
                _tempSiteId = created.Id;
            }

            WasSuccessful = true;
            _tempSiteId = null; // Don't delete on cancel since we renamed it
            StatusMessage = $"✓ Proveedor \"{CreatedSite!.Name}\" guardado exitosamente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al guardar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancel
    // ──────────────────────────────────────────────────────────────────────────

    private async Task ExecuteCancelAsync()
    {
        _cts?.Cancel();

        // If there's a temp site and we haven't saved, delete it
        if (_tempSiteId.HasValue && !WasSuccessful)
        {
            try
            {
                await _apiClient.DeleteSiteAsync(_tempSiteId.Value);
                _tempSiteId = null;
            }
            catch
            {
                // Best-effort; TempSiteCleanupService will handle it later
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private SiteProfile BuildSiteProfile(string name)
    {
        var siteSelectors = new SiteSelectors
        {
            ProductListSelector = string.IsNullOrWhiteSpace(WizardConfig.ProductContainerSelector) ? null : WizardConfig.ProductContainerSelector,
            ProductCardClassPrefix = string.IsNullOrWhiteSpace(WizardConfig.ProductCardSelector) ? null : WizardConfig.ProductCardSelector,
            ProductLinkSelector = string.IsNullOrWhiteSpace(WizardConfig.ProductCardSelector) ? null : WizardConfig.ProductCardSelector,
            SkuSelector = string.IsNullOrWhiteSpace(WizardConfig.SkuSelector) ? null : WizardConfig.SkuSelector,
            TitleSelector = string.IsNullOrWhiteSpace(WizardConfig.NameSelector) ? null : WizardConfig.NameSelector,
            ImageSelector = string.IsNullOrWhiteSpace(WizardConfig.ImageSelector) ? null : WizardConfig.ImageSelector,
            PriceSelector = string.IsNullOrWhiteSpace(WizardConfig.PriceSelector) ? null : WizardConfig.PriceSelector,
            CharacteristicsSelector = string.IsNullOrWhiteSpace(WizardConfig.CharacteristicsSelector) ? null : WizardConfig.CharacteristicsSelector
        };

        var strategies = new List<ScrapingStrategyDefinition>();
        if (WizardConfig.UseDirectStrategy)
            strategies.Add(new ScrapingStrategyDefinition { StrategyName = "Direct", Priority = 1, IsEnabled = true });
        if (WizardConfig.UseListStrategy)
            strategies.Add(new ScrapingStrategyDefinition { StrategyName = "List", Priority = 2, IsEnabled = true });
        if (WizardConfig.UseFamiliesStrategy)
            strategies.Add(new ScrapingStrategyDefinition { StrategyName = "Families", Priority = 3, IsEnabled = true });

        if (WizardConfig.StrategyType.Equals("Shopify", StringComparison.OrdinalIgnoreCase)
            && !strategies.Any(s => s.StrategyName.Equals("Shopify", StringComparison.OrdinalIgnoreCase)))
        {
            strategies.Insert(0, new ScrapingStrategyDefinition { StrategyName = "Shopify", Priority = 1, IsEnabled = true });
        }

        var strategyType = strategies.Any() ? "Orchestrated" : WizardConfig.StrategyType;

        return new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = name,
            BaseUrl = WizardConfig.BaseUrl,
            StrategyType = strategyType,
            IsActive = true,
            RequiresLogin = false,
            MaxProductsPerScrape = 120,
            BrandOverride = string.IsNullOrWhiteSpace(BrandOverride) ? null : BrandOverride.Trim(),
            Selectors = siteSelectors,
            SecondarySelectors = AnalysisResult?.SecondarySelectors ?? new Dictionary<string, List<string>>(),
            Strategies = strategies,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
