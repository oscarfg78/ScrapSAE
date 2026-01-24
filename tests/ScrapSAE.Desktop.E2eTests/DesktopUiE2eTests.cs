using System.Diagnostics;
using FluentAssertions;
using FlaUI.Core;
using FlaUI.UIA3;

namespace ScrapSAE.Desktop.E2eTests;

public class DesktopUiE2eTests
{
    [StaFact]
    public void MainWindow_ShouldLaunch()
    {
        var exePath = GetDesktopExePath();
        File.Exists(exePath).Should().BeTrue($"desktop executable not found at {exePath}");

        Application? app = null;
        try
        {
            app = Application.Launch(exePath);
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            window.Should().NotBeNull();
            window!.Title.Should().Be("ScrapSAE - Consola");
        }
        finally
        {
            try
            {
                app?.Close();
                app?.Dispose();
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static string GetDesktopExePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var relative = Path.Combine(baseDir, "../../../../../src/ScrapSAE.Desktop/bin/Release/net8.0-windows/ScrapSAE.Desktop.exe");
        return Path.GetFullPath(relative);
    }
}
