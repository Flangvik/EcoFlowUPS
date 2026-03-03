using System;
using System.IO;
using EcoFlowMonitor.Models;
using Newtonsoft.Json;

namespace EcoFlowMonitor.Config
{
    public static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EcoFlowMonitor");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new AppConfig();
                var json = File.ReadAllText(ConfigPath);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }
}
