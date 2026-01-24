using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Services;

public sealed class StubSaeSdkService : ISaeSdkService
{
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> SendProductAsync(StagingProduct product, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
