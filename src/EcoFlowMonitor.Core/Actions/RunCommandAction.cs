using System.Diagnostics;
using System.Text.Json;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Actions;

/// <summary>
/// Executes a per-OS command via <see cref="IShellExecutor"/>.
/// Implements FR-008 + the test scenarios T050-T054. If no command is
/// configured for the current OS, returns <c>skipped</c> cleanly.
/// </summary>
public static class RunCommandAction
{
    public static async Task<(RuleFiringActionOutcome outcome, string? errorText, string? detailJson)>
        RunAsync(
            RunCommandActionData cfg,
            DeviceConfig device,
            DeviceState state,
            IShellExecutor shell,
            CancellationToken ct)
    {
        var (osName, cmd) = PickForCurrentOs(cfg);
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return (
                RuleFiringActionOutcome.Skipped,
                $"no command for {osName}",
                JsonSerializer.Serialize(new { os = osName, reason = "no command configured" }));
        }

        var expanded = TemplateExpander.ExpandString(cmd, device, state);
        var wd       = cfg.WorkingDirectory is { Length: > 0 } w
            ? TemplateExpander.ExpandString(w, device, state)
            : null;

        var shellKind = cfg.Shell switch
        {
            RunCommandShell.Sh         => ShellKind.Sh,
            RunCommandShell.Cmd        => ShellKind.Cmd,
            RunCommandShell.PowerShell => ShellKind.PowerShell,
            _                          => ShellKind.Default,
        };

        var timeout = TimeSpan.FromMilliseconds(Math.Max(1000, cfg.TimeoutMs));
        var request = new ShellExecRequest(expanded, shellKind, wd, timeout);

        ShellExecResult result;
        try
        {
            result = await shell.RunAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (RuleFiringActionOutcome.Failure, Truncate(ex.Message, 512),
                JsonSerializer.Serialize(new { os = osName, error = ex.GetType().Name }));
        }

        var detail = JsonSerializer.Serialize(new
        {
            os         = osName,
            exitCode   = result.ExitCode,
            stdoutHead = result.StdOutHead,
            stderrHead = result.StdErrHead,
            timedOut   = result.TimedOut,
        });

        if (result.TimedOut)
        {
            return (RuleFiringActionOutcome.Timeout,
                $"command timed out after {timeout.TotalMilliseconds:F0}ms",
                detail);
        }

        if (result.ExitCode == 0)
            return (RuleFiringActionOutcome.Success, null, detail);

        var errText = $"exit {result.ExitCode}" +
                      (string.IsNullOrEmpty(result.StdErrHead) ? "" : $": {Truncate(result.StdErrHead, 450)}");
        return (RuleFiringActionOutcome.Failure, Truncate(errText, 512), detail);
    }

    private static (string osName, string? command) PickForCurrentOs(RunCommandActionData cfg)
    {
        if (OperatingSystem.IsWindows()) return ("windows", cfg.CommandWindows);
        if (OperatingSystem.IsMacOS())   return ("macos",   cfg.CommandMacOS);
        if (OperatingSystem.IsLinux())   return ("linux",   cfg.CommandLinux);
        return ("unknown", null);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
