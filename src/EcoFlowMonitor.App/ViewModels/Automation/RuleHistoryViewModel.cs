using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.History;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.ViewModels.Automation;

public partial class RuleHistoryViewModel : ViewModelBase
{
    private readonly IRuleFiringStore _store;

    public ObservableCollection<RuleFiringRowViewModel> Firings { get; } = new();

    [ObservableProperty] private bool _isLoading;

    public RuleHistoryViewModel(IRuleFiringStore store)
    {
        _store = store;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await _store.QueryAsync(limit: 500);
            Firings.Clear();
            foreach (var r in rows)
                Firings.Add(new RuleFiringRowViewModel(r));
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class RuleFiringRowViewModel
{
    public RuleFiring Firing { get; }

    public string Timestamp  => Firing.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string RuleName   => Firing.RuleName;
    public string Device     => Firing.DeviceSerialNumber;
    public string Trigger    => Firing.TriggerType;
    public string Summary    => string.Join(" · ", Firing.Actions.Select(a => $"{a.ActionType}:{a.Outcome}"));
    public string TestTag    => Firing.IsTest ? "[TEST] " : "";
    public string Detail     => string.Join("\n", Firing.Actions.Select(a =>
        $"[{a.Ordinal}] {a.ActionType} → {a.Outcome} ({a.DurationMs}ms){(string.IsNullOrEmpty(a.ErrorText) ? "" : $"\n    error: {a.ErrorText}")}{(string.IsNullOrEmpty(a.DetailJson) ? "" : $"\n    detail: {a.DetailJson}")}"));

    /// <summary>
    /// Per-condition truth values parsed out of the firing's trigger-context
    /// JSON (populated by <c>TriggerContextBuilder</c>). Empty when the row
    /// was captured before feature 002 landed or when the JSON is malformed.
    /// </summary>
    public IReadOnlyList<ConditionFiringRowVm> Conditions { get; }

    public string OperatorLabel { get; }

    public bool HasConditions => Conditions.Count > 0;

    public RuleFiringRowViewModel(RuleFiring firing)
    {
        Firing = firing;
        (OperatorLabel, Conditions) = ParseConditions(firing.TriggerValueJson);
    }

    private static (string op, IReadOnlyList<ConditionFiringRowVm> rows) ParseConditions(string triggerJson)
    {
        var rows = new List<ConditionFiringRowVm>();
        string op = "";
        if (string.IsNullOrEmpty(triggerJson)) return (op, rows);
        try
        {
            using var doc = JsonDocument.Parse(triggerJson);
            if (!doc.RootElement.TryGetProperty("trigger", out var trigger)) return (op, rows);
            if (trigger.TryGetProperty("operator", out var opEl) && opEl.ValueKind == JsonValueKind.String)
                op = opEl.GetString() ?? "";
            if (!trigger.TryGetProperty("conditions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return (op, rows);

            foreach (var c in arr.EnumerateArray())
            {
                int index  = c.TryGetProperty("index", out var iEl) ? iEl.GetInt32() : rows.Count;
                string type = c.TryGetProperty("type",  out var tEl) ? (tEl.GetString() ?? "") : "";
                bool   val  = c.TryGetProperty("value", out var vEl) && vEl.ValueKind == JsonValueKind.True;
                string parm = BuildParamLabel(c);
                rows.Add(new ConditionFiringRowVm(index, type, parm, val));
            }
        }
        catch { /* legacy pre-002 rows — leave list empty */ }
        return (op, rows);
    }

    private static string BuildParamLabel(JsonElement c)
    {
        var parts = new List<string>();
        if (c.TryGetProperty("threshold", out var thr) && thr.ValueKind == JsonValueKind.Number)
        {
            var v = thr.GetInt32();
            if (v != 0) parts.Add($"thr={v}");
        }
        if (c.TryGetProperty("thresholdF", out var thrF) && thrF.ValueKind == JsonValueKind.Number)
            parts.Add($"thrF={thrF.GetSingle()}");
        if (c.TryGetProperty("windowSeconds", out var w) && w.ValueKind == JsonValueKind.Number)
            parts.Add($"window={w.GetInt32()}s");
        return parts.Count == 0 ? "" : string.Join(" ", parts);
    }
}

public record ConditionFiringRowVm(int Index, string Type, string Params, bool Value)
{
    public string ValueGlyph => Value ? "✓" : "✗";
}
