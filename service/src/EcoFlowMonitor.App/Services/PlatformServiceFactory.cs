using System.Runtime.InteropServices;
using EcoFlowMonitor.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor.Services;

public static class PlatformServiceFactory
{
    public static void Register(IServiceCollection services)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            RegisterWindows(services);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            RegisterMacOS(services);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            RegisterLinux(services);
        }
        else
        {
            services.AddSingleton<INotificationService, NoOpNotificationService>();
            services.AddSingleton<IPowerActionService, NoOpPowerActionService>();
            services.AddSingleton<IStartupService, NoOpStartupService>();
            services.AddSingleton<IScriptRunnerService, NoOpScriptRunnerService>();
            services.AddSingleton<IElevationService, NoOpElevationService>();
        }

        // BLE adapter — native CoreBluetooth on macOS, stub elsewhere
        if (OperatingSystem.IsMacOS())
        {
            var adapterType = typeof(PlatformServiceFactory).Assembly.GetType("EcoFlowMonitor.Services.CoreBluetoothBleAdapter");
            if (adapterType != null)
                services.AddSingleton(typeof(IBleAdapter), adapterType);
            else
                services.AddSingleton<IBleAdapter, StubBleAdapter>();
        }
        else
        {
            services.AddSingleton<IBleAdapter, StubBleAdapter>();
        }
    }

    private static void RegisterWindows(IServiceCollection services)
    {
        // These types are loaded at runtime from the Windows platform assembly
        var asm = System.Reflection.Assembly.Load("EcoFlowMonitor.Platform.Windows");
        services.AddSingleton(typeof(INotificationService), asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsNotificationService")!);
        services.AddSingleton(typeof(IPowerActionService), asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsPowerActionService")!);
        services.AddSingleton(typeof(IStartupService), asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsStartupService")!);
        services.AddSingleton(typeof(IScriptRunnerService), asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsScriptRunnerService")!);
        services.AddSingleton(typeof(IElevationService), asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsElevationService")!);
    }

    private static void RegisterMacOS(IServiceCollection services)
    {
        var asm = System.Reflection.Assembly.Load("EcoFlowMonitor.Platform.macOS");
        services.AddSingleton(typeof(INotificationService), asm.GetType("EcoFlowMonitor.Platform.macOS.MacNotificationService")!);
        services.AddSingleton(typeof(IPowerActionService), asm.GetType("EcoFlowMonitor.Platform.macOS.MacPowerActionService")!);
        services.AddSingleton(typeof(IStartupService), asm.GetType("EcoFlowMonitor.Platform.macOS.MacStartupService")!);
        services.AddSingleton(typeof(IScriptRunnerService), asm.GetType("EcoFlowMonitor.Platform.macOS.MacScriptRunnerService")!);
        services.AddSingleton(typeof(IElevationService), asm.GetType("EcoFlowMonitor.Platform.macOS.MacElevationService")!);
    }

    private static void RegisterLinux(IServiceCollection services)
    {
        var asm = System.Reflection.Assembly.Load("EcoFlowMonitor.Platform.Linux");
        services.AddSingleton(typeof(INotificationService), asm.GetType("EcoFlowMonitor.Platform.Linux.LinuxNotificationService")!);
        services.AddSingleton(typeof(IPowerActionService), asm.GetType("EcoFlowMonitor.Platform.Linux.LinuxPowerActionService")!);
        services.AddSingleton(typeof(IStartupService), asm.GetType("EcoFlowMonitor.Platform.Linux.LinuxStartupService")!);
        services.AddSingleton(typeof(IScriptRunnerService), asm.GetType("EcoFlowMonitor.Platform.Linux.LinuxScriptRunnerService")!);
        services.AddSingleton(typeof(IElevationService), asm.GetType("EcoFlowMonitor.Platform.Linux.LinuxElevationService")!);
    }
}

// No-op fallback implementations
internal class NoOpNotificationService : INotificationService { public void ShowNotification(string title, string body) { } }
internal class NoOpPowerActionService : IPowerActionService { public void Shutdown() { } public void Hibernate() { } public void Sleep() { } }
internal class NoOpStartupService : IStartupService { public bool IsEnabled() => false; public bool Enable() => false; public bool Disable() => false; }
internal class NoOpScriptRunnerService : IScriptRunnerService { public void RunScript(string scriptPath) { } }
internal class NoOpElevationService : IElevationService { public bool IsElevated() => false; public bool RestartElevated(string[] args) => false; }

// Stub BLE adapter — does nothing. Replace with platform-specific implementations.
internal class StubBleAdapter : IBleAdapter
{
    public event EventHandler<BleAdvertisement>? AdvertisementReceived;
    public Task StartScanAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void StopScan() { }
    public IBleGattConnection CreateConnection() => new StubGattConnection();
}

internal class StubGattConnection : IBleGattConnection
{
    public bool IsConnected => false;
    public event EventHandler<byte[]>? NotificationReceived;
    public Task ConnectAsync(string deviceId, CancellationToken ct = default) => throw new NotSupportedException("BLE not available on this platform. Install a BLE adapter implementation.");
    public Task SubscribeNotifyAsync(Guid serviceUuid, Guid characteristicUuid, CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
