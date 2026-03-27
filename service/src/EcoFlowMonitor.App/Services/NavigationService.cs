using CommunityToolkit.Mvvm.ComponentModel;

namespace EcoFlowMonitor.Services;

public class NavigationService : ObservableObject
{
    private object? _currentView;
    public object? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public void NavigateTo(object viewModel)
    {
        CurrentView = viewModel;
    }
}
