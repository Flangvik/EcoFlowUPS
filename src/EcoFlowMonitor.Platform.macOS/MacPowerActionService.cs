using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.macOS;

public class MacPowerActionService : IPowerActionService
{
    public void Shutdown()
    {
        RunProcess("osascript", "-e 'tell application \"System Events\" to shut down'");
    }

    public void Hibernate()
    {
        // macOS does not have a separate hibernate mode on modern hardware;
        // fall back to sleep which is functionally equivalent.
        Sleep();
    }

    public void Sleep()
    {
        RunProcess("pmset", "sleepnow");
    }

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
