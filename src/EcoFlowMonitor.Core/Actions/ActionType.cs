namespace EcoFlowMonitor.Actions;

public enum ActionType
{
    // -- v0 (existing) --
    RunScript,
    Shutdown,
    Hibernate,
    Sleep,
    Notification,
    WriteLog,

    // -- v1 (rules-engine feature 001) --
    /// <summary>
    /// HTTP POST/PUT to a user-configured URL with user-supplied headers +
    /// body template. Configurable retries. See data-model.md for field set.
    /// </summary>
    Webhook,

    /// <summary>
    /// Execute a per-OS command string through the native shell via
    /// <see cref="Platform.IShellExecutor"/>. Template variables expanded
    /// before invocation.
    /// </summary>
    RunCommand,
}
