using System;
using System.Threading.Tasks;
using EcoFlowMonitor.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace EcoFlowMonitor.Core
{
    public class StateChangedEventArgs : EventArgs
    {
        public DeviceState State { get; }
        public PowerStatus PreviousPower { get; }
        public StateChangedEventArgs(DeviceState state, PowerStatus previousPower)
        {
            State = state;
            PreviousPower = previousPower;
        }
    }

    public class MqttMonitor : IDisposable
    {
        private IManagedMqttClient _client;
        private readonly DeviceConfig _config;
        private readonly DeviceState _state;

        /// <summary>
        /// Raised on a ThreadPool thread whenever BMS or Display data is received.
        /// EventArgs carries the updated DeviceState AND the power status before the update.
        /// Callers must marshal to the UI thread if needed.
        /// </summary>
        public event EventHandler<StateChangedEventArgs> StateChanged;

        public MqttMonitor(DeviceConfig config, DeviceState state)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _state  = state  ?? throw new ArgumentNullException(nameof(state));
        }

        // ------------------------------------------------------------------
        // Start — connect managed MQTT client and subscribe to device topic
        // ------------------------------------------------------------------
        public async Task StartAsync(MqttCredentials creds, string sn)
        {
            if (creds == null) throw new ArgumentNullException(nameof(creds));
            if (string.IsNullOrWhiteSpace(sn)) throw new ArgumentException("Serial number must not be empty", nameof(sn));

            var factory  = new MqttFactory();
            _client      = factory.CreateManagedMqttClient();

            string clientId = $"ANDROID_{Guid.NewGuid():N}_monitor";
            string topic    = $"/app/device/property/{sn}";

            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithTcpServer(creds.Host, creds.Port)
                .WithCredentials(creds.Username, creds.Password)
                .WithTlsOptions(o =>
                {
                    o.UseTls = true;
                    // Skip certificate validation (mirrors Python's CERT_NONE)
                    o.CertificateValidationHandler = _ => true;
                })
                .WithCleanSession()
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(clientOptions)
                .Build();

            // Wire up message handler before starting
            _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _client.ConnectedAsync    += OnConnectedAsync;
            _client.DisconnectedAsync += OnDisconnectedAsync;

            await _client.StartAsync(managedOptions).ConfigureAwait(false);

            await _client.SubscribeAsync(
                topic,
                MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .ConfigureAwait(false);
        }

        // ------------------------------------------------------------------
        // Stop
        // ------------------------------------------------------------------
        public async Task StopAsync()
        {
            if (_client != null)
            {
                _state.IsConnected = false;
                await _client.StopAsync().ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------
        // Message handler
        // ------------------------------------------------------------------
        private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var segment = e.ApplicationMessage.PayloadSegment;
                if (segment.Count == 0)
                    return Task.CompletedTask;

                byte[] raw;
                if (segment.Offset == 0 && segment.Array != null && segment.Count == segment.Array.Length)
                {
                    raw = segment.Array;
                }
                else
                {
                    raw = new byte[segment.Count];
                    if (segment.Array != null)
                        Array.Copy(segment.Array, segment.Offset, raw, 0, segment.Count);
                }

                if (ProtobufDecoder.Dispatch(raw, out BmsData bms, out DisplayData display))
                {
                    PowerStatus previousPower = _state.Power.Status;

                    if (bms != null)
                        _state.Bms = bms;

                    if (display != null)
                        _state.Display = display;

                    _state.Power       = PowerStateMachine.Update(_state.Power, _state);
                    _state.LastUpdated = DateTime.Now;

                    StateChanged?.Invoke(this, new StateChangedEventArgs(_state, previousPower));
                }
            }
            catch
            {
                // Swallow decode errors — malformed packets should not crash the monitor
            }

            return Task.CompletedTask;
        }

        private Task OnConnectedAsync(MqttClientConnectedEventArgs e)
        {
            _state.IsConnected = true;
            return Task.CompletedTask;
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            _state.IsConnected = false;
            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------
        // IDisposable
        // ------------------------------------------------------------------
        public void Dispose()
        {
            if (_client != null)
            {
                _client.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
                _client.ConnectedAsync    -= OnConnectedAsync;
                _client.DisconnectedAsync -= OnDisconnectedAsync;
                _client.Dispose();
                _client = null;
            }
        }
    }
}
