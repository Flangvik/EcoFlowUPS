using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.ViewModels.Automation;

/// <summary>
/// Working-copy editor for a single <see cref="RuleConfig"/>. The owning
/// window binds Save/Cancel commands; on Save the working copy is merged
/// back into the parent <see cref="DeviceConfig"/> and persisted.
/// </summary>
public partial class RuleEditorViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly IElevationService _elevation;

    [ObservableProperty] private string _ruleName = "New rule";
    [ObservableProperty] private bool _enabled = true;

    // Trigger fields
    [ObservableProperty] private TriggerType _triggerType = TriggerType.PowerLost;
    [ObservableProperty] private int _triggerThreshold;
    [ObservableProperty] private string _triggerThresholdF = "";  // decimal string for temp/etc
    [ObservableProperty] private int _triggerCooldownSeconds = 300;
    [ObservableProperty] private int _triggerWindowSeconds = 300;

    public TriggerType[] AllTriggerTypes { get; } = Enum.GetValues<TriggerType>();
    public ActionType[] AllActionTypes { get; } = Enum.GetValues<ActionType>();
    public RunCommandShell[] AllShellKinds { get; } = Enum.GetValues<RunCommandShell>();

    /// <summary>Devices the rule can target.</summary>
    public ObservableCollection<DeviceConfig> Devices { get; } = new();

    [ObservableProperty] private DeviceConfig? _selectedDevice;

    /// <summary>Actions being edited. Each entry is an ActionRow with flat bindable fields.</summary>
    public ObservableCollection<ActionRowViewModel> Actions { get; } = new();

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

    public RuleConfig? EditingRule { get; private set; }
    public DeviceConfig? EditingDeviceScope { get; private set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(RuleName) && SelectedDevice != null && Actions.Count > 0;

    public RuleEditorViewModel(AppConfig config, IElevationService elevation)
    {
        _config = config;
        _elevation = elevation;

        foreach (var d in _config.Devices) Devices.Add(d);
        SelectedDevice = Devices.FirstOrDefault();
    }

    /// <summary>Populate from an existing rule for editing.</summary>
    public void LoadRule(DeviceConfig owningDevice, RuleConfig rule)
    {
        EditingDeviceScope = owningDevice;
        EditingRule = rule;
        RuleName = rule.Name;
        Enabled = rule.Enabled;
        SelectedDevice = Devices.FirstOrDefault(d => d.SerialNumber == owningDevice.SerialNumber)
                      ?? Devices.FirstOrDefault();

        TriggerType = rule.Trigger.Type;
        TriggerThreshold = rule.Trigger.Threshold;
        TriggerThresholdF = rule.Trigger.ThresholdF?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        TriggerCooldownSeconds = rule.Trigger.CooldownSeconds ?? 300;
        TriggerWindowSeconds = rule.Trigger.WindowSeconds ?? 300;

        Actions.Clear();
        foreach (var a in rule.Actions) Actions.Add(ActionRowViewModel.FromConfig(a));

        OnPropertyChanged(nameof(ElevationWarning));
    }

    /// <summary>Populate for a new rule (optionally pre-selecting a device).</summary>
    public void LoadNewRule(DeviceConfig? device = null)
    {
        EditingDeviceScope = null;
        EditingRule = null;
        RuleName = "New rule";
        Enabled = true;
        if (device != null)
            SelectedDevice = Devices.FirstOrDefault(d => d.SerialNumber == device.SerialNumber);

        TriggerType = TriggerType.PowerLost;
        TriggerThreshold = 0;
        TriggerThresholdF = "";
        TriggerCooldownSeconds = 300;
        TriggerWindowSeconds = 300;
        Actions.Clear();
        Actions.Add(new ActionRowViewModel { Type = ActionType.Notification, NotificationTitle = "EcoFlow Alert", NotificationBody = "{device}: {status}" });

        OnPropertyChanged(nameof(ElevationWarning));
    }

    [RelayCommand]
    private void AddAction()
    {
        Actions.Add(new ActionRowViewModel { Type = ActionType.WriteLog, LogMessage = "Rule fired: {device} {status}" });
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
        float? thresholdF = null;
        if (float.TryParse(TriggerThresholdF, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var f))
            thresholdF = f;

        var rule = EditingRule ?? new RuleConfig();
        rule.Name = RuleName;
        rule.Enabled = Enabled;
        rule.Trigger = new TriggerConfig
        {
            Type             = TriggerType,
            Threshold        = TriggerThreshold,
            ThresholdF       = thresholdF,
            CooldownSeconds  = TriggerCooldownSeconds,
            WindowSeconds    = TriggerWindowSeconds,
        };
        rule.Actions = Actions.Select(a => a.ToConfig()).ToList();
        return rule;
    }

    /// <summary>Persist the rule to the selected device's config.</summary>
    public void Save()
    {
        if (SelectedDevice == null) return;
        var built = BuildRule();

        // Remove from old device if moving.
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
    [ObservableProperty] private ActionType _type = ActionType.WriteLog;

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
