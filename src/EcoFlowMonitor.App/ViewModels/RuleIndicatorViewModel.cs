using CommunityToolkit.Mvvm.ComponentModel;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.ViewModels;

/// <summary>
/// Thin view-model wrapper around a <see cref="RuleConfig"/> on the dashboard
/// rules card (US4 / FR-018). Computes a per-condition tally against the most
/// recent <see cref="DeviceState"/> so the card can render "N / M met" plus
/// an inline per-condition truth list.
/// </summary>
public partial class RuleIndicatorViewModel : ViewModelBase
{
    public RuleConfig Rule { get; }

    [ObservableProperty] private int _conditionsMet;
    [ObservableProperty] private int _conditionsTotal;
    [ObservableProperty] private string _tallyLabel = "";
    [ObservableProperty] private string _triggerLabel = "";
    [ObservableProperty] private string _summary = "";

    /// <summary>Live per-condition truth list for the hover/tooltip breakdown.</summary>
    public List<string> ConditionBreakdown { get; private set; } = new();

    /// <summary>The tally indicator is only useful for composite rules.</summary>
    public bool ShowTally => ConditionsTotal >= 2;

    public bool Enabled
    {
        get => Rule.Enabled;
        set
        {
            if (Rule.Enabled == value) return;
            Rule.Enabled = value;
            OnPropertyChanged();
        }
    }

    public RuleIndicatorViewModel(RuleConfig rule)
    {
        Rule = rule;
        Rule.EnsureConditionsHydrated();
        RefreshLabels();
    }

    /// <summary>Recompute the tally from the current device state.</summary>
    public void UpdateFromState(DeviceState state)
    {
        Rule.EnsureConditionsHydrated();
        var truths = TriggerEvaluator.EvaluateConditions(Rule, state);

        ConditionsTotal = truths.Length;
        ConditionsMet   = truths.Count(b => b);
        TallyLabel      = $"{ConditionsMet} / {ConditionsTotal} met";

        ConditionBreakdown = Rule.Conditions
            .Select((c, i) => $"{c.Type}{FormatParams(c)}: {(truths[i] ? "\u2713" : "\u2717")}")
            .ToList();

        OnPropertyChanged(nameof(ConditionBreakdown));
        OnPropertyChanged(nameof(ShowTally));
        RefreshLabels();
    }

    public void NotifyRuleReplaced()
    {
        Rule.EnsureConditionsHydrated();
        OnPropertyChanged(nameof(Enabled));
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        Rule.EnsureConditionsHydrated();
        string trigger;
        if (Rule.Conditions.Count == 0)
            trigger = "(no conditions)";
        else if (Rule.Conditions.Count == 1)
            trigger = Rule.Conditions[0].Type.ToString();
        else
            trigger = Rule.Operator == RuleConditionOperator.All
                ? $"All of {Rule.Conditions.Count} conditions"
                : $"Any of {Rule.Conditions.Count} conditions";
        TriggerLabel = trigger;

        var actions = string.Join(", ", Rule.Actions.Select(a => a.Type.ToString()));
        Summary = string.IsNullOrEmpty(actions) ? trigger : $"{trigger} → {actions}";
    }

    private static string FormatParams(ConditionConfig c) => c.Type switch
    {
        TriggerType.BatteryBelow       => $" {c.Threshold}",
        TriggerType.BatteryAbove       => $" {c.Threshold}",
        TriggerType.TimeRemainingBelow => $" {c.Threshold}m",
        TriggerType.InputWattsBelow    => $" {c.Threshold}W",
        TriggerType.OutputWattsAbove   => $" {c.Threshold}W",
        TriggerType.TempAbove          => $" {c.ThresholdF ?? c.Threshold}°C",
        TriggerType.TempBelow          => $" {c.ThresholdF ?? c.Threshold}°C",
        TriggerType.DeviceOffline      => $" {c.WindowSeconds ?? 300}s",
        _ => "",
    };
}
