using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests.Triggers;

/// <summary>
/// US1 + US2 end-to-end composite rising-edge behaviour. Covers the AND
/// operator (this file's primary scope) and, later, the OR cases that
/// T014/T016 add.
/// </summary>
public class CompositeTriggerTests
{
    private static RuleConfig AndRule(params ConditionConfig[] conditions) =>
        new()
        {
            Id = "rule-and",
            Name = "AND",
            Enabled = true,
            Operator = RuleConditionOperator.All,
            Conditions = conditions.ToList(),
        };

    private static RuleConfig OrRule(params ConditionConfig[] conditions) =>
        new()
        {
            Id = "rule-or",
            Name = "OR",
            Enabled = true,
            Operator = RuleConditionOperator.Any,
            Conditions = conditions.ToList(),
        };

    private static DeviceState State(float? battery = null, bool? ac = null,
                                     float? tempC = null, bool isOffline = false)
    {
        return new DeviceState
        {
            Bms = battery.HasValue || tempC.HasValue
                ? new BmsData { BatteryPct = battery, TempC = tempC }
                : null,
            Display = ac.HasValue ? new DisplayData { AcPluggedIn = ac } : null,
            Power = new PowerState { Status = PowerStatus.Charging },
            IsOffline = isOffline,
            LastDataReceived = DateTime.UtcNow,
        };
    }

    // -- AND ----------------------------------------------------------------

    [Fact]
    public void And_BothTrue_FiresOnceOnRisingEdge()
    {
        var rule  = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });
        var state = State(battery: 15f, ac: false);

        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeTrue();
        // Still true on next eval → NO re-fire.
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void And_OneConditionFalse_DoesNotFire()
    {
        var rule  = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });

        // Battery low but AC plugged in → no fire.
        TriggerEvaluator.EvaluateComposite(rule, State(battery: 15f, ac: true), DateTime.UtcNow).Should().BeFalse();

        // AC out but battery fine → no fire.
        TriggerEvaluator.EvaluateComposite(rule, State(battery: 50f, ac: false), DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void And_FalseThenTrue_RisingEdgeFires()
    {
        var rule = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });
        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 50f },
            Display = new DisplayData { AcPluggedIn = true },
            Power = new PowerState { Status = PowerStatus.Charging },
        };

        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeFalse();

        // Now both go true.
        state.Bms!.BatteryPct = 15f;
        state.Display!.AcPluggedIn = false;
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void And_CooldownSuppressesSecondFireWithinWindow()
    {
        var rule = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20, CooldownSeconds = 300 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });
        var state = State(battery: 15f, ac: false);
        var t0 = DateTime.UtcNow;

        TriggerEvaluator.EvaluateComposite(rule, state, t0).Should().BeTrue();

        // Composite drops (AC back in), then rises 30s later → inside cooldown.
        state.Display!.AcPluggedIn = true;
        TriggerEvaluator.EvaluateComposite(rule, state, t0.AddSeconds(10)).Should().BeFalse();

        state.Display!.AcPluggedIn = false;
        TriggerEvaluator.EvaluateComposite(rule, state, t0.AddSeconds(30)).Should().BeFalse();
    }

    [Fact]
    public void And_FiresAgainAfterCooldownElapsed()
    {
        var rule = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20, CooldownSeconds = 60 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });
        var state = State(battery: 15f, ac: false);
        var t0 = DateTime.UtcNow;

        TriggerEvaluator.EvaluateComposite(rule, state, t0).Should().BeTrue();

        // Drop then rise outside cooldown → fires again.
        state.Display!.AcPluggedIn = true;
        TriggerEvaluator.EvaluateComposite(rule, state, t0.AddSeconds(30)).Should().BeFalse();
        state.Display!.AcPluggedIn = false;
        TriggerEvaluator.EvaluateComposite(rule, state, t0.AddSeconds(90)).Should().BeTrue();
    }

    [Fact]
    public void SingleCondition_Rule_BehavesLikeLegacy_FirstObservationFires()
    {
        var rule  = AndRule(new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 });
        var state = State(battery: 15f);

        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeTrue();
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void MissingTelemetry_CompositeStaysFalse_NoFire()
    {
        var rule  = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });
        // BMS + Display both null.
        var state = new DeviceState
        {
            Power = new PowerState { Status = PowerStatus.Charging },
        };

        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void EvaluateConditions_ReturnsPerConditionTruthValues()
    {
        var rule  = AndRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            new ConditionConfig { Type = TriggerType.AcUnplugged });

        var state = State(battery: 15f, ac: true);
        var truths = TriggerEvaluator.EvaluateConditions(rule, state);

        truths.Should().HaveCount(2);
        truths[0].Should().BeTrue();  // BatteryBelow 20 with 15%
        truths[1].Should().BeFalse(); // AcUnplugged while plugged in
    }

    // -- OR -----------------------------------------------------------------

    [Fact]
    public void Or_OneOfThreeTrue_FiresOnce()
    {
        var rule = OrRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f },
            new ConditionConfig { Type = TriggerType.DeviceOffline });

        // Only temperature is hot; rule should fire.
        var state = State(tempC: 60f);
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void Or_SecondConditionGoesTrue_WhileCompositeAlreadyTrue_NoReFire()
    {
        var rule = OrRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f });

        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 3f, TempC = 20f },
            Display = null,
            Power = new PowerState { Status = PowerStatus.Charging },
        };

        // Battery drives the first fire.
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeTrue();

        // Temperature now also goes true — composite is still true, no rising edge.
        state.Bms!.TempC = 60f;
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Or_AllFalseThenOneRises_FiresAgain()
    {
        var rule = OrRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5, CooldownSeconds = 0 },
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f });

        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 3f, TempC = 20f },
            Power = new PowerState { Status = PowerStatus.Charging },
        };
        var t0 = DateTime.UtcNow;

        TriggerEvaluator.EvaluateComposite(rule, state, t0).Should().BeTrue();

        // Battery recovers → all conditions false.
        state.Bms!.BatteryPct = 50f;
        TriggerEvaluator.EvaluateComposite(rule, state, t0.AddSeconds(1)).Should().BeFalse();

        // Temperature rises → fire again.
        state.Bms!.TempC = 60f;
        TriggerEvaluator.EvaluateComposite(rule, state, t0.AddSeconds(2)).Should().BeTrue();
    }

    [Fact]
    public void Or_AllFalse_DoesNotFire()
    {
        var rule = OrRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f });

        var state = State(battery: 80f, tempC: 25f);
        TriggerEvaluator.EvaluateComposite(rule, state, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void EvaluateConditions_ForOrRule_CapturesAllBranches()
    {
        var rule = OrRule(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f },
            new ConditionConfig { Type = TriggerType.DeviceOffline });

        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 3f, TempC = 20f },
            Power = new PowerState { Status = PowerStatus.Charging },
            IsOffline = false,
            LastDataReceived = DateTime.UtcNow,
        };
        var truths = TriggerEvaluator.EvaluateConditions(rule, state);

        truths.Should().HaveCount(3);
        truths[0].Should().BeTrue();   // BatteryBelow 5 at 3%
        truths[1].Should().BeFalse();  // Temp 20°C not above 55°C
        truths[2].Should().BeFalse();  // Device online
    }
}
