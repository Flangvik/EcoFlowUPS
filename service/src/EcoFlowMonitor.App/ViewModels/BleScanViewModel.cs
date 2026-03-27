using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Client.Ble;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor.ViewModels;

public partial class BleScanViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly MonitorOrchestrator _orchestrator;
    private readonly AppConfig _config;
    private readonly BleScanner _scanner;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "Press Scan to search for nearby EcoFlow devices";
    [ObservableProperty] private bool _isConnecting;

    public ObservableCollection<BleDeviceInfo> DiscoveredDevices { get; } = new();

    public BleScanViewModel(NavigationService navigation, MonitorOrchestrator orchestrator, AppConfig config, BleScanner scanner)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _config = config;
        _scanner = scanner;
        _scanner.DeviceDiscovered += OnDeviceDiscovered;
    }

    private void OnDeviceDiscovered(object? sender, BleDeviceInfo device)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!DiscoveredDevices.Any(d => d.SerialNumber == device.SerialNumber))
            {
                DiscoveredDevices.Add(device);
                StatusMessage = $"Found {DiscoveredDevices.Count} device(s)";
            }
        });
    }

    [RelayCommand]
    private async Task ToggleScanAsync()
    {
        if (IsScanning)
        {
            _scanner.StopScan();
            IsScanning = false;
            StatusMessage = DiscoveredDevices.Count > 0
                ? $"Found {DiscoveredDevices.Count} device(s)"
                : "No devices found. Try again.";
        }
        else
        {
            DiscoveredDevices.Clear();
            IsScanning = true;
            StatusMessage = "Scanning for EcoFlow devices...";

            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await _scanner.StartScanAsync(cts.Token);
                }
                catch { }
                finally
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsScanning = false;
                        StatusMessage = DiscoveredDevices.Count > 0
                            ? $"Found {DiscoveredDevices.Count} device(s)"
                            : "No devices found. Make sure your EcoFlow device is nearby.";
                    });
                }
            });
        }
    }

    [RelayCommand]
    private async Task ConnectDeviceAsync(BleDeviceInfo? device)
    {
        if (device == null) return;

        IsConnecting = true;
        StatusMessage = $"Connecting to {device.Name}...";

        try
        {
            await _orchestrator.AddBleDeviceAsync(
                device.Name, device.SerialNumber, device.Address,
                device.EncryptionType, device.ProtocolVersion);

            var dashboard = App.Services!.GetRequiredService<DashboardViewModel>();
            _navigation.NavigateTo(dashboard);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _scanner.StopScan();
        // Go back to dashboard if we have devices, otherwise to login
        if (_config.IsConfigured)
        {
            var dashboard = App.Services!.GetRequiredService<DashboardViewModel>();
            _navigation.NavigateTo(dashboard);
        }
        else
        {
            var login = App.Services!.GetRequiredService<LoginViewModel>();
            _navigation.NavigateTo(login);
        }
    }
}
