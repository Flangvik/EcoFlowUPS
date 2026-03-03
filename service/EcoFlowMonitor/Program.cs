using System;
using System.Threading;
using System.Windows.Forms;
using EcoFlowMonitor.Core;
using EcoFlowMonitor.UI;

namespace EcoFlowMonitor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool minimized = Array.IndexOf(args, "--minimized") >= 0;

            // --- Elevation check -------------------------------------------
            // If the app is not running as administrator, offer to restart
            // elevated.  When launched from the Task Scheduler autostart
            // (--minimized), do it silently; otherwise ask the user.
            if (!ElevationHelper.IsAdministrator())
            {
                if (minimized)
                {
                    // Autostart path: re-launch elevated silently.
                    // RestartElevated() calls Environment.Exit on success.
                    ElevationHelper.RestartElevated(args);
                    return;
                }
                else
                {
                    var answer = MessageBox.Show(
                        "EcoFlow Monitor works best with administrator privileges.\n\n" +
                        "Running elevated allows:\n" +
                        "  • Autostart at boot without a UAC prompt\n" +
                        "  • Running scripts or commands that require admin rights\n\n" +
                        "Restart as Administrator now?",
                        "Administrator Privileges Recommended",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (answer == DialogResult.Yes)
                    {
                        ElevationHelper.RestartElevated(args);
                        return;
                    }
                    // User chose No — continue in non-elevated mode
                }
            }
            // ---------------------------------------------------------------

            bool isFirst;
            using (var mutex = new Mutex(true, "EcoFlowMonitor_SingleInstance", out isFirst))
            {
                if (!isFirst)
                {
                    MessageBox.Show("EcoFlow Monitor is already running.\n\nCheck the system tray.",
                        "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext(minimized));
            }
        }
    }
}
