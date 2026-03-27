using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly AppConfig _config;
    private readonly IStartupService _startup;
    private readonly IElevationService _elevation;

    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _darkMode = true;
    [ObservableProperty] private string _logPath = "";
    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private string _accountEmail = "";

    public SettingsViewModel(NavigationService navigation, AppConfig config, IStartupService startup, IElevationService elevation)
    {
        _navigation = navigation;
        _config = config;
        _startup = startup;
        _elevation = elevation;

        StartWithSystem = _startup.IsEnabled();
        DarkMode = _config.General.DarkMode;
        LogPath = _config.General.ErrorLogPath;
        IsElevated = _elevation.IsElevated();
        AccountEmail = _config.Account?.Email ?? "";
    }

    [RelayCommand]
    private void Save()
    {
        _config.General.DarkMode = DarkMode;
        _config.General.ErrorLogPath = LogPath;
        _config.General.StartWithWindows = StartWithSystem;

        if (StartWithSystem) _startup.Enable();
        else _startup.Disable();

        ConfigManager.Save(_config);
        GoBack();
    }

    [RelayCommand]
    private void GoBack()
    {
        var dashboard = App.Services!.GetRequiredService<DashboardViewModel>();
        _navigation.NavigateTo(dashboard);
    }

    [RelayCommand]
    private void SignOut()
    {
        _config.Account = null;
        _config.Devices.Clear();
        ConfigManager.Save(_config);

        var login = App.Services!.GetRequiredService<LoginViewModel>();
        _navigation.NavigateTo(login);
    }
}
