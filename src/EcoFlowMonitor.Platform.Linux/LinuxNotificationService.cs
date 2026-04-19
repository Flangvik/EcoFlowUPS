using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

public class LinuxNotificationService : INotificationService
{
    public void ShowNotification(string title, string body)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                Arguments = $"\"{title.Replace("\"", "\\\"")}\" \"{body.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // notify-send not available -- silently skip
        }
    }
}
