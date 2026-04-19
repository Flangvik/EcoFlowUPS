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

    // Trigger type (drives which of the inputs below is shown)
    [ObservableProperty] private TriggerType _triggerType = TriggerType.PowerLost;

    // Per-type threshold inputs. Exactly one of these is visible/edited at
    // a time; the rest sit at their defaults. Keeping them separate (rather
    // than one polymorphic value) avoids double-interpretation and lets each
    // NumericUpDown have a sensible Min/Max for its concrete unit.
    [ObservableProperty] private int _triggerPercent          = 20;   // BatteryBelow/Above
    [ObservableProperty] private int _triggerMinutes          = 10;   // TimeRemainingBelow
    [ObservableProperty] private int _triggerWatts            = 50;   // InputWattsBelow / OutputWattsAbove
    [ObservableProperty] private decimal _triggerTempC        = 45m;  // TempAbove/Below
    [ObservableProperty] private int _triggerOfflineSeconds   = 300;  // DeviceOffline window

    // Always-visible cooldown. Default 300s for level triggers, 0s for edge
    // triggers (set by OnTriggerTypeChanged below).
    [ObservableProperty] private int _triggerCooldownSeconds  = 300;

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

    // ── Per-trigger-type UI visibility / labels ────────────────────────────
    // These drive which row of the trigger card is shown, so the form only
    // ever asks for the exact value that the selected trigger consumes.

    public bool IsEdgeOnly        => TriggerType is TriggerType.PowerLost or TriggerType.PowerRestored
                                                 or TriggerType.AcPlugged or TriggerType.AcUnplugged
                                                 or TriggerType.DeviceOnline;

    public bool IsPercentTrigger  => TriggerType is TriggerType.BatteryBelow or TriggerType.BatteryAbove;
    public bool IsMinutesTrigger  => TriggerType is TriggerType.TimeRemainingBelow;
    public bool IsWattsTrigger    => TriggerType is TriggerType.InputWattsBelow or TriggerType.OutputWattsAbove;
    public bool IsTempTrigger     => TriggerType is TriggerType.TempAbove or TriggerType.TempBelow;
    public bool IsOfflineTrigger  => TriggerType is TriggerType.DeviceOffline;

    /// <summary>Short description of what the selected trigger does, shown
    /// under the trigger-type picker to remove ambiguity.</summary>
    public string TriggerExplanation => TriggerType switch
    {
        TriggerType.PowerLost          => "Fires once when AC power drops (transition into PowerLost).",
        TriggerType.PowerRestored      => "Fires once when AC power returns (transition out of PowerLost).",
        TriggerType.AcPlugged          => "Fires once when the AC line is plugged into the station.",
        TriggerType.AcUnplugged        => "Fires once when the AC line is unplugged from the station.",
        TriggerType.DeviceOnline       => "Fires once when telemetry resumes after DeviceOffline.",
        TriggerType.BatteryBelow       => "Fires while battery % is below the threshold, throttled by cooldown.",
        TriggerType.BatteryAbove       => "Fires while battery % is above the threshold, throttled by cooldown.",
        TriggerType.TimeRemainingBelow => "Fires while the device's estimated remaining runtime is below the threshold, throttled by cooldown.",
        TriggerType.TempAbove          => "Fires while the BMS temperature is above the threshold, throttled by cooldown.",
        TriggerType.TempBelow          => "Fires while the BMS temperature is below the threshold, throttled by cooldown.",
        TriggerType.InputWattsBelow    => "Fires while total input wattage is below the threshold, throttled by cooldown.",
        TriggerType.OutputWattsAbove   => "Fires while total output wattage is above the threshold, throttled by cooldown.",
        TriggerType.DeviceOffline      => "Fires once when no telemetry has been received on any channel for the configured window.",
        _                              => "",
    };

    public string ThresholdLabel => TriggerType switch
    {
        TriggerType.BatteryBelow       => "Fire when battery is below",
        TriggerType.BatteryAbove       => "Fire when battery is above",
        TriggerType.TimeRemainingBelow => "Fire when remaining runtime is below",
        TriggerType.InputWattsBelow    => "Fire when total input is below",
        TriggerType.OutputWattsAbove   => "Fire when total output exceeds",
        TriggerType.TempAbove          => "Fire when temperature rises above",
        TriggerType.TempBelow          => "Fire when temperature drops below",
        TriggerType.DeviceOffline      => "Fire when no telemetry for at least",
        _                              => "",
    };

    public string CooldownHint => IsEdgeOnly
        ? "Edge trigger: a cooldown > 0 throttles repeated transitions. Default 0 (fire every transition)."
        : "Level trigger: fires at most once per cooldown window while the condition holds. Default 300s.";

    // When the trigger type changes, reset cooldown to the sensible default
    // for that family and fan out the IsXxx/Label/Explanation notifications
    // so the XAML IsVisible/Text bindings refresh.
    partial void OnTriggerTypeChanged(TriggerType oldValue, TriggerType newValue)
    {
        OnPropertyChanged(nameof(IsEdgeOnly));
        OnPropertyChanged(nameof(IsPercentTrigger));
        OnPropertyChanged(nameof(IsMinutesTrigger));
        OnPropertyChanged(nameof(IsWattsTrigger));
        OnPropertyChanged(nameof(IsTempTrigger));
        OnPropertyChanged(nameof(IsOfflineTrigger));
        OnPropertyChanged(nameof(TriggerExplanation));
        OnPropertyChanged(nameof(ThresholdLabel));
        OnPropertyChanged(nameof(CooldownHint));

        // Only reset cooldown when the FAMILY changed (edge ↔ level), to
        // avoid surprising the user who just tweaked it.
        bool wasEdge = oldValue is TriggerType.PowerLost or TriggerType.PowerRestored
                                 or TriggerType.AcPlugged or TriggerType.AcUnplugged
                                 or TriggerType.DeviceOnline;
        bool isEdge  = IsEdgeOnly;
        if (wasEdge != isEdge)
            TriggerCooldownSeconds = isEdge ? 0 : 300;
    }

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
        LoadTriggerValuesFromConfig(rule.Trigger);

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
        TriggerPercent        = 20;
        TriggerMinutes        = 10;
        TriggerWatts          = 50;
        TriggerTempC          = 45m;
        TriggerOfflineSeconds = 300;
        TriggerCooldownSeconds = 0;  // PowerLost is edge → no cooldown by default
        Actions.Clear();
        Actions.Add(new ActionRowViewModel { Type = ActionType.Notification, NotificationTitle = "EcoFlow Alert", NotificationBody = "{device}: {status}" });

        OnPropertyChanged(nameof(ElevationWarning));
    }

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
        var trigger = new TriggerConfig
        {
            Type            = TriggerType,
            CooldownSeconds = TriggerCooldownSeconds,
        };

        switch (TriggerType)
        {
            case TriggerType.BatteryBelow:
            case TriggerType.BatteryAbove:
                trigger.Threshold = TriggerPercent;
                break;
            case TriggerType.TimeRemainingBelow:
                trigger.Threshold = TriggerMinutes;
                break;
            case TriggerType.InputWattsBelow:
            case TriggerType.OutputWattsAbove:
                trigger.Threshold = TriggerWatts;
                break;
            case TriggerType.TempAbove:
            case TriggerType.TempBelow:
                trigger.ThresholdF = (float)TriggerTempC;
                break;
            case TriggerType.DeviceOffline:
                trigger.WindowSeconds = TriggerOfflineSeconds;
                break;
            // edge triggers carry no threshold
        }

        var rule = EditingRule ?? new RuleConfig();
        rule.Name    = RuleName;
        rule.Enabled = Enabled;
        rule.Trigger = trigger;
        rule.Actions = Actions.Select(a => a.ToConfig()).ToList();
        return rule;
    }

    /// <summary>Load the right per-type field out of <paramref name="t"/>.</summary>
    private void LoadTriggerValuesFromConfig(TriggerConfig t)
    {
        TriggerCooldownSeconds = t.CooldownSeconds
            ?? (IsEdgeOnly ? 0 : 300);

        switch (t.Type)
        {
            case TriggerType.BatteryBelow:
            case TriggerType.BatteryAbove:
                TriggerPercent = Math.Clamp(t.Threshold, 0, 100);
                break;
            case TriggerType.TimeRemainingBelow:
                TriggerMinutes = Math.Clamp(t.Threshold, 1, 1440);
                break;
            case TriggerType.InputWattsBelow:
            case TriggerType.OutputWattsAbove:
                TriggerWatts = Math.Clamp(t.Threshold, 0, 10000);
                break;
            case TriggerType.TempAbove:
            case TriggerType.TempBelow:
                TriggerTempC = t.ThresholdF.HasValue
                    ? (decimal)t.ThresholdF.Value
                    : (decimal)Math.Clamp(t.Threshold, -40, 120);
                break;
            case TriggerType.DeviceOffline:
                TriggerOfflineSeconds = Math.Clamp(t.WindowSeconds ?? 300, 30, 86400);
                break;
        }
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
    [ObservableProperty] private ActionType _type = ActionType.Notification;

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
