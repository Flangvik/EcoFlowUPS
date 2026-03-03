using System;
using System.Threading;
using System.Windows.Forms;
using EcoFlowMonitor.UI;

namespace EcoFlowMonitor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
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

                bool minimized = Array.IndexOf(args, "--minimized") >= 0;
                Application.Run(new TrayApplicationContext(minimized));
            }
        }
    }
}
