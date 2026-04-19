using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.Models;

/// <summary>
/// Flat trigger configuration. Fields relevant to a given <see cref="Type"/>
/// are populated; others remain default/null.
///
/// v0 shape (<see cref="Type"/> + <see cref="Threshold"/>) is preserved for
/// backward compatibility with existing <c>config.json</c> files.
/// </summary>
public class TriggerConfig
{
    public TriggerType Type { get; set; }

    /// <summary>
    /// Integer threshold used by v0 level triggers
    /// (<see cref="TriggerType.BatteryBelow"/>,
    /// <see cref="TriggerType.TimeRemainingBelow"/>) and by v1 int-valued level
    /// triggers (<see cref="TriggerType.BatteryAbove"/>,
    /// <see cref="TriggerType.InputWattsBelow"/>,
    /// <see cref="TriggerType.OutputWattsAbove"/>).
    /// </summary>
    public int Threshold { get; set; }

    // -- v1 additions (rules-engine feature 001) --

    /// <summary>
    /// Optional cooldown override in seconds. Null = type default
    /// (300 s for level triggers, 0 s for edge triggers).
    /// </summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>
    /// Decimal threshold for <see cref="TriggerType.TempAbove"/> and
    /// <see cref="TriggerType.TempBelow"/>. Ignored for int-valued triggers.
    /// </summary>
    public float? ThresholdF { get; set; }

    /// <summary>
    /// Window size in seconds for <see cref="TriggerType.DeviceOffline"/>.
    /// Null → default 300 s.
    /// </summary>
    public int? WindowSeconds { get; set; }
}
