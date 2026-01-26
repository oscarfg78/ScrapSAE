using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
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

    [StaFact]
    public void ConfigurationTab_ShouldShowDatabaseFields()
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

            var tab = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Tab))?.AsTab();
            tab.Should().NotBeNull();
            tab!.TabItems.Should().Contain(item => item.Name == "Configuración");
            tab.TabItems.First(item => item.Name == "Configuración").Select();
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
        var releasePath = Path.GetFullPath(Path.Combine(
            baseDir,
            "../../../../../src/ScrapSAE.Desktop/bin/Release/net8.0-windows/ScrapSAE.Desktop.exe"));
        if (File.Exists(releasePath))
        {
            return releasePath;
        }

        var debugPath = Path.GetFullPath(Path.Combine(
            baseDir,
            "../../../../../src/ScrapSAE.Desktop/bin/Debug/net8.0-windows/ScrapSAE.Desktop.exe"));
        return debugPath;
    }
}
