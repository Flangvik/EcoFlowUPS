using EcoFlowMonitor.Actions;

namespace EcoFlowMonitor.Models;

/// <summary>
/// Flat (non-polymorphic) action configuration. Fields relevant to a given
/// <see cref="Type"/> are populated; others remain default/null.
///
/// The flat shape is preserved from v0 for backward compatibility with existing
/// <c>config.json</c> files. Variant-specific groups land in nested sub-records
/// (<see cref="Webhook"/>, <see cref="RunCommand"/>) so v1 fields don't crowd
/// the top-level namespace.
/// </summary>
public class ActionConfig
{
    public ActionType Type { get; set; }

    // -- v0 fields (unchanged shape) --
    public string? ScriptPath { get; set; }
    public string NotificationTitle { get; set; } = "EcoFlow Alert";
    public string? NotificationBody { get; set; }
    public string? LogPath { get; set; }
    public string? LogMessage { get; set; }

    // -- v1 (rules-engine feature 001) --

    /// <summary>Populated when <see cref="Type"/> is <see cref="ActionType.Webhook"/>.</summary>
    public WebhookActionData? Webhook { get; set; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="ActionType.RunCommand"/>.</summary>
    public RunCommandActionData? RunCommand { get; set; }
}

/// <summary>
/// Webhook action parameters. See spec FR-006, FR-007 and
/// contracts/webhook-request.json for the default body shape.
/// </summary>
public class WebhookActionData
{
    /// <summary>Absolute http/https URI. Required.</summary>
    public string Url { get; set; } = "";

    /// <summary>HTTP method. "POST" (default) or "PUT".</summary>
    public string Method { get; set; } = "POST";

    /// <summary>User-supplied request headers. Secret-looking names redacted in audit.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Optional user-provided body template (template variables expanded before
    /// sending). When null, the default JSON body from contracts/webhook-request.json
    /// is used.
    /// </summary>
    public string? BodyTemplate { get; set; }

    /// <summary>Retry count on transient failure. 0 = no retries. Range 0–5.</summary>
    public int Retries { get; set; } = 0;

    /// <summary>Delay between retries. Range 100–60000. Default 1000.</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>Per-attempt HTTP timeout. Range 1000–60000. Default 10000.</summary>
    public int TimeoutMs { get; set; } = 10000;
}

/// <summary>
/// RunCommand action parameters. See spec FR-008 and data-model.md.
/// At least one of the per-OS command fields MUST be non-null; the other
/// two are optional. Missing command for the current OS → action skips
/// cleanly (see <see cref="ActionRunner"/>).
/// </summary>
public class RunCommandActionData
{
    public string? CommandWindows { get; set; }
    public string? CommandMacOS { get; set; }
    public string? CommandLinux { get; set; }

    /// <summary>
    /// Which shell to invoke. Default → cmd on Windows, /bin/sh on macOS/Linux.
    /// PowerShell = pwsh on PATH (falls back to powershell.exe on Windows).
    /// </summary>
    public RunCommandShell Shell { get; set; } = RunCommandShell.Default;

    /// <summary>Working directory override. Template-expanded.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-action timeout. Range 1000–60000. Default 30000.</summary>
    public int TimeoutMs { get; set; } = 30000;
}

/// <summary>
/// Which shell the <see cref="RunCommand"/> action picks. Maps directly onto
/// <see cref="Platform.ShellKind"/>; kept as a separate enum in
/// <c>Models/</c> so the serialized name is stable regardless of the
/// Platform namespace layout.
/// </summary>
public enum RunCommandShell
{
    Default,
    Sh,
    Cmd,
    PowerShell,
}
