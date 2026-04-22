using System.Collections.Concurrent;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Triggers;

/// <summary>
/// Tracks per-device last-data timestamps and fires <c>DeviceOffline</c>
/// rules when a device has been silent for longer than the rule's configured
/// window. Once the device returns, fires <c>DeviceOnline</c>.
///
/// Plan R-006 chose a 10-second <see cref="PeriodicTimer"/> over per-device
/// resettable timers — one central poller is simpler and cheap.
/// </summary>
public sealed class DeviceOfflineWatcher : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, (DeviceConfig device, DeviceState state)> _devices = new();
    private readonly Func<DeviceConfig, DeviceState, TriggerType, Task> _fireCallback;
    private readonly TimeSpan _tickInterval;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DeviceOfflineWatcher(
        Func<DeviceConfig, DeviceState, TriggerType, Task> fireCallback,
        TimeSpan? tickInterval = null)
    {
        _fireCallback = fireCallback;
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(10);
    }

    public void Track(DeviceConfig device, DeviceState state)
    {
        if (string.IsNullOrEmpty(device.SerialNumber)) return;
        _devices[device.SerialNumber] = (device, state);
    }

    public void Untrack(string serialNumber) => _devices.TryRemove(serialNumber, out _);

    public void Start(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_tickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = DateTime.UtcNow;
                foreach (var (sn, entry) in _devices)
                {
                    await EvaluateOneAsync(entry.device, entry.state, now).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task EvaluateOneAsync(DeviceConfig device, DeviceState state, DateTime now)
    {
        var lastData = state.LastDataReceived;

        // Find the smallest offline-window in any active rule's conditions;
        // default 300 s. Every rule is hydrated into Conditions before we
        // inspect it so composite + legacy rules are handled uniformly.
        int? minWindow = null;
        foreach (var rule in device.Rules)
        {
            if (!rule.Enabled) continue;
            rule.EnsureConditionsHydrated();
            foreach (var c in rule.Conditions)
            {
                if (c.Type != TriggerType.DeviceOffline) continue;
                var w = c.WindowSeconds ?? 300;
                if (minWindow == null || w < minWindow) minWindow = w;
            }
        }
        if (minWindow == null) return;

        var wasOffline = state.IsOffline;
        var isOffline  = !lastData.HasValue || (now - lastData.Value) > TimeSpan.FromSeconds(minWindow.Value);

        if (isOffline && !wasOffline)
        {
            state.IsOffline = true;
            foreach (var rule in device.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Conditions.Any(c => c.Type == TriggerType.DeviceOffline))
                    await _fireCallback(device, state, TriggerType.DeviceOffline).ConfigureAwait(false);
            }
        }
        else if (!isOffline && wasOffline)
        {
            state.IsOffline = false;
            foreach (var rule in device.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Conditions.Any(c => c.Type == TriggerType.DeviceOnline))
                    await _fireCallback(device, state, TriggerType.DeviceOnline).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts?.Dispose();
    }
}
