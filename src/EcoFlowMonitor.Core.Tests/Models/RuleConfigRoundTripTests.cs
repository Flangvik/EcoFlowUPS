using System.Text.Json;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests.Models;

/// <summary>
/// FR-015: a composite rule that round-trips through JSON produces
/// byte-identical output on the second pass. No legacy <c>trigger</c> field
/// is introduced by the writer.
/// </summary>
public class RuleConfigRoundTripTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = true,
    };

    public static IEnumerable<object[]> Rules()
    {
        yield return new object[] { new RuleConfig
        {
            Id = "r-simple",
            Name = "Simple single-condition",
            Enabled = true,
            Operator = RuleConditionOperator.All,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20, CooldownSeconds = 300 },
            },
        }};

        yield return new object[] { new RuleConfig
        {
            Id = "r-and",
            Name = "AND (UPS)",
            Enabled = true,
            Operator = RuleConditionOperator.All,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20, CooldownSeconds = 300 },
                new ConditionConfig { Type = TriggerType.AcUnplugged },
            },
        }};

        yield return new object[] { new RuleConfig
        {
            Id = "r-or",
            Name = "OR (Danger)",
            Enabled = true,
            Operator = RuleConditionOperator.Any,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
                new ConditionConfig { Type = TriggerType.TempAbove,    ThresholdF = 55.0f },
                new ConditionConfig { Type = TriggerType.DeviceOffline, WindowSeconds = 120 },
            },
        }};
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void Rule_RoundTripsByteIdentical(RuleConfig rule)
    {
        var first   = JsonSerializer.Serialize(rule, Opts);
        var parsed  = JsonSerializer.Deserialize<RuleConfig>(first, Opts)!;
        parsed.EnsureConditionsHydrated();
        var second  = JsonSerializer.Serialize(parsed, Opts);

        second.Should().Be(first);
        first.Should().NotContain("\"trigger\"");
    }
}
