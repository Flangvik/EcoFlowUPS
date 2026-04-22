using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Triggers;

/// <summary>
/// Pure function that evaluates one condition against the current device
/// state. Never throws; missing telemetry (R-001) yields <c>false</c>.
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>
    /// Evaluate a single condition. Caller holds <see cref="DeviceState.SyncLock"/>.
    /// </summary>
    public static bool Evaluate(ConditionConfig c, DeviceState s)
    {
        switch (c.Type)
        {
            case TriggerType.PowerLost:
                return s.Power.Status == PowerStatus.PowerLost;

            case TriggerType.PowerRestored:
                return s.Power.Status == PowerStatus.Charging;

            case TriggerType.AcPlugged:
                return s.Display?.AcPluggedIn == true;

            case TriggerType.AcUnplugged:
                return s.Display is { AcPluggedIn: false };

            case TriggerType.BatteryBelow:
                return s.Bms?.BatteryPct is float bb && bb < c.Threshold;

            case TriggerType.BatteryAbove:
                return s.Bms?.BatteryPct is float ba && ba > c.Threshold;

            case TriggerType.TimeRemainingBelow:
                return s.Bms?.RemainMin is int rm && rm < c.Threshold;

            case TriggerType.TempAbove:
            {
                if (s.Bms?.TempC is not float t) return false;
                var thr = c.ThresholdF ?? c.Threshold;
                return t > thr;
            }

            case TriggerType.TempBelow:
            {
                if (s.Bms?.TempC is not float t) return false;
                var thr = c.ThresholdF ?? c.Threshold;
                return t < thr;
            }

            case TriggerType.InputWattsBelow:
                return s.Display?.TotalInW is int wi && wi < c.Threshold;

            case TriggerType.OutputWattsAbove:
                return s.Display?.TotalOutW is int wo && wo > c.Threshold;

            case TriggerType.DeviceOffline:
                return s.IsOffline;

            case TriggerType.DeviceOnline:
                return !s.IsOffline && s.LastDataReceived.HasValue;

            default:
                return false;
        }
    }
}
