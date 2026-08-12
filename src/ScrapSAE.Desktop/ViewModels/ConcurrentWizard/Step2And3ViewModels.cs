using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Desktop.Infrastructure;

namespace ScrapSAE.Desktop.ViewModels.ConcurrentWizard;

// ─────────────────────────────────────────────────────────────────────────────
// Step 2 — Target Configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel del Step 2: configura hasta dos fuentes objetivo de búsqueda.
/// Permite detectar modo de búsqueda y descubrir selectores con IA.
/// </summary>
public sealed class Step2TargetConfigViewModel : ViewModelBase
{
    private readonly ISelectorDiscoveryService _selectorService;

    // Target 1 (obligatorio)
    private string _target1Url = string.Empty;
    private SearchMode _target1Mode = SearchMode.QueryParam;
    private string _target1UrlTemplate = string.Empty;
    private SelectorConfig _target1Selectors = new();
    private string _target1Status = string.Empty;
    private bool _isDiscoveringTarget1;

    // Target 2 (opcional)
    private bool _useTarget2;
    private string _target2Url = string.Empty;
    private SearchMode _target2Mode = SearchMode.QueryParam;
    private string _target2UrlTemplate = string.Empty;
    private SelectorConfig _target2Selectors = new();
    private string _target2Status = string.Empty;
    private bool _isDiscoveringTarget2;

    private string _sampleSku = string.Empty;

    public Step2TargetConfigViewModel(ISelectorDiscoveryService selectorService)
    {
        _selectorService = selectorService;

        DiscoverTarget1Command = new AsyncCommand(
            () => ExecuteDiscoverAsync(1),
            () => !string.IsNullOrWhiteSpace(Target1Url) && !_isDiscoveringTarget1);

        DiscoverTarget2Command = new AsyncCommand(
            () => ExecuteDiscoverAsync(2),
            () => UseTarget2 && !string.IsNullOrWhiteSpace(Target2Url) && !_isDiscoveringTarget2);
    }

    // ── Target 1 ─────────────────────────────────────────────────────────────

    public string Target1Url
    {
        get => _target1Url;
        set
        {
            SetField(ref _target1Url, value);
            if (value.Contains("{sku}") || value.Contains("[sku]"))
            {
                if (!value.Contains("?"))
                    Target1Mode = SearchMode.DirectDetail;
                else
                    Target1Mode = SearchMode.QueryParam;

                if (string.IsNullOrWhiteSpace(Target1UrlTemplate))
                    Target1UrlTemplate = value;
            }
            (DiscoverTarget1Command as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    public SearchMode Target1Mode
    {
        get => _target1Mode;
        set { SetField(ref _target1Mode, value); OnPropertyChanged(nameof(CanContinue)); }
    }

    public string Target1UrlTemplate
    {
        get => _target1UrlTemplate;
        set => SetField(ref _target1UrlTemplate, value);
    }

    public SelectorConfig Target1Selectors
    {
        get => _target1Selectors;
        set => SetField(ref _target1Selectors, value);
    }

    public string Target1Status
    {
        get => _target1Status;
        private set => SetField(ref _target1Status, value);
    }

    // ── Target 2 ─────────────────────────────────────────────────────────────

    public bool UseTarget2
    {
        get => _useTarget2;
        set
        {
            SetField(ref _useTarget2, value);
            OnPropertyChanged(nameof(CanContinue));
            (DiscoverTarget2Command as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string Target2Url
    {
        get => _target2Url;
        set
        {
            SetField(ref _target2Url, value);
            if (value.Contains("{sku}") || value.Contains("[sku]"))
            {
                if (!value.Contains("?"))
                    Target2Mode = SearchMode.DirectDetail;
                else
                    Target2Mode = SearchMode.QueryParam;

                if (string.IsNullOrWhiteSpace(Target2UrlTemplate))
                    Target2UrlTemplate = value;
            }
            (DiscoverTarget2Command as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    public SearchMode Target2Mode
    {
        get => _target2Mode;
        set { SetField(ref _target2Mode, value); OnPropertyChanged(nameof(CanContinue)); }
    }

    public string Target2UrlTemplate
    {
        get => _target2UrlTemplate;
        set => SetField(ref _target2UrlTemplate, value);
    }

    public SelectorConfig Target2Selectors
    {
        get => _target2Selectors;
        set => SetField(ref _target2Selectors, value);
    }

    public string Target2Status
    {
        get => _target2Status;
        private set => SetField(ref _target2Status, value);
    }

    // ── General ──────────────────────────────────────────────────────────────

    public string SampleSku
    {
        get => _sampleSku;
        set => SetField(ref _sampleSku, value);
    }

    public bool CanContinue => !string.IsNullOrWhiteSpace(Target1Url) &&
                               Target1Selectors.IsValid(Target1Mode) &&
                               (!UseTarget2 || (!string.IsNullOrWhiteSpace(Target2Url) && Target2Selectors.IsValid(Target2Mode)));

    public void LoadFromConfig(TargetSearchConfig? t1, TargetSearchConfig? t2)
    {
        if (t1 != null)
        {
            Target1Url = t1.BaseSearchUrl;
            Target1Mode = t1.SearchMode;
            Target1UrlTemplate = t1.SearchUrlTemplate;
            Target1Selectors = t1.Selectors ?? new();
        }
        if (t2 != null)
        {
            UseTarget2 = true;
            Target2Url = t2.BaseSearchUrl;
            Target2Mode = t2.SearchMode;
            Target2UrlTemplate = t2.SearchUrlTemplate;
            Target2Selectors = t2.Selectors ?? new();
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand DiscoverTarget1Command { get; }
    public ICommand DiscoverTarget2Command { get; }

    // ── Discovery ────────────────────────────────────────────────────────────

    private async Task ExecuteDiscoverAsync(int targetNum)
    {
        var url = targetNum == 1 ? Target1Url : Target2Url;

        if (targetNum == 1)
        {
            _isDiscoveringTarget1 = true;
            Target1Status = "Analizando con IA...";
        }
        else
        {
            _isDiscoveringTarget2 = true;
            Target2Status = "Analizando con IA...";
        }

        (DiscoverTarget1Command as AsyncCommand)?.RaiseCanExecuteChanged();
        (DiscoverTarget2Command as AsyncCommand)?.RaiseCanExecuteChanged();

        try
        {
            var selectors = await _selectorService.DiscoverSelectorsAsync(url);

            if (targetNum == 1)
            {
                Target1Selectors = selectors;
                Target1Status = selectors.IsValid()
                    ? "✓ Selectores descubiertos. Puedes editarlos manualmente."
                    : "⚠ IA no pudo identificar selectores. Ingrésalos manualmente.";
            }
            else
            {
                Target2Selectors = selectors;
                Target2Status = selectors.IsValid()
                    ? "✓ Selectores descubiertos. Puedes editarlos manualmente."
                    : "⚠ IA no pudo identificar selectores. Ingrésalos manualmente.";
            }

            OnPropertyChanged(nameof(CanContinue));
        }
        finally
        {
            if (targetNum == 1) _isDiscoveringTarget1 = false;
            else _isDiscoveringTarget2 = false;
            (DiscoverTarget1Command as AsyncCommand)?.RaiseCanExecuteChanged();
            (DiscoverTarget2Command as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Construye los TargetSearchConfig para la sesión.</summary>
    public (TargetSearchConfig target1, TargetSearchConfig? target2) BuildTargetConfigs()
    {
        var t1 = new TargetSearchConfig
        {
            Label           = "Target 1",
            BaseSearchUrl   = Target1Url,
            SearchMode      = Target1Mode,
            SearchUrlTemplate = Target1UrlTemplate,
            Selectors       = Target1Selectors
        };

        TargetSearchConfig? t2 = null;
        if (UseTarget2 && !string.IsNullOrWhiteSpace(Target2Url))
        {
            t2 = new TargetSearchConfig
            {
                Label           = "Target 2",
                BaseSearchUrl   = Target2Url,
                SearchMode      = Target2Mode,
                SearchUrlTemplate = Target2UrlTemplate,
                Selectors       = Target2Selectors
            };
        }

        return (t1, t2);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Step 3 — Source Priority
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel del Step 3: el usuario designa cuál fuente provee el precio y cuál las imágenes.
/// Si solo hay un target, este paso se omite automáticamente.
/// </summary>
public sealed class Step3SourcePriorityViewModel : ViewModelBase
{
    private DataSource _priceSource = DataSource.Target1;
    private DataSource _imageSource = DataSource.Target1;

    public Step3SourcePriorityViewModel(bool hasTarget2, SourcePriorityConfig? existingConfig = null)
    {
        HasTarget2 = hasTarget2;
        if (existingConfig != null)
        {
            PriceSource = existingConfig.PriceSource;
            ImageSource = existingConfig.ImageSource;
        }
    }

    public bool HasTarget2 { get; }

    public DataSource PriceSource
    {
        get => _priceSource;
        set => SetField(ref _priceSource, value);
    }

    public DataSource ImageSource
    {
        get => _imageSource;
        set => SetField(ref _imageSource, value);
    }

    public SourcePriorityConfig BuildPriorityConfig() => new()
    {
        PriceSource = PriceSource,
        ImageSource = ImageSource
    };
}
