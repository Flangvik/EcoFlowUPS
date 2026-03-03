using System;
using System.Windows.Forms;
using EcoFlowMonitor.Core;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Actions
{
    public static class ActionRunner
    {
        /// <summary>
        /// Expands template variables in the action configuration then dispatches
        /// to the appropriate concrete handler.
        ///
        /// Template variables:
        ///   {device}  - device display name
        ///   {battery} - battery % (one decimal place, or "?" if unknown)
        ///   {remain}  - remaining runtime, e.g. "2h 15m" (or "?" if unknown)
        ///   {status}  - current power status string
        ///   {in_w}    - total input watts (or "?" if unknown)
        ///   {out_w}   - total output watts (or "?" if unknown)
        /// </summary>
        public static void Run(ActionConfig action, DeviceConfig device, DeviceState state, NotifyIcon trayIcon)
        {
            NotificationAction.SetSharedIcon(trayIcon);

            var expanded = ExpandTemplates(action, device, state);

            switch (action.Type)
            {
                case ActionType.RunScript:
                    ScriptRunner.Run(expanded, expanded.ScriptPath);
                    break;

                case ActionType.Shutdown:
                case ActionType.Hibernate:
                case ActionType.Sleep:
                    PowerAction.Execute(action.Type);
                    break;

                case ActionType.Notification:
                    NotificationAction.Show(expanded.NotificationTitle, expanded.NotificationBody);
                    break;

                case ActionType.WriteLog:
                    LogAction.Write(expanded.LogPath, expanded.LogMessage);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Template expansion
        // ------------------------------------------------------------------
        private static ActionConfig ExpandTemplates(ActionConfig action, DeviceConfig device, DeviceState state)
        {
            string Expand(string s)
            {
                if (s == null) return null;

                var batt = state.Bms?.BatteryPct?.ToString("F1") ?? "?";

                int? remainMin = state.Bms?.RemainMin;
                string remain  = remainMin.HasValue
                    ? $"{remainMin.Value / 60}h {remainMin.Value % 60}m"
                    : "?";

                var status = state.Power.Status.ToString();
                var inW    = state.Display?.TotalInW?.ToString() ?? "?";
                var outW   = state.Display?.TotalOutW?.ToString() ?? "?";

                return s
                    .Replace("{device}",  device.DisplayName)
                    .Replace("{battery}", batt)
                    .Replace("{remain}",  remain)
                    .Replace("{status}",  status)
                    .Replace("{in_w}",    inW)
                    .Replace("{out_w}",   outW);
            }

            return new ActionConfig
            {
                Type              = action.Type,
                ScriptPath        = Expand(action.ScriptPath),
                NotificationTitle = Expand(action.NotificationTitle),
                NotificationBody  = Expand(action.NotificationBody),
                LogPath           = action.LogPath,          // paths are not template-expanded
                LogMessage        = Expand(action.LogMessage)
            };
        }
    }
}
