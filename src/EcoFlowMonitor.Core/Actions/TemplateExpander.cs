using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Actions;

public static class TemplateExpander
{
    /// <summary>
    /// Expands template variables in the action configuration.
    ///
    /// Template variables:
    ///   {device}  - device display name
    ///   {battery} - battery % (one decimal place, or "?" if unknown)
    ///   {remain}  - remaining runtime, e.g. "2h 15m" (or "?" if unknown)
    ///   {status}  - current power status string
    ///   {in_w}    - total input watts (or "?" if unknown)
    ///   {out_w}   - total output watts (or "?" if unknown)
    /// </summary>
    public static ActionConfig Expand(ActionConfig action, DeviceConfig device, DeviceState state)
    {
        string? ExpandStr(string? s)
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
            ScriptPath        = ExpandStr(action.ScriptPath),
            NotificationTitle = ExpandStr(action.NotificationTitle) ?? "EcoFlow Alert",
            NotificationBody  = ExpandStr(action.NotificationBody),
            LogPath           = action.LogPath,          // paths are not template-expanded
            LogMessage        = ExpandStr(action.LogMessage)
        };
    }
}
