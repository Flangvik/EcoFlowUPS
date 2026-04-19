using System.Diagnostics;
using System.Text;

namespace EcoFlowMonitor.Platform;

/// <summary>
/// Cross-platform <see cref="IShellExecutor"/>. Works on Windows / macOS /
/// Linux — picks the right shell at runtime via <see cref="OperatingSystem"/>
/// checks. No per-OS platform project needed: the only OS-specific bit is
/// which binary to invoke, and the BCL's <see cref="Process"/> class handles
/// the rest identically everywhere.
/// </summary>
public sealed class ShellExecutor : IShellExecutor
{
    private const int HeadByteCap = 4 * 1024;   // 4 KiB stdout/stderr truncation per contract
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<ShellExecResult> RunAsync(
        ShellExecRequest request,
        CancellationToken cancellationToken = default)
    {
        var (fileName, argv) = ResolveShell(request.Command, request.Shell);

        var psi = new ProcessStartInfo
        {
            FileName               = fileName,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = request.WorkingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };

        var stdout = new StringBuilder(HeadByteCap);
        var stderr = new StringBuilder(HeadByteCap);
        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutDone.TrySetResult(); return; }
            if (stdout.Length < HeadByteCap)
                stdout.Append(e.Data).Append('\n');
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrDone.TrySetResult(); return; }
            if (stderr.Length < HeadByteCap)
                stderr.Append(e.Data).Append('\n');
        };

        var started = proc.Start();
        if (!started)
            throw new InvalidOperationException($"Failed to start {fileName}");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var effectiveTimeout = request.Timeout > TimeSpan.Zero ? request.Timeout : DefaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        var sw = Stopwatch.StartNew();
        bool timedOut = false;

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(proc);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        sw.Stop();

        // Let any buffered output flush (bounded wait; process is dead or exited).
        await Task.WhenAny(Task.WhenAll(stdoutDone.Task, stderrDone.Task),
                          Task.Delay(250, cancellationToken)).ConfigureAwait(false);

        return new ShellExecResult(
            ExitCode:    timedOut ? -1 : proc.ExitCode,
            StdOutHead:  Truncate(stdout.ToString(), HeadByteCap),
            StdErrHead:  Truncate(stderr.ToString(), HeadByteCap),
            TimedOut:    timedOut,
            Duration:    sw.Elapsed);
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* best-effort */ }
    }

    private static (string fileName, string[] argv) ResolveShell(string command, ShellKind kind)
    {
        var isWindows = OperatingSystem.IsWindows();

        return (kind, isWindows) switch
        {
            (ShellKind.Cmd,        true)   => ("cmd.exe", new[] { "/c", command }),
            (ShellKind.Cmd,        false)  => throw new PlatformNotSupportedException("cmd.exe is Windows-only"),
            (ShellKind.Sh,         true)   => throw new PlatformNotSupportedException("/bin/sh is not the Windows default; use cmd or pwsh"),
            (ShellKind.Sh,         false)  => ("/bin/sh", new[] { "-c", command }),
            (ShellKind.PowerShell, true)   => (FindPwshOrPowerShellWindows(), new[] { "-NoProfile", "-NonInteractive", "-Command", command }),
            (ShellKind.PowerShell, false)  => ("pwsh",    new[] { "-NoProfile", "-NonInteractive", "-Command", command }),
            // Default → cmd on Windows, /bin/sh on macOS/Linux
            (ShellKind.Default,    true)   => ("cmd.exe", new[] { "/c", command }),
            (ShellKind.Default,    false)  => ("/bin/sh", new[] { "-c", command }),
            _ => throw new InvalidOperationException($"Unsupported shell kind: {kind}"),
        };
    }

    private static string FindPwshOrPowerShellWindows()
    {
        // Prefer cross-platform pwsh; fall back to windows-bundled powershell.exe.
        foreach (var candidate in new[] { "pwsh.exe", "powershell.exe" })
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var dir in paths)
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full)) return candidate;
            }
        }
        return "powershell.exe"; // let Process.Start report the error if neither is on PATH
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max);
}
