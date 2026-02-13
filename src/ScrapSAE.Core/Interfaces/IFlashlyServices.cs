using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Core.Interfaces;

public interface IFlashlySyncService
{
    Task<FlashlySyncResult> SyncProductsAsync(IEnumerable<StagingProduct> products, CancellationToken cancellationToken = default);
}

public interface ICsvExportService
{
    Task<string> ExportProductsToCsvAsync(IEnumerable<StagingProduct> products, string outputPath, CancellationToken cancellationToken = default);
}
