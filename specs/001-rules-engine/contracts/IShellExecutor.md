# Contract: `IShellExecutor`

**Location:** `src/EcoFlowMonitor.Core/Platform/IShellExecutor.cs`
**Consumers:** `RunCommandAction` in `Core/Actions/`
**Implementations:** `Platform.Windows/WindowsShellExecutor.cs`,
`Platform.macOS/MacShellExecutor.cs`,
`Platform.Linux/LinuxShellExecutor.cs`

This is a new platform abstraction required by **Constitution II
(Platform Abstraction at the Boundary)** — `RunCommand` cannot live in
`Core` directly because it needs to spawn processes via per-OS shells,
and Core must have zero OS dependencies beyond `OperatingSystem.IsXxx`.

## Purpose

Given a command string and a shell flavour, execute it and return the
exit code plus bounded-size stdout/stderr captures.

## Shape

```csharp
namespace EcoFlowMonitor.Platform;

public enum ShellKind
{
    /// <summary>/bin/sh on macOS/Linux, cmd.exe on Windows.</summary>
    Default,
    /// <summary>/bin/sh on macOS/Linux. Invalid on Windows.</summary>
    Sh,
    /// <summary>cmd.exe. Invalid on macOS/Linux.</summary>
    Cmd,
    /// <summary>pwsh.exe on Windows, pwsh on macOS/Linux when installed.</summary>
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

public interface IShellExecutor
{
    /// <summary>
    /// Run <paramref name="request"/> through the selected shell and
    /// return its result. The implementation MUST NOT throw on non-zero
    /// exit codes or on process failure: those outcomes are reported in
    /// the returned record. It MUST throw only on genuinely unexpected
    /// infrastructure failure (shell executable missing on disk,
    /// permission denied to spawn processes, etc.).
    /// </summary>
    Task<ShellExecResult> RunAsync(
        ShellExecRequest request,
        CancellationToken cancellationToken = default);
}
```

## Behavioural contract

| Scenario | Expected behaviour |
|---|---|
| `request.Timeout == default` | Implementation picks a sensible default (recommended 30 s); MUST NOT run unbounded. |
| `request.Timeout` elapses | Kill the process tree. Return `TimedOut = true`, `ExitCode = -1`. Do not throw. |
| `CancellationToken` fires | Kill the process tree and throw `OperationCanceledException`. |
| `request.Shell` invalid for OS (e.g. `Cmd` on Linux) | Throw `PlatformNotSupportedException` with a clear message. |
| `request.WorkingDirectory` does not exist | Return `ExitCode` from the shell's own error handling. Do not throw. |
| Process writes > 4 KiB to stdout/stderr | Truncate to first 4 KiB each; full output is NOT buffered in memory. |
| Process produces non-UTF-8 bytes | Decode with `Encoding.UTF8.GetString(ReadOnlySpan<byte>, throw: false)` — invalid sequences become U+FFFD replacement characters. |

## Per-platform invocation

| OS | Default shell | Argv |
|---|---|---|
| Windows | `cmd.exe` | `cmd.exe /c "<command>"` |
| Windows, `Shell=PowerShell` | `pwsh.exe` if on PATH else `powershell.exe` | `pwsh -NoProfile -NonInteractive -Command "<command>"` |
| macOS | `/bin/sh` | `/bin/sh -c "<command>"` |
| macOS, `Shell=PowerShell` | `pwsh` if on PATH (else throw `PlatformNotSupportedException`) | `pwsh -NoProfile -Command "<command>"` |
| Linux | `/bin/sh` | `/bin/sh -c "<command>"` |
| Linux, `Shell=PowerShell` | `pwsh` if on PATH (else throw) | `pwsh -NoProfile -Command "<command>"` |

All implementations use `ProcessStartInfo`:

```
UseShellExecute       = false
RedirectStandardOutput = true
RedirectStandardError  = true
CreateNoWindow         = true
WorkingDirectory       = request.WorkingDirectory ?? Environment.CurrentDirectory
```

## Registration

`PlatformServiceFactory.Register(IServiceCollection, IConfiguration)`
wires the per-OS implementation into DI:

```csharp
if (OperatingSystem.IsWindows())
    services.AddSingleton<IShellExecutor, WindowsShellExecutor>();
else if (OperatingSystem.IsMacOS())
    services.AddSingleton<IShellExecutor, MacShellExecutor>();
else if (OperatingSystem.IsLinux())
    services.AddSingleton<IShellExecutor, LinuxShellExecutor>();
else
    services.AddSingleton<IShellExecutor, StubShellExecutor>();
    // StubShellExecutor throws PlatformNotSupportedException from RunAsync
```

This mirrors the existing pattern for `INotificationService`,
`IPowerActionService`, etc.

## Test surface

`EcoFlowMonitor.Core.Tests/RunCommandActionTests.cs` uses a
`FakeShellExecutor : IShellExecutor` to verify:
- Command strings are template-expanded before the executor sees them.
- Per-OS dispatch picks the correct `commandWindows|macOS|linux` field.
- `skipped` outcome is produced when the matching field is null/empty.
- `timeout` outcome when the executor reports `TimedOut = true`.
- Non-zero exit code maps to `failure` with `errorText` populated from
  `StdErrHead`.
