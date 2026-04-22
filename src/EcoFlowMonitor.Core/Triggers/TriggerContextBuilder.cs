using System.Text.Json;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Triggers;

/// <summary>
/// Builds the JSON payload stored on <c>rule_firings.trigger_value_json</c>.
/// Extracted from the orchestrator so unit tests can assert the shape
/// without standing up the full monitor stack.
/// Shape (feature 002):
/// <code>
/// {
///   "device":  { ... snapshot ... },
///   "trigger": {
///     "operator": "All" | "Any",
///     "conditions": [ { "index": n, "type": "...", "threshold": n,
///                       "thresholdF": n|null, "windowSeconds": n|null,
///                       "value": true|false } ]
///   }
/// }
/// </code>
/// </summary>
public static class TriggerContextBuilder
{
    public static string Build(DeviceConfig device, RuleConfig rule, DeviceState state)
    {
        rule.EnsureConditionsHydrated();
        var truths = TriggerEvaluator.EvaluateConditions(rule, state);

        var conditions = new List<object>(rule.Conditions.Count);
        for (int i = 0; i < rule.Conditions.Count; i++)
        {
            var c = rule.Conditions[i];
            conditions.Add(new
            {
                index         = i,
                type          = c.Type.ToString(),
                threshold     = c.Threshold,
                thresholdF    = c.ThresholdF,
                windowSeconds = c.WindowSeconds,
                value         = truths[i],
            });
        }

        var ctx = new
        {
            device = new
            {
                serialNumber = device.SerialNumber,
                name         = device.DisplayName,
                batteryPct   = state.Bms?.BatteryPct,
                remainMin    = state.Bms?.RemainMin,
                tempC        = state.Bms?.TempC,
                totalInW     = state.Display?.TotalInW,
                totalOutW    = state.Display?.TotalOutW,
                acPluggedIn  = state.Display?.AcPluggedIn,
                chargeState  = state.Ems?.ChgState,
                powerStatus  = state.Power.Status.ToString(),
            },
            trigger = new
            {
                @operator  = rule.Operator.ToString(),
                conditions,
            },
        };
        return JsonSerializer.Serialize(ctx);
    }
}
