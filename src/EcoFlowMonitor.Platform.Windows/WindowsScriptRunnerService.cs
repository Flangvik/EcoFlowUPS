using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Windows;

public class WindowsScriptRunnerService : IScriptRunnerService
{
    public void RunScript(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
