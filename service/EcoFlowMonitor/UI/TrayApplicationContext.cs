using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Core;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.UI
{
    public class TrayApplicationContext : ApplicationContext
    {
        // ------------------------------------------------------------------
        // Live state event — fired on the UI thread for each MQTT update
        // ------------------------------------------------------------------
        public class DeviceStateEventArgs : EventArgs
        {
            public DeviceConfig Config { get; set; }
            public DeviceState  State  { get; set; }
        }

        public event EventHandler<DeviceStateEventArgs> DeviceUpdated;

        public IReadOnlyList<(DeviceConfig Config, DeviceState State)> GetLiveStates()
        {
            var result = new List<(DeviceConfig, DeviceState)>();
            foreach (var m in _monitors)
                result.Add((m.Config, m.State));
            return result;
        }

        // ------------------------------------------------------------------
        // Private state
        // ------------------------------------------------------------------

        private NotifyIcon _trayIcon;
        private readonly List<MonitorEntry> _monitors = new List<MonitorEntry>();
        private AppConfig _appConfig;
        private readonly SynchronizationContext _syncContext;
        private readonly bool _startMinimized;

        private MainForm     _mainForm;
        private SettingsForm _settingsForm;

        private DeviceState _latestState;

        private class MonitorEntry
        {
            public MqttMonitor Monitor;
            public DeviceState State;
            public DeviceConfig Config;
            public EventHandler<StateChangedEventArgs> Handler;
        }

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------

        public TrayApplicationContext(bool startMinimized)
        {
            _startMinimized = startMinimized;
            _syncContext    = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _appConfig = ConfigManager.Load();
            Logger.Init(_appConfig.General?.ErrorLogPath);
            Logger.Log($"Config loaded. IsConfigured={_appConfig.IsConfigured}, Devices={_appConfig.Devices?.Count ?? 0}");
            ThemeManager.SetMode(_appConfig.General?.DarkMode ?? true);
            InitializeTray();
            NotificationAction.SetSharedIcon(_trayIcon);
            StartMonitors();

            // If not configured and not minimized, open immediately after tray is ready
            if (!_appConfig.IsConfigured && !_startMinimized)
                BeginInvoke(OpenMain);
        }

        // ------------------------------------------------------------------
        // Tray icon and context menu
        // ------------------------------------------------------------------

        private void InitializeTray()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open");
            openItem.Click += (s, e) => OpenMain();
            menu.Items.Add(openItem);

            var settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += (s, e) => OpenSettings();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += ExitApp;
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon             = CreateColoredIcon(Color.DarkGray),
                ContextMenuStrip = menu,
                Text             = "EcoFlow Monitor",
                Visible          = true
            };

            ThemeManager.Apply(menu);
            _trayIcon.DoubleClick += (s, e) => OpenMain();
        }

        // ------------------------------------------------------------------
        // Monitor lifecycle
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Public API refresh — called by MainForm's Refresh button
        // ------------------------------------------------------------------

        public async Task<bool> RefreshDevicesAsync()
        {
            Logger.Log("RefreshDevicesAsync: start");
            if (_appConfig?.Account == null || string.IsNullOrEmpty(_appConfig.Account.Email))
            {
                Logger.Log("RefreshDevicesAsync: no account configured, aborting");
                return false;
            }

            List<(string sn, string name)> discovered;
            try
            {
                using (var client = new EcoFlowClient())
                {
                    await client.LoginAsync(_appConfig.Account.Email, _appConfig.Account.Password).ConfigureAwait(false);
                    discovered = await client.GetAllDevicesAsync().ConfigureAwait(false);
                    Logger.Log($"RefreshDevicesAsync: discovered {discovered.Count} device(s)");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"RefreshDevicesAsync: FAILED — {ex}");
                return false;
            }

            // Merge discovered into config, preserving existing rules
            var existing = new Dictionary<string, DeviceConfig>();
            foreach (var d in _appConfig.Devices)
                if (!string.IsNullOrEmpty(d.SerialNumber))
                    existing[d.SerialNumber] = d;

            _appConfig.Devices.Clear();
            foreach (var (sn, name) in discovered)
            {
                if (existing.TryGetValue(sn, out var dev))
                {
                    dev.DisplayName = name;
                    _appConfig.Devices.Add(dev);
                }
                else
                {
                    _appConfig.Devices.Add(new DeviceConfig { SerialNumber = sn, DisplayName = name });
                }
            }

            Logger.Log($"RefreshDevicesAsync: config now has {_appConfig.Devices.Count} device(s)");
            ConfigManager.Save(_appConfig);
            StopMonitors();
            StartMonitors();
            return true;
        }

        private void StartMonitors()
        {
            if (_appConfig?.Account == null || string.IsNullOrEmpty(_appConfig.Account.Email)) return;
            if (_appConfig?.Devices == null || _appConfig.Devices.Count == 0) return;

            foreach (var device in _appConfig.Devices)
            {
                if (string.IsNullOrWhiteSpace(device.SerialNumber)) continue;

                var state   = new DeviceState { DeviceName = device.DisplayName, SerialNumber = device.SerialNumber };
                var monitor = new MqttMonitor(device, state);
                var entry   = new MonitorEntry { Monitor = monitor, State = state, Config = device };

                entry.Handler = (s, e) => OnStateChanged(s, e);
                monitor.StateChanged += entry.Handler;
                _monitors.Add(entry);

                Task.Run(() => ConnectDeviceAsync(entry));
            }
        }

        private async Task ConnectDeviceAsync(MonitorEntry entry)
        {
            Logger.Log($"ConnectDeviceAsync: start for '{entry.Config.DisplayName}' sn={entry.Config.SerialNumber}");
            try
            {
                using (var client = new EcoFlowClient())
                {
                    await client.LoginAsync(_appConfig.Account.Email, _appConfig.Account.Password)
                        .ConfigureAwait(false);
                    Logger.Log($"ConnectDeviceAsync: logged in");

                    string sn = entry.Config.SerialNumber;
                    if (string.IsNullOrWhiteSpace(sn))
                    {
                        var devices = await client.GetAllDevicesAsync().ConfigureAwait(false);
                        if (devices.Count > 0)
                        {
                            sn                       = devices[0].sn;
                            entry.State.SerialNumber = sn;
                            entry.State.DeviceName   = devices[0].name;
                        }
                    }

                    var creds = await client.GetMqttCredsAsync().ConfigureAwait(false);
                    Logger.Log($"ConnectDeviceAsync: MQTT creds host={creds.Host}:{creds.Port} userId={client.UserId}");
                    await entry.Monitor.StartAsync(creds, sn, client.UserId).ConfigureAwait(false);
                    Logger.Log($"ConnectDeviceAsync: monitor started for sn={sn}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ConnectDeviceAsync: FAILED — {ex}");
            }
        }

        private void StopMonitors()
        {
            foreach (var entry in _monitors)
            {
                try
                {
                    if (entry.Handler != null) entry.Monitor.StateChanged -= entry.Handler;
                    Task.Run(() => entry.Monitor.StopAsync()).Wait(3000);
                    entry.Monitor.Dispose();
                }
                catch { }
            }
            _monitors.Clear();
        }

        // ------------------------------------------------------------------
        // State change handler — called from MQTT background thread
        // ------------------------------------------------------------------

        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            var entry = _monitors.Find(m => m.Monitor == sender);
            if (entry != null)
                FireRules(entry, e.PreviousPower);

            var state = e.State;
            _syncContext.Post(_ =>
            {
                _latestState = state;
                UpdateTooltip(state);
                UpdateIcon(state);

                // Notify any open MainForm
                var entry2 = _monitors.Find(m => m.Monitor == sender);
                if (entry2 != null)
                    DeviceUpdated?.Invoke(this, new DeviceStateEventArgs { Config = entry2.Config, State = state });
            }, null);
        }

        private void FireRules(MonitorEntry entry, EcoFlowMonitor.Core.PowerStatus previousPower)
        {
            var rules = TriggerEvaluator.Evaluate(entry.Config, entry.State, previousPower);
            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    try { ActionRunner.Run(action, entry.Config, entry.State, _trayIcon); }
                    catch { }
                }
                TriggerEvaluator.RecordFired(rule, entry.State);
            }
        }

        // ------------------------------------------------------------------
        // Tooltip + icon
        // ------------------------------------------------------------------

        private void UpdateTooltip(DeviceState state)
        {
            if (state == null) { _trayIcon.Text = "EcoFlow Monitor"; return; }

            int battery = (int)(state.Bms?.BatteryPct ?? 0);
            int inW     = state.Display?.TotalInW  ?? state.Bms?.InputW  ?? 0;
            int outW    = state.Display?.TotalOutW ?? state.Bms?.OutputW ?? 0;

            string text = $"EcoFlow Monitor \u2014 {battery}% | {inW}W in / {outW}W out";
            if (text.Length > 63) text = text.Substring(0, 63);
            _trayIcon.Text = text;
        }

        private void UpdateIcon(DeviceState state)
        {
            if (state?.Power == null) { _trayIcon.Icon = CreateColoredIcon(Color.DarkGray); return; }

            switch (state.Power.Status)
            {
                case EcoFlowMonitor.Core.PowerStatus.Charging:  _trayIcon.Icon = CreateColoredIcon(Color.LimeGreen); break;
                case EcoFlowMonitor.Core.PowerStatus.Idle:       _trayIcon.Icon = CreateColoredIcon(Color.Gray);      break;
                case EcoFlowMonitor.Core.PowerStatus.PowerLost:  _trayIcon.Icon = CreateColoredIcon(Color.Red);       break;
                default:                                          _trayIcon.Icon = CreateColoredIcon(Color.DarkGray);  break;
            }
        }

        private Icon CreateColoredIcon(Color color)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(new SolidBrush(color), 1, 1, 14, 14);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        // ------------------------------------------------------------------
        // Menu actions
        // ------------------------------------------------------------------

        private void OpenMain()
        {
            // If not logged in yet, show login form first
            if (!_appConfig.IsConfigured)
            {
                using (var login = new LoginForm())
                {
                    if (login.ShowDialog() != DialogResult.OK) return;

                    _appConfig.Account = login.Account;

                    var existing = new Dictionary<string, DeviceConfig>();
                    foreach (var d in _appConfig.Devices)
                        if (!string.IsNullOrEmpty(d.SerialNumber))
                            existing[d.SerialNumber] = d;

                    _appConfig.Devices.Clear();
                    foreach (var (sn, name) in login.DiscoveredDevices)
                    {
                        if (existing.TryGetValue(sn, out var dev))
                        {
                            dev.DisplayName = name;
                            _appConfig.Devices.Add(dev);
                        }
                        else
                        {
                            _appConfig.Devices.Add(new DeviceConfig { SerialNumber = sn, DisplayName = name });
                        }
                    }

                    ConfigManager.Save(_appConfig);
                    StopMonitors();
                    StartMonitors();
                }
            }

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                if (_mainForm.WindowState == FormWindowState.Minimized)
                    _mainForm.WindowState = FormWindowState.Normal;
                _mainForm.BringToFront();
                _mainForm.Activate();
                return;
            }

            _mainForm = new MainForm(_appConfig, this);
            _mainForm.FormClosed += (s, e) =>
            {
                _appConfig = ConfigManager.Load();
                _mainForm  = null;
            };
            _mainForm.Show();
        }

        private void OpenSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.BringToFront();
                _settingsForm.Activate();
                return;
            }

            _appConfig    = ConfigManager.Load();
            _settingsForm = new SettingsForm(_appConfig);
            _settingsForm.FormClosed += (s, e) =>
            {
                _appConfig    = ConfigManager.Load();
                _settingsForm = null;
            };
            _settingsForm.Show();
        }

        private void ExitApp(object sender, EventArgs e)
        {
            StopMonitors();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }

        // Simple helper to invoke on the UI thread without a Form reference
        private static void BeginInvoke(Action action)
        {
            var dummy = new Control();
            dummy.CreateControl();
            dummy.BeginInvoke(action);
        }
    }
}
