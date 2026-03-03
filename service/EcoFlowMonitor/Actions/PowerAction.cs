using System.Diagnostics;

namespace EcoFlowMonitor.Actions
{
    public static class PowerAction
    {
        public static void Execute(ActionType type)
        {
            switch (type)
            {
                case ActionType.Shutdown:
                    RunShutdown("/s /t 0");
                    break;
                case ActionType.Hibernate:
                    RunShutdown("/h");
                    break;
                case ActionType.Sleep:
                    RunRundll32("powrprof.dll,SetSuspendState 0,1,0");
                    break;
            }
        }

        private static void RunShutdown(string args)
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args)
            {
                UseShellExecute = false
            });
        }

        private static void RunRundll32(string args)
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", args)
            {
                UseShellExecute = false
            });
        }
    }
}
