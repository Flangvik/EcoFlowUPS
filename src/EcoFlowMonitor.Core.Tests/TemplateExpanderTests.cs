using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests;

public class TemplateExpanderTests
{
    private static (DeviceConfig device, DeviceState state) MakeFixture()
    {
        var device = new DeviceConfig
        {
            DisplayName   = "Living Room UPS",
            SerialNumber  = "DELTA3-MAX-000000000000",
        };
        var state = new DeviceState
        {
            Bms     = new BmsData { BatteryPct = 42.5f, RemainMin = 135, TempC = 31.1f },
            Display = new DisplayData { TotalInW = 0, TotalOutW = 180, AcPluggedIn = false },
            Ems     = new EmsData { ChgState = 1 },
            Power   = new PowerState { Status = PowerStatus.PowerLost },
        };
        return (device, state);
    }

    [Fact]
    public void ExpandString_ExpandsAllLegacyVariables()
    {
        var (d, s) = MakeFixture();
        var result = TemplateExpander.ExpandString(
            "{device}|{battery}|{remain}|{status}|{in_w}|{out_w}", d, s);
        result.Should().Be("Living Room UPS|42.5|2h 15m|PowerLost|0|180");
    }

    [Fact]
    public void ExpandString_ExpandsAllNewVariables()
    {
        var (d, s) = MakeFixture();
        var result = TemplateExpander.ExpandString(
            "{temp_c}|{ac_plugged}|{charge_state}|{device_sn}", d, s);
        result.Should().Be("31.1|false|1|DELTA3-MAX-000000000000");
    }

    [Fact]
    public void ExpandString_UsesUnknownPlaceholderForMissingNewVariables()
    {
        var device = new DeviceConfig { DisplayName = "Test" };
        var state  = new DeviceState(); // no Bms/Display/Ems
        var result = TemplateExpander.ExpandString(
            "{temp_c}|{ac_plugged}|{charge_state}|{device_sn}", device, state);
        result.Should().Be("<unknown>|<unknown>|<unknown>|<unknown>");
    }

    [Fact]
    public void ExpandString_UsesLegacyQuestionMarkForMissingLegacyVariables()
    {
        var device = new DeviceConfig { DisplayName = "Test" };
        var state  = new DeviceState();
        var result = TemplateExpander.ExpandString(
            "{battery}|{remain}|{in_w}|{out_w}", device, state);
        result.Should().Be("?|?|?|?");
    }

    [Fact]
    public void Expand_ActionConfig_ProducesExpandedCopyWithoutMutatingInput()
    {
        var (d, s) = MakeFixture();
        var input = new ActionConfig
        {
            Type              = ActionType.Notification,
            NotificationTitle = "{device}",
            NotificationBody  = "Battery at {battery}% ({remain})",
        };
        var result = TemplateExpander.Expand(input, d, s);

        result.NotificationTitle.Should().Be("Living Room UPS");
        result.NotificationBody.Should().Be("Battery at 42.5% (2h 15m)");
        // Input untouched.
        input.NotificationTitle.Should().Be("{device}");
    }
}
