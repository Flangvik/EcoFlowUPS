using System.Collections.ObjectModel;
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

    public RuleFiringRowViewModel(RuleFiring firing)
    {
        Firing = firing;
    }
}
