using System.Collections.Concurrent;
using System.Threading;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Services;

public sealed class ScrapeControlService : IScrapeControlService
{
    private sealed class Session
    {
        public CancellationTokenSource Cts { get; } = new();
        public ManualResetEventSlim PauseEvent { get; } = new(true);
        public ScrapeStatus Status { get; } = new();
    }

    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public ScrapeStatus GetStatus(Guid siteId)
    {
        if (_sessions.TryGetValue(siteId, out var session))
        {
            return session.Status;
        }

        return new ScrapeStatus { SiteId = siteId, State = ScrapeRunState.Idle };
    }

    public CancellationToken Start(Guid siteId)
    {
        var session = _sessions.AddOrUpdate(
            siteId,
            _ => new Session(),
            (_, existing) => existing.Cts.IsCancellationRequested ? new Session() : existing);
        session.Status.SiteId = siteId;
        session.Status.State = ScrapeRunState.Running;
        session.Status.StartedAtUtc = DateTime.UtcNow;
        session.Status.UpdatedAtUtc = DateTime.UtcNow;
        session.Status.Message = "Ejecución iniciada.";
        session.PauseEvent.Set();
        return session.Cts.Token;
    }

    public void MarkCompleted(Guid siteId, string? message = null)
    {
        if (_sessions.TryGetValue(siteId, out var session))
        {
            session.Status.State = ScrapeRunState.Completed;
            session.Status.Message = message ?? "Finalizado.";
            session.Status.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void MarkError(Guid siteId, string message)
    {
        if (_sessions.TryGetValue(siteId, out var session))
        {
            session.Status.State = ScrapeRunState.Error;
            session.Status.Message = message;
            session.Status.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void Pause(Guid siteId)
    {
        if (_sessions.TryGetValue(siteId, out var session))
        {
            session.Status.State = ScrapeRunState.Paused;
            session.Status.Message = "Pausado.";
            session.Status.UpdatedAtUtc = DateTime.UtcNow;
            session.PauseEvent.Reset();
        }
    }

    public void Resume(Guid siteId)
    {
        if (_sessions.TryGetValue(siteId, out var session))
        {
            session.Status.State = ScrapeRunState.Running;
            session.Status.Message = "Reanudado.";
            session.Status.UpdatedAtUtc = DateTime.UtcNow;
            session.PauseEvent.Set();
        }
    }

    public void Stop(Guid siteId)
    {
        if (_sessions.TryGetValue(siteId, out var session))
        {
            session.Status.State = ScrapeRunState.Stopped;
            session.Status.Message = "Detenido.";
            session.Status.UpdatedAtUtc = DateTime.UtcNow;
            session.Cts.Cancel();
            session.PauseEvent.Set();
        }
    }

    public Task WaitIfPausedAsync(Guid siteId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(siteId, out var session))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                session.PauseEvent.Wait(500, cancellationToken);
                if (session.PauseEvent.IsSet)
                {
                    break;
                }
            }
        }, cancellationToken);
    }
}
