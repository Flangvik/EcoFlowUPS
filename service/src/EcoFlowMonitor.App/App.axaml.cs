using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Client.Ble;
using EcoFlowMonitor.Services;
using EcoFlowMonitor.ViewModels;
using EcoFlowMonitor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EcoFlowMonitor;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var config = ConfigManager.Load();
        Logger.Init(string.IsNullOrWhiteSpace(config.General.ErrorLogPath) ? null : config.General.ErrorLogPath);

        var services = new ServiceCollection();
        services.AddSingleton(config);
        PlatformServiceFactory.Register(services);
        services.AddSingleton<MonitorOrchestrator>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<BleScanner>();
        services.AddTransient<BleScanViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Setup tray icon
            SetupTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var trayIcon = new TrayIcon
        {
            ToolTipText = "EcoFlow Monitor",
            IsVisible = true,
            Menu = new NativeMenu
            {
                new NativeMenuItem("Open") { Command = new RelayCommand(() => desktop.MainWindow?.Show()) },
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Exit") { Command = new RelayCommand(() => desktop.Shutdown()) }
            }
        };

        // The TrayIcons need to be set on the Application
        var trayIcons = new TrayIcons { trayIcon };
        SetValue(TrayIcon.IconsProperty, trayIcons);
    }

    // Simple relay command for tray menu
    private class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged;
    }
}
