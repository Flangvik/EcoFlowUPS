using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Actions;

public class ActionRunner
{
    private readonly INotificationService _notifications;
    private readonly IPowerActionService _power;
    private readonly IScriptRunnerService _scripts;

    public ActionRunner(INotificationService notifications, IPowerActionService power, IScriptRunnerService scripts)
    {
        _notifications = notifications;
        _power = power;
        _scripts = scripts;
    }

    public void Run(ActionConfig action, DeviceConfig device, DeviceState state)
    {
        var expanded = TemplateExpander.Expand(action, device, state);
        switch (action.Type)
        {
            case ActionType.RunScript:
                _scripts.RunScript(expanded.ScriptPath ?? "");
                break;
            case ActionType.Shutdown:
                _power.Shutdown();
                break;
            case ActionType.Hibernate:
                _power.Hibernate();
                break;
            case ActionType.Sleep:
                _power.Sleep();
                break;
            case ActionType.Notification:
                _notifications.ShowNotification(expanded.NotificationTitle, expanded.NotificationBody ?? "");
                break;
            case ActionType.WriteLog:
                LogAction.Write(expanded.LogPath, expanded.LogMessage);
                break;
        }
    }
}
