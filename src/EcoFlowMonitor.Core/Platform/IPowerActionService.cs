namespace EcoFlowMonitor.Platform;

public interface IPowerActionService
{
    void Shutdown();
    void Hibernate();
    void Sleep();
}
