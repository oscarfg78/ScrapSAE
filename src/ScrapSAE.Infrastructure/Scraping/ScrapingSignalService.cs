using System.Collections.Concurrent;

namespace ScrapSAE.Infrastructure.Scraping;

public interface IScrapingSignalService
{
    Task WaitForLoginConfirmationAsync(string siteId, CancellationToken cancellationToken);
    void ConfirmLogin(string siteId);
}

public class ScrapingSignalService : IScrapingSignalService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _loginSignals = new();

    public async Task WaitForLoginConfirmationAsync(string siteId, CancellationToken cancellationToken)
    {
        var tcs = _loginSignals.GetOrAdd(siteId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());
        
        // Wait for the signal
        await tcs.Task;
        
        // Clean up after success
        _loginSignals.TryRemove(siteId, out _);
    }

    public void ConfirmLogin(string siteId)
    {
        if (_loginSignals.TryGetValue(siteId, out var tcs))
        {
            tcs.TrySetResult();
        }
    }
}
