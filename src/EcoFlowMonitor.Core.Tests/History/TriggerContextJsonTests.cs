using System.Text.Json;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests.History;

/// <summary>
/// Asserts the audit-row <c>trigger_value_json</c> shape for composite rules
/// (R-007 / FR-016). Driven by <see cref="TriggerContextBuilder"/>.
/// </summary>
public class TriggerContextJsonTests
{
    private static DeviceConfig Device() =>
        new() { SerialNumber = "SN-AUD", DisplayName = "Delta 3" };

    [Fact]
    public void And_SingleCondition_ProducesOneEntryArray()
    {
        var device = Device();
        var rule   = new RuleConfig
        {
            Id = "r",
            Operator = RuleConditionOperator.All,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
            },
        };
        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 15f },
            Power = new PowerState { Status = PowerStatus.PowerLost },
        };

        var json = TriggerContextBuilder.Build(device, rule, state);
        using var doc = JsonDocument.Parse(json);

        var trigger = doc.RootElement.GetProperty("trigger");
        trigger.GetProperty("operator").GetString().Should().Be("All");
        var conditions = trigger.GetProperty("conditions");
        conditions.GetArrayLength().Should().Be(1);

        var c0 = conditions[0];
        c0.GetProperty("index").GetInt32().Should().Be(0);
        c0.GetProperty("type").GetString().Should().Be("BatteryBelow");
        c0.GetProperty("threshold").GetInt32().Should().Be(20);
        c0.GetProperty("value").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Or_ThreeConditions_CapturesEachBranchTruth()
    {
        var device = Device();
        var rule   = new RuleConfig
        {
            Id = "r",
            Operator = RuleConditionOperator.Any,
            Conditions =
            {
                new ConditionConfig { Type = TriggerType.BatteryBelow, Threshold = 5 },
                new ConditionConfig { Type = TriggerType.TempAbove,    ThresholdF = 55.0f },
                new ConditionConfig { Type = TriggerType.DeviceOffline, WindowSeconds = 120 },
            },
        };
        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 3f, TempC = 20f },
            Power = new PowerState { Status = PowerStatus.Charging },
            IsOffline = false,
            LastDataReceived = DateTime.UtcNow,
        };

        var json = TriggerContextBuilder.Build(device, rule, state);
        using var doc = JsonDocument.Parse(json);

        var trigger = doc.RootElement.GetProperty("trigger");
        trigger.GetProperty("operator").GetString().Should().Be("Any");
        var conditions = trigger.GetProperty("conditions");
        conditions.GetArrayLength().Should().Be(3);

        conditions[0].GetProperty("type").GetString().Should().Be("BatteryBelow");
        conditions[0].GetProperty("value").GetBoolean().Should().BeTrue();

        conditions[1].GetProperty("type").GetString().Should().Be("TempAbove");
        conditions[1].GetProperty("thresholdF").GetSingle().Should().Be(55.0f);
        conditions[1].GetProperty("value").GetBoolean().Should().BeFalse();

        conditions[2].GetProperty("type").GetString().Should().Be("DeviceOffline");
        conditions[2].GetProperty("windowSeconds").GetInt32().Should().Be(120);
        conditions[2].GetProperty("value").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void LegacySingleTrigger_Hydrated_ProducesIdenticalShape()
    {
        var device = Device();
        var rule   = new RuleConfig
        {
            Id = "r",
            Trigger = new TriggerConfig { Type = TriggerType.BatteryBelow, Threshold = 20 },
        };
        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 15f },
            Power = new PowerState { Status = PowerStatus.Charging },
        };

        var json = TriggerContextBuilder.Build(device, rule, state);
        using var doc = JsonDocument.Parse(json);

        var conditions = doc.RootElement.GetProperty("trigger").GetProperty("conditions");
        conditions.GetArrayLength().Should().Be(1);
        conditions[0].GetProperty("type").GetString().Should().Be("BatteryBelow");
        conditions[0].GetProperty("threshold").GetInt32().Should().Be(20);
    }

    [Fact]
    public void DeviceSnapshot_AlwaysIncludedAlongsideTrigger()
    {
        var device = Device();
        var rule   = new RuleConfig
        {
            Conditions = { new ConditionConfig { Type = TriggerType.PowerLost } },
        };
        var state = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 42.5f },
            Display = new DisplayData { TotalInW = 10, TotalOutW = 200, AcPluggedIn = false },
            Power = new PowerState { Status = PowerStatus.PowerLost },
        };

        var json = TriggerContextBuilder.Build(device, rule, state);
        using var doc = JsonDocument.Parse(json);

        var deviceNode = doc.RootElement.GetProperty("device");
        deviceNode.GetProperty("serialNumber").GetString().Should().Be("SN-AUD");
        deviceNode.GetProperty("name").GetString().Should().Be("Delta 3");
        deviceNode.GetProperty("batteryPct").GetSingle().Should().BeApproximately(42.5f, 0.001f);
        deviceNode.GetProperty("acPluggedIn").GetBoolean().Should().BeFalse();
        deviceNode.GetProperty("powerStatus").GetString().Should().Be("PowerLost");
    }
}
