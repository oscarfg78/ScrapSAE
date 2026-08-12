using FluentAssertions;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Infrastructure.Services;
using Xunit;

namespace ScrapSAE.Infrastructure.Tests.Services;

public class WizardSessionRepositoryTests : IDisposable
{
    private readonly WizardSessionRepository _repository = new();
    private readonly string _testSessionId = "test_session_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SaveAndLoad_Session_ShouldRoundtripSuccessfully()
    {
        var session = new ConcurrentWizardSession
        {
            SessionId = _testSessionId,
            Name = "Test Session",
            ExcelFilePath = @"C:\data\test.xlsx",
            TotalExcelRows = 100,
            LastCompletedRowIndex = 49,
            Target1 = new TargetSearchConfig
            {
                Label = "Target 1",
                BaseSearchUrl = "https://target1.com",
                SearchMode = SearchMode.QueryParam,
                SearchUrlTemplate = "https://target1.com/search?q={sku}"
            }
        };

        // 1. Save
        await _repository.SaveAsync(session);

        // 2. List
        var sessions = await _repository.ListSavedSessionsAsync();
        sessions.Should().Contain(s => s.SessionId == _testSessionId);

        // 3. Load
        var (loadedSession, loadedResults) = await _repository.LoadAsync(_testSessionId);
        loadedSession.Should().NotBeNull();
        loadedSession!.Name.Should().Be("Test Session");
        loadedSession.TotalExcelRows.Should().Be(100);
        loadedSession.LastCompletedRowIndex.Should().Be(49);
    }

    [Fact]
    public async Task SaveTick_ShouldAppendResultsIncrementally()
    {
        var session = new ConcurrentWizardSession
        {
            SessionId = _testSessionId,
            Name = "Tick Session",
            TotalExcelRows = 50,
            LastCompletedRowIndex = 0
        };

        var batch1 = new List<ConsolidatedProductResult>
        {
            new() { RowIndex = 0, Sku = "SKU-001", SupplierCost = 10m, Status = ConsolidatedStatus.Matched }
        };

        var batch2 = new List<ConsolidatedProductResult>
        {
            new() { RowIndex = 1, Sku = "SKU-002", SupplierCost = 20m, Status = ConsolidatedStatus.Matched }
        };

        await _repository.SaveTickAsync(session, batch1);
        await _repository.SaveTickAsync(session, batch2);

        var (loadedSession, loadedResults) = await _repository.LoadAsync(_testSessionId);
        loadedResults.Should().HaveCount(2);
        loadedResults.Select(r => r.Sku).Should().ContainInOrder("SKU-001", "SKU-002");
    }

    public void Dispose()
    {
        _repository.DeleteAsync(_testSessionId).GetAwaiter().GetResult();
    }
}
