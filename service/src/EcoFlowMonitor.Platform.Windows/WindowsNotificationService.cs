using EcoFlowMonitor.Platform;
using Microsoft.Toolkit.Uwp.Notifications;

namespace EcoFlowMonitor.Platform.Windows;

public class WindowsNotificationService : INotificationService
{
    public void ShowNotification(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch
        {
            // Toast not available on this Windows version -- silently skip
        }
    }
}
