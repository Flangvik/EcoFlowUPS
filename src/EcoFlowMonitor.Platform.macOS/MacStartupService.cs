using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.macOS;

/// <summary>
/// Manages autostart on macOS via a LaunchAgent plist in ~/Library/LaunchAgents.
/// </summary>
public class MacStartupService : IStartupService
{
    private const string Label = "com.ecoflowmonitor";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{Label}.plist");

    public bool IsEnabled()
    {
        return File.Exists(PlistPath);
    }

    public bool Enable()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            string plistContent = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{Label}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                        <string>--minimized</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """;

            string? directory = Path.GetDirectoryName(PlistPath);
            if (directory is not null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(PlistPath, plistContent);
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
            if (File.Exists(PlistPath))
                File.Delete(PlistPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
