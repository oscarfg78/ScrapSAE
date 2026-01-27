using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping;

public sealed class NoOpScrapeControlService : IScrapeControlService
{
    public ScrapeStatus GetStatus(Guid siteId) => new() { SiteId = siteId, State = ScrapeRunState.Idle };
    public CancellationToken Start(Guid siteId) => CancellationToken.None;
    public void MarkCompleted(Guid siteId, string? message = null) { }
    public void MarkError(Guid siteId, string message) { }
    public void Pause(Guid siteId) { }
    public void Resume(Guid siteId) { }
    public void Stop(Guid siteId) { }
    public Task WaitIfPausedAsync(Guid siteId, CancellationToken cancellationToken) => Task.CompletedTask;
}
