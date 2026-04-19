using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

/// <summary>
/// Manages autostart on Linux via a .desktop file in ~/.config/autostart.
/// </summary>
public class LinuxStartupService : IStartupService
{
    private const string DesktopFileName = "ecoflowmonitor.desktop";

    private static string DesktopFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart", DesktopFileName);

    public bool IsEnabled()
    {
        return File.Exists(DesktopFilePath);
    }

    public bool Enable()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            string desktopEntry = $"""
                [Desktop Entry]
                Type=Application
                Name=EcoFlow Monitor
                Exec={exePath} --minimized
                Hidden=false
                NoDisplay=false
                X-GNOME-Autostart-enabled=true
                """;

            string? directory = Path.GetDirectoryName(DesktopFilePath);
            if (directory is not null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(DesktopFilePath, desktopEntry);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            if (File.Exists(DesktopFilePath))
                File.Delete(DesktopFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
