using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using ScrapSAE.Api.Services;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Tests;

public class FirebirdSaeSdkServiceTests
{
    [Fact]
    public async Task TestConnection_MissingDbPath_ShouldReturnFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SAE:DbPath"] = "" })
            .Build();

        var settingsStore = new SettingsStore(new TestWebHostEnvironment());
        var logger = LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FirebirdSaeSdkService>();
        var service = new FirebirdSaeSdkService(configuration, settingsStore, logger);

        var result = await service.TestConnectionAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendProduct_MissingSku_ShouldReturnFalse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SAE:DbPath"] = @"C:\Temp\SAE90EMPRE01.FDB"
            })
            .Build();

        var settingsStore = new SettingsStore(new TestWebHostEnvironment());
        var logger = LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<FirebirdSaeSdkService>();
        var service = new FirebirdSaeSdkService(configuration, settingsStore, logger);
        var product = new StagingProduct { Id = Guid.NewGuid() };

        var result = await service.SendProductAsync(product);

        result.Should().BeFalse();
    }
}

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public TestWebHostEnvironment()
    {
        ContentRootPath = Path.Combine(Path.GetTempPath(), "ScrapSAE.Api.Tests");
        Directory.CreateDirectory(ContentRootPath);
    }

    public string ApplicationName { get; set; } = "ScrapSAE.Api.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string EnvironmentName { get; set; } = "Development";
}
