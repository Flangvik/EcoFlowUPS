using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.Models;

/// <summary>
/// One boolean condition inside a <see cref="RuleConfig"/>'s composite
/// predicate. Field-for-field identical to <see cref="TriggerConfig"/> so
/// migration in/out is trivial and new trigger types do not need to be
/// added in two places.
/// </summary>
public class ConditionConfig
{
    public TriggerType Type { get; set; }

    /// <summary>
    /// Integer threshold (percent, minutes, watts). Used by most level-style
    /// conditions; ignored by edge conditions.
    /// </summary>
    public int Threshold { get; set; }

    /// <summary>
    /// Ignored at the composite level — the rule-level cooldown is
    /// authoritative for composite rising-edge fires. Retained so the field
    /// set matches <see cref="TriggerConfig"/> exactly.
    /// </summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>
    /// Decimal threshold for <see cref="TriggerType.TempAbove"/> and
    /// <see cref="TriggerType.TempBelow"/>.
    /// </summary>
    public float? ThresholdF { get; set; }

    /// <summary>
    /// Window size in seconds for <see cref="TriggerType.DeviceOffline"/>.
    /// </summary>
    public int? WindowSeconds { get; set; }

    public ConditionConfig() { }

    /// <summary>
    /// Hydrate a condition from a legacy <see cref="TriggerConfig"/>. Used
    /// by <see cref="RuleConfig.EnsureConditionsHydrated"/> when loading a
    /// pre-composite <c>config.json</c>.
    /// </summary>
    public ConditionConfig(TriggerConfig t)
    {
        Type            = t.Type;
        Threshold       = t.Threshold;
        CooldownSeconds = t.CooldownSeconds;
        ThresholdF      = t.ThresholdF;
        WindowSeconds   = t.WindowSeconds;
    }
}
