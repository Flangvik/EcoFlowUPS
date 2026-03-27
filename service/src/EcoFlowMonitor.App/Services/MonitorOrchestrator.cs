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

            var state = new DeviceState
            {
                DeviceName = device.DisplayName,
                SerialNumber = device.SerialNumber
            };

            if (device.ConnectionType == ConnectionType.Cloud)
            {
                if (_config.Account == null || string.IsNullOrEmpty(_config.Account.Email)) continue;
                _ = Task.Run(() => ConnectMqttAsync(device, state));
            }
            else if (device.ConnectionType == ConnectionType.Ble)
            {
                if (string.IsNullOrEmpty(_config.LocalUserId)) continue;
                var monitor = new BleMonitor(device, state, _config.LocalUserId, _bleAdapter);
                var entry = new MonitorEntry(device, state, monitor);
                _monitors.Add(entry);
                monitor.StateChanged += (s, e) => OnStateChanged(entry, e);
                _ = Task.Run(() => monitor.StartAsync());
            }
        }
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
            Logger.Log($"MonitorOrchestrator: connect failed for {device.DisplayName}: {ex.Message}");
        }
    }

    public async Task AddBleDeviceAsync(string displayName, string serialNumber, string bleAddress, int encryptionType, int protocolVersion)
    {
        var device = new DeviceConfig
        {
            DisplayName = displayName,
            SerialNumber = serialNumber,
            ConnectionType = ConnectionType.Ble,
            BleAddress = bleAddress,
            BleEncryptionType = encryptionType,
            BleProtocolVersion = protocolVersion
        };
        _config.Devices.Add(device);
        if (string.IsNullOrEmpty(_config.LocalUserId))
            _config.LocalUserId = Guid.NewGuid().ToString();
        ConfigManager.Save(_config);

        var state = new DeviceState { DeviceName = device.DisplayName, SerialNumber = device.SerialNumber };

        var monitor = new BleMonitor(device, state, _config.LocalUserId, _bleAdapter);
        var entry = new MonitorEntry(device, state, monitor);
        _monitors.Add(entry);
        monitor.StateChanged += (s, e) => OnStateChanged(entry, e);
        _ = Task.Run(() => monitor.StartAsync());

        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(state));
    }

    private void OnStateChanged(MonitorEntry entry, StateChangedEventArgs e)
    {
        // Fire rules
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

        // Notify UI
        DeviceUpdated?.Invoke(this, new DeviceStateEventArgs(e.State));
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
                if (!_config.Devices.Any(d => d.SerialNumber == sn))
                {
                    _config.Devices.Add(new DeviceConfig { SerialNumber = sn, DisplayName = name });
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
    public DeviceStateEventArgs(DeviceState state) => State = state;
}
