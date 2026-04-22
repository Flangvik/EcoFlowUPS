using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.ViewModels.Automation;

/// <summary>
/// Non-partial static bag of enum values the editor AXAML binds to via
/// <c>{x:Static ...}</c>. Kept separate because XAML <c>x:Static</c> doesn't
/// play nicely with generator-emitted partial classes.
/// </summary>
public static class RuleEditorViewModelStatics
{
    public static RuleConditionOperator[] AllOperators { get; } = Enum.GetValues<RuleConditionOperator>();
}

/// <summary>
/// Working-copy editor for a single <see cref="RuleConfig"/>. Feature 002
/// extends the editor to support composite predicates: an ordered list of
/// <see cref="ConditionRowViewModel"/> rows combined by a single
/// <see cref="RuleConditionOperator"/>.
/// </summary>
public partial class RuleEditorViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly IElevationService _elevation;

    [ObservableProperty] private string _ruleName = "New rule";
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Always-visible cooldown, in seconds. Default 300 s (level).
    /// Defaults to 0 s when the first condition is edge-only.</summary>
    [ObservableProperty] private int _ruleCooldownSeconds = 300;

    /// <summary>Per-rule composite operator.</summary>
    [ObservableProperty] private RuleConditionOperator _ruleOperator = RuleConditionOperator.All;

    /// <summary>Ordered condition list (feature 002).</summary>
    public ObservableCollection<ConditionRowViewModel> Conditions { get; } = new();

    public ObservableCollection<DeviceConfig> Devices { get; } = new();
    [ObservableProperty] private DeviceConfig? _selectedDevice;

    public ObservableCollection<ActionRowViewModel> Actions { get; } = new();

    public RuleConfig?   EditingRule        { get; private set; }
    public DeviceConfig? EditingDeviceScope { get; private set; }

    public bool IsOperatorVisible => Conditions.Count >= 2;

    /// <summary>
    /// Save is enabled only when: a name is present, a device is selected, at
    /// least one condition is configured, and at least one action exists.
    /// </summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(RuleName) &&
        SelectedDevice != null &&
        Conditions.Count > 0 &&
        Actions.Count > 0;

    public string? ElevationWarning
    {
        get
        {
            var needsElev = Actions.Any(a =>
                a.Type == ActionType.Shutdown ||
                a.Type == ActionType.Hibernate);
            if (!needsElev) return null;
            var elevated = false;
            try { elevated = _elevation.IsElevated(); } catch { }
            if (elevated) return null;
            if (OperatingSystem.IsWindows())
                return "This rule uses Shutdown/Hibernate which typically need the app to run as Administrator on Windows. Right-click EcoFlowMonitor.App → 'Run as administrator' to make these actions work.";
            if (OperatingSystem.IsMacOS())
                return "Shutdown on macOS usually requires admin privileges. The action may fail unless the app is launched via a LaunchDaemon configured to run privileged.";
            if (OperatingSystem.IsLinux())
                return "Shutdown/Hibernate on Linux require root or a configured polkit rule for your user.";
            return null;
        }
    }

    public RuleEditorViewModel(AppConfig config, IElevationService elevation)
    {
        _config    = config;
        _elevation = elevation;

        foreach (var d in _config.Devices) Devices.Add(d);
        SelectedDevice = Devices.FirstOrDefault();

        Conditions.CollectionChanged += OnConditionsChanged;
        Actions.CollectionChanged    += OnActionsChanged;
    }

    private void OnConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsOperatorVisible));
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ElevationWarning));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnRuleNameChanged(string value)      => OnPropertyChanged(nameof(CanSave));
    partial void OnSelectedDeviceChanged(DeviceConfig? value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>Populate from an existing rule for editing.</summary>
    public void LoadRule(DeviceConfig owningDevice, RuleConfig rule)
    {
        EditingDeviceScope = owningDevice;
        EditingRule        = rule;
        RuleName           = rule.Name;
        Enabled            = rule.Enabled;
        SelectedDevice     = Devices.FirstOrDefault(d => d.SerialNumber == owningDevice.SerialNumber)
                          ?? Devices.FirstOrDefault();

        rule.EnsureConditionsHydrated();

        Conditions.Clear();
        foreach (var c in rule.Conditions)
            Conditions.Add(ConditionRowViewModel.FromConfig(c));

        RuleOperator = rule.Operator;

        // Hydrate rule-level cooldown from condition[0].CooldownSeconds (where
        // legacy rules parked it); fall back to family default.
        var legacyCooldown = rule.Conditions.FirstOrDefault()?.CooldownSeconds;
        RuleCooldownSeconds = legacyCooldown
            ?? (Conditions.FirstOrDefault()?.IsEdgeOnly == true ? 0 : 300);

        Actions.Clear();
        foreach (var a in rule.Actions) Actions.Add(ActionRowViewModel.FromConfig(a));

        OnPropertyChanged(nameof(ElevationWarning));
        OnPropertyChanged(nameof(IsOperatorVisible));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Populate for a new rule (optionally pre-selecting a device).</summary>
    public void LoadNewRule(DeviceConfig? device = null)
    {
        EditingDeviceScope = null;
        EditingRule        = null;
        RuleName           = "New rule";
        Enabled            = true;
        if (device != null)
            SelectedDevice = Devices.FirstOrDefault(d => d.SerialNumber == device.SerialNumber);

        Conditions.Clear();
        Conditions.Add(new ConditionRowViewModel { TriggerType = TriggerType.BatteryBelow, TriggerPercent = 20 });

        RuleOperator        = RuleConditionOperator.All;
        RuleCooldownSeconds = 300;

        Actions.Clear();
        Actions.Add(new ActionRowViewModel
        {
            Type              = ActionType.Notification,
            NotificationTitle = "EcoFlow Alert",
            NotificationBody  = "{device}: {status}",
        });

        OnPropertyChanged(nameof(ElevationWarning));
        OnPropertyChanged(nameof(IsOperatorVisible));
        OnPropertyChanged(nameof(CanSave));
    }

    // -- Condition commands -------------------------------------------------

    [RelayCommand]
    private void AddCondition()
    {
        Conditions.Add(new ConditionRowViewModel { TriggerType = TriggerType.BatteryBelow, TriggerPercent = 20 });
    }

    [RelayCommand]
    private void RemoveCondition(ConditionRowViewModel? row)
    {
        if (row == null) return;
        Conditions.Remove(row);
    }

    [RelayCommand]
    private void MoveConditionUp(ConditionRowViewModel? row)
    {
        if (row == null) return;
        var i = Conditions.IndexOf(row);
        if (i <= 0) return;
        Conditions.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveConditionDown(ConditionRowViewModel? row)
    {
        if (row == null) return;
        var i = Conditions.IndexOf(row);
        if (i < 0 || i >= Conditions.Count - 1) return;
        Conditions.Move(i, i + 1);
    }

    [RelayCommand]
    private void ToggleOperator()
    {
        RuleOperator = RuleOperator == RuleConditionOperator.All
            ? RuleConditionOperator.Any
            : RuleConditionOperator.All;
    }

    // -- Action commands ----------------------------------------------------

    [RelayCommand]
    private void AddAction()
    {
        Actions.Add(new ActionRowViewModel
        {
            Type              = ActionType.Notification,
            NotificationTitle = "EcoFlow Alert",
            NotificationBody  = "{device}: {status}",
        });
        OnPropertyChanged(nameof(ElevationWarning));
    }

    [RelayCommand]
    private void RemoveAction(ActionRowViewModel? row)
    {
        if (row == null) return;
        Actions.Remove(row);
        OnPropertyChanged(nameof(ElevationWarning));
    }

    /// <summary>Serialise the working copy back to a RuleConfig.</summary>
    public RuleConfig BuildRule()
    {
        var rule = EditingRule ?? new RuleConfig();
        rule.Name       = RuleName;
        rule.Enabled    = Enabled;
        rule.Operator   = RuleOperator;
        rule.Conditions = Conditions.Select(c => c.ToConfig()).ToList();

        // Park the rule-level cooldown on Conditions[0] for backward-compat
        // with the existing evaluator — it reads Conditions[0].CooldownSeconds
        // as the authoritative cooldown for the composite's fires.
        if (rule.Conditions.Count > 0)
            rule.Conditions[0].CooldownSeconds = RuleCooldownSeconds;

        rule.Trigger = null;
        rule.Actions = Actions.Select(a => a.ToConfig()).ToList();
        return rule;
    }

    /// <summary>Persist the rule to the selected device's config.</summary>
    public void Save()
    {
        if (SelectedDevice == null) return;
        if (Conditions.Count == 0)  return;
        var built = BuildRule();

        if (EditingDeviceScope != null && EditingDeviceScope.SerialNumber != SelectedDevice.SerialNumber)
            EditingDeviceScope.Rules.RemoveAll(r => r.Id == built.Id);

        var existing = SelectedDevice.Rules.FirstOrDefault(r => r.Id == built.Id);
        if (existing != null)
        {
            var idx = SelectedDevice.Rules.IndexOf(existing);
            SelectedDevice.Rules[idx] = built;
        }
        else
        {
            SelectedDevice.Rules.Add(built);
        }
        ConfigManager.Save(_config);
    }
}

/// <summary>
/// Flat bindable representation of a single ActionConfig, used by the editor
/// so XAML bindings can target plain properties without nested nulls.
/// </summary>
public partial class ActionRowViewModel : ViewModelBase
{
    [ObservableProperty] private ActionType _type = ActionType.Notification;

    /// <summary>Exposed on the row itself so the ComboBox can bind to the row's
    /// own DataContext — avoids the $parent[ItemsControl] lookup that can race
    /// against SelectedItem binding inside DataTemplates in Avalonia 11.</summary>
    public static ActionType[] AllTypes { get; } = Enum.GetValues<ActionType>();

    /// <summary>Same reasoning as <see cref="AllTypes"/> — exposed on the row.</summary>
    public static RunCommandShell[] AllShellKinds { get; } = Enum.GetValues<RunCommandShell>();

    public bool IsNotification => Type == ActionType.Notification;
    public bool IsScript       => Type == ActionType.RunScript;
    public bool IsLog          => Type == ActionType.WriteLog;
    public bool IsWebhook      => Type == ActionType.Webhook;
    public bool IsRunCommand   => Type == ActionType.RunCommand;

    partial void OnTypeChanged(ActionType value)
    {
        OnPropertyChanged(nameof(IsNotification));
        OnPropertyChanged(nameof(IsScript));
        OnPropertyChanged(nameof(IsLog));
        OnPropertyChanged(nameof(IsWebhook));
        OnPropertyChanged(nameof(IsRunCommand));
    }

    // Shared-ish fields
    [ObservableProperty] private string? _scriptPath;
    [ObservableProperty] private string _notificationTitle = "EcoFlow Alert";
    [ObservableProperty] private string? _notificationBody;
    [ObservableProperty] private string? _logPath;
    [ObservableProperty] private string? _logMessage;

    // Webhook
    [ObservableProperty] private string _webhookUrl = "";
    [ObservableProperty] private string _webhookMethod = "POST";
    [ObservableProperty] private string _webhookHeaders = "";     // "Key: Value" per line
    [ObservableProperty] private string? _webhookBodyTemplate;
    [ObservableProperty] private int _webhookRetries;
    [ObservableProperty] private int _webhookRetryDelayMs = 1000;
    [ObservableProperty] private int _webhookTimeoutMs = 10000;

    // RunCommand
    [ObservableProperty] private string? _commandWindows;
    [ObservableProperty] private string? _commandMacOS;
    [ObservableProperty] private string? _commandLinux;
    [ObservableProperty] private RunCommandShell _shell = RunCommandShell.Default;
    [ObservableProperty] private string? _workingDirectory;
    [ObservableProperty] private int _commandTimeoutMs = 30000;

    public static ActionRowViewModel FromConfig(ActionConfig a)
    {
        var row = new ActionRowViewModel
        {
            Type              = a.Type,
            ScriptPath        = a.ScriptPath,
            NotificationTitle = a.NotificationTitle,
            NotificationBody  = a.NotificationBody,
            LogPath           = a.LogPath,
            LogMessage        = a.LogMessage,
        };
        if (a.Webhook is { } w)
        {
            row.WebhookUrl = w.Url;
            row.WebhookMethod = w.Method;
            row.WebhookHeaders = string.Join("\n", w.Headers.Select(kv => $"{kv.Key}: {kv.Value}"));
            row.WebhookBodyTemplate = w.BodyTemplate;
            row.WebhookRetries = w.Retries;
            row.WebhookRetryDelayMs = w.RetryDelayMs;
            row.WebhookTimeoutMs = w.TimeoutMs;
        }
        if (a.RunCommand is { } c)
        {
            row.CommandWindows = c.CommandWindows;
            row.CommandMacOS = c.CommandMacOS;
            row.CommandLinux = c.CommandLinux;
            row.Shell = c.Shell;
            row.WorkingDirectory = c.WorkingDirectory;
            row.CommandTimeoutMs = c.TimeoutMs;
        }
        return row;
    }

    public ActionConfig ToConfig()
    {
        var cfg = new ActionConfig
        {
            Type              = Type,
            ScriptPath        = ScriptPath,
            NotificationTitle = NotificationTitle,
            NotificationBody  = NotificationBody,
            LogPath           = LogPath,
            LogMessage        = LogMessage,
        };
        if (Type == ActionType.Webhook)
        {
            var headers = new Dictionary<string, string>();
            foreach (var line in (WebhookHeaders ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
            cfg.Webhook = new WebhookActionData
            {
                Url          = WebhookUrl,
                Method       = WebhookMethod,
                Headers      = headers,
                BodyTemplate = WebhookBodyTemplate,
                Retries      = WebhookRetries,
                RetryDelayMs = WebhookRetryDelayMs,
                TimeoutMs    = WebhookTimeoutMs,
            };
        }
        if (Type == ActionType.RunCommand)
        {
            cfg.RunCommand = new RunCommandActionData
            {
                CommandWindows   = CommandWindows,
                CommandMacOS     = CommandMacOS,
                CommandLinux     = CommandLinux,
                Shell            = Shell,
                WorkingDirectory = WorkingDirectory,
                TimeoutMs        = CommandTimeoutMs,
            };
        }
        return cfg;
    }
}
