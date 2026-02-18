using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Api.Services;

public interface IRescrapeJobService
{
    Task<RescrapeJobResponse> EnqueueAsync(RescrapeRequest request, CancellationToken cancellationToken = default);
    Task<RescrapeJobStatusResponse?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RescrapeJobItemResponse>> GetItemsAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RescrapeJobLogResponse>> GetLogsAsync(Guid jobId, int take = 200, CancellationToken cancellationToken = default);
    Task<bool> PauseAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> ResumeAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ProcessNextQueuedJobAsync(CancellationToken cancellationToken = default);
}
