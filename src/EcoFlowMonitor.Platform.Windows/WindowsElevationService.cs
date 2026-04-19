using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Windows;

/// <summary>
/// Windows elevation via UAC (Verb = "runas").
/// </summary>
public class WindowsElevationService : IElevationService
{
    public bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args)
            };

            Process.Start(psi);
            Environment.Exit(0);
            return true; // unreachable, but satisfies the compiler
        }
        catch (Win32Exception)
        {
            // User clicked "No" on the UAC prompt
            return false;
        }
        catch
        {
            return false;
        }
    }
}
