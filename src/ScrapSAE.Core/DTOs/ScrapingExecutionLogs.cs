using System;
using System.Collections.Generic;

namespace ScrapSAE.Core.DTOs;

public class ScrapingLogStep
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string Selector { get; set; } = string.Empty;
    public int ElementCount { get; set; }
    public string Details { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string JsonPayload { get; set; } = string.Empty;
}

public class ScrapingLogTracker
{
    private readonly List<ScrapingLogStep> _logs = new();
    public IReadOnlyList<ScrapingLogStep> Logs => _logs.AsReadOnly();

    public void AddLog(string action, string selector = "", int count = 0, string details = "", string error = "", string jsonPayload = "")
    {
        _logs.Add(new ScrapingLogStep
        {
            Timestamp = DateTime.UtcNow,
            Action = action,
            Selector = selector,
            ElementCount = count,
            Details = details,
            Error = error,
            JsonPayload = jsonPayload
        });
    }
}
