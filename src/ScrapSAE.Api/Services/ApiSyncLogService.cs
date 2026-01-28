using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Services;

public sealed class ApiSyncLogService : ISyncLogService
{
    private readonly SupabaseTableService<SyncLog> _table;

    public ApiSyncLogService(SupabaseTableService<SyncLog> table)
    {
        _table = table;
    }

    public async Task LogOperationAsync(SyncLog log)
    {
        log.Id = Guid.NewGuid();
        log.CreatedAt = DateTime.UtcNow;
        await _table.CreateAsync(log);
    }

    public async Task<IEnumerable<SyncLog>> GetLogsAsync(DateTime from, DateTime to)
    {
        var logs = await _table.GetAllAsync();
        return logs.Where(l => l.CreatedAt >= from && l.CreatedAt <= to);
    }
}
