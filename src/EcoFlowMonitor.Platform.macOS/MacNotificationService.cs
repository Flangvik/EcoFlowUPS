using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.macOS;

public class MacNotificationService : INotificationService
{
    public void ShowNotification(string title, string body)
    {
        try
        {
            string escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string escapedBody = body.Replace("\\", "\\\\").Replace("\"", "\\\"");

            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e 'display notification \"{escapedBody}\" with title \"{escapedTitle}\"'",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Notification unavailable -- silently skip
        }
    }
}
