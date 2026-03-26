using System;
using System.IO;

namespace EcoFlowMonitor.Core
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _path;

        public static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EcoFlowMonitor", "debug.log");

        public static void Init(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            Log("=== EcoFlow Monitor started ===");
        }

        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(_path)) return;
            lock (_lock)
            {
                try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}"); }
                catch { }
            }
        }
    }
}
