using System.Diagnostics;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

/// <summary>
/// Linux elevation using pkexec (PolicyKit).
/// </summary>
public class LinuxElevationService : IElevationService
{
    public bool IsElevated()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });

            if (process is null) return false;

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return output == "0";
        }
        catch
        {
            return false;
        }
    }

    public bool RestartElevated(string[] args)
    {
        try
        {
            string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            string joinedArgs = args.Length > 0 ? " " + string.Join(" ", args) : string.Empty;

            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                Arguments = $"{exePath}{joinedArgs}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
            Environment.Exit(0);
            return true; // unreachable, but satisfies the compiler
        }
        catch
        {
            return false;
        }
    }
}
