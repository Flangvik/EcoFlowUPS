using System;
using System.Collections.Generic;
using EcoFlowMonitor.Core;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Triggers
{
    public static class TriggerEvaluator
    {
        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Evaluate all rules for a device.
        /// Returns the list of rules that should fire in this update cycle.
        /// <paramref name="previousPower"/> is the power status BEFORE the current update.
        /// </summary>
        public static List<RuleConfig> Evaluate(DeviceConfig device, DeviceState state, PowerStatus previousPower)
        {
            var toFire = new List<RuleConfig>();
            var now    = DateTime.UtcNow;

            foreach (var rule in device.Rules)
            {
                if (!rule.Enabled) continue;
                if (!ShouldFire(rule, state, previousPower, now)) continue;
                toFire.Add(rule);
            }

            return toFire;
        }

        private static bool ShouldFire(RuleConfig rule, DeviceState state, PowerStatus previousPower, DateTime now)
        {
            var trigger = rule.Trigger;

            switch (trigger.Type)
            {
                case TriggerType.PowerLost:
                    // Edge trigger: fires once on transition INTO PowerLost
                    return previousPower != PowerStatus.PowerLost
                        && state.Power.Status == PowerStatus.PowerLost;

                case TriggerType.PowerRestored:
                    // Edge trigger: fires once on transition FROM PowerLost TO Charging
                    return previousPower == PowerStatus.PowerLost
                        && state.Power.Status == PowerStatus.Charging;

                case TriggerType.BatteryBelow:
                    // Level trigger with cooldown
                    if (state.Bms?.BatteryPct == null) return false;
                    if (state.Bms.BatteryPct.Value >= trigger.Threshold) return false;
                    return !IsOnCooldown(rule, state, now);

                case TriggerType.TimeRemainingBelow:
                    // Level trigger with cooldown
                    if (state.Bms?.RemainMin == null) return false;
                    if (state.Bms.RemainMin.Value >= trigger.Threshold) return false;
                    return !IsOnCooldown(rule, state, now);

                default:
                    return false;
            }
        }

        private static bool IsOnCooldown(RuleConfig rule, DeviceState state, DateTime now)
        {
            if (state.RuleLastFired.TryGetValue(rule.Id, out DateTime lastFired))
                return (now - lastFired) < Cooldown;
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
}
