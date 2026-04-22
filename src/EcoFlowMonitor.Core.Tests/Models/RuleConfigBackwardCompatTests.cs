using System.Text.Json;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests.Models;

/// <summary>
/// Covers backward-compatible load + clean new-shape write for
/// <see cref="RuleConfig"/>. FR-003, FR-015.
/// </summary>
public class RuleConfigBackwardCompatTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [Fact]
    public void Legacy_SingleTrigger_HydratesIntoOneConditionWithAllOperator()
    {
        // TriggerType serialises as an integer in production config.json.
        // BatteryBelow == 2.
        const string legacy = """
        {
          "id": "r1",
          "name": "Low batt",
          "enabled": true,
          "trigger": { "type": 2, "threshold": 20 },
          "actions": []
        }
        """;

        var rule = JsonSerializer.Deserialize<RuleConfig>(legacy, Opts)!;
        rule.EnsureConditionsHydrated().Should().BeTrue();

        rule.Conditions.Should().HaveCount(1);
        rule.Conditions[0].Type.Should().Be(TriggerType.BatteryBelow);
        rule.Conditions[0].Threshold.Should().Be(20);
        rule.Operator.Should().Be(RuleConditionOperator.All);
        rule.Trigger.Should().BeNull();
    }

    [Fact]
    public void Hydration_IsIdempotent()
    {
        var rule = new RuleConfig
        {
            Trigger = new TriggerConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
        };
        rule.EnsureConditionsHydrated().Should().BeTrue();
        // Second call returns false and does not duplicate.
        rule.EnsureConditionsHydrated().Should().BeFalse();
        rule.Conditions.Should().HaveCount(1);
    }

    [Fact]
    public void BothLegacyAndNew_PrefersConditions_DropsLegacy()
    {
        // BatteryBelow == 2, AcUnplugged == 8
        const string both = """
        {
          "id": "r1",
          "name": "Mixed",
          "enabled": true,
          "trigger": { "type": 2, "threshold": 50 },
          "conditions": [
            { "type": 8 }
          ],
          "operator": "All",
          "actions": []
        }
        """;

        var rule = JsonSerializer.Deserialize<RuleConfig>(both, Opts)!;
        rule.EnsureConditionsHydrated().Should().BeFalse();

        rule.Conditions.Should().HaveCount(1);
        rule.Conditions[0].Type.Should().Be(TriggerType.AcUnplugged);
        rule.Trigger.Should().BeNull();
    }

    [Fact]
    public void NewShape_RoundTripsWithoutLegacyTrigger()
    {
        var rule = new RuleConfig
        {
            Id = "r2",
            Name = "Dangerous",
            Enabled = true,
            Operator = RuleConditionOperator.Any,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
                new ConditionConfig { Type = TriggerType.TempAbove,    ThresholdF = 55.0f },
            },
        };

        var first = JsonSerializer.Serialize(rule, Opts);
        first.Should().NotContain("\"trigger\"");
        first.Should().Contain("\"operator\":\"Any\"");
        first.Should().Contain("\"conditions\"");

        var deserialized = JsonSerializer.Deserialize<RuleConfig>(first, Opts)!;
        deserialized.EnsureConditionsHydrated().Should().BeFalse();

        var second = JsonSerializer.Serialize(deserialized, Opts);
        second.Should().Be(first);
    }

    [Fact]
    public void OperatorEnum_SerialisesAsString()
    {
        var rule = new RuleConfig
        {
            Operator = RuleConditionOperator.All,
            Conditions = { new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 } },
        };
        var json = JsonSerializer.Serialize(rule, Opts);
        json.Should().Contain("\"operator\":\"All\"");
        json.Should().NotContain("\"operator\":0");
    }

    [Fact]
    public void EmptyConditions_AndNoLegacyTrigger_HydrateReturnsFalse()
    {
        var rule = new RuleConfig();
        rule.EnsureConditionsHydrated().Should().BeFalse();
        rule.Conditions.Should().BeEmpty();
    }
}
