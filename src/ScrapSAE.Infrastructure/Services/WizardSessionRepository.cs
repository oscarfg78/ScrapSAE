using System.Text.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Repositorio de sesiones del Concurrent Scraping Wizard.
/// 
/// Estructura de archivos en %AppData%/ScrapSAE/sessions/:
///   {sessionId}.json         → cabecera de sesión (ConcurrentWizardSession sin results)
///   {sessionId}.results.json → lista de ConsolidatedProductResult
///
/// Escritura atómica: tmp → rename para evitar corrupción en crashes.
/// </summary>
public class WizardSessionRepository : IWizardSessionRepository
{
    private readonly string _sessionsDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WizardSessionRepository()
    {
        _sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScrapSAE", "sessions");
        Directory.CreateDirectory(_sessionsDir);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ConcurrentWizardSession session, CancellationToken cancellationToken = default)
    {
        session.LastSavedAt = DateTime.UtcNow;
        await WriteAtomicAsync(SessionPath(session.SessionId), session, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveTickAsync(
        ConcurrentWizardSession session,
        IReadOnlyList<ConsolidatedProductResult> newResults,
        CancellationToken cancellationToken = default)
    {
        // 1. Actualizar cabecera de sesión
        session.LastSavedAt = DateTime.UtcNow;
        await WriteAtomicAsync(SessionPath(session.SessionId), session, cancellationToken);

        // 2. Append de nuevos resultados al archivo de resultados
        if (newResults.Count == 0) return;

        var resultsPath = ResultsPath(session.SessionId);

        List<ConsolidatedProductResult> existing = new();
        if (File.Exists(resultsPath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(resultsPath, cancellationToken);
                existing = JsonSerializer.Deserialize<List<ConsolidatedProductResult>>(existingJson, JsonOptions)
                           ?? new List<ConsolidatedProductResult>();
            }
            catch { existing = new(); }
        }

        existing.AddRange(newResults);
        await WriteAtomicAsync(resultsPath, existing, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConcurrentWizardSession>> ListSavedSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = new List<ConcurrentWizardSession>();

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json")
                                      .Where(f => !f.EndsWith(".results.json")))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var session = JsonSerializer.Deserialize<ConcurrentWizardSession>(json, JsonOptions);
                if (session != null)
                    sessions.Add(session);
            }
            catch { /* Skip archivos corruptos */ }
        }

        return sessions.OrderByDescending(s => s.LastSavedAt).ToList();
    }

    /// <inheritdoc/>
    public async Task<(ConcurrentWizardSession? Session, List<ConsolidatedProductResult> Results)> LoadAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ConcurrentWizardSession? session = null;
        var results = new List<ConsolidatedProductResult>();

        var sessionPath = SessionPath(sessionId);
        if (!File.Exists(sessionPath)) return (null, results);

        try
        {
            var json = await File.ReadAllTextAsync(sessionPath, cancellationToken);
            session = JsonSerializer.Deserialize<ConcurrentWizardSession>(json, JsonOptions);
        }
        catch { return (null, results); }

        var resultsPath = ResultsPath(sessionId);
        if (File.Exists(resultsPath))
        {
            try
            {
                var rJson = await File.ReadAllTextAsync(resultsPath, cancellationToken);
                results = JsonSerializer.Deserialize<List<ConsolidatedProductResult>>(rJson, JsonOptions)
                          ?? new List<ConsolidatedProductResult>();
            }
            catch { results = new(); }
        }

        return (session, results);
    }

    /// <inheritdoc/>
    public async Task TruncateResultsAsync(string sessionId, int lastIndexToKeep, CancellationToken cancellationToken = default)
    {
        var resultsPath = ResultsPath(sessionId);
        if (!File.Exists(resultsPath)) return;

        List<ConsolidatedProductResult> results;
        try
        {
            var json = await File.ReadAllTextAsync(resultsPath, cancellationToken);
            results = JsonSerializer.Deserialize<List<ConsolidatedProductResult>>(json, JsonOptions) ?? new();
        }
        catch { return; }

        if (results.Count > lastIndexToKeep)
        {
            results = results.Take(lastIndexToKeep).ToList();
            await WriteAtomicAsync(resultsPath, results, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try { File.Delete(SessionPath(sessionId)); } catch { }
        try { File.Delete(ResultsPath(sessionId)); } catch { }
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string SessionPath(string sessionId) =>
        Path.Combine(_sessionsDir, $"{sessionId}.json");

    private string ResultsPath(string sessionId) =>
        Path.Combine(_sessionsDir, $"{sessionId}.results.json");

    private static async Task WriteAtomicAsync<T>(string targetPath, T data, CancellationToken cancellationToken)
    {
        var tmpPath = targetPath + ".tmp";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(tmpPath, json, cancellationToken);
        // Rename atómico: si el proceso crashea durante la escritura, el .tmp no corrompe el .json
        File.Move(tmpPath, targetPath, overwrite: true);
    }
}
