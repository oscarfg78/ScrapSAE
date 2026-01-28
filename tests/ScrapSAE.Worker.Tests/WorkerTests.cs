using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Worker;

namespace ScrapSAE.Worker.Tests;

public class WorkerTests
{
    private readonly Worker _worker;

    public WorkerTests()
    {
        var logger = new Mock<ILogger<Worker>>();
        var scraping = new Mock<IScrapingService>();
        var staging = new Mock<IStagingService>();
        var aiProcessor = new Mock<IAIProcessorService>();
        var syncLogService = new Mock<ISyncLogService>();
        _worker = new Worker(logger.Object, scraping.Object, staging.Object, aiProcessor.Object, syncLogService.Object);
    }

    [Fact]
    public void ParseCronField_Wildcard_ShouldReturnAllValues()
    {
        var result = InvokePrivate<HashSet<int>?>(_worker, "ParseCronField", "*", 0, 59, 0);

        result.Should().NotBeNull();
        result!.Count.Should().Be(60);
        result.Should().Contain(0);
        result.Should().Contain(59);
    }

    [Fact]
    public void ParseCronField_Step_ShouldReturnExpectedValues()
    {
        var result = InvokePrivate<HashSet<int>?>(_worker, "ParseCronField", "*/15", 0, 59, 0);

        result.Should().NotBeNull();
        result!.Should().BeEquivalentTo(new[] { 0, 15, 30, 45 });
    }

    [Fact]
    public void ParseStepField_Range_ShouldReturnRangeAndStep()
    {
        var result = InvokePrivate<(Tuple<int, int>?, int?)>(_worker, "ParseStepField", "1-10/2", 0, 59);

        result.Item1.Should().NotBeNull();
        result.Item1!.Item1.Should().Be(1);
        result.Item1!.Item2.Should().Be(10);
        result.Item2.Should().Be(2);
    }

    [Fact]
    public void ShouldRun_WithAlwaysCron_ShouldReturnTrue()
    {
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CronExpression = "ALWAYS"
        };

        var result = InvokePrivate<bool>(_worker, "ShouldRun", site);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRun_WithInvalidCron_ShouldReturnFalse()
    {
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CronExpression = "invalid"
        };

        var result = InvokePrivate<bool>(_worker, "ShouldRun", site);

        result.Should().BeFalse();
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] parameters)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            throw new InvalidOperationException($"Method {methodName} not found.");
        }

        return (T)method.Invoke(target, parameters)!;
    }
}
