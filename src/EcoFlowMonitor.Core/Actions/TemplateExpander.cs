using System.Globalization;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Actions;

public static class TemplateExpander
{
    /// <summary>
    /// Expands template variables in the action configuration.
    ///
    /// Legacy variables (kept for backward compat with existing rules):
    ///   {device}  - device display name
    ///   {battery} - battery % (one decimal place, or "?" if unknown)
    ///   {remain}  - remaining runtime, e.g. "2h 15m" (or "?" if unknown)
    ///   {status}  - current power status string
    ///   {in_w}    - total input watts (or "?" if unknown)
    ///   {out_w}   - total output watts (or "?" if unknown)
    ///
    /// Rules-engine v1 additions (FR-009):
    ///   {temp_c}        - BMS temperature in °C (1 decimal, or "&lt;unknown&gt;")
    ///   {ac_plugged}    - "true"/"false" (or "&lt;unknown&gt;")
    ///   {charge_state}  - raw charge-state integer (or "&lt;unknown&gt;")
    ///   {device_sn}     - device serial number (or "&lt;unknown&gt;")
    ///
    /// Unknown variables / null values expand to "&lt;unknown&gt;" on the new vars,
    /// and to "?" on the legacy ones for backward compatibility.
    /// </summary>
    public static ActionConfig Expand(ActionConfig action, DeviceConfig device, DeviceState state)
    {
        string? ExpandStr(string? s)
        {
            if (s == null) return null;
            return ExpandString(s, device, state);
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

    /// <summary>
    /// Expand template variables in an arbitrary string. Used by webhook body
    /// templates, command strings for RunCommand, etc. Null/missing values
    /// expand to "&lt;unknown&gt;" on the new vars and "?" on the legacy ones.
    /// </summary>
    public static string ExpandString(string input, DeviceConfig device, DeviceState state)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // All numeric formatting uses InvariantCulture so expansions don't vary
        // with the user's locale — webhook payloads and shell commands MUST be
        // locale-neutral.
        var inv = CultureInfo.InvariantCulture;

        // -- Legacy vars (preserve "?" fallback) --
        var batt   = state.Bms?.BatteryPct?.ToString("F1", inv) ?? "?";

        int? remainMin = state.Bms?.RemainMin;
        string remain  = remainMin.HasValue
            ? string.Format(inv, "{0}h {1}m", remainMin.Value / 60, remainMin.Value % 60)
            : "?";

        var status = state.Power.Status.ToString();
        var inW    = state.Display?.TotalInW?.ToString(inv) ?? "?";
        var outW   = state.Display?.TotalOutW?.ToString(inv) ?? "?";

        // -- New vars ("<unknown>" fallback per spec FR-009) --
        const string U = "<unknown>";
        var tempC       = state.Bms?.TempC.HasValue == true ? state.Bms.TempC!.Value.ToString("F1", inv) : U;
        var acPlugged   = state.Display?.AcPluggedIn.HasValue == true ? state.Display.AcPluggedIn!.Value ? "true" : "false" : U;
        var chargeState = state.Ems?.ChgState.HasValue == true ? state.Ems.ChgState!.Value.ToString(inv) : U;
        var deviceSn    = !string.IsNullOrEmpty(device.SerialNumber) ? device.SerialNumber : U;

        return input
            // legacy
            .Replace("{device}",       device.DisplayName ?? U)
            .Replace("{battery}",      batt)
            .Replace("{remain}",       remain)
            .Replace("{status}",       status)
            .Replace("{in_w}",         inW)
            .Replace("{out_w}",        outW)
            // new (rules-engine v1)
            .Replace("{temp_c}",       tempC)
            .Replace("{ac_plugged}",   acPlugged)
            .Replace("{charge_state}", chargeState)
            .Replace("{device_sn}",    deviceSn);
    }
}
