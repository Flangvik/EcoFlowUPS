using EcoFlowMonitor.Actions;

namespace EcoFlowMonitor.Models;

public class ActionConfig
{
    public ActionType Type { get; set; }
    public string? ScriptPath { get; set; }
    public string NotificationTitle { get; set; } = "EcoFlow Alert";
    public string? NotificationBody { get; set; }
    public string? LogPath { get; set; }
    public string? LogMessage { get; set; }
}
