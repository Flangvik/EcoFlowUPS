using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

public class LinuxScriptRunnerService : IScriptRunnerService
{
    public void RunScript(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"-c \"{scriptPath.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
