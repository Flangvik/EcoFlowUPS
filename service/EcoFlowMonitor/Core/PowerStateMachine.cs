using System;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Core
{
    public enum PowerStatus { Unknown, Idle, Charging, PowerLost }

    public class PowerState
    {
        public PowerStatus Status { get; set; } = PowerStatus.Unknown;
        public int LastInputW { get; set; }
        public DateTime? LostAt { get; set; }
        public DateTime? RestoredAt { get; set; }
    }

    public static class PowerStateMachine
    {
        /// <summary>
        /// Returns a new PowerState based on the current DeviceState readings.
        /// Never mutates the <paramref name="current"/> instance.
        /// </summary>
        public static PowerState Update(PowerState current, DeviceState ds)
        {
            // No display data or TotalInW not reported -> Idle
            if (ds?.Display?.TotalInW == null)
            {
                return Copy(current, s => s.Status = PowerStatus.Idle);
            }

            int watts = ds.Display.TotalInW.Value;

            if (watts >= 10)
            {
                // Charging
                DateTime? restoredAt = (current.Status == PowerStatus.PowerLost)
                    ? DateTime.Now
                    : current.RestoredAt;

                return new PowerState
                {
                    Status      = PowerStatus.Charging,
                    LastInputW  = watts,
                    LostAt      = null,
                    RestoredAt  = restoredAt
                };
            }

            if (watts == 0)
            {
                if (current.Status == PowerStatus.Charging)
                {
                    // Transition: charging -> power lost
                    return new PowerState
                    {
                        Status      = PowerStatus.PowerLost,
                        LastInputW  = current.LastInputW,
                        LostAt      = DateTime.Now,
                        RestoredAt  = current.RestoredAt
                    };
                }

                if (current.Status == PowerStatus.PowerLost)
                {
                    // No change — stay in power-lost state
                    return current;
                }
            }

            // 0 < watts < 10 (or any other case) -> Idle
            return Copy(current, s => s.Status = PowerStatus.Idle);
        }

        // Creates a shallow copy then applies a mutation via callback
        private static PowerState Copy(PowerState src, Action<PowerState> mutate)
        {
            var next = new PowerState
            {
                Status     = src.Status,
                LastInputW = src.LastInputW,
                LostAt     = src.LostAt,
                RestoredAt = src.RestoredAt
            };
            mutate(next);
            return next;
        }
    }
}
