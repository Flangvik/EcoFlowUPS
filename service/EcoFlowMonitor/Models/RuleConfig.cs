using System;
using System.Collections.Generic;

namespace EcoFlowMonitor.Models
{
    public class RuleConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Rule";
        public bool Enabled { get; set; } = true;
        public TriggerConfig Trigger { get; set; } = new TriggerConfig();
        public List<ActionConfig> Actions { get; set; } = new List<ActionConfig>();
    }
}
