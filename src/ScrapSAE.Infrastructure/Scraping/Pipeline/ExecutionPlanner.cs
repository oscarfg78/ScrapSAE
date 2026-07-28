using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Pipeline;

public class ExecutionPlanner : IExecutionPlanner
{
    private readonly IReconciliationEngine _reconciliationEngine;

    public ExecutionPlanner(IReconciliationEngine reconciliationEngine)
    {
        _reconciliationEngine = reconciliationEngine;
    }

    public async Task<ExtractionRunReport> ExecutePipelineAsync(ExtractionExecutionRequest request, IEnumerable<IContributor> availableContributors, CancellationToken cancellationToken)
    {
        var report = new ExtractionRunReport
        {
            RunId = request.RunId,
            IsDemo = request.IsDemo,
            StartedAt = DateTime.UtcNow
        };

        var allObservations = new List<ProductObservation>();
        var sw = Stopwatch.StartNew();

        // Very basic DAG/Policy for now: try primary (List/Direct), if NoData, try fallback (Legacy)
        
        var primaryContributor = availableContributors.FirstOrDefault(c => c.Descriptor.Type == "Primary");
        var fallbackContributor = availableContributors.FirstOrDefault(c => c.Descriptor.Type == "LegacyFallback");

        if (primaryContributor != null)
        {
            var result = await primaryContributor.ExecuteAsync(request, cancellationToken);
            report.ContributorResults.Add(result);
            if (result.Status == ContributorStatus.Success || result.Status == ContributorStatus.Partial)
            {
                allObservations.AddRange(result.Observations);
            }
        }

        // Fallback policy
        if (!allObservations.Any() && fallbackContributor != null)
        {
            var result = await fallbackContributor.ExecuteAsync(request, cancellationToken);
            report.ContributorResults.Add(result);
            if (result.Status == ContributorStatus.Success || result.Status == ContributorStatus.Partial)
            {
                allObservations.AddRange(result.Observations);
            }
        }

        // Reconciliation
        report.Products = _reconciliationEngine.Reconcile(allObservations);
        report.QualityGate = _reconciliationEngine.EvaluateQualityGate(report.Products);

        sw.Stop();
        report.CompletedAt = DateTime.UtcNow;
        report.TotalDurationMs = (int)sw.ElapsedMilliseconds;

        return report;
    }
}
