using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Services;

public interface ISaeSdkService
{
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<bool> SendProductAsync(StagingProduct product, CancellationToken cancellationToken = default);
}
