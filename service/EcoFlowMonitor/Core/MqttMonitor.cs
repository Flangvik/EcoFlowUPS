using System;
using System.Threading;
using System.Threading.Tasks;
using EcoFlowMonitor.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Diagnostics;
using MQTTnet.Formatter;

namespace EcoFlowMonitor.Core
{
    // Pipes MQTTnet internal trace to our Logger so we can see every packet
    internal sealed class MqttNetLogger : IMqttNetLogger
    {
        public bool IsEnabled => true;
        public void Publish(MqttNetLogLevel level, string source, string message, object[] parameters, Exception exception)
        {
            if (level < MqttNetLogLevel.Warning) return;
            var text = parameters?.Length > 0 ? string.Format(message, parameters) : message;
            Logger.Log($"[MQTT/{level}] {source}: {text}{(exception != null ? " — " + exception.Message : "")}");
        }
    }

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
        private IMqttClient _client;
        private readonly DeviceConfig _config;
        private readonly DeviceState _state;
        private string _topic;
        private string _wakeTopic;
        private MqttClientOptions _options;
        private CancellationTokenSource _cts;

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
        // Start — connect plain MQTT client and subscribe to device topic
        // ------------------------------------------------------------------
        public Task StartAsync(MqttCredentials creds, string sn, string userId)
        {
            if (creds == null) throw new ArgumentNullException(nameof(creds));
            if (string.IsNullOrWhiteSpace(sn)) throw new ArgumentException("Serial number must not be empty", nameof(sn));

            _topic     = $"/app/device/property/{sn}";
            _wakeTopic = $"/app/{userId}/{sn}/thing/property/get";
            _cts       = new CancellationTokenSource();

            // Match Python POC: ANDROID_{UUID-UPPERCASE-WITH-DASHES}_{userId}
            string clientId = $"ANDROID_{Guid.NewGuid().ToString().ToUpper()}_{userId}";
            Logger.Log($"MqttMonitor: StartAsync clientId={clientId} topic={_topic}");

            _options = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithTcpServer(creds.Host, creds.Port)
                .WithCredentials(creds.Username, creds.Password)
                .WithProtocolVersion(MqttProtocolVersion.V311)   // EcoFlow broker requires MQTT 3.1.1
                .WithTlsOptions(o =>
                {
                    o.UseTls();
                    o.WithCertificateValidationHandler(_ => true);
                })
                .WithCleanSession()
                .Build();

            var factory = new MqttFactory(new MqttNetLogger());
            _client = factory.CreateMqttClient();

            _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _client.ConnectedAsync    += OnConnectedAsync;
            _client.DisconnectedAsync += OnDisconnectedAsync;

            // Fire-and-forget connection loop (mirrors Python's loop_start)
            Task.Run(() => ConnectLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        private async Task ConnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var cred = _options.Credentials;
                    Logger.Log($"MqttMonitor: connecting to {_options.ChannelOptions} user={cred?.GetUserName(_options)} keepAlive={_options.KeepAlivePeriod.TotalSeconds}s");
                    var result = await _client.ConnectAsync(_options, ct).ConfigureAwait(false);
                    Logger.Log($"MqttMonitor: ConnectAsync result={result.ResultCode}");
                    return; // success — DisconnectedAsync drives reconnect
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Logger.Log($"MqttMonitor: connect failed — {ex.Message}, retry in 5s");
                    try { await Task.Delay(5000, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        // ------------------------------------------------------------------
        // Stop
        // ------------------------------------------------------------------
        public async Task StopAsync()
        {
            _cts?.Cancel();
            if (_client != null && _client.IsConnected)
            {
                _state.IsConnected = false;
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------
        // Connection callbacks — fire on ThreadPool threads
        // ------------------------------------------------------------------
        private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
        {
            Logger.Log($"MqttMonitor: connected sn={_state.SerialNumber}, subscribing to {_topic}");
            _state.IsConnected = true;
            StateChanged?.Invoke(this, new StateChangedEventArgs(_state, _state.Power.Status));

            try
            {
                var subOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(_topic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                    .Build();

                var subResult = await _client.SubscribeAsync(subOptions).ConfigureAwait(false);
                foreach (var item in subResult.Items)
                    Logger.Log($"MqttMonitor: SUBACK topic={item.TopicFilter.Topic} resultCode={item.ResultCode}");

                // Publish wake command — triggers device to push its full current state.
                // Without this the broker stays silent until the EcoFlow mobile app opens.
                var wakePayload = System.Text.Encoding.UTF8.GetBytes(
                    "{\"from\":\"HomeAssistant\",\"id\":\"999954321\",\"version\":\"1.1\"," +
                    "\"moduleType\":0,\"operateType\":\"latestQuotas\",\"params\":{}}");

                await _client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(_wakeTopic)
                    .WithPayload(wakePayload)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                    .Build()).ConfigureAwait(false);
                Logger.Log($"MqttMonitor: wake command published to {_wakeTopic}");
            }
            catch (Exception ex)
            {
                Logger.Log($"MqttMonitor: subscribe/wake failed — {ex.Message}");
            }
        }

        private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            Logger.Log($"MqttMonitor: disconnected sn={_state.SerialNumber} reason={e.Reason}");
            _state.IsConnected = false;
            StateChanged?.Invoke(this, new StateChangedEventArgs(_state, _state.Power.Status));

            if (_cts != null && !_cts.IsCancellationRequested)
            {
                Logger.Log($"MqttMonitor: reconnecting in 5s...");
                try { await Task.Delay(5000, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                await ConnectLoopAsync(_cts.Token).ConfigureAwait(false);
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
                Logger.Log($"MqttMonitor: message received sn={_state.SerialNumber} bytes={segment.Count} topic={e.ApplicationMessage.Topic}");
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

                bool dispatched = ProtobufDecoder.Dispatch(raw, out BmsData bms, out DisplayData display, out EmsData ems);
                Logger.Log($"MqttMonitor: dispatch result={dispatched} bms={bms != null} display={display != null} ems={ems != null}");
                if (dispatched)
                {
                    PowerStatus previousPower = _state.Power.Status;

                    if (bms != null)
                        _state.Bms = bms;

                    if (display != null)
                        _state.Display = display;

                    if (ems != null)
                        _state.Ems = ems;

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

        // ------------------------------------------------------------------
        // IDisposable
        // ------------------------------------------------------------------
        public void Dispose()
        {
            _cts?.Cancel();
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
