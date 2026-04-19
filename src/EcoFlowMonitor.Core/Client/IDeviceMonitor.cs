using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Client;

public interface IDeviceMonitor : IDisposable
{
    event EventHandler<StateChangedEventArgs>? StateChanged;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}
