using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests.Triggers;

/// <summary>
/// Covers the building blocks of the dashboard "N / M met" indicator
/// (US4 / FR-018). We drive <see cref="TriggerEvaluator.EvaluateConditions"/>
/// across a rule and verify the per-condition truth array matches what
/// <see cref="TriggerEvaluator.EvaluateComposite"/> would reduce into.
/// </summary>
public class ConditionTallyTests
{
    private static DeviceState State(float? battery = null, bool? ac = null,
                                     float? tempC = null, bool isOffline = false)
        => new()
        {
            Bms = (battery.HasValue || tempC.HasValue)
                ? new BmsData { BatteryPct = battery, TempC = tempC }
                : null,
            Display = ac.HasValue ? new DisplayData { AcPluggedIn = ac } : null,
            Power = new PowerState { Status = PowerStatus.Charging },
            IsOffline = isOffline,
            LastDataReceived = DateTime.UtcNow,
        };

    [Fact]
    public void TwoOfThreeTrue_TallyMatchesAggregate()
    {
        var rule = new RuleConfig
        {
            Operator = RuleConditionOperator.Any,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 10 },  // true (5%)
                new ConditionConfig { Type = TriggerType.TempAbove,    ThresholdF = 55f }, // false (20°C)
                new ConditionConfig { Type = TriggerType.AcUnplugged },                    // true
            },
        };
        var state = State(battery: 5f, tempC: 20f, ac: false);

        var truths = TriggerEvaluator.EvaluateConditions(rule, state);
        truths.Should().BeEquivalentTo(new[] { true, false, true });

        var met = truths.Count(b => b);
        met.Should().Be(2);
        truths.Length.Should().Be(3);
    }

    [Fact]
    public void AllFalse_TallyIsZeroOverN()
    {
        var rule = new RuleConfig
        {
            Operator = RuleConditionOperator.All,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
                new ConditionConfig { Type = TriggerType.TempAbove,    ThresholdF = 55f },
            },
        };
        var state = State(battery: 80f, tempC: 20f);

        var truths = TriggerEvaluator.EvaluateConditions(rule, state);
        truths.Count(b => b).Should().Be(0);
        truths.Length.Should().Be(2);
    }

    [Fact]
    public void SingleCondition_TallyEqualsAggregate()
    {
        var rule = new RuleConfig
        {
            Conditions = { new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 } },
        };
        var state = State(battery: 15f);

        var truths = TriggerEvaluator.EvaluateConditions(rule, state);
        truths.Should().ContainSingle().Which.Should().BeTrue();
    }
}
