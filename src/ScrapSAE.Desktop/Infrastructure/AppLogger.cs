using System;
using System.IO;
using System.Linq;

namespace ScrapSAE.Desktop.Infrastructure;

public static class AppLogger
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrapSAE");
    private static readonly string LogPath = Path.Combine(LogDir, "desktop.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static string GetLogPath() => LogPath;

    public static Task<string[]> ReadLatestAsync(int maxLines)
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return Task.FromResult(Array.Empty<string>());
            }

            var lines = File.ReadAllLines(LogPath);
            if (lines.Length <= maxLines)
            {
                return Task.FromResult(lines);
            }

            return Task.FromResult(lines.Skip(Math.Max(0, lines.Length - maxLines)).ToArray());
        }
        catch
        {
            return Task.FromResult(Array.Empty<string>());
        }
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"{DateTime.UtcNow:O} [{level}] {message}";
            if (ex != null)
            {
                line += $"{Environment.NewLine}{ex}";
            }

            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
