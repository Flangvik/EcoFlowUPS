using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly AppConfig _config;

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSigningIn;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public LoginViewModel(NavigationService navigation, AppConfig config)
    {
        _navigation = navigation;
        _config = config;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        IsSigningIn = true;

        try
        {
            using var client = new EcoFlowClient();
            await client.LoginAsync(Email, Password);
            var devices = await client.GetAllDevicesAsync();

            // Save account
            _config.Account = new AccountConfig { Email = Email, Password = Password };

            // Add discovered devices
            foreach (var (sn, name) in devices)
            {
                if (!_config.Devices.Any(d => d.SerialNumber == sn))
                    _config.Devices.Add(new DeviceConfig { SerialNumber = sn, DisplayName = name });
            }

            ConfigManager.Save(_config);

            // Navigate to dashboard
            var dashboard = App.Services!.GetRequiredService<DashboardViewModel>();
            _navigation.NavigateTo(dashboard);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message.Contains("401") || ex.Message.Contains("credential", StringComparison.OrdinalIgnoreCase)
                ? "Invalid email or password"
                : $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    [RelayCommand]
    private void UseBleOnly()
    {
        if (string.IsNullOrEmpty(_config.LocalUserId))
            _config.LocalUserId = Guid.NewGuid().ToString();
        ConfigManager.Save(_config);

        var scanVm = App.Services!.GetRequiredService<BleScanViewModel>();
        _navigation.NavigateTo(scanVm);
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }
}
