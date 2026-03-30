using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.History;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Client.Ble;
using EcoFlowMonitor.Services;
using EcoFlowMonitor.ViewModels;
using EcoFlowMonitor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace EcoFlowMonitor;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var config = ConfigManager.Load();

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EcoFlowMonitor");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "history.db");

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EcoFlowMonitor", "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("MQTTnet", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 3,
                rollOnFileSizeLimit: true,
                buffered: true,
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging(lb => lb.ClearProviders().AddSerilog(dispose: true));
        PlatformServiceFactory.Register(services);
        services.AddSingleton<MonitorOrchestrator>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<BleScanner>();
        services.AddTransient<BleScanViewModel>();
        services.AddSingleton<IHistoryStore>(_ => new SqliteHistoryStore(dbPath));
        services.AddSingleton<IEventStore>(_ => new SqliteEventStore(dbPath));
        services.AddTransient<HistoryViewModel>();

        Services = services.BuildServiceProvider();

        var historyStore = Services.GetRequiredService<IHistoryStore>();
        var eventStore   = Services.GetRequiredService<IEventStore>();
        await historyStore.StartAsync();
        await eventStore.StartAsync();

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
