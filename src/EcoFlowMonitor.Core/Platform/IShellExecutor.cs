namespace EcoFlowMonitor.Platform;

/// <summary>
/// Which shell to invoke for <see cref="IShellExecutor.RunAsync"/>.
/// See contracts/IShellExecutor.md for the full behavioural contract.
/// </summary>
public enum ShellKind
{
    /// <summary>/bin/sh on macOS/Linux, cmd.exe on Windows.</summary>
    Default,
    /// <summary>/bin/sh on macOS/Linux. Invalid on Windows.</summary>
    Sh,
    /// <summary>cmd.exe. Invalid on macOS/Linux.</summary>
    Cmd,
    /// <summary>pwsh on the PATH. Falls back to powershell.exe on Windows when pwsh is missing.</summary>
    PowerShell,
}

public sealed record ShellExecRequest(
    string Command,
    ShellKind Shell = ShellKind.Default,
    string? WorkingDirectory = null,
    TimeSpan Timeout = default);

public sealed record ShellExecResult(
    int ExitCode,
    string StdOutHead,   // first 4 KiB, UTF-8 best-effort
    string StdErrHead,   // first 4 KiB
    bool   TimedOut,
    TimeSpan Duration);

/// <summary>
/// Cross-platform shell invocation. Per Constitution principle II, Core must not
/// depend on Process-spawning primitives; concrete implementations live in the
/// per-OS Platform.* projects.
/// </summary>
public interface IShellExecutor
{
    /// <summary>
    /// Run <paramref name="request"/> through the selected shell and return its
    /// result. Implementations MUST NOT throw on non-zero exit codes or on
    /// process failure — those outcomes are reported in the returned record.
    /// They MUST throw only on genuinely unexpected infrastructure failure
    /// (shell executable missing, permission denied to spawn, shell/OS
    /// mismatch).
    /// </summary>
    Task<ShellExecResult> RunAsync(
        ShellExecRequest request,
        CancellationToken cancellationToken = default);
}
