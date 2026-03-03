using Microsoft.Win32;
using System;
using System.Reflection;

namespace EcoFlowMonitor.Config
{
    public static class StartupManager
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "EcoFlowMonitor";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                return key?.GetValue(ValueName) != null;
        }

        public static void Enable()
        {
            var exePath = Assembly.GetExecutingAssembly().Location;
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                key?.SetValue(ValueName, $"\"{exePath}\" --minimized");
        }

        public static void Disable()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                key?.DeleteValue(ValueName, false);
        }
    }
}
