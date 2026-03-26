namespace EcoFlowMonitor.Models
{
    /// <summary>
    /// EMS (Energy Management System) data from CMS heartbeat (cmdFunc=32, cmdId=2).
    /// Contains charging state, fan level, UPS mode, connected batteries, and remain times.
    /// </summary>
    public class EmsData
    {
        // EMS v1.0 fields
        public int? ChgState { get; set; }
        public int? FanLevel { get; set; }
        public int? MaxChargeSoc { get; set; }
        public int? UpsMode { get; set; }
        public int? ChgRemainMin { get; set; }
        public int? DsgRemainMin { get; set; }

        /// <summary>
        /// Array of connected BMS slot values. Non-zero entries indicate a battery pack is connected.
        /// e.g. [3, 0, 1] means slots 0 and 2 are connected.
        /// </summary>
        public int[] BmsConnected { get; set; }

        // EMS v1.3 fields
        public int? ChgLinePlugged { get; set; }
    }
}
