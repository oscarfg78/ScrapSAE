using ScrapSAE.Api.Services;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Tests.Stubs;

public sealed class StubSaeSdkServiceSuccess : ISaeSdkService
{
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<bool> SendProductAsync(StagingProduct product, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
