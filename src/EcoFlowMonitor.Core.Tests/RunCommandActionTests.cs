using System.Text.Json;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.State;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests;

public class RunCommandActionTests
{
    private sealed class FakeShell : IShellExecutor
    {
        public ShellExecRequest? LastRequest { get; private set; }
        public ShellExecResult Response { get; set; } =
            new(ExitCode: 0, StdOutHead: "", StdErrHead: "", TimedOut: false, Duration: TimeSpan.FromMilliseconds(10));
        public Func<ShellExecRequest, ShellExecResult>? Override { get; set; }

        public Task<ShellExecResult> RunAsync(ShellExecRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            var r = Override is null ? Response : Override(request);
            return Task.FromResult(r);
        }
    }

    private static (DeviceConfig, DeviceState) Fixture() => (
        new DeviceConfig { DisplayName = "Station", SerialNumber = "SN-C" },
        new DeviceState
        {
            Bms = new BmsData { BatteryPct = 42.5f, RemainMin = 30 },
            Display = new DisplayData { TotalInW = 0, TotalOutW = 100 },
            Power = new PowerState { Status = PowerStatus.PowerLost },
        });

    [Fact]
    public async Task CurrentOsCommand_IsInvoked()
    {
        var (d, s) = Fixture();
        var shell = new FakeShell();
        var cfg = new RunCommandActionData
        {
            CommandWindows = "echo win",
            CommandMacOS   = "echo mac",
            CommandLinux   = "echo lin",
        };

        var (outcome, _, _) = await RunCommandAction.RunAsync(cfg, d, s, shell, default);

        outcome.Should().Be(RuleFiringActionOutcome.Success);
        shell.LastRequest.Should().NotBeNull();
        var expected = OperatingSystem.IsWindows() ? "echo win"
                     : OperatingSystem.IsMacOS()   ? "echo mac"
                     : "echo lin";
        shell.LastRequest!.Command.Should().Be(expected);
    }

    [Fact]
    public async Task NoCommandForCurrentOs_SkipsCleanly_WithoutInvokingShell()
    {
        var (d, s) = Fixture();
        var shell = new FakeShell();
        var cfg = new RunCommandActionData();
        if (OperatingSystem.IsWindows()) cfg.CommandMacOS = "echo mac";
        if (OperatingSystem.IsMacOS())   cfg.CommandWindows = "echo win";
        if (OperatingSystem.IsLinux())   cfg.CommandWindows = "echo win";

        var (outcome, err, _) = await RunCommandAction.RunAsync(cfg, d, s, shell, default);

        outcome.Should().Be(RuleFiringActionOutcome.Skipped);
        err.Should().Contain("no command for");
        shell.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task TemplateVariables_AreExpanded_BeforeDispatch()
    {
        var (d, s) = Fixture();
        var shell = new FakeShell();
        var tmpl = "echo device={device} pct={battery}";
        var cfg = new RunCommandActionData
        {
            CommandWindows = tmpl,
            CommandMacOS   = tmpl,
            CommandLinux   = tmpl,
        };

        await RunCommandAction.RunAsync(cfg, d, s, shell, default);

        shell.LastRequest!.Command.Should().Be("echo device=Station pct=42.5");
    }

    [Fact]
    public async Task Timeout_MapsToTimeoutOutcome()
    {
        var (d, s) = Fixture();
        var shell = new FakeShell
        {
            Response = new ShellExecResult(-1, "", "", TimedOut: true, Duration: TimeSpan.FromSeconds(1)),
        };
        var cfg = new RunCommandActionData
        {
            CommandWindows = "sleep 60", CommandMacOS = "sleep 60", CommandLinux = "sleep 60",
            TimeoutMs = 1000,
        };

        var (outcome, err, detail) = await RunCommandAction.RunAsync(cfg, d, s, shell, default);

        outcome.Should().Be(RuleFiringActionOutcome.Timeout);
        err.Should().Contain("timed out");
        using var doc = JsonDocument.Parse(detail!);
        doc.RootElement.GetProperty("timedOut").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task NonZeroExit_MapsToFailure_WithStdErrInError()
    {
        var (d, s) = Fixture();
        var shell = new FakeShell
        {
            Response = new ShellExecResult(2, "", "boom: file missing", TimedOut: false, Duration: TimeSpan.FromMilliseconds(10)),
        };
        var cfg = new RunCommandActionData
        {
            CommandWindows = "false", CommandMacOS = "false", CommandLinux = "false",
        };

        var (outcome, err, detail) = await RunCommandAction.RunAsync(cfg, d, s, shell, default);

        outcome.Should().Be(RuleFiringActionOutcome.Failure);
        err.Should().StartWith("exit 2");
        err.Should().Contain("boom");
        using var doc = JsonDocument.Parse(detail!);
        doc.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ShellKind_FlowsThroughToRequest()
    {
        var (d, s) = Fixture();
        var shell = new FakeShell();
        var cfg = new RunCommandActionData
        {
            CommandWindows = "echo", CommandMacOS = "echo", CommandLinux = "echo",
            Shell = RunCommandShell.PowerShell,
        };

        await RunCommandAction.RunAsync(cfg, d, s, shell, default);

        shell.LastRequest!.Shell.Should().Be(ShellKind.PowerShell);
    }
}
