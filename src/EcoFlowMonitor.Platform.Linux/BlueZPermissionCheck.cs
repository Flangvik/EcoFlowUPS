namespace EcoFlowMonitor.Platform.Linux;

public static class BlueZPermissionCheck
{
    public static void EnsureBluetoothGroupMembership()
    {
        // Only run this check on Linux; on other OS (e.g. macOS build machine) skip.
        if (!OperatingSystem.IsLinux()) return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("id", "-nG")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start 'id' process");
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            var groups = output.Split(new[] { ' ', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            if (!groups.Contains("bluetooth"))
            {
                throw new InvalidOperationException(
                    "BLE on Linux requires membership in the 'bluetooth' group.\n" +
                    "Fix: sudo usermod -aG bluetooth $USER\n" +
                    "Then log out and log back in, or run: newgrp bluetooth");
            }
        }
        catch (InvalidOperationException)
        {
            throw; // re-throw our own descriptive errors
        }
        catch (Exception ex)
        {
            // If 'id' binary is missing or unreadable, log and continue rather than block startup.
            // The D-Bus call will fail with a descriptive error if permissions truly are wrong.
            System.Diagnostics.Debug.WriteLine($"[BlueZPermissionCheck] Could not verify bluetooth group: {ex.Message}");
        }
    }
}
