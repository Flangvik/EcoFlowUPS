using EcoFlowMonitor.Client;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests;

public class TriggerEvaluatorTests
{
    private static DeviceConfig Device(RuleConfig rule) => new()
    {
        DisplayName = "Test",
        SerialNumber = "SN-T",
        Rules = new List<RuleConfig> { rule },
    };

    private static DeviceState State(float? battery = null, float? tempC = null,
        int? remainMin = null, int? inW = null, int? outW = null, bool? acPluggedIn = null,
        PowerStatus power = PowerStatus.Charging) =>
        new()
        {
            Bms = new BmsData { BatteryPct = battery, TempC = tempC, RemainMin = remainMin },
            Display = new DisplayData { TotalInW = inW, TotalOutW = outW, AcPluggedIn = acPluggedIn },
            Power = new PowerState { Status = power },
        };

    // ---------- Battery above (new) ----------

    [Fact]
    public void BatteryAbove_FiresWhileAboveThreshold()
    {
        var rule = new RuleConfig { Name = "hi", Trigger = new TriggerConfig { Type = TriggerType.BatteryAbove, Threshold = 80 } };
        var fired = TriggerEvaluator.Evaluate(Device(rule), State(battery: 85f), PowerStatus.Charging);
        fired.Should().ContainSingle();
    }

    [Fact]
    public void BatteryAbove_DoesNotFireWhenAtOrBelowThreshold()
    {
        var rule = new RuleConfig { Name = "hi", Trigger = new TriggerConfig { Type = TriggerType.BatteryAbove, Threshold = 80 } };
        TriggerEvaluator.Evaluate(Device(rule), State(battery: 80f), PowerStatus.Charging).Should().BeEmpty();
        TriggerEvaluator.Evaluate(Device(rule), State(battery: 70f), PowerStatus.Charging).Should().BeEmpty();
    }

    // ---------- Temperature (new, decimal threshold) ----------

    [Fact]
    public void TempAbove_FiresWhenExceedsDecimalThreshold()
    {
        var rule = new RuleConfig { Name = "hot", Trigger = new TriggerConfig { Type = TriggerType.TempAbove, ThresholdF = 55f } };
        TriggerEvaluator.Evaluate(Device(rule), State(tempC: 60f), PowerStatus.Charging).Should().ContainSingle();
        TriggerEvaluator.Evaluate(Device(rule), State(tempC: 50f), PowerStatus.Charging).Should().BeEmpty();
    }

    [Fact]
    public void TempBelow_FiresWhenBelowDecimalThreshold()
    {
        var rule = new RuleConfig { Name = "cold", Trigger = new TriggerConfig { Type = TriggerType.TempBelow, ThresholdF = 0f } };
        TriggerEvaluator.Evaluate(Device(rule), State(tempC: -5f), PowerStatus.Charging).Should().ContainSingle();
        TriggerEvaluator.Evaluate(Device(rule), State(tempC: 5f), PowerStatus.Charging).Should().BeEmpty();
    }

    [Fact]
    public void TempAbove_UsesIntThreshold_WhenDecimalOmitted()
    {
        var rule = new RuleConfig { Name = "hot2", Trigger = new TriggerConfig { Type = TriggerType.TempAbove, Threshold = 40 } };
        TriggerEvaluator.Evaluate(Device(rule), State(tempC: 45f), PowerStatus.Charging).Should().ContainSingle();
    }

    // ---------- AC plug edges (new) ----------

    [Fact]
    public void AcPlugged_FiresOnceOnFalseToTrueTransition()
    {
        var rule = new RuleConfig { Name = "plug", Trigger = new TriggerConfig { Type = TriggerType.AcPlugged } };
        var device = Device(rule);

        // Seed the previous AC-plug observation as "false".
        var state1 = State(acPluggedIn: false, power: PowerStatus.Idle);
        TriggerEvaluator.Evaluate(device, state1, PowerStatus.Idle);  // primes LastAcPluggedIn=false; no fire.

        // Second evaluation: plugged in.
        var state2 = State(acPluggedIn: true, power: PowerStatus.Charging);
        state2.LastAcPluggedIn = state1.LastAcPluggedIn; // carry-over (in real life same DeviceState instance)
        var fired = TriggerEvaluator.Evaluate(device, state2, PowerStatus.Idle);

        fired.Should().ContainSingle();

        // Third evaluation: still plugged → no fire.
        var state3 = State(acPluggedIn: true, power: PowerStatus.Charging);
        state3.LastAcPluggedIn = state2.LastAcPluggedIn;
        TriggerEvaluator.Evaluate(device, state3, PowerStatus.Charging).Should().BeEmpty();
    }

    [Fact]
    public void AcUnplugged_FiresOnceOnTrueToFalseTransition()
    {
        var rule = new RuleConfig { Name = "unplug", Trigger = new TriggerConfig { Type = TriggerType.AcUnplugged } };
        var device = Device(rule);

        var state = State(acPluggedIn: true, power: PowerStatus.Charging);
        TriggerEvaluator.Evaluate(device, state, PowerStatus.Charging);
        state.Display!.AcPluggedIn = false;
        state.Power = new PowerState { Status = PowerStatus.PowerLost };
        var fired = TriggerEvaluator.Evaluate(device, state, PowerStatus.Charging);
        fired.Should().ContainSingle();
    }

    // ---------- Watts levels (new) ----------

    [Fact]
    public void InputWattsBelow_FiresWhenBelowThreshold()
    {
        var rule = new RuleConfig { Name = "solar", Trigger = new TriggerConfig { Type = TriggerType.InputWattsBelow, Threshold = 50 } };
        TriggerEvaluator.Evaluate(Device(rule), State(inW: 20), PowerStatus.Charging).Should().ContainSingle();
        TriggerEvaluator.Evaluate(Device(rule), State(inW: 60), PowerStatus.Charging).Should().BeEmpty();
    }

    [Fact]
    public void OutputWattsAbove_FiresWhenAboveThreshold()
    {
        var rule = new RuleConfig { Name = "load", Trigger = new TriggerConfig { Type = TriggerType.OutputWattsAbove, Threshold = 500 } };
        TriggerEvaluator.Evaluate(Device(rule), State(outW: 800), PowerStatus.Charging).Should().ContainSingle();
        TriggerEvaluator.Evaluate(Device(rule), State(outW: 400), PowerStatus.Charging).Should().BeEmpty();
    }

    // ---------- Cooldown (existing semantics, exercised on a new trigger) ----------

    [Fact]
    public void LevelTrigger_RespectsCooldownBetweenFires()
    {
        var rule = new RuleConfig { Name = "flap", Trigger = new TriggerConfig { Type = TriggerType.BatteryAbove, Threshold = 80, CooldownSeconds = 60 } };
        var device = Device(rule);
        var state = State(battery: 90f);

        var fired = TriggerEvaluator.Evaluate(device, state, PowerStatus.Charging);
        fired.Should().ContainSingle();
        TriggerEvaluator.RecordFired(rule, state);

        // Immediate re-evaluation: still above threshold, but cooldown blocks.
        TriggerEvaluator.Evaluate(device, state, PowerStatus.Charging).Should().BeEmpty();
    }

    // ---------- Legacy regression (unchanged semantics) ----------

    [Fact]
    public void PowerLost_EdgeFiresOnlyOnTransition()
    {
        var rule = new RuleConfig { Name = "lost", Trigger = new TriggerConfig { Type = TriggerType.PowerLost } };
        var device = Device(rule);
        var state = State(power: PowerStatus.PowerLost);

        TriggerEvaluator.Evaluate(device, state, previousPower: PowerStatus.Charging).Should().ContainSingle();
        TriggerEvaluator.Evaluate(device, state, previousPower: PowerStatus.PowerLost).Should().BeEmpty();
    }
}
