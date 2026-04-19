namespace EcoFlowMonitor.Triggers;

public enum TriggerType
{
    // -- v0 (existing) --
    PowerLost,
    PowerRestored,
    BatteryBelow,
    TimeRemainingBelow,

    // -- v1 (rules-engine feature 001) --
    /// <summary>Counterpart to BatteryBelow; fires while BatteryPct &gt; threshold.</summary>
    BatteryAbove,

    /// <summary>Fires while BMS TempC &gt; threshold (level, with cooldown).</summary>
    TempAbove,

    /// <summary>Fires while BMS TempC &lt; threshold (level, with cooldown).</summary>
    TempBelow,

    /// <summary>Edge: AC line plugged into the station (AcPluggedIn: false → true).</summary>
    AcPlugged,

    /// <summary>Edge: AC line unplugged from the station (AcPluggedIn: true → false).</summary>
    AcUnplugged,

    /// <summary>Fires while TotalInW &lt; threshold (level, with cooldown).</summary>
    InputWattsBelow,

    /// <summary>Fires while TotalOutW &gt; threshold (level, with cooldown).</summary>
    OutputWattsAbove,

    /// <summary>
    /// Edge: no telemetry on any channel for <c>Threshold</c> seconds.
    /// Default 300 s. Fired by <see cref="Triggers.DeviceOfflineWatcher"/>.
    /// </summary>
    DeviceOffline,

    /// <summary>Edge: first telemetry received after being DeviceOffline.</summary>
    DeviceOnline,
}
