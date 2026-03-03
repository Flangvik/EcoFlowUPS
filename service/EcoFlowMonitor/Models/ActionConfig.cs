using EcoFlowMonitor.Actions;

namespace EcoFlowMonitor.Models
{
    public class ActionConfig
    {
        public ActionType Type { get; set; }

        // RunScript
        public string ScriptPath { get; set; }

        // Notification
        public string NotificationTitle { get; set; } = "EcoFlow Alert";
        public string NotificationBody { get; set; }

        // WriteLog
        public string LogPath { get; set; }
        public string LogMessage { get; set; }
    }
}
