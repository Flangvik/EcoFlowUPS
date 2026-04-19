using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

public class LinuxPowerActionService : IPowerActionService
{
    public void Shutdown() => RunProcess("systemctl", "poweroff");
    public void Hibernate() => RunProcess("systemctl", "hibernate");
    public void Sleep() => RunProcess("systemctl", "suspend");

    private static void RunProcess(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
