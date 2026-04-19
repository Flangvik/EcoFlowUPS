using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.ViewModels;

public partial class RuleWizardViewModel : ViewModelBase
{
    private readonly Action<RuleConfig?> _onComplete;
    private RuleConfig? _existingRule;

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private int _totalSteps = 4;
    [ObservableProperty] private string _stepTitle = "Name Your Rule";

    // Step 1: Name
    [ObservableProperty] private string _ruleName = "New Rule";
    [ObservableProperty] private bool _ruleEnabled = true;

    // Step 2: Trigger
    [ObservableProperty] private TriggerType _selectedTrigger = TriggerType.PowerLost;

    // Step 3: Threshold
    [ObservableProperty] private int _threshold = 20;
    [ObservableProperty] private bool _showThreshold;

    // Step 4: Actions
    public ObservableCollection<ActionConfig> Actions { get; } = new();

    public bool IsFirstStep => CurrentStep == 0;
    public bool IsLastStep => CurrentStep >= TotalSteps - 1;
    public string NextButtonText => IsLastStep ? "Finish" : "Next >";

    public RuleWizardViewModel(Action<RuleConfig?> onComplete, RuleConfig? existingRule = null)
    {
        _onComplete = onComplete;
        if (existingRule != null)
        {
            _existingRule = existingRule;
            _ruleName = existingRule.Name;
            _ruleEnabled = existingRule.Enabled;
            _selectedTrigger = existingRule.Trigger.Type;
            _threshold = existingRule.Trigger.Threshold;
            foreach (var action in existingRule.Actions)
                Actions.Add(action);
        }
        UpdateStepState();
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep) { Finish(); return; }
        CurrentStep++;
        UpdateStepState();
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0) { CurrentStep--; UpdateStepState(); }
    }

    [RelayCommand]
    private void Cancel() => _onComplete(null);

    private void Finish()
    {
        var rule = _existingRule ?? new RuleConfig();
        rule.Name = RuleName;
        rule.Enabled = RuleEnabled;
        rule.Trigger = new TriggerConfig { Type = SelectedTrigger, Threshold = Threshold };
        rule.Actions = new List<ActionConfig>(Actions);
        _onComplete(rule);
    }

    [RelayCommand]
    private void AddNotification()
    {
        Actions.Add(new ActionConfig
        {
            Type = ActionType.Notification,
            NotificationTitle = "EcoFlow Alert",
            NotificationBody = "Power event on {device}: {status}"
        });
    }

    [RelayCommand]
    private void AddShutdown() => Actions.Add(new ActionConfig { Type = ActionType.Shutdown });

    [RelayCommand]
    private void AddLogAction()
    {
        Actions.Add(new ActionConfig
        {
            Type = ActionType.WriteLog,
            LogMessage = "{device}: {status} at {battery}%"
        });
    }

    [RelayCommand]
    private void RemoveAction(ActionConfig? action)
    {
        if (action != null) Actions.Remove(action);
    }

    partial void OnSelectedTriggerChanged(TriggerType value)
    {
        ShowThreshold = value == TriggerType.BatteryBelow || value == TriggerType.TimeRemainingBelow;
        TotalSteps = ShowThreshold ? 4 : 3;
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextButtonText));
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextButtonText));
    }

    private void UpdateStepState()
    {
        StepTitle = CurrentStep switch
        {
            0 => "Name Your Rule",
            1 => "Choose a Trigger",
            2 when ShowThreshold => "Set the Threshold",
            _ => "Add Actions"
        };
    }
}
