using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace EcoFlowMonitor.Core
{
    public static class ElevationHelper
    {
        /// <summary>
        /// Returns true if the current process is running as administrator.
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Re-launches the current executable elevated via UAC (Verb = "runas").
        /// Preserves all original command-line arguments plus any extras.
        /// Exits the current (non-elevated) process immediately on success.
        /// Returns false if the user cancels the UAC prompt.
        /// </summary>
        public static bool RestartElevated(string[] originalArgs = null, string extraArg = null)
        {
            try
            {
                var args = new System.Collections.Generic.List<string>(originalArgs ?? new string[0]);
                if (extraArg != null && !args.Contains(extraArg))
                    args.Add(extraArg);

                var psi = new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", args)
                };

                Process.Start(psi);
                Environment.Exit(0);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User clicked "No" on the UAC prompt — do nothing
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not restart with elevated privileges:\n{ex.Message}",
                    "Elevation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
