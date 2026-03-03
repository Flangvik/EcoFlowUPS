using System;
using System.IO;

namespace EcoFlowMonitor.Actions
{
    public static class LogAction
    {
        /// <summary>
        /// Appends a timestamped line to the file at <paramref name="logPath"/>.
        /// If <paramref name="logPath"/> is null or whitespace the call is a no-op.
        /// The directory is created automatically if it does not exist.
        /// </summary>
        public static void Write(string logPath, string message)
        {
            if (string.IsNullOrWhiteSpace(logPath)) return;

            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }
}
