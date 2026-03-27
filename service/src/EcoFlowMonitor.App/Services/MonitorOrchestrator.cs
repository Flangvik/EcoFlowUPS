using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Client.Ble;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.Services;

public class MonitorOrchestrator : IDisposable
{
    private readonly AppConfig _config;
    private readonly ActionRunner _actionRunner;
    private readonly IBleAdapter _bleAdapter;
    private readonly List<MonitorEntry> _monitors = new();

    public event EventHandler<DeviceStateEventArgs>? DeviceUpdated;

    public MonitorOrchestrator(AppConfig config, INotificationService notifications, IPowerActionService power, IScriptRunnerService scripts, IBleAdapter bleAdapter)
    {
        _config = config;
        _actionRunner = new ActionRunner(notifications, power, scripts);
        _bleAdapter = bleAdapter;
    }

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
                    // Try BLE first if available, otherwise Cloud
                    if (device.HasBle && !string.IsNullOrEmpty(GetUserId()))
                    {
                        StartBleMonitor(device, state);
                    }
                    else if (_config.Account != null && !string.IsNullOrEmpty(_config.Account.Email))
                    {
                        _ = Task.Run(() => ConnectMqttAsync(device, state));
                    }
                    break;
            }
        }
    }

    private string GetUserId()
    {
        return !string.IsNullOrEmpty(_config.CloudUserId) ? _config.CloudUserId : _config.LocalUserId;
    }

    private void StartBleMonitor(DeviceConfig device, DeviceState state)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        Logger.Log($"MonitorOrchestrator: starting BLE monitor for {device.DisplayName} sn={device.SerialNumber}");

        // Notify UI about BLE scanning status
        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(state, "Scanning for BLE..."));

        var monitor = new BleMonitor(device, state, userId, _bleAdapter);
        var entry = new MonitorEntry(device, state, monitor);
        _monitors.Add(entry);
        monitor.StateChanged += (s, e) => OnStateChanged(entry, e);
        _ = Task.Run(async () =>
        {
            try { await monitor.StartAsync(); }
            catch (Exception ex) { Logger.Log($"MonitorOrchestrator: BLE monitor crashed — {ex.GetType().Name}: {ex.Message}"); }
        });
    }

    private async Task ConnectMqttAsync(DeviceConfig device, DeviceState state)
    {
        try
        {
            using var client = new EcoFlowClient();
            await client.LoginAsync(_config.Account!.Email!, _config.Account.Password!);
            var creds = await client.GetMqttCredsAsync();

            var monitor = new MqttMonitor(device, state, creds, client.UserId!);
            var entry = new MonitorEntry(device, state, monitor);
            _monitors.Add(entry);
            monitor.StateChanged += (s, e) => OnStateChanged(entry, e);
            await monitor.StartAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"MonitorOrchestrator: MQTT connect failed for {device.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Merge a BLE scan result into the device list.
    /// If a device with the same SN exists, update its BLE fields.
    /// If new, add it. Returns the device config.
    /// </summary>
    public DeviceConfig MergeBleScanResult(string bleName, string serialNumber, string bleAddress, int encryptionType, int protocolVersion)
    {
        var existing = _config.Devices.FirstOrDefault(d => d.SerialNumber == serialNumber);

        if (existing != null)
        {
            // Merge BLE info into existing device
            existing.BleAddress = bleAddress;
            existing.BleEncryptionType = encryptionType;
            existing.BleProtocolVersion = protocolVersion;
            existing.BleName = bleName;
            // Upgrade to Auto if currently Cloud-only
            if (existing.ConnectionMode == ConnectionMode.Cloud)
                existing.ConnectionMode = ConnectionMode.Auto;
            Logger.Log($"MonitorOrchestrator: merged BLE into existing device {existing.DisplayName} sn={serialNumber}");
        }
        else
        {
            // New device only seen via BLE
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
            Logger.Log($"MonitorOrchestrator: added new BLE device {bleName} sn={serialNumber}");
        }

        if (string.IsNullOrEmpty(_config.LocalUserId))
            _config.LocalUserId = Guid.NewGuid().ToString();

        ConfigManager.Save(_config);
        return existing;
    }

    /// <summary>
    /// Start monitoring a device that was just merged from BLE scan.
    /// If already monitored via cloud, switch to BLE or leave as-is.
    /// </summary>
    public void StartBleForDevice(DeviceConfig device)
    {
        var existingMonitor = _monitors.FirstOrDefault(m => m.Device.SerialNumber == device.SerialNumber);
        if (existingMonitor != null)
        {
            Logger.Log($"MonitorOrchestrator: device {device.SerialNumber} already monitored via {(existingMonitor.Monitor is BleMonitor ? "BLE" : "Cloud")}");
            // Already connected — notify UI to refresh
            DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(existingMonitor.State));
            return;
        }

        var state = new DeviceState { DeviceName = device.DisplayName, SerialNumber = device.SerialNumber };
        StartBleMonitor(device, state);
        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(state));
    }

    private void OnStateChanged(MonitorEntry entry, StateChangedEventArgs e)
    {
        var toFire = TriggerEvaluator.Evaluate(entry.Device, e.State, e.PreviousPower);
        foreach (var rule in toFire)
        {
            foreach (var action in rule.Actions)
            {
                try { _actionRunner.Run(action, entry.Device, e.State); }
                catch (Exception ex) { Logger.Log($"Action failed: {ex.Message}"); }
            }
            TriggerEvaluator.RecordFired(rule, e.State);
        }
        var source = entry.Monitor is BleMonitor ? "BLE" : "Cloud";
        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(e.State, source));
    }

    /// <summary>
    /// Stop the current monitor for a device and restart with its current ConnectionMode.
    /// </summary>
    public async Task RestartDeviceAsync(DeviceConfig device)
    {
        var existing = _monitors.FirstOrDefault(m => m.Device.SerialNumber == device.SerialNumber);
        if (existing != null)
        {
            Logger.Log($"MonitorOrchestrator: stopping monitor for {device.SerialNumber}");
            try { await existing.Monitor.StopAsync(); } catch { }
            existing.Monitor.Dispose();
            _monitors.Remove(existing);
        }

        // Re-create state (keep existing data if available)
        var state = existing?.State ?? new DeviceState
        {
            DeviceName = device.DisplayName,
            SerialNumber = device.SerialNumber
        };

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
                {
                    _config.Devices.Add(new DeviceConfig { SerialNumber = sn, DisplayName = name, HasCloud = true });
                }
                else
                {
                    existing.HasCloud = true;
                    // Don't override display name if user customized it
                }
            }
            ConfigManager.Save(_config);
        }
        catch (Exception ex)
        {
            Logger.Log($"RefreshDevices failed: {ex.Message}");
        }
    }

    private record MonitorEntry(DeviceConfig Device, DeviceState State, IDeviceMonitor Monitor);
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
