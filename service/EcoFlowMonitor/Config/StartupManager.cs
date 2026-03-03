using System;
using System.Diagnostics;
using System.Reflection;

namespace EcoFlowMonitor.Config
{
    /// <summary>
    /// Manages "start with Windows" via Task Scheduler when running elevated,
    /// with a transparent fallback to the HKCU Run registry key when not.
    ///
    /// Task Scheduler is the only mechanism that supports running an elevated
    /// process at logon without a UAC prompt on every boot.  The registry Run
    /// key is kept as a fallback for non-elevated installs where the user only
    /// needs the app to start (not necessarily with admin rights).
    /// </summary>
    public static class StartupManager
    {
        private const string TaskName = "EcoFlowMonitor";
        private const string RegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegValueName = "EcoFlowMonitor";

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public static bool IsEnabled()
        {
            return TaskExists() || RegistryEnabled();
        }

        /// <summary>
        /// Enables autostart.  Uses Task Scheduler when running as admin
        /// (so the task runs elevated on boot); falls back to the registry
        /// Run key otherwise.
        /// </summary>
        public static bool Enable()
        {
            if (Core.ElevationHelper.IsAdministrator())
                return CreateTask();
            else
                return EnableRegistry();
        }

        public static bool Disable()
        {
            bool ok = true;
            if (TaskExists()) ok &= DeleteTask();
            if (RegistryEnabled()) DisableRegistry();
            return ok;
        }

        // ------------------------------------------------------------------
        // Task Scheduler (elevated path)
        // ------------------------------------------------------------------

        private static bool TaskExists()
        {
            try
            {
                using (var p = RunSchtasks($"/query /tn \"{TaskName}\" /fo list", redirect: true))
                    return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static bool CreateTask()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                // /sc onlogon  — trigger at user logon
                // /rl highest  — run with highest available privileges (no UAC prompt)
                // /f           — force-overwrite if the task already exists
                string args = $"/create /tn \"{TaskName}\" " +
                              $"/tr \"\\\"{exe}\\\" --minimized\" " +
                              $"/sc onlogon /rl highest /f";

                using (var p = RunSchtasks(args, redirect: false))
                    return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static bool DeleteTask()
        {
            try
            {
                using (var p = RunSchtasks($"/delete /tn \"{TaskName}\" /f", redirect: false))
                    return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static Process RunSchtasks(string arguments, bool redirect)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect
            };
            var p = Process.Start(psi);
            p.WaitForExit(5000);
            return p;
        }

        // ------------------------------------------------------------------
        // Registry fallback (non-elevated path)
        // ------------------------------------------------------------------

        private static bool RegistryEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKeyPath, false))
                    return key?.GetValue(RegValueName) != null;
            }
            catch { return false; }
        }

        private static bool EnableRegistry()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKeyPath, true))
                    key?.SetValue(RegValueName, $"\"{exe}\" --minimized");
                return true;
            }
            catch { return false; }
        }

        private static void DisableRegistry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKeyPath, true))
                    key?.DeleteValue(RegValueName, false);
            }
            catch { }
        }
    }
}
