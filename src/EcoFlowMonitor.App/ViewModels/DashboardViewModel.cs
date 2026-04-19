using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Services;
using EcoFlowMonitor.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EcoFlowMonitor.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly MonitorOrchestrator _orchestrator;
    private readonly AppConfig _config;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty] private DeviceViewModel? _selectedDevice;
    [ObservableProperty] private bool _isRefreshing;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public DashboardViewModel(NavigationService navigation, MonitorOrchestrator orchestrator, AppConfig config, ILogger<DashboardViewModel> logger)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _config = config;
        _logger = logger;

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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var vm = Devices.FirstOrDefault(d => d.SerialNumber == e.State.SerialNumber);
            if (vm != null)
            {
                if (e.Source.Contains("..."))
                {
                    vm.StatusText = e.Source;
                    return;
                }
                vm.StatusText = "";
                vm.UpdateFromState(e.State);
                vm.SetActiveSource($"via {e.Source}");
            }
        });
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
    private void OpenHistory()
    {
        var historyVm = App.Services!.GetRequiredService<HistoryViewModel>();
        if (SelectedDevice?.SerialNumber != null)
            historyVm.SetDevice(SelectedDevice.SerialNumber);
        _navigation.NavigateTo(historyVm);
    }

    [RelayCommand]
    private void ScanBle()
    {
        var scanVm = App.Services!.GetRequiredService<BleScanViewModel>();
        _navigation.NavigateTo(scanVm);
    }

    [RelayCommand]
    private async Task AddRule()
    {
        var vm = App.Services!.GetRequiredService<ViewModels.Automation.RuleEditorViewModel>();
        var deviceCfg = SelectedDevice == null
            ? _config.Devices.FirstOrDefault()
            : _config.Devices.FirstOrDefault(d => d.SerialNumber == SelectedDevice.SerialNumber);
        vm.LoadNewRule(deviceCfg);

        var win = new Views.Automation.RuleEditorWindow { DataContext = vm };
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } owner)
        {
            await win.ShowDialog(owner);
        }
        else
        {
            win.Show();
        }
        // Refresh the dashboard's rule list binding
        OnPropertyChanged(nameof(Devices));
        foreach (var dv in Devices) dv.RaiseRulesChanged();
    }

    [RelayCommand]
    private async Task EditRule(RuleConfig? rule)
    {
        if (rule == null || SelectedDevice == null) return;
        var deviceCfg = _config.Devices.FirstOrDefault(d => d.SerialNumber == SelectedDevice.SerialNumber);
        if (deviceCfg == null) return;

        var vm = App.Services!.GetRequiredService<ViewModels.Automation.RuleEditorViewModel>();
        vm.LoadRule(deviceCfg, rule);

        var win = new Views.Automation.RuleEditorWindow { DataContext = vm };
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } owner)
        {
            await win.ShowDialog(owner);
        }
        else
        {
            win.Show();
        }
        foreach (var dv in Devices) dv.RaiseRulesChanged();
    }

    [RelayCommand]
    private void DeleteRule(RuleConfig? rule)
    {
        if (rule == null || SelectedDevice == null) return;
        var deviceCfg = _config.Devices.FirstOrDefault(d => d.SerialNumber == SelectedDevice.SerialNumber);
        if (deviceCfg == null) return;
        deviceCfg.Rules.RemoveAll(r => r.Id == rule.Id);
        ConfigManager.Save(_config);
        foreach (var dv in Devices) dv.RaiseRulesChanged();
    }

    [RelayCommand]
    private void TestRule(RuleConfig? rule)
    {
        if (rule == null || SelectedDevice == null) return;
        var deviceCfg = _config.Devices.FirstOrDefault(d => d.SerialNumber == SelectedDevice.SerialNumber);
        if (deviceCfg == null) return;
        _orchestrator.TestRule(deviceCfg, rule);
    }

    [RelayCommand]
    private void ViewRuleHistory()
    {
        var vm = App.Services!.GetRequiredService<ViewModels.Automation.RuleHistoryViewModel>();
        var win = new Views.Automation.RuleHistoryWindow { DataContext = vm };
        win.Show();
    }

    [RelayCommand]
    private void ShowErrorDetail()
    {
        if (SelectedDevice == null || !SelectedDevice.HasError) return;
        // D-08: show friendly message + expandable technical detail
        // For now, update StatusText to show the error detail (Phase 4 adds a proper dialog)
        SelectedDevice.StatusText = SelectedDevice.ErrorDetail;
    }

    [RelayCommand]
    private async Task CycleConnectionModeAsync()
    {
        if (SelectedDevice == null) return;
        var previousMode = SelectedDevice.Config.ConnectionMode; // snapshot for rollback
        SelectedDevice.CycleConnectionMode();
        SelectedDevice.StatusText = $"Switching to {SelectedDevice.ConnectionBadge}...";
        try
        {
            await _orchestrator.RestartDeviceAsync(SelectedDevice.Config);
            ConfigManager.Save(_config); // only save if restart succeeded (CONN-05)
        }
        catch (Exception ex)
        {
            // Revert to previous mode on failure
            SelectedDevice.Config.ConnectionMode = previousMode;
            SelectedDevice.UpdateBadge(); // refresh badge to reflect rollback
            SelectedDevice.StatusText = $"Switch failed: {ex.Message}";
            _logger.LogWarning(ex, "CycleConnectionMode failed, reverted to {Mode}", previousMode);
        }
    }
}
