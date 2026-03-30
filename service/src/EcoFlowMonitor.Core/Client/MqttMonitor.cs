using System.Text;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Protocol;
using EcoFlowMonitor.State;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Diagnostics;
using MQTTnet.Formatter;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Stateless;

namespace EcoFlowMonitor.Client;

// Pipes MQTTnet internal trace to ILogger so we can see every packet
internal sealed class MqttNetLogger : IMqttNetLogger
{
    private readonly ILogger _logger;

    public MqttNetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => true;

    public void Publish(MqttNetLogLevel level, string source, string message, object[] parameters, Exception exception)
    {
        if (level < MqttNetLogLevel.Warning) return;
        var text = parameters?.Length > 0 ? string.Format(message, parameters) : message;
        var logLevel = level < MqttNetLogLevel.Error ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(logLevel, exception, "{Source}: {Message}", source, text);
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

public class MqttMonitor : IDeviceMonitor
{
    private IMqttClient? _client;
    private readonly DeviceConfig _config;
    private readonly DeviceState _state;
    private readonly MqttCredentials _creds;
    private readonly string _userId;
    private readonly ILogger<MqttMonitor> _logger;
    private string? _topic;
    private string? _wakeTopic;
    private MqttClientOptions? _options;
    private CancellationTokenSource? _cts;

    // Signals Polly that a disconnect occurred (Polly should retry)
    private TaskCompletionSource<bool>? _disconnectTcs;

    // -- FSM --
    private StateMachine<ConnectionStatus, ConnectionTrigger>? _machine;
    private StateMachine<ConnectionStatus, ConnectionTrigger>.TriggerWithParameters<TimeSpan>? _retryTrigger;
    private StateMachine<ConnectionStatus, ConnectionTrigger>.TriggerWithParameters<string>? _errorTrigger;
    private ResiliencePipeline? _connectPipeline;
    private int _retryAttempt;
    private TimeSpan _retryDelay;
    private readonly object _machineLock = new(); // Stateless is not thread-safe

    /// <summary>
    /// Raised on a ThreadPool thread whenever BMS or Display data is received.
    /// EventArgs carries the updated DeviceState AND the power status before the update.
    /// Callers must marshal to the UI thread if needed.
    /// </summary>
    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public MqttMonitor(DeviceConfig config, DeviceState state, MqttCredentials creds, string userId, ILogger<MqttMonitor> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _state  = state  ?? throw new ArgumentNullException(nameof(state));
        _creds  = creds  ?? throw new ArgumentNullException(nameof(creds));
        _userId = userId ?? throw new ArgumentNullException(nameof(userId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitStateMachine();
    }

    // -- FSM setup --

    private void InitStateMachine()
    {
        _machine = new StateMachine<ConnectionStatus, ConnectionTrigger>(ConnectionStatus.Idle);

        _retryTrigger = _machine.SetTriggerParameters<TimeSpan>(ConnectionTrigger.RetryScheduled);
        _errorTrigger = _machine.SetTriggerParameters<string>(ConnectionTrigger.ErrorOccurred);

        // MQTT starts at Connecting — no device scanning step
        _machine.Configure(ConnectionStatus.Idle)
            .Permit(ConnectionTrigger.Start, ConnectionStatus.Connecting);

        _machine.Configure(ConnectionStatus.Connecting)
            .OnEntry(NotifyStateChanged)
            .Permit(ConnectionTrigger.Connected, ConnectionStatus.Authenticating)
            .Permit(ConnectionTrigger.RetryScheduled, ConnectionStatus.Retrying)
            .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle)
            .Permit(ConnectionTrigger.ErrorOccurred, ConnectionStatus.Error);

        _machine.Configure(ConnectionStatus.Authenticating)
            .OnEntry(NotifyStateChanged)
            .Permit(ConnectionTrigger.Authenticated, ConnectionStatus.Streaming)
            .Permit(ConnectionTrigger.RetryScheduled, ConnectionStatus.Retrying)
            .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle)
            .Permit(ConnectionTrigger.ErrorOccurred, ConnectionStatus.Error);

        _machine.Configure(ConnectionStatus.Streaming)
            .OnEntry(NotifyStateChanged)
            .Permit(ConnectionTrigger.Disconnected, ConnectionStatus.Retrying)
            .Permit(ConnectionTrigger.ErrorOccurred, ConnectionStatus.Error)
            .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

        _machine.Configure(ConnectionStatus.Retrying)
            .OnEntryFrom(_retryTrigger, delay =>
            {
                _retryDelay = delay;
                NotifyStateChanged();
            })
            .Permit(ConnectionTrigger.Start, ConnectionStatus.Connecting) // Polly fires next attempt
            .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

        _machine.Configure(ConnectionStatus.Error)
            .OnEntryFrom(_errorTrigger, msg =>
            {
                lock (_state.SyncLock)
                {
                    _state.LastErrorMessage = msg;
                    _state.LastErrorDetail = msg;
                }
                NotifyStateChanged();
            })
            .Permit(ConnectionTrigger.Start, ConnectionStatus.Connecting) // manual retry from UI
            .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

        _machine.Configure(ConnectionStatus.Disconnected)
            .OnEntry(NotifyStateChanged)
            .Permit(ConnectionTrigger.Start, ConnectionStatus.Connecting);

        // Polly pipeline: circuit breaker (prevents EcoFlow broker rate-limit lockout) + exponential retry
        _connectPipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30),
                SamplingDuration = TimeSpan.FromSeconds(30),
                OnOpened = args =>
                {
                    _logger.LogWarning("MQTT circuit breaker OPEN — broker rate limit protection active, pausing for 30s");
                    return ValueTask.CompletedTask;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromMinutes(5),
                UseJitter = true,
                OnRetry = args =>
                {
                    _retryAttempt = args.AttemptNumber + 1;
                    lock (_machineLock)
                    {
                        if (_machine!.CanFire(ConnectionTrigger.RetryScheduled))
                            _machine.Fire(_retryTrigger!, args.RetryDelay);
                    }
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private void NotifyStateChanged()
    {
        lock (_state.SyncLock)
        {
            _state.ConnectionStatus = _machine!.State;
            _state.RetryAttempt = _retryAttempt;
            _state.RetryDelay = _retryDelay;
            _state.IsConnected = _machine.State == ConnectionStatus.Streaming;
        }
        // StateChanged raised OUTSIDE the lock -- prevents deadlock if handler reads _state
        StateChanged?.Invoke(this, new StateChangedEventArgs(_state, _state.Power.Status));
    }

    // ------------------------------------------------------------------
    // Start — connect plain MQTT client and subscribe to device topic
    // ------------------------------------------------------------------
    public Task StartAsync(CancellationToken ct = default)
    {
        var sn = _config.SerialNumber ?? throw new InvalidOperationException("SerialNumber is required");

        _topic     = $"/app/device/property/{sn}";
        _wakeTopic = $"/app/{_userId}/{sn}/thing/property/get";
        _cts       = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _retryAttempt = 0;

        // Match Python POC: ANDROID_{UUID-UPPERCASE-WITH-DASHES}_{userId}
        string clientId = $"ANDROID_{Guid.NewGuid().ToString().ToUpper()}_{_userId}";
        _logger.LogInformation("StartAsync clientId={ClientId} topic={Topic}", clientId, _topic);

        _options = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(_creds.Host, _creds.Port)
            .WithCredentials(_creds.Username, _creds.Password)
            .WithProtocolVersion(MqttProtocolVersion.V311)   // EcoFlow broker requires MQTT 3.1.1
            .WithTlsOptions(o =>
            {
                o.UseTls();
                o.WithCertificateValidationHandler(_ => true);
            })
            .WithCleanSession()
            .Build();

        var factory = new MqttFactory(new MqttNetLogger(_logger));
        _client = factory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.ConnectedAsync    += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;

        // Fire Start trigger: Idle -> Connecting
        lock (_machineLock) { _machine!.Fire(ConnectionTrigger.Start); }

        // Fire-and-forget connection loop (mirrors Python's loop_start)
        Task.Run(() => ConnectLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        try
        {
            await _connectPipeline!.ExecuteAsync(async token =>
            {
                // Advance FSM to Connecting for this attempt
                lock (_machineLock)
                {
                    if (_machine!.CanFire(ConnectionTrigger.Start))
                        _machine.Fire(ConnectionTrigger.Start);
                }

                // Fresh disconnect signal for this attempt
                _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var cred = _options!.Credentials;
                _logger.LogInformation("Connecting to {ChannelOptions} user={User} keepAlive={KeepAlive}s",
                    _options.ChannelOptions, cred?.GetUserName(_options), _options.KeepAlivePeriod.TotalSeconds);
                var result = await _client!.ConnectAsync(_options, token).ConfigureAwait(false);
                _logger.LogInformation("ConnectAsync result={ResultCode}", result.ResultCode);

                // Wait for disconnect — OnDisconnectedAsync throws via the TCS to cause Polly retry
                using var reg = token.Register(() => _disconnectTcs.TrySetCanceled());
                await _disconnectTcs.Task.ConfigureAwait(false);
            }, ct);
        }
        catch (OperationCanceledException) { /* StopAsync() called */ }
        catch (Exception ex)
        {
            lock (_machineLock)
            {
                _machine!.Fire(_errorTrigger!, ex.Message);
            }
            _logger.LogError(ex, "Non-retriable MQTT error for {SerialNumber}", _config.SerialNumber);
        }
    }

    // ------------------------------------------------------------------
    // Stop
    // ------------------------------------------------------------------
    public async Task StopAsync()
    {
        lock (_machineLock)
        {
            if (_machine!.CanFire(ConnectionTrigger.Stop))
                _machine.Fire(ConnectionTrigger.Stop);
        }
        _cts?.Cancel();
        if (_client != null && _client.IsConnected)
        {
            lock (_state.SyncLock) { _state.IsConnected = false; }
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // Connection callbacks — fire on ThreadPool threads
    // ------------------------------------------------------------------
    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("Connected sn={SerialNumber}, subscribing to {Topic}", _state.SerialNumber, _topic);

        // Fire Connected: Connecting -> Authenticating
        lock (_machineLock)
        {
            if (_machine!.CanFire(ConnectionTrigger.Connected))
                _machine.Fire(ConnectionTrigger.Connected);
        }

        try
        {
            var subOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(_topic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            var subResult = await _client!.SubscribeAsync(subOptions).ConfigureAwait(false);
            foreach (var item in subResult.Items)
                _logger.LogInformation("SUBACK topic={Topic} resultCode={ResultCode}", item.TopicFilter.Topic, item.ResultCode);

            // Publish wake command — triggers device to push its full current state.
            // Without this the broker stays silent until the EcoFlow mobile app opens.
            var wakePayload = Encoding.UTF8.GetBytes(
                "{\"from\":\"HomeAssistant\",\"id\":\"999954321\",\"version\":\"1.1\"," +
                "\"moduleType\":0,\"operateType\":\"latestQuotas\",\"params\":{}}");

            await _client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(_wakeTopic)
                .WithPayload(wakePayload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
                .Build()).ConfigureAwait(false);
            _logger.LogInformation("Wake command published to {WakeTopic}", _wakeTopic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscribe/wake failed");
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogInformation("Disconnected sn={SerialNumber} reason={Reason}", _state.SerialNumber, e.Reason);

        // Fire Disconnected: Streaming -> Retrying (Polly handles the reconnect timing)
        lock (_machineLock)
        {
            if (_machine!.CanFire(ConnectionTrigger.Disconnected))
                _machine.Fire(ConnectionTrigger.Disconnected);
        }

        // Signal Polly's await to throw (causing retry) — use exception so Polly retries rather than treating it as cancellation
        _disconnectTcs?.TrySetException(new InvalidOperationException($"MQTT disconnected: {e.Reason}"));

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Message handler
    // ------------------------------------------------------------------
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var segment = e.ApplicationMessage.PayloadSegment;
            _logger.LogDebug("Message received sn={SerialNumber} bytes={Bytes} topic={Topic}",
                _state.SerialNumber, segment.Count, e.ApplicationMessage.Topic);
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

            bool dispatched = ProtobufDecoder.Dispatch(raw, out BmsData? bms, out DisplayData? display, out EmsData? ems);
            _logger.LogDebug("Dispatch result={Dispatched} bms={HasBms} display={HasDisplay} ems={HasEms}",
                dispatched, bms != null, display != null, ems != null);
            if (dispatched)
            {
                // Transition to Streaming on first data if still Authenticating
                lock (_machineLock)
                {
                    if (_machine!.State == ConnectionStatus.Authenticating ||
                        _machine.State == ConnectionStatus.Connecting)
                    {
                        if (_machine.CanFire(ConnectionTrigger.Authenticated))
                            _machine.Fire(ConnectionTrigger.Authenticated);
                    }
                }

                // Snapshot previous power BEFORE lock so we can pass it to StateChangedEventArgs
                var previousPower = _state.Power.Status;
                lock (_state.SyncLock)
                {
                    if (bms != null) _state.Bms = bms;
                    if (display != null) _state.Display = display;
                    if (ems != null) _state.Ems = ems;
                    _state.Power = PowerStateMachine.Update(_state.Power, _state);
                    _state.LastUpdated = DateTime.Now;
                    _state.LastDataReceived = DateTime.Now;
                }
                // StateChanged raised OUTSIDE the lock -- prevents deadlock if handler reads _state
                StateChanged?.Invoke(this, new StateChangedEventArgs(_state, previousPower));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode MQTT message (payload may be malformed or protocol changed)");
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
