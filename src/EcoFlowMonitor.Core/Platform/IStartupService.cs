namespace EcoFlowMonitor.Platform;

public interface IStartupService
{
    bool IsEnabled();
    bool Enable();
    bool Disable();
}
