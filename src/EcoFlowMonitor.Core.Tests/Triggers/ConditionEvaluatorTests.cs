using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests.Triggers;

/// <summary>
/// Covers every <see cref="TriggerType"/> × (present true / present false /
/// missing telemetry). Missing telemetry MUST return false (R-001, FR-005).
/// </summary>
public class ConditionEvaluatorTests
{
    private static DeviceState State(
        float? battery = null, float? tempC = null, int? remainMin = null,
        int? inW = null, int? outW = null, bool? acPluggedIn = null,
        PowerStatus power = PowerStatus.Charging,
        bool isOffline = false,
        bool hasAnyData = true) =>
        new()
        {
            Bms = (battery is null && tempC is null && remainMin is null)
                ? null
                : new BmsData { BatteryPct = battery, TempC = tempC, RemainMin = remainMin },
            Display = (inW is null && outW is null && acPluggedIn is null)
                ? null
                : new DisplayData { TotalInW = inW, TotalOutW = outW, AcPluggedIn = acPluggedIn },
            Power = new PowerState { Status = power },
            IsOffline = isOffline,
            LastDataReceived = hasAnyData ? DateTime.UtcNow : null,
        };

    // -- PowerLost / PowerRestored ------------------------------------------

    [Fact]
    public void PowerLost_True_WhenPowerStatusIsPowerLost()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.PowerLost },
            State(power: PowerStatus.PowerLost))
        .Should().BeTrue();
    }

    [Fact]
    public void PowerLost_False_WhenCharging()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.PowerLost },
            State(power: PowerStatus.Charging))
        .Should().BeFalse();
    }

    [Fact]
    public void PowerRestored_True_WhenCharging()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.PowerRestored },
            State(power: PowerStatus.Charging))
        .Should().BeTrue();
    }

    [Fact]
    public void PowerRestored_False_WhenPowerLost()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.PowerRestored },
            State(power: PowerStatus.PowerLost))
        .Should().BeFalse();
    }

    // -- AC plug / unplug ----------------------------------------------------

    [Fact]
    public void AcPlugged_True_WhenAcPluggedIn()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.AcPlugged },
            State(acPluggedIn: true))
        .Should().BeTrue();
    }

    [Fact]
    public void AcPlugged_False_WhenAcUnplugged()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.AcPlugged },
            State(acPluggedIn: false))
        .Should().BeFalse();
    }

    [Fact]
    public void AcUnplugged_True_WhenDisplayReportsFalse()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.AcUnplugged },
            State(acPluggedIn: false))
        .Should().BeTrue();
    }

    [Fact]
    public void AcUnplugged_False_WhenAcPluggedIn()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.AcUnplugged },
            State(acPluggedIn: true))
        .Should().BeFalse();
    }

    [Fact]
    public void AcUnplugged_False_WhenDisplayMissing()
    {
        // Missing Display data should NOT coerce AcUnplugged to true.
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.AcUnplugged },
            State())
        .Should().BeFalse();
    }

    // -- BatteryBelow / BatteryAbove ----------------------------------------

    [Theory]
    [InlineData(15f, 20, true)]
    [InlineData(20f, 20, false)]  // not strictly below
    [InlineData(25f, 20, false)]
    public void BatteryBelow_EvaluatesCorrectly(float pct, int thr, bool expected)
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = thr },
            State(battery: pct))
        .Should().Be(expected);
    }

    [Fact]
    public void BatteryBelow_False_WhenBmsMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            State())
        .Should().BeFalse();
    }

    [Theory]
    [InlineData(85f, 80, true)]
    [InlineData(80f, 80, false)]
    [InlineData(50f, 80, false)]
    public void BatteryAbove_EvaluatesCorrectly(float pct, int thr, bool expected)
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.BatteryAbove, Threshold = thr },
            State(battery: pct))
        .Should().Be(expected);
    }

    [Fact]
    public void BatteryAbove_False_WhenBmsMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.BatteryAbove, Threshold = 80 },
            State())
        .Should().BeFalse();
    }

    // -- TimeRemainingBelow --------------------------------------------------

    [Theory]
    [InlineData(10, 30, true)]
    [InlineData(30, 30, false)]
    [InlineData(60, 30, false)]
    public void TimeRemainingBelow_EvaluatesCorrectly(int remain, int thr, bool expected)
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TimeRemainingBelow, Threshold = thr },
            State(remainMin: remain))
        .Should().Be(expected);
    }

    [Fact]
    public void TimeRemainingBelow_False_WhenBmsMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TimeRemainingBelow, Threshold = 30 },
            State())
        .Should().BeFalse();
    }

    // -- TempAbove / TempBelow ----------------------------------------------

    [Fact]
    public void TempAbove_True_WhenAboveFloatThreshold()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55.0f },
            State(tempC: 60f))
        .Should().BeTrue();
    }

    [Fact]
    public void TempAbove_UsesIntWhenFloatNull()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TempAbove, Threshold = 50 },
            State(tempC: 51f))
        .Should().BeTrue();
    }

    [Fact]
    public void TempAbove_False_WhenAtOrBelow()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f },
            State(tempC: 55f))
        .Should().BeFalse();
    }

    [Fact]
    public void TempAbove_False_WhenBmsMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TempAbove, ThresholdF = 55f },
            State())
        .Should().BeFalse();
    }

    [Fact]
    public void TempBelow_True_WhenBelowThreshold()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TempBelow, ThresholdF = 10f },
            State(tempC: 5f))
        .Should().BeTrue();
    }

    [Fact]
    public void TempBelow_False_WhenBmsMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.TempBelow, ThresholdF = 10f },
            State())
        .Should().BeFalse();
    }

    // -- InputWattsBelow / OutputWattsAbove ---------------------------------

    [Fact]
    public void InputWattsBelow_True_WhenStrictlyBelow()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.InputWattsBelow, Threshold = 50 },
            State(inW: 10))
        .Should().BeTrue();
    }

    [Fact]
    public void InputWattsBelow_False_WhenDisplayMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.InputWattsBelow, Threshold = 50 },
            State())
        .Should().BeFalse();
    }

    [Fact]
    public void OutputWattsAbove_True_WhenAbove()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.OutputWattsAbove, Threshold = 1000 },
            State(outW: 1500))
        .Should().BeTrue();
    }

    [Fact]
    public void OutputWattsAbove_False_WhenDisplayMissing()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.OutputWattsAbove, Threshold = 1000 },
            State())
        .Should().BeFalse();
    }

    // -- DeviceOffline / DeviceOnline ---------------------------------------

    [Fact]
    public void DeviceOffline_True_WhenIsOfflineTrue()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.DeviceOffline },
            State(isOffline: true))
        .Should().BeTrue();
    }

    [Fact]
    public void DeviceOffline_False_WhenOnline()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.DeviceOffline },
            State(isOffline: false))
        .Should().BeFalse();
    }

    [Fact]
    public void DeviceOnline_True_WhenNotOfflineAndHasData()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.DeviceOnline },
            State(isOffline: false, hasAnyData: true))
        .Should().BeTrue();
    }

    [Fact]
    public void DeviceOnline_False_WhenNoDataEverReceived()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.DeviceOnline },
            State(isOffline: false, hasAnyData: false))
        .Should().BeFalse();
    }

    [Fact]
    public void DeviceOnline_False_WhenIsOffline()
    {
        ConditionEvaluator.Evaluate(
            new ConditionConfig { Type = TriggerType.DeviceOnline },
            State(isOffline: true))
        .Should().BeFalse();
    }
}
