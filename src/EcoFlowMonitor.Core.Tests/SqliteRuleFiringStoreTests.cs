using EcoFlowMonitor.History;
using EcoFlowMonitor.Models;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests;

/// <summary>
/// Exercises <see cref="SqliteRuleFiringStore"/> against a temp on-disk database.
/// (In-memory SQLite via <c>Data Source=:memory:</c> doesn't survive a close,
/// and the store opens separate read/write connections, so we use a temp file
/// and clean up.)
/// </summary>
public class SqliteRuleFiringStoreTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteRuleFiringStore _store = default!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(),
            $"ecoflow-rulefiring-{Guid.NewGuid():N}.db");
        _store = new SqliteRuleFiringStore(_dbPath);
        await _store.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        // Also clear WAL sidecars.
        foreach (var sidecar in new[] { _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(sidecar)) File.Delete(sidecar);
    }

    private static RuleFiring MakeFiring(
        string ruleId,
        string deviceSn,
        DateTimeOffset ts,
        RuleFiringActionOutcome outcome = RuleFiringActionOutcome.Success,
        bool isTest = false) => new(
            Timestamp:          ts,
            RuleId:             ruleId,
            RuleName:           "Test rule",
            DeviceSerialNumber: deviceSn,
            TriggerType:        "PowerLost",
            TriggerValueJson:   "{\"batteryPct\":42}",
            Actions: new[]
            {
                new RuleFiringAction(
                    Ordinal:    0,
                    ActionType: "Webhook",
                    Outcome:    outcome,
                    DurationMs: 123,
                    ErrorText:  outcome == RuleFiringActionOutcome.Failure ? "boom" : null,
                    DetailJson: "{\"httpStatus\":200}"),
            },
            IsTest: isTest);

    [Fact]
    public async Task AppendThenQuery_RoundTripsFiringAndActions()
    {
        var ts = DateTimeOffset.UtcNow;
        await _store.AppendAsync(MakeFiring("r1", "SN-A", ts));

        var rows = await _store.QueryAsync();

        rows.Should().HaveCount(1);
        var f = rows[0];
        f.RuleId.Should().Be("r1");
        f.DeviceSerialNumber.Should().Be("SN-A");
        f.Timestamp.ToUnixTimeSeconds().Should().Be(ts.ToUnixTimeSeconds());
        f.Actions.Should().HaveCount(1);
        f.Actions[0].ActionType.Should().Be("Webhook");
        f.Actions[0].Outcome.Should().Be(RuleFiringActionOutcome.Success);
        f.Actions[0].DurationMs.Should().Be(123);
        f.Actions[0].DetailJson.Should().Be("{\"httpStatus\":200}");
    }

    [Fact]
    public async Task QueryAsync_FiltersByDevice()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now));
        await _store.AppendAsync(MakeFiring("r2", "SN-B", now));

        var onlyA = await _store.QueryAsync(deviceSerialNumber: "SN-A");

        onlyA.Should().HaveCount(1);
        onlyA[0].DeviceSerialNumber.Should().Be("SN-A");
    }

    [Fact]
    public async Task QueryAsync_FiltersByRuleId()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now));
        await _store.AppendAsync(MakeFiring("r2", "SN-A", now));

        var onlyR1 = await _store.QueryAsync(ruleId: "r1");

        onlyR1.Should().HaveCount(1);
        onlyR1[0].RuleId.Should().Be("r1");
    }

    [Fact]
    public async Task QueryAsync_ReturnsNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now.AddMinutes(-10)));
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now));
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now.AddMinutes(-5)));

        var rows = await _store.QueryAsync();
        rows.Should().HaveCount(3);
        rows.Select(r => r.Timestamp.ToUnixTimeSeconds())
            .Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task PruneOlderThan_DeletesOnlyOlderRowsAndCascadesToChildren()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now.AddDays(-40)));  // old
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now));               // new

        var deleted = await _store.PruneOlderThanAsync(now.AddDays(-30));

        deleted.Should().Be(1);
        var remaining = await _store.QueryAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Timestamp.ToUnixTimeSeconds().Should().Be(now.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task TestFiring_PersistsIsTestFlag()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.AppendAsync(MakeFiring("r1", "SN-A", now, isTest: true));

        var rows = await _store.QueryAsync();
        rows.Should().HaveCount(1);
        rows[0].IsTest.Should().BeTrue();
    }

    [Fact]
    public async Task FailureAction_PersistsErrorTextAndTruncatesBeyond512Chars()
    {
        var now = DateTimeOffset.UtcNow;
        var longErr = new string('x', 2000);

        var firing = new RuleFiring(
            Timestamp:          now,
            RuleId:             "r1",
            RuleName:           "Test",
            DeviceSerialNumber: "SN-A",
            TriggerType:        "PowerLost",
            TriggerValueJson:   "{}",
            Actions: new[]
            {
                new RuleFiringAction(
                    Ordinal:    0,
                    ActionType: "Webhook",
                    Outcome:    RuleFiringActionOutcome.Failure,
                    DurationMs: 10,
                    ErrorText:  longErr)
            });
        await _store.AppendAsync(firing);

        var rows = await _store.QueryAsync();
        rows.Should().HaveCount(1);
        rows[0].Actions[0].ErrorText.Should().HaveLength(512);
    }
}
