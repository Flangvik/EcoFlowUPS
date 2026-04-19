using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Services;

namespace EcoFlowMonitor.ViewModels.Automation;

/// <summary>
/// Shows all rules across all devices. Backs the RulesListView
/// (and the "Rules" tab in Settings / future Automation page).
/// </summary>
public partial class RulesListViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly MonitorOrchestrator _orchestrator;

    public ObservableCollection<RuleRowViewModel> Rules { get; } = new();

    public RulesListViewModel(AppConfig config, MonitorOrchestrator orchestrator)
    {
        _config = config;
        _orchestrator = orchestrator;
        Refresh();
    }

    public void Refresh()
    {
        Rules.Clear();
        foreach (var device in _config.Devices)
        {
            foreach (var rule in device.Rules)
                Rules.Add(new RuleRowViewModel(device, rule));
        }
    }

    [RelayCommand]
    private void ToggleEnabled(RuleRowViewModel? row)
    {
        if (row == null) return;
        row.Rule.Enabled = !row.Rule.Enabled;
        row.NotifyUpdated();
        ConfigManager.Save(_config);
    }

    [RelayCommand]
    private void DeleteRule(RuleRowViewModel? row)
    {
        if (row == null) return;
        row.Device.Rules.RemoveAll(r => r.Id == row.Rule.Id);
        Rules.Remove(row);
        ConfigManager.Save(_config);
    }

    [RelayCommand]
    private void DuplicateRule(RuleRowViewModel? row)
    {
        if (row == null) return;
        var copy = new RuleConfig
        {
            Id      = Guid.NewGuid().ToString(),
            Name    = row.Rule.Name + " (copy)",
            Enabled = false,
            Trigger = new TriggerConfig
            {
                Type             = row.Rule.Trigger.Type,
                Threshold        = row.Rule.Trigger.Threshold,
                ThresholdF       = row.Rule.Trigger.ThresholdF,
                CooldownSeconds  = row.Rule.Trigger.CooldownSeconds,
                WindowSeconds    = row.Rule.Trigger.WindowSeconds,
            },
            Actions = row.Rule.Actions.Select(a => new ActionConfig
            {
                Type              = a.Type,
                ScriptPath        = a.ScriptPath,
                NotificationTitle = a.NotificationTitle,
                NotificationBody  = a.NotificationBody,
                LogPath           = a.LogPath,
                LogMessage        = a.LogMessage,
                Webhook           = a.Webhook is null ? null : new WebhookActionData
                {
                    Url          = a.Webhook.Url,
                    Method       = a.Webhook.Method,
                    Headers      = new Dictionary<string, string>(a.Webhook.Headers),
                    BodyTemplate = a.Webhook.BodyTemplate,
                    Retries      = a.Webhook.Retries,
                    RetryDelayMs = a.Webhook.RetryDelayMs,
                    TimeoutMs    = a.Webhook.TimeoutMs,
                },
                RunCommand        = a.RunCommand is null ? null : new RunCommandActionData
                {
                    CommandWindows   = a.RunCommand.CommandWindows,
                    CommandMacOS     = a.RunCommand.CommandMacOS,
                    CommandLinux     = a.RunCommand.CommandLinux,
                    Shell            = a.RunCommand.Shell,
                    WorkingDirectory = a.RunCommand.WorkingDirectory,
                    TimeoutMs        = a.RunCommand.TimeoutMs,
                },
            }).ToList(),
        };
        row.Device.Rules.Add(copy);
        Rules.Add(new RuleRowViewModel(row.Device, copy));
        ConfigManager.Save(_config);
    }

    [RelayCommand]
    private void TestRule(RuleRowViewModel? row)
    {
        if (row == null) return;
        _orchestrator.TestRule(row.Device, row.Rule);
    }
}

public partial class RuleRowViewModel : ViewModelBase
{
    public DeviceConfig Device { get; }
    public RuleConfig Rule { get; }

    public string Name => Rule.Name;
    public string DeviceName => Device.DisplayName;
    public string Summary
    {
        get
        {
            var actionNames = string.Join(", ", Rule.Actions.Select(a => a.Type.ToString()));
            return $"{Rule.Trigger.Type} → {actionNames}";
        }
    }
    public bool Enabled => Rule.Enabled;
    public string EnabledLabel => Rule.Enabled ? "Enabled" : "Disabled";

    public RuleRowViewModel(DeviceConfig device, RuleConfig rule)
    {
        Device = device;
        Rule = rule;
    }

    public void NotifyUpdated()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(EnabledLabel));
    }
}
