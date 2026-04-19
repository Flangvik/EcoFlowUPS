namespace EcoFlowMonitor.Platform;

public interface IElevationService
{
    bool IsElevated();
    bool RestartElevated(string[] args);
}
