using System.Text.Json;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Config;

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EcoFlowMonitor");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();
            var json   = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();

            // Lazy migration (feature 002): map any legacy `trigger` field into
            // `conditions[0]`. File is not re-written here; the editor will
            // persist in the new shape next time the user saves the rule.
            foreach (var device in config.Devices)
                foreach (var rule in device.Rules)
                    rule.EnsureConditionsHydrated();

            return config;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ConfigManager.Load failed, using defaults: {ex.Message}");
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
