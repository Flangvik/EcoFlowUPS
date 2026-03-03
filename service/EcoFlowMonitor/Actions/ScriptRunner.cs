using System.Diagnostics;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Actions
{
    public static class ScriptRunner
    {
        /// <summary>
        /// Launches the script at <paramref name="expandedScript"/> as a hidden shell process.
        /// The path should already have template variables expanded before calling this method.
        /// </summary>
        public static void Run(ActionConfig action, string expandedScript)
        {
            if (string.IsNullOrWhiteSpace(expandedScript))
                return;

            var psi = new ProcessStartInfo
            {
                FileName         = expandedScript,
                UseShellExecute  = true,
                WindowStyle      = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
        }
    }
}
