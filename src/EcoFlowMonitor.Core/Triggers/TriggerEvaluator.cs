using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Triggers;

public static class TriggerEvaluator
{
    private static readonly TimeSpan DefaultLevelCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Evaluate all rules for a device.
    /// Returns the list of rules that should fire in this update cycle.
    /// <paramref name="previousPower"/> is the power status BEFORE the current update.
    /// Also maintains edge-trigger bookkeeping on <see cref="DeviceState.LastAcPluggedIn"/>.
    /// </summary>
    public static List<RuleConfig> Evaluate(DeviceConfig device, DeviceState state, PowerStatus previousPower)
    {
        var toFire = new List<RuleConfig>();
        var now    = DateTime.UtcNow;

        // Capture the previous AC-plug state before any rules observe it; update
        // the bookkeeping field AFTER rule evaluation so AcPlugged/AcUnplugged
        // rules see the edge.
        var prevAc = state.LastAcPluggedIn;
        var currAc = state.Display?.AcPluggedIn;

        foreach (var rule in device.Rules)
        {
            if (!rule.Enabled) continue;
            if (!ShouldFire(rule, state, previousPower, prevAc, currAc, now)) continue;
            toFire.Add(rule);
        }

        // Commit the AC-plug observation for next pass.
        if (currAc.HasValue) state.LastAcPluggedIn = currAc;
        return toFire;
    }

    private static bool ShouldFire(
        RuleConfig   rule,
        DeviceState  state,
        PowerStatus  previousPower,
        bool?        prevAc,
        bool?        currAc,
        DateTime     now)
    {
        var trigger = rule.Trigger;

        switch (trigger.Type)
        {
            // -- Power edges (existing) --
            case TriggerType.PowerLost:
                return previousPower != PowerStatus.PowerLost
                    && state.Power.Status == PowerStatus.PowerLost;

            case TriggerType.PowerRestored:
                return previousPower == PowerStatus.PowerLost
                    && state.Power.Status == PowerStatus.Charging;

            // -- Battery % level (existing + new "above") --
            case TriggerType.BatteryBelow:
                if (state.Bms?.BatteryPct == null) return false;
                if (state.Bms.BatteryPct.Value >= trigger.Threshold) return false;
                return !IsOnCooldown(rule, state, now, trigger);

            case TriggerType.BatteryAbove:
                if (state.Bms?.BatteryPct == null) return false;
                if (state.Bms.BatteryPct.Value <= trigger.Threshold) return false;
                return !IsOnCooldown(rule, state, now, trigger);

            // -- Time remaining (existing) --
            case TriggerType.TimeRemainingBelow:
                if (state.Bms?.RemainMin == null) return false;
                if (state.Bms.RemainMin.Value >= trigger.Threshold) return false;
                return !IsOnCooldown(rule, state, now, trigger);

            // -- Temperature level (new) --
            case TriggerType.TempAbove:
                if (state.Bms?.TempC == null) return false;
                {
                    var thr = trigger.ThresholdF ?? trigger.Threshold;
                    if (state.Bms.TempC.Value <= thr) return false;
                    return !IsOnCooldown(rule, state, now, trigger);
                }

            case TriggerType.TempBelow:
                if (state.Bms?.TempC == null) return false;
                {
                    var thr = trigger.ThresholdF ?? trigger.Threshold;
                    if (state.Bms.TempC.Value >= thr) return false;
                    return !IsOnCooldown(rule, state, now, trigger);
                }

            // -- AC plug edges (new) --
            case TriggerType.AcPlugged:
                return prevAc == false && currAc == true;

            case TriggerType.AcUnplugged:
                return prevAc == true && currAc == false;

            // -- Watts level (new) --
            case TriggerType.InputWattsBelow:
                if (state.Display?.TotalInW == null) return false;
                if (state.Display.TotalInW.Value >= trigger.Threshold) return false;
                return !IsOnCooldown(rule, state, now, trigger);

            case TriggerType.OutputWattsAbove:
                if (state.Display?.TotalOutW == null) return false;
                if (state.Display.TotalOutW.Value <= trigger.Threshold) return false;
                return !IsOnCooldown(rule, state, now, trigger);

            // -- Device offline edges (new) --
            // These fire from DeviceOfflineWatcher rather than Evaluate, but we
            // also handle them here so TEST fires from the UI work.
            case TriggerType.DeviceOffline:
            case TriggerType.DeviceOnline:
                return false;

            default:
                return false;
        }
    }

    private static bool IsOnCooldown(RuleConfig rule, DeviceState state, DateTime now, TriggerConfig trigger)
    {
        var cooldown = trigger.CooldownSeconds.HasValue
            ? TimeSpan.FromSeconds(trigger.CooldownSeconds.Value)
            : DefaultLevelCooldown;

        if (state.RuleLastFired.TryGetValue(rule.Id, out DateTime lastFired))
            return (now - lastFired) < cooldown;
        return false;
    }

    /// <summary>
    /// Record that a rule has fired so that cooldown tracking is correct.
    /// Call this after executing a rule's actions.
    /// </summary>
    public static void RecordFired(RuleConfig rule, DeviceState state)
    {
        state.RuleLastFired[rule.Id] = DateTime.UtcNow;
    }
}
