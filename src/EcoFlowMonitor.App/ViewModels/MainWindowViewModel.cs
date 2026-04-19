using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly AppConfig _config;

    [ObservableProperty]
    private object? _currentPage;

    public MainWindowViewModel(NavigationService navigation, AppConfig config)
    {
        _navigation = navigation;
        _config = config;

        _navigation.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NavigationService.CurrentView))
                CurrentPage = _navigation.CurrentView;
        };

        // Start on login or dashboard depending on config
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
