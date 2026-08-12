using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Desktop.Infrastructure;

namespace ScrapSAE.Desktop.ViewModels.ConcurrentWizard;

/// <summary>
/// ViewModel raíz del Concurrent Provider Wizard.
/// Gestiona la navegación entre pasos y mantiene la sesión centralizada.
/// </summary>
public sealed class ConcurrentProviderWizardViewModel : ViewModelBase, IDisposable
{
    private readonly IExcelIngestionService    _excelService;
    private readonly ISelectorDiscoveryService _selectorService;
    private readonly IConcurrentScrapingEngine _engine;
    private readonly IWizardSessionRepository  _sessionRepository;
    private readonly ScrapSAE.Desktop.Services.ApiClient _apiClient;

    private ViewModelBase? _currentStepViewModel;
    private int _currentStep = 1;
    private string _errorMessage = string.Empty;

    // Sub-ViewModels (creados en navegación)
    private Step1ExcelIngestionViewModel? _step1;
    private Step2TargetConfigViewModel?   _step2;
    private Step3SourcePriorityViewModel? _step3;
    private Step4ExecutionViewModel?      _step4;

    public ConcurrentWizardSession Session { get; } = new();

    public ConcurrentProviderWizardViewModel(
        IExcelIngestionService excelService,
        ISelectorDiscoveryService selectorService,
        IConcurrentScrapingEngine engine,
        IWizardSessionRepository sessionRepository,
        ScrapSAE.Desktop.Services.ApiClient apiClient)
    {
        _excelService      = excelService;
        _selectorService   = selectorService;
        _engine            = engine;
        _sessionRepository = sessionRepository;
        _apiClient         = apiClient;

        NextCommand   = new AsyncCommand(ExecuteNextAsync,   CanGoNext);
        BackCommand   = new RelayCommand(ExecuteBack,        CanGoBack);
        CancelCommand = new RelayCommand(ExecuteCancel);

        // Iniciar en Step 1
        NavigateToStep(1);
    }

    // ── Properties ───────────────────────────────────────────────────────────

    public ViewModelBase? CurrentStepViewModel
    {
        get => _currentStepViewModel;
        private set => SetField(ref _currentStepViewModel, value);
    }

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
            OnPropertyChanged(nameof(Step1Completed));
            OnPropertyChanged(nameof(Step2Completed));
            OnPropertyChanged(nameof(Step3Completed));
        }
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;

    public bool Step1Completed => CurrentStep > 1;
    public bool Step2Completed => CurrentStep > 2;
    public bool Step3Completed => CurrentStep > 3;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set { SetField(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand NextCommand   { get; }
    public ICommand BackCommand   { get; }
    public ICommand CancelCommand { get; }

    // ── Navigation ───────────────────────────────────────────────────────────

    private bool CanGoNext() => _currentStep switch
    {
        1 => _step1?.CanContinue ?? false,
        2 => _step2?.CanContinue ?? false,
        3 => true,
        _ => false
    };

    private bool CanGoBack()
    {
        if (_currentStep <= 1) return false;
        if (_currentStep == 4) return _step4 == null || !_step4.IsRunning;
        return true;
    }

    private async Task ExecuteNextAsync()
    {
        ErrorMessage = string.Empty;

        switch (_currentStep)
        {
            case 1:
                // Guardar mapeo de columnas en sesión
                Session.ExcelFilePath = _step1!.FilePath;
                Session.ColumnMapping = _step1.BuildMapping();
                Session.TotalExcelRows = _step1.TotalRowCount;
                Session.Name = System.IO.Path.GetFileNameWithoutExtension(_step1.FilePath);
                Session.TargetSiteId = _step1.SelectedSite?.Id;
                Session.TargetSiteName = _step1.SelectedSite?.Name;
                await _sessionRepository.SaveAsync(Session);
                NavigateToStep(2);
                break;

            case 2:
                // Guardar configuración de targets en sesión
                var (t1, t2) = _step2!.BuildTargetConfigs();
                Session.Target1 = t1;
                Session.Target2 = t2;
                await _sessionRepository.SaveAsync(Session);
                // Si hay solo 1 target, skip step 3
                NavigateToStep(Session.HasTarget2 ? 3 : 4);
                break;

            case 3:
                // Guardar prioridad de fuentes en sesión
                Session.SourcePriority = _step3!.BuildPriorityConfig();
                await _sessionRepository.SaveAsync(Session);
                NavigateToStep(4);
                break;
        }

        (NextCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteBack()
    {
        var target = _currentStep == 4 && !Session.HasTarget2 ? 2 : _currentStep - 1;
        NavigateToStep(target);
    }

    private void ExecuteCancel()
    {
        // El wizard cierra; los cambios parciales ya fueron guardados en el repositorio
    }

    private void NavigateToStep(int step)
    {
        CurrentStep = step;

        CurrentStepViewModel = step switch
        {
            1 => _step1 ??= CreateStep1(),
            2 => _step2 ??= CreateStep2(),
            3 => _step3 ??= new Step3SourcePriorityViewModel(Session.HasTarget2, Session.SourcePriority),
            4 => _step4 ??= new Step4ExecutionViewModel(_engine, _sessionRepository, Session, _apiClient),
            _ => null
        };

        (NextCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private Step1ExcelIngestionViewModel CreateStep1()
    {
        var vm = new Step1ExcelIngestionViewModel(_excelService, _apiClient);
        
        if (!string.IsNullOrEmpty(Session.ExcelFilePath) || Session.TargetSiteId.HasValue)
        {
            _ = vm.InitializeFromSessionAsync(Session);
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Step1ExcelIngestionViewModel.CanContinue))
            {
                (NextCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        };
        return vm;
    }

    private Step2TargetConfigViewModel CreateStep2()
    {
        var vm = new Step2TargetConfigViewModel(_selectorService);

        if (Session.Target1 != null || Session.Target2 != null)
        {
            vm.LoadFromConfig(Session.Target1, Session.Target2);
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Step2TargetConfigViewModel.CanContinue))
            {
                (NextCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        };
        return vm;
    }

    /// <summary>
    /// Restaura el estado de una sesión guardada, navegando directamente al step apropiado.
    /// </summary>
    public void RestoreSession(ConcurrentWizardSession saved, System.Collections.Generic.List<ConsolidatedProductResult> existingResults)
    {
        // Copiar estado de la sesión guardada
        Session.SessionId             = saved.SessionId;
        Session.Name                  = saved.Name;
        Session.ExcelFilePath         = saved.ExcelFilePath;
        Session.ColumnMapping         = saved.ColumnMapping;
        Session.TotalExcelRows        = saved.TotalExcelRows;
        Session.Target1               = saved.Target1;
        Session.Target2               = saved.Target2;
        Session.SourcePriority        = saved.SourcePriority;
        Session.LastCompletedRowIndex = saved.LastCompletedRowIndex;
        Session.TargetSiteId          = saved.TargetSiteId;
        Session.TargetSiteName        = saved.TargetSiteName;

        // Si ya hay filas procesadas (>= 0), ir a Step 4. De lo contrario, ir a Step 2 (Configuración)
        int targetStep = saved.LastCompletedRowIndex >= 0 ? 4 : 2;
        NavigateToStep(targetStep);

        // Pre-cargar datos en el Step 2 si se navegó a Step 2
        if (targetStep == 2 && _step2 != null)
        {
            _step2.LoadFromConfig(saved.Target1, saved.Target2);
        }

        // Pre-poblar resultados existentes en el Step4 ViewModel
        if (_step4 != null && existingResults.Count > 0)
        {
            foreach (var r in existingResults)
            {
                _step4.LiveResults.Add(new ConsolidatedProductCard
                {
                    Sku           = r.Sku,
                    Title         = r.Title,
                    SupplierCost  = r.SupplierCost,
                    RetailPrice   = r.RetailPrice,
                    FirstImageUrl = r.ImageUrls.FirstOrDefault(),
                    Status        = r.Status,
                    WarningMessage = r.WarningMessage,
                    FullResult    = r
                });
            }
        }
    }

    public void Dispose()
    {
        _step4?.Dispose();
    }
}
