using System.Text.Json.Serialization;

namespace EcoFlowMonitor.Models;

/// <summary>
/// Boolean reducer for a rule's composite predicate.
/// Serialised as the string value ("All" / "Any") via
/// <see cref="JsonStringEnumConverter"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleConditionOperator
{
    /// <summary>Logical AND across all conditions.</summary>
    All,

    /// <summary>Logical OR across all conditions.</summary>
    Any,
}
