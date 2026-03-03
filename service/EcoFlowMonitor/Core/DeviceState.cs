using System;
using System.Collections.Generic;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Core
{
    public class DeviceState
    {
        public BmsData Bms { get; set; }
        public DisplayData Display { get; set; }
        public PowerState Power { get; set; } = new PowerState();

        // Keyed by RuleConfig.Id, stores the last time the rule fired
        public Dictionary<string, DateTime> RuleLastFired { get; set; } = new Dictionary<string, DateTime>();

        public string DeviceName { get; set; }
        public string SerialNumber { get; set; }
        public bool IsConnected { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
