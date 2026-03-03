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
        private NotifyIcon _trayIcon;

        // Each entry keeps the monitor and the latest known state for that device
        private readonly List<MonitorEntry> _monitors = new List<MonitorEntry>();

        private AppConfig _appConfig;
        private readonly SynchronizationContext _syncContext;
        private readonly bool _startMinimized;

        // Tracks the most recently received state across all devices (for tooltip)
        private DeviceState _latestState;

        private class MonitorEntry
        {
            public MqttMonitor Monitor;
            public DeviceState State;
            public DeviceConfig Config;
            public EventHandler<StateChangedEventArgs> Handler;
        }

        private void FireRules(MonitorEntry entry, PowerStatus previousPower)
        {
            var rules = TriggerEvaluator.Evaluate(entry.Config, entry.State, previousPower);
            foreach (var rule in rules)
            {
                foreach (var action in rule.Actions)
                {
                    try { ActionRunner.Run(action, entry.Config, entry.State, _trayIcon); }
                    catch { /* don't let a bad action crash the monitor */ }
                }
                TriggerEvaluator.RecordFired(rule, entry.State);
            }
        }

        public TrayApplicationContext(bool startMinimized)
        {
            _startMinimized = startMinimized;
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _appConfig = ConfigManager.Load();
            InitializeTray();
            NotificationAction.SetSharedIcon(_trayIcon);
            StartMonitors();
        }

        // ------------------------------------------------------------------
        // Tray icon and context menu
        // ------------------------------------------------------------------

        private void InitializeTray()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open Settings");
            openItem.Click += OpenSettings;
            menu.Items.Add(openItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += ExitApp;
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = CreateColoredIcon(Color.DarkGray),
                ContextMenuStrip = menu,
                Text = "EcoFlow Monitor",
                Visible = true
            };

            _trayIcon.DoubleClick += OpenSettings;
        }

        // ------------------------------------------------------------------
        // Monitor lifecycle
        // ------------------------------------------------------------------

        private void StartMonitors()
        {
            if (_appConfig?.Devices == null || _appConfig.Devices.Count == 0)
                return;

            foreach (var device in _appConfig.Devices)
            {
                // Skip devices with no credentials
                if (string.IsNullOrWhiteSpace(device.Email) || string.IsNullOrWhiteSpace(device.Password))
                    continue;

                var state = new DeviceState
                {
                    DeviceName = device.DisplayName,
                    SerialNumber = device.SerialNumber
                };

                var monitor = new MqttMonitor(device, state);
                var entry = new MonitorEntry { Monitor = monitor, State = state, Config = device };
                entry.Handler = (s, e) => OnStateChanged(s, e);
                monitor.StateChanged += entry.Handler;
                _monitors.Add(entry);

                // Login, resolve SN, then connect — all on a background thread
                Task.Run(() => ConnectDeviceAsync(entry));
            }
        }

        private async Task ConnectDeviceAsync(MonitorEntry entry)
        {
            try
            {
                using (var client = new EcoFlowClient())
                {
                    await client.LoginAsync(entry.Config.Email, entry.Config.Password)
                        .ConfigureAwait(false);

                    // Resolve serial number — use configured value or auto-detect
                    string sn = entry.Config.SerialNumber;
                    if (string.IsNullOrWhiteSpace(sn))
                    {
                        var (detectedSn, detectedName) = await client.GetFirstDeviceAsync()
                            .ConfigureAwait(false);
                        sn = detectedSn;
                        entry.State.SerialNumber = sn;
                        entry.State.DeviceName   = detectedName;
                    }

                    MqttCredentials creds = await client.GetMqttCredsAsync().ConfigureAwait(false);

                    await entry.Monitor.StartAsync(creds, sn).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Failure to connect is non-fatal — show the device as disconnected
                // and optionally log to the configured error log
                string logPath = _appConfig?.General?.ErrorLogPath;
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    try
                    {
                        System.IO.File.AppendAllText(logPath,
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Failed to connect device '{entry.Config.DisplayName}': {ex.Message}{Environment.NewLine}");
                    }
                    catch { /* ignore secondary log failures */ }
                }
            }
        }

        private void StopMonitors()
        {
            foreach (var entry in _monitors)
            {
                try
                {
                    if (entry.Handler != null)
                        entry.Monitor.StateChanged -= entry.Handler;
                    Task.Run(() => entry.Monitor.StopAsync()).Wait(3000);
                    entry.Monitor.Dispose();
                }
                catch { /* best-effort shutdown */ }
            }
            _monitors.Clear();
        }

        // ------------------------------------------------------------------
        // State change handler — called from MQTT background thread
        // ------------------------------------------------------------------

        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            // Rule evaluation happens on the MQTT thread (before UI marshal)
            var entry = _monitors.Find(m => m.Monitor == sender);
            if (entry != null)
                FireRules(entry, e.PreviousPower);

            // UI updates marshalled to the tray thread
            var state = e.State;
            _syncContext.Post(_ =>
            {
                _latestState = state;
                UpdateTooltip(state);
                UpdateIcon(state);
            }, null);
        }

        // ------------------------------------------------------------------
        // Tooltip + icon updates (always called on UI thread)
        // ------------------------------------------------------------------

        private void UpdateTooltip(DeviceState state)
        {
            if (state == null)
            {
                _trayIcon.Text = "EcoFlow Monitor";
                return;
            }

            int battery = (int)(state.Bms?.BatteryPct ?? 0);
            int inW     = state.Display?.TotalInW ?? state.Bms?.InputW ?? 0;
            int outW    = state.Display?.TotalOutW ?? state.Bms?.OutputW ?? 0;

            string text = $"EcoFlow Monitor \u2014 {battery}% | {inW}W in / {outW}W out";

            // NotifyIcon.Text has a 63-character limit enforced by Windows
            if (text.Length > 63)
                text = text.Substring(0, 63);

            _trayIcon.Text = text;
        }

        private void UpdateIcon(DeviceState state)
        {
            if (state?.Power == null)
            {
                _trayIcon.Icon = CreateColoredIcon(Color.DarkGray);
                return;
            }

            switch (state.Power.Status)
            {
                case PowerStatus.Charging:
                    _trayIcon.Icon = CreateColoredIcon(Color.LimeGreen);
                    break;
                case PowerStatus.Idle:
                    _trayIcon.Icon = CreateColoredIcon(Color.Gray);
                    break;
                case PowerStatus.PowerLost:
                    _trayIcon.Icon = CreateColoredIcon(Color.Red);
                    break;
                default:
                    _trayIcon.Icon = CreateColoredIcon(Color.DarkGray);
                    break;
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

        private void OpenSettings(object sender, EventArgs e)
        {
            // Always reload from disk so the form reflects the saved state
            _appConfig = ConfigManager.Load();

            using (var form = new SettingsForm(_appConfig))
            {
                form.ShowDialog();
                // Reload after save (form writes to disk on Save)
                _appConfig = ConfigManager.Load();
            }
        }

        private void ExitApp(object sender, EventArgs e)
        {
            StopMonitors();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            Application.Exit();
        }
    }
}
