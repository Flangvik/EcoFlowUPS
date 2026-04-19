using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Actions;

/// <summary>
/// Dispatches rule actions through a bounded concurrency queue with an
/// audit-row callback. Tasks T017 + T018 from specs/001-rules-engine/tasks.md.
///
/// Per rule, actions run sequentially in ordinal order. Across rules,
/// actions run in parallel up to <see cref="MaxConcurrent"/> (default 8),
/// with overflow queued in a bounded FIFO (default capacity 256, drop-oldest
/// on overflow — overflow writes an audit row tagged "dropped" per FR-010a).
/// </summary>
public sealed class ActionRunner : IAsyncDisposable
{
    private readonly INotificationService _notifications;
    private readonly IPowerActionService _power;
    private readonly IScriptRunnerService _scripts;
    private readonly IShellExecutor _shell;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Audit hook — called for every completed action attempt (including
    /// retries, skips, and dropped-from-queue). Kept as a mutable delegate
    /// so <c>MonitorOrchestrator</c> can inject the store after construction
    /// without making ActionRunner depend on <see cref="History.IRuleFiringStore"/>
    /// directly.
    /// </summary>
    public Func<RuleFiringAction, Task>? OnActionCompleted { get; set; }

    // -- Concurrency plumbing (plan R-004) --

    private readonly object _lifecycleLock = new();
    private Channel<QueuedRuleFire> _queue;
    private SemaphoreSlim _concurrencyLimiter;
    private CancellationTokenSource? _consumerCts;
    private Task? _consumerTask;

    public int MaxConcurrent { get; private set; } = 8;
    public int QueueCapacity { get; private set; } = 256;

    public ActionRunner(
        INotificationService notifications,
        IPowerActionService power,
        IScriptRunnerService scripts,
        IShellExecutor shell,
        HttpClient httpClient)
    {
        _notifications = notifications;
        _power         = power;
        _scripts       = scripts;
        _shell         = shell;
        _httpClient    = httpClient;
        (_queue, _concurrencyLimiter) = BuildQueue(QueueCapacity, MaxConcurrent);
        StartConsumer();
    }

    /// <summary>
    /// Live-reconfigure the concurrency cap + queue capacity (called by
    /// Settings). Pending items are moved to the fresh queue in order.
    /// </summary>
    public void ConfigureConcurrency(int maxConcurrent, int queueCapacity)
    {
        if (maxConcurrent < 1 || maxConcurrent > 64)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "must be 1..64");
        if (queueCapacity < 1 || queueCapacity > 4096)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), "must be 1..4096");

        lock (_lifecycleLock)
        {
            MaxConcurrent = maxConcurrent;
            QueueCapacity = queueCapacity;

            StopConsumer();
            (_queue, _concurrencyLimiter) = BuildQueue(queueCapacity, maxConcurrent);
            StartConsumer();
        }
    }

    /// <summary>
    /// Enqueue a rule fire for asynchronous execution. Returns immediately.
    /// The monitor pipeline MUST NOT await actions.
    /// </summary>
    public void Enqueue(RuleConfig rule, DeviceConfig device, DeviceState state, bool isTest = false)
    {
        var job = new QueuedRuleFire(rule, device, state, isTest);
        if (!_queue.Writer.TryWrite(job))
        {
            // Capacity full AND a same-rule oldest wasn't evictable — record dropped.
            _ = WriteDroppedAuditAsync(rule, "queue full");
        }
    }

    /// <summary>
    /// For the "Test rule now" UI button: execute exactly once synchronously
    /// (via the queue) and tag every audit row <c>isTest=true</c>.
    /// </summary>
    public void EnqueueForTest(RuleConfig rule, DeviceConfig device, DeviceState state)
        => Enqueue(rule, device, state, isTest: true);

    public async ValueTask DisposeAsync()
    {
        lock (_lifecycleLock) { StopConsumer(); }
        await Task.CompletedTask;
    }

    // ----------------------------------------------------------------------
    // Internals
    // ----------------------------------------------------------------------

    private sealed record QueuedRuleFire(
        RuleConfig   Rule,
        DeviceConfig Device,
        DeviceState  State,
        bool         IsTest);

    private static (Channel<QueuedRuleFire> queue, SemaphoreSlim limiter) BuildQueue(int capacity, int maxConcurrent)
    {
        var q = Channel.CreateBounded<QueuedRuleFire>(new BoundedChannelOptions(capacity)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        return (q, new SemaphoreSlim(maxConcurrent, maxConcurrent));
    }

    private void StartConsumer()
    {
        _consumerCts  = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumeAsync(_consumerCts.Token));
    }

    private void StopConsumer()
    {
        try { _queue.Writer.TryComplete(); } catch { /* already completed */ }
        _consumerCts?.Cancel();
        _consumerCts?.Dispose();
        _consumerCts = null;
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var job))
                {
                    await _concurrencyLimiter.WaitAsync(ct).ConfigureAwait(false);
                    _ = Task.Run(async () =>
                    {
                        try { await ExecuteRuleAsync(job, ct).ConfigureAwait(false); }
                        catch (Exception ex) { Debug.WriteLine($"[ActionRunner] rule-execution error: {ex}"); }
                        finally { _concurrencyLimiter.Release(); }
                    }, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task ExecuteRuleAsync(QueuedRuleFire job, CancellationToken ct)
    {
        var ordinal = 0;
        foreach (var action in job.Rule.Actions)
        {
            var sw = Stopwatch.StartNew();
            RuleFiringActionOutcome outcome;
            string? errorText = null;
            string? detailJson = null;
            try
            {
                (outcome, errorText, detailJson) = await DispatchAsync(action, job.Device, job.State, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                outcome   = RuleFiringActionOutcome.Failure;
                errorText = Truncate(ex.Message, 512);
            }
            sw.Stop();

            var row = new RuleFiringAction(
                Ordinal:    ordinal++,
                ActionType: action.Type.ToString(),
                Outcome:    outcome,
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorText:  errorText,
                DetailJson: detailJson);
            await InvokeAuditHookAsync(row).ConfigureAwait(false);
        }
    }

    private async Task<(RuleFiringActionOutcome, string? err, string? detail)> DispatchAsync(
        ActionConfig action, DeviceConfig device, DeviceState state, CancellationToken ct)
    {
        switch (action.Type)
        {
            case ActionType.RunScript:
            {
                var expanded = TemplateExpander.Expand(action, device, state);
                _scripts.RunScript(expanded.ScriptPath ?? "");
                return (RuleFiringActionOutcome.Success, null, null);
            }
            case ActionType.Shutdown:   { _power.Shutdown();  return (RuleFiringActionOutcome.Success, null, null); }
            case ActionType.Hibernate:  { _power.Hibernate(); return (RuleFiringActionOutcome.Success, null, null); }
            case ActionType.Sleep:      { _power.Sleep();     return (RuleFiringActionOutcome.Success, null, null); }
            case ActionType.Notification:
            {
                var expanded = TemplateExpander.Expand(action, device, state);
                _notifications.ShowNotification(expanded.NotificationTitle, expanded.NotificationBody ?? "");
                return (RuleFiringActionOutcome.Success, null, null);
            }
            case ActionType.WriteLog:
            {
                var expanded = TemplateExpander.Expand(action, device, state);
                LogAction.Write(expanded.LogPath, expanded.LogMessage);
                return (RuleFiringActionOutcome.Success, null, null);
            }
            case ActionType.Webhook:
                if (action.Webhook is null)
                    return (RuleFiringActionOutcome.Failure, "Webhook config missing", null);
                return await WebhookAction.RunAsync(action.Webhook, device, state, _httpClient, ct).ConfigureAwait(false);
            case ActionType.RunCommand:
                if (action.RunCommand is null)
                    return (RuleFiringActionOutcome.Failure, "RunCommand config missing", null);
                return await RunCommandAction.RunAsync(action.RunCommand, device, state, _shell, ct).ConfigureAwait(false);
            default:
                return (RuleFiringActionOutcome.Skipped, $"Unknown action type: {action.Type}", null);
        }
    }

    private async Task WriteDroppedAuditAsync(RuleConfig rule, string reason)
    {
        int ordinal = 0;
        foreach (var action in rule.Actions)
        {
            var row = new RuleFiringAction(
                Ordinal:    ordinal++,
                ActionType: action.Type.ToString(),
                Outcome:    RuleFiringActionOutcome.Dropped,
                DurationMs: 0,
                ErrorText:  reason);
            await InvokeAuditHookAsync(row).ConfigureAwait(false);
        }
    }

    private async Task InvokeAuditHookAsync(RuleFiringAction row)
    {
        var hook = OnActionCompleted;
        if (hook is null) return;
        try { await hook(row).ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine($"[ActionRunner] audit hook failed: {ex.Message}"); }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    /// <summary>
    /// Backward-compatible synchronous entry point used by legacy call sites
    /// (tests, CLI). Executes inline on the calling thread without queueing.
    /// New code should use <see cref="Enqueue"/>.
    /// </summary>
    public void Run(ActionConfig action, DeviceConfig device, DeviceState state)
    {
        var rule = new RuleConfig { Name = "<ad-hoc>", Actions = new List<ActionConfig> { action } };
        Enqueue(rule, device, state);
    }
}
