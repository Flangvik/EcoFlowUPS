using System.Collections.Concurrent;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.State;

public class DeviceState
{
    public BmsData? Bms { get; set; }
    public DisplayData? Display { get; set; }
    public EmsData? Ems { get; set; }
    public PowerState Power { get; set; } = new();
    public ConcurrentDictionary<string, DateTime> RuleLastFired { get; set; } = new();
    public string? DeviceName { get; set; }
    public string? SerialNumber { get; set; }
    public bool IsConnected { get; set; }
    public DateTime LastUpdated { get; set; }

    // -- Thread safety -------------------------------------------------------
    // All writes from monitor background threads must acquire this lock.
    // StateChanged must be raised OUTSIDE the lock (deadlock prevention).
    public readonly object SyncLock = new();

    // -- Connection state machine fields (written by FSM, read by UI) --------
    public ConnectionStatus ConnectionStatus { get; set; } = ConnectionStatus.Idle;
    public int RetryAttempt { get; set; }
    public TimeSpan RetryDelay { get; set; }

    // -- Error surfacing (D-07, D-08) ----------------------------------------
    // LastErrorMessage: friendly text shown in state bar ("BLE auth failed")
    // LastErrorDetail: expandable technical info (actual exception message)
    public string? LastErrorMessage { get; set; }
    public string? LastErrorDetail { get; set; }

    // -- Staleness watchdog (D-05, D-06) -------------------------------------
    // Updated on every decoded packet -- used to compute "Last update: Xm ago"
    public DateTime? LastDataReceived { get; set; }

    // -- Rules-engine edge-trigger bookkeeping (feature 001) -----------------

    /// <summary>
    /// Previous value of <c>Display.AcPluggedIn</c> observed at the last
    /// trigger evaluation. Used by <see cref="EcoFlowMonitor.Triggers.TriggerEvaluator"/>
    /// to fire <c>AcPlugged</c> / <c>AcUnplugged</c> edge triggers.
    /// Null until first observation.
    /// </summary>
    public bool? LastAcPluggedIn { get; set; }

    /// <summary>
    /// True while the device is considered offline (no telemetry for longer
    /// than any <c>DeviceOffline</c> rule's window). Set by
    /// <see cref="EcoFlowMonitor.Triggers.DeviceOfflineWatcher"/>.
    /// </summary>
    public bool IsOffline { get; set; }
}
