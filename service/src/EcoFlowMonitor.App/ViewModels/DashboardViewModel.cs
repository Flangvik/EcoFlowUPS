using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Services;
using EcoFlowMonitor.State;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly MonitorOrchestrator _orchestrator;
    private readonly AppConfig _config;

    [ObservableProperty] private DeviceViewModel? _selectedDevice;
    [ObservableProperty] private bool _isRefreshing;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public DashboardViewModel(NavigationService navigation, MonitorOrchestrator orchestrator, AppConfig config)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _config = config;

        _orchestrator.DeviceUpdated += OnDeviceUpdated;

        // Populate device list
        foreach (var device in _config.Devices)
        {
            Devices.Add(new DeviceViewModel(device));
        }

        if (Devices.Count > 0)
            SelectedDevice = Devices[0];

        // Start monitoring
        _ = _orchestrator.StartAsync();
    }

    private void OnDeviceUpdated(object? sender, DeviceStateEventArgs e)
    {
        var vm = Devices.FirstOrDefault(d => d.SerialNumber == e.State.SerialNumber);
        if (vm != null)
        {
            // Status messages (e.g. "Scanning for BLE...") go to StatusText
            if (e.Source.Contains("..."))
            {
                vm.StatusText = e.Source;
                return;
            }

            vm.StatusText = "";
            vm.UpdateFromState(e.State);
            vm.SetActiveSource($"via {e.Source}");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await _orchestrator.RefreshDevicesAsync();
            // Sync device list
            foreach (var device in _config.Devices)
            {
                if (!Devices.Any(d => d.SerialNumber == device.SerialNumber))
                    Devices.Add(new DeviceViewModel(device));
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settings = App.Services!.GetRequiredService<SettingsViewModel>();
        _navigation.NavigateTo(settings);
    }

    [RelayCommand]
    private void ScanBle()
    {
        var scanVm = App.Services!.GetRequiredService<BleScanViewModel>();
        _navigation.NavigateTo(scanVm);
    }

    [RelayCommand]
    private void AddRule()
    {
        // TODO: open rule wizard for selected device
    }

    [RelayCommand]
    private void CycleConnectionMode()
    {
        if (SelectedDevice == null) return;
        SelectedDevice.CycleConnectionMode();
        ConfigManager.Save(_config);
        // TODO: restart monitor with new mode
    }
}
