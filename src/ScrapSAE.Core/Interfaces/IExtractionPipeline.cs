using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Core.Interfaces;

public interface IContributor
{
    ContributorDescriptor Descriptor { get; }
    Task<ContributorResult> ExecuteAsync(ExtractionExecutionRequest request, CancellationToken cancellationToken);
}

public class ExtractionExecutionRequest
{
    public string RunId { get; set; } = string.Empty;
    public bool IsDemo { get; set; }
    public ProviderConfigurationSnapshot ProviderConfig { get; set; } = new();
    public int ProductLimit { get; set; } = 10;
    // other budget settings
}

public class ProviderConfigurationSnapshot
{
    public string ProviderId { get; set; } = string.Empty;
    public string CatalogUrl { get; set; } = string.Empty;
    public string? DetailUrl { get; set; }
    public SiteSelectors Selectors { get; set; } = new();
    public Dictionary<string, string> AuthParameters { get; set; } = new();
}

public interface IExecutionPlanner
{
    Task<ExtractionRunReport> ExecutePipelineAsync(ExtractionExecutionRequest request, IEnumerable<IContributor> availableContributors, CancellationToken cancellationToken);
}

public interface IReconciliationEngine
{
    List<ReconciledProduct> Reconcile(List<ProductObservation> observations);
    QualityGateEvaluation EvaluateQualityGate(List<ReconciledProduct> products);
}
