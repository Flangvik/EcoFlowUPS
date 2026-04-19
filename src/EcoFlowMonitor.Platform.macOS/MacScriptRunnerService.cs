using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.macOS;

public class MacScriptRunnerService : IScriptRunnerService
{
    public void RunScript(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath)) return;

        // Use /bin/bash -c for shell scripts, direct execution for binaries
        if (scriptPath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{scriptPath.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }
}
