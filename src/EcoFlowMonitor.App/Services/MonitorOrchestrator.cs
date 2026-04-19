using System.Text.Json;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Client.Ble;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.History;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;
using Microsoft.Extensions.Logging;

namespace EcoFlowMonitor.Services;

public class MonitorOrchestrator : IDisposable
{
    private readonly AppConfig _config;
    private readonly ActionRunner _actionRunner;
    private readonly IBleAdapter _bleAdapter;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MonitorOrchestrator> _logger;
    private readonly IHistoryStore _historyStore;
    private readonly IEventStore _eventStore;
    private readonly IRuleFiringStore _ruleFiringStore;
    private readonly DeviceOfflineWatcher _offlineWatcher;
    private readonly List<MonitorEntry> _monitors = new();

    // -- pending audit row per rule-firing --
    private readonly object _pendingLock = new();
    private long _pendingFiringSeq;
    private readonly Dictionary<long, PendingFiring> _pending = new();

    public event EventHandler<DeviceStateEventArgs>? DeviceUpdated;

    public MonitorOrchestrator(
        AppConfig config,
        INotificationService notifications,
        IPowerActionService power,
        IScriptRunnerService scripts,
        IBleAdapter bleAdapter,
        ILoggerFactory loggerFactory,
        IHistoryStore historyStore,
        IEventStore eventStore,
        IRuleFiringStore ruleFiringStore,
        IShellExecutor shellExecutor,
        HttpClient httpClient)
    {
        _config          = config;
        _actionRunner    = new ActionRunner(notifications, power, scripts, shellExecutor, httpClient);
        _bleAdapter      = bleAdapter;
        _loggerFactory   = loggerFactory;
        _logger          = loggerFactory.CreateLogger<MonitorOrchestrator>();
        _historyStore    = historyStore;
        _eventStore      = eventStore;
        _ruleFiringStore = ruleFiringStore;

        // Apply saved settings to the runner.
        _actionRunner.ConfigureConcurrency(
            Math.Clamp(_config.General.MaxConcurrentActions, 1, 64),
            Math.Clamp(_config.General.ActionQueueCapacity, 1, 4096));
        _actionRunner.OnActionCompleted = HandleActionCompletedAsync;

        _offlineWatcher = new DeviceOfflineWatcher(FireSyntheticAsync);
        _offlineWatcher.Start();
    }

    public ActionRunner ActionRunner => _actionRunner;

    public IReadOnlyList<DeviceState> GetLiveStates() => _monitors.Select(m => m.State).ToList();

    public async Task StartAsync()
    {
        foreach (var device in _config.Devices)
        {
            if (string.IsNullOrEmpty(device.SerialNumber)) continue;
            if (_monitors.Any(m => m.Device.SerialNumber == device.SerialNumber)) continue;

            var state = new DeviceState
            {
                DeviceName = device.DisplayName,
                SerialNumber = device.SerialNumber
            };
            _offlineWatcher.Track(device, state);

            switch (device.ConnectionMode)
            {
                case ConnectionMode.Cloud:
                    if (_config.Account == null || string.IsNullOrEmpty(_config.Account.Email)) continue;
                    _ = Task.Run(() => ConnectMqttAsync(device, state));
                    break;

                case ConnectionMode.Ble:
                    if (!device.HasBle) continue;
                    StartBleMonitor(device, state);
                    break;

                case ConnectionMode.Auto:
                    if (device.HasBle && !string.IsNullOrEmpty(GetUserId()))
                        StartBleMonitor(device, state);
                    else if (_config.Account != null && !string.IsNullOrEmpty(_config.Account.Email))
                        _ = Task.Run(() => ConnectMqttAsync(device, state));
                    break;
            }
        }
        await Task.CompletedTask;
    }

    private string GetUserId()
        => !string.IsNullOrEmpty(_config.CloudUserId) ? _config.CloudUserId : _config.LocalUserId;

    private void StartBleMonitor(DeviceConfig device, DeviceState state)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        _logger.LogInformation("Starting BLE monitor for {DisplayName} sn={SerialNumber}", device.DisplayName, device.SerialNumber);
        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(state, "Scanning for BLE..."));

        var monitor = new BleMonitor(device, state, userId, _bleAdapter,
            _loggerFactory.CreateLogger<BleMonitor>(), _loggerFactory);
        var entry = new MonitorEntry(device, state, monitor);
        _monitors.Add(entry);
        monitor.StateChanged += (s, e) => OnStateChanged(entry, e);
        _ = Task.Run(async () =>
        {
            try { await monitor.StartAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "BLE monitor crashed — {ExType}: {Message}", ex.GetType().Name, ex.Message); }
        });
    }

    private async Task ConnectMqttAsync(DeviceConfig device, DeviceState state)
    {
        try
        {
            using var client = new EcoFlowClient();
            await client.LoginAsync(_config.Account!.Email!, _config.Account.Password!);
            var creds = await client.GetMqttCredsAsync();

            var monitor = new MqttMonitor(device, state, creds, client.UserId!,
                _loggerFactory.CreateLogger<MqttMonitor>());
            var entry = new MonitorEntry(device, state, monitor);
            _monitors.Add(entry);
            monitor.StateChanged += (s, e) => OnStateChanged(entry, e);
            await monitor.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT connect failed for {DisplayName}", device.DisplayName);
        }
    }

    public DeviceConfig MergeBleScanResult(string bleName, string serialNumber, string bleAddress, int encryptionType, int protocolVersion)
    {
        var existing = _config.Devices.FirstOrDefault(d => d.SerialNumber == serialNumber);
        if (existing != null)
        {
            existing.BleAddress = bleAddress;
            existing.BleEncryptionType = encryptionType;
            existing.BleProtocolVersion = protocolVersion;
            existing.BleName = bleName;
            if (existing.ConnectionMode == ConnectionMode.Cloud)
                existing.ConnectionMode = ConnectionMode.Auto;
            _logger.LogInformation("Merged BLE into existing device {DisplayName} sn={SerialNumber}", existing.DisplayName, serialNumber);
        }
        else
        {
            existing = new DeviceConfig
            {
                DisplayName = bleName,
                SerialNumber = serialNumber,
                ConnectionMode = ConnectionMode.Ble,
                BleAddress = bleAddress,
                BleEncryptionType = encryptionType,
                BleProtocolVersion = protocolVersion,
                BleName = bleName,
                HasCloud = false
            };
            _config.Devices.Add(existing);
            _logger.LogInformation("Added new BLE device {BleName} sn={SerialNumber}", bleName, serialNumber);
        }

        if (string.IsNullOrEmpty(_config.LocalUserId))
            _config.LocalUserId = Guid.NewGuid().ToString();

        ConfigManager.Save(_config);
        return existing;
    }

    public void StartBleForDevice(DeviceConfig device)
    {
        var existingMonitor = _monitors.FirstOrDefault(m => m.Device.SerialNumber == device.SerialNumber);
        if (existingMonitor != null)
        {
            _logger.LogInformation("Device {SerialNumber} already monitored via {MonitorType}",
                device.SerialNumber, existingMonitor.Monitor is BleMonitor ? "BLE" : "Cloud");
            DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(existingMonitor.State));
            return;
        }

        var state = new DeviceState { DeviceName = device.DisplayName, SerialNumber = device.SerialNumber };
        _offlineWatcher.Track(device, state);
        StartBleMonitor(device, state);
        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(state));
    }

    /// <summary>
    /// Manually fire a rule from the UI ("Test rule now"). Builds a synthetic
    /// firing-context from the device's last known state and tags every audit
    /// row isTest=true.
    /// </summary>
    public void TestRule(DeviceConfig device, RuleConfig rule)
    {
        var entry = _monitors.FirstOrDefault(m => m.Device.SerialNumber == device.SerialNumber);
        var state = entry?.State ?? new DeviceState { DeviceName = device.DisplayName, SerialNumber = device.SerialNumber };
        QueueFiring(device, rule, state, isTest: true);
    }

    private void OnStateChanged(MonitorEntry entry, StateChangedEventArgs e)
    {
        var source = entry.Monitor is BleMonitor ? "BLE" : "Cloud";
        var toFire = TriggerEvaluator.Evaluate(entry.Device, e.State, e.PreviousPower);
        foreach (var rule in toFire)
        {
            QueueFiring(entry.Device, rule, e.State, isTest: false);
            TriggerEvaluator.RecordFired(rule, e.State);
        }

        // -- History persistence (DATA-01) --
        var snapshot = new TelemetrySnapshot(
            DeviceSn:   entry.Device.SerialNumber ?? "",
            Ts:         DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            BatteryPct: e.State.Bms?.BatteryPct,
            TotalInW:   e.State.Display?.TotalInW,
            TotalOutW:  e.State.Display?.TotalOutW,
            PowerState: e.State.Power.Status.ToString(),
            RemainMin:  e.State.Bms?.RemainMin,
            TempC:      e.State.Bms?.TempC,
            Source:     source);
        _historyStore.EnqueueSnapshot(snapshot);

        // -- Event log (DATA-03): only on power state transitions --
        if (e.PreviousPower != e.State.Power.Status)
        {
            var eventType = DeriveEventType(e.PreviousPower, e.State.Power.Status);
            if (eventType != null)
            {
                var detail = $"Battery {e.State.Bms?.BatteryPct:F0}%";
                _eventStore.EnqueueEvent(new PowerEvent(
                    DeviceSn:  entry.Device.SerialNumber ?? "",
                    Ts:        DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    EventType: eventType,
                    Detail:    detail,
                    Source:    source));
            }
        }

        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(e.State, source));
    }

    private void QueueFiring(DeviceConfig device, RuleConfig rule, DeviceState state, bool isTest)
    {
        // Snapshot trigger context — frozen JSON for the audit row.
        var ctx = BuildTriggerContextJson(device, rule, state);

        long seq = Interlocked.Increment(ref _pendingFiringSeq);
        lock (_pendingLock)
        {
            _pending[seq] = new PendingFiring
            {
                Seq                = seq,
                RemainingActions   = rule.Actions.Count,
                Actions            = new List<RuleFiringAction>(),
                Timestamp          = DateTimeOffset.UtcNow,
                RuleId             = rule.Id,
                RuleName           = rule.Name,
                DeviceSn           = device.SerialNumber ?? "",
                TriggerType        = rule.Trigger.Type.ToString(),
                TriggerValueJson   = ctx,
                IsTest             = isTest,
            };
        }

        // Tag the in-flight ActionRunner audit callback with this sequence by
        // decorating: we wrap the runner's hook via a token in DispatchContext.
        // Simplest path: the ActionRunner callback reports per-action rows; we
        // associate them with the most-recent-pending-for-rule using a thread-local
        // queue of sequences keyed by rule id.
        EnqueueAssociation(rule.Id, seq);

        _actionRunner.Enqueue(rule, device, state, isTest);
    }

    // -- association helpers: FIFO queue per-rule of pending seqs --

    private readonly Dictionary<string, Queue<long>> _assoc = new();
    private void EnqueueAssociation(string ruleId, long seq)
    {
        lock (_assoc)
        {
            if (!_assoc.TryGetValue(ruleId, out var q)) { q = new Queue<long>(); _assoc[ruleId] = q; }
            q.Enqueue(seq);
        }
    }

    private long? PeekOrDequeueAssociation(string ruleId, bool dequeue)
    {
        lock (_assoc)
        {
            if (!_assoc.TryGetValue(ruleId, out var q) || q.Count == 0) return null;
            return dequeue ? q.Dequeue() : q.Peek();
        }
    }

    private async Task HandleActionCompletedAsync(RuleFiringAction row)
    {
        // We don't know the rule id directly from the row; the callback provides
        // action type + ordinal but not rule id. Workaround: every in-flight
        // firing is tracked by seq; we store rows in arrival order and flush
        // when we see the final ordinal for a given seq.
        // For simplicity we flush on every call and match seqs FIFO.
        PendingFiring? toFlush = null;
        lock (_pendingLock)
        {
            // Find the earliest pending firing that still needs rows.
            var kvp = _pending.OrderBy(x => x.Value.Seq)
                              .FirstOrDefault(x => x.Value.RemainingActions > 0);
            if (kvp.Value is null) return;

            kvp.Value.Actions.Add(row);
            kvp.Value.RemainingActions--;

            if (kvp.Value.RemainingActions <= 0)
            {
                toFlush = kvp.Value;
                _pending.Remove(kvp.Key);
                PeekOrDequeueAssociation(kvp.Value.RuleId, dequeue: true);
            }
        }

        if (toFlush is not null)
        {
            var firing = new RuleFiring(
                Timestamp:          toFlush.Timestamp,
                RuleId:             toFlush.RuleId,
                RuleName:           toFlush.RuleName,
                DeviceSerialNumber: toFlush.DeviceSn,
                TriggerType:        toFlush.TriggerType,
                TriggerValueJson:   toFlush.TriggerValueJson,
                Actions:            toFlush.Actions,
                IsTest:             toFlush.IsTest);
            try
            {
                await _ruleFiringStore.AppendAsync(firing).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist rule firing audit row for {RuleName}", toFlush.RuleName);
            }
        }
    }

    private static string BuildTriggerContextJson(DeviceConfig device, RuleConfig rule, DeviceState state)
    {
        var ctx = new
        {
            device = new
            {
                serialNumber = device.SerialNumber,
                name = device.DisplayName,
                batteryPct = state.Bms?.BatteryPct,
                remainMin = state.Bms?.RemainMin,
                tempC = state.Bms?.TempC,
                totalInW = state.Display?.TotalInW,
                totalOutW = state.Display?.TotalOutW,
                acPluggedIn = state.Display?.AcPluggedIn,
                chargeState = state.Ems?.ChgState,
                powerStatus = state.Power.Status.ToString(),
            },
            trigger = new
            {
                type = rule.Trigger.Type.ToString(),
                threshold = rule.Trigger.Threshold,
                thresholdF = rule.Trigger.ThresholdF,
                windowSeconds = rule.Trigger.WindowSeconds,
            },
        };
        return JsonSerializer.Serialize(ctx);
    }

    private async Task FireSyntheticAsync(DeviceConfig device, DeviceState state, TriggerType type)
    {
        foreach (var rule in device.Rules)
        {
            if (!rule.Enabled) continue;
            if (rule.Trigger.Type != type) continue;
            QueueFiring(device, rule, state, isTest: false);
        }
        await Task.CompletedTask;
    }

    private static string? DeriveEventType(PowerStatus previous, PowerStatus current) =>
        (previous, current) switch
        {
            (PowerStatus.Charging, PowerStatus.PowerLost) => "PowerLost",
            (PowerStatus.Idle,     PowerStatus.PowerLost) => "PowerLost",
            (PowerStatus.PowerLost, PowerStatus.Charging) => "PowerRestored",
            (PowerStatus.PowerLost, PowerStatus.Idle)     => "PowerRestored",
            _ => null
        };

    public async Task RestartDeviceAsync(DeviceConfig device)
    {
        var existing = _monitors.FirstOrDefault(m => m.Device.SerialNumber == device.SerialNumber);
        if (existing != null)
        {
            _logger.LogInformation("Stopping monitor for {SerialNumber}", device.SerialNumber);
            try { await existing.Monitor.StopAsync(); } catch { }
            existing.Monitor.Dispose();
            _monitors.Remove(existing);
        }

        var state = existing?.State ?? new DeviceState
        {
            DeviceName = device.DisplayName,
            SerialNumber = device.SerialNumber
        };
        _offlineWatcher.Track(device, state);

        switch (device.ConnectionMode)
        {
            case ConnectionMode.Cloud:
                if (_config.Account != null && !string.IsNullOrEmpty(_config.Account.Email))
                    _ = Task.Run(() => ConnectMqttAsync(device, state));
                break;
            case ConnectionMode.Ble:
                if (device.HasBle)
                    StartBleMonitor(device, state);
                break;
            case ConnectionMode.Auto:
                if (device.HasBle && !string.IsNullOrEmpty(GetUserId()))
                    StartBleMonitor(device, state);
                else if (_config.Account != null)
                    _ = Task.Run(() => ConnectMqttAsync(device, state));
                break;
        }
    }

    public async Task StopAsync()
    {
        foreach (var entry in _monitors)
        {
            try { await entry.Monitor.StopAsync(); }
            catch { }
        }
        _monitors.Clear();
    }

    public void Dispose()
    {
        foreach (var entry in _monitors)
            entry.Monitor.Dispose();
        _monitors.Clear();
        _ = _offlineWatcher.DisposeAsync().AsTask();
    }

    public async Task RefreshDevicesAsync()
    {
        if (_config.Account == null) return;
        try
        {
            using var client = new EcoFlowClient();
            await client.LoginAsync(_config.Account.Email!, _config.Account.Password!);
            var devices = await client.GetAllDevicesAsync();

            foreach (var (sn, name) in devices)
            {
                var existing = _config.Devices.FirstOrDefault(d => d.SerialNumber == sn);
                if (existing == null)
                    _config.Devices.Add(new DeviceConfig { SerialNumber = sn, DisplayName = name, HasCloud = true });
                else
                    existing.HasCloud = true;
            }
            ConfigManager.Save(_config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RefreshDevices failed");
        }
    }

    private record MonitorEntry(DeviceConfig Device, DeviceState State, IDeviceMonitor Monitor);

    private sealed class PendingFiring
    {
        public long Seq;
        public int RemainingActions;
        public List<RuleFiringAction> Actions = new();
        public DateTimeOffset Timestamp;
        public string RuleId = "";
        public string RuleName = "";
        public string DeviceSn = "";
        public string TriggerType = "";
        public string TriggerValueJson = "";
        public bool IsTest;
    }
}

public class DeviceStateEventArgs : EventArgs
{
    public DeviceState State { get; }
    public string Source { get; }
    public DeviceStateEventArgs(DeviceState state, string source = "Cloud")
    {
        State = state;
        Source = source;
    }
}
