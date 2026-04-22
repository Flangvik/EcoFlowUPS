using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Triggers;

/// <summary>
/// Composite-trigger evaluator (feature 002). Every rule carries an ordered
/// list of <see cref="ConditionConfig"/> combined by a
/// <see cref="RuleConditionOperator"/>; the rule fires once on the rising
/// edge of the combined predicate and is throttled by per-rule cooldown.
///
/// Legacy single-trigger rules are migrated on-the-fly via
/// <see cref="RuleConfig.EnsureConditionsHydrated"/> so existing callers keep
/// working unchanged (see FR-003).
/// </summary>
public static class TriggerEvaluator
{
    private static readonly TimeSpan DefaultLevelCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Evaluate all rules for a device. Returns the list of rules whose
    /// composite predicate has just risen from false to true (honoring the
    /// per-rule cooldown). <paramref name="previousPower"/> is unused by the
    /// composite path but retained for API compatibility — edge-style triggers
    /// (PowerLost, AcPlugged, …) are captured naturally by the composite
    /// rising-edge when their condition flips.
    /// </summary>
    public static List<RuleConfig> Evaluate(DeviceConfig device, DeviceState state, PowerStatus previousPower)
    {
        var toFire = new List<RuleConfig>();
        var now    = DateTime.UtcNow;

        foreach (var rule in device.Rules)
        {
            if (!rule.Enabled) continue;
            rule.EnsureConditionsHydrated();
            if (rule.Conditions.Count == 0) continue; // orphaned rule — skip
            if (EvaluateComposite(rule, state, now)) toFire.Add(rule);
        }

        // Maintain LastAcPluggedIn bookkeeping for any legacy consumers (kept
        // for safety; the composite path doesn't need it).
        var currAc = state.Display?.AcPluggedIn;
        if (currAc.HasValue) state.LastAcPluggedIn = currAc;

        return toFire;
    }

    /// <summary>
    /// Evaluate a single rule's composite predicate against the current device
    /// state. Returns true when the composite just rose false → true AND the
    /// per-rule cooldown has elapsed.
    /// Side-effects: updates <see cref="RuleCompositeState.LastTrue"/> and, on
    /// fire, <see cref="RuleCompositeState.LastFired"/> in
    /// <see cref="DeviceState.RuleCompositeStates"/>.
    /// </summary>
    public static bool EvaluateComposite(RuleConfig rule, DeviceState state, DateTime nowUtc)
    {
        var currentTrue = ReduceConditions(rule, state);

        if (!state.RuleCompositeStates.TryGetValue(rule.Id, out var cs))
        {
            cs = new RuleCompositeState();
            state.RuleCompositeStates[rule.Id] = cs;
        }

        bool fires = false;
        if (!cs.LastTrue && currentTrue)
        {
            var cooldown = EffectiveCooldown(rule);
            if (cs.LastFired is null || (nowUtc - cs.LastFired.Value) >= cooldown)
            {
                fires = true;
                cs.LastFired = nowUtc;
                // Mirror into the legacy dict so existing audit paths see it.
                state.RuleLastFired[rule.Id] = nowUtc;
            }
        }

        cs.LastTrue = currentTrue;
        return fires;
    }

    /// <summary>
    /// Evaluate each condition and return the per-condition truth values in
    /// order. Used by the audit-log path (R-007 / FR-016) and the dashboard
    /// tally (FR-018).
    /// </summary>
    public static bool[] EvaluateConditions(RuleConfig rule, DeviceState state)
    {
        var values = new bool[rule.Conditions.Count];
        for (int i = 0; i < rule.Conditions.Count; i++)
            values[i] = ConditionEvaluator.Evaluate(rule.Conditions[i], state);
        return values;
    }

    private static bool ReduceConditions(RuleConfig rule, DeviceState state)
    {
        if (rule.Operator == RuleConditionOperator.All)
        {
            foreach (var c in rule.Conditions)
                if (!ConditionEvaluator.Evaluate(c, state)) return false;
            return true;
        }
        else // Any
        {
            foreach (var c in rule.Conditions)
                if (ConditionEvaluator.Evaluate(c, state)) return true;
            return false;
        }
    }

    private static TimeSpan EffectiveCooldown(RuleConfig rule)
    {
        // Legacy trigger-level cooldown was hydrated onto Conditions[0] —
        // honor it at the rule level for backward compat.
        int? cd = rule.Conditions.Count > 0 ? rule.Conditions[0].CooldownSeconds : null;
        return cd.HasValue
            ? TimeSpan.FromSeconds(cd.Value)
            : DefaultLevelCooldown;
    }

    /// <summary>
    /// Legacy no-op entry point. The composite path already records the fire
    /// time inside <see cref="EvaluateComposite"/>; this method is retained
    /// purely for backward-compat with callers that used to invoke it after
    /// running a rule's actions.
    /// </summary>
    public static void RecordFired(RuleConfig rule, DeviceState state)
    {
        if (!state.RuleCompositeStates.TryGetValue(rule.Id, out var cs))
        {
            cs = new RuleCompositeState();
            state.RuleCompositeStates[rule.Id] = cs;
        }
        cs.LastFired = DateTime.UtcNow;
        state.RuleLastFired[rule.Id] = DateTime.UtcNow;
    }
}
