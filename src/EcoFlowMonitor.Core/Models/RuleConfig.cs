using System.Text.Json.Serialization;

namespace EcoFlowMonitor.Models;

public class RuleConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Rule";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Legacy single-trigger field. Deserialised from pre-composite config
    /// files; never written back out (see
    /// <see cref="EnsureConditionsHydrated"/>). Nullable so new-shape rules
    /// don't emit it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TriggerConfig? Trigger { get; set; }

    /// <summary>
    /// Ordered list of conditions combined by <see cref="Operator"/>. Must be
    /// non-empty after <see cref="EnsureConditionsHydrated"/>; editor blocks
    /// saving an empty list.
    /// </summary>
    public List<ConditionConfig> Conditions { get; set; } = new();

    /// <summary>
    /// Boolean reducer across <see cref="Conditions"/>. Defaults to
    /// <see cref="RuleConditionOperator.All"/>; moot when the list has
    /// exactly one entry.
    /// </summary>
    public RuleConditionOperator Operator { get; set; } = RuleConditionOperator.All;

    public List<ActionConfig> Actions { get; set; } = new();

    /// <summary>
    /// Lazily migrates a legacy <see cref="Trigger"/> into
    /// <see cref="Conditions"/>[0]. Idempotent. Called by
    /// <c>ConfigManager.Load</c> for every rule and by the rule editor
    /// before opening.
    /// </summary>
    /// <returns><c>true</c> if hydration ran (legacy trigger migrated).</returns>
    public bool EnsureConditionsHydrated()
    {
        if (Conditions.Count > 0)
        {
            // New-shape wins; drop any stray legacy trigger so it's never
            // re-serialised.
            Trigger = null;
            return false;
        }

        if (Trigger != null)
        {
            Conditions.Add(new ConditionConfig(Trigger));
            Operator = RuleConditionOperator.All;
            Trigger  = null;
            return true;
        }

        return false;
    }
}
