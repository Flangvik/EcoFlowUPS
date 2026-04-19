using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Windows;

public class WindowsPowerActionService : IPowerActionService
{
    public void Shutdown() => RunProcess("shutdown.exe", "/s /t 0");
    public void Hibernate() => RunProcess("shutdown.exe", "/h");
    public void Sleep() => RunProcess("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");

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
