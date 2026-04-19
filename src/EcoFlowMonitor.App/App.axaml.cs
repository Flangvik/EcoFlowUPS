using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.History;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.Client.Ble;
using EcoFlowMonitor.Services;
using EcoFlowMonitor.ViewModels;
using EcoFlowMonitor.ViewModels.Automation;
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
        services.AddSingleton<IRuleFiringStore>(_ => new SqliteRuleFiringStore(dbPath));
        services.AddSingleton<IShellExecutor, ShellExecutor>();
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression   = System.Net.DecompressionMethods.All,
        }));
        services.AddTransient<HistoryViewModel>();
        services.AddSingleton<RulesListViewModel>();
        services.AddTransient<RuleEditorViewModel>();
        services.AddSingleton<RuleHistoryViewModel>();

        Services = services.BuildServiceProvider();

        var historyStore     = Services.GetRequiredService<IHistoryStore>();
        var eventStore       = Services.GetRequiredService<IEventStore>();
        var ruleFiringStore  = Services.GetRequiredService<IRuleFiringStore>();
        await historyStore.StartAsync();
        await eventStore.StartAsync();
        await ruleFiringStore.StartAsync();

        // Daily audit-log retention pruning (T020).
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var days = Math.Max(1, config.General.AuditRetentionDays);
                    await ruleFiringStore.PruneOlderThanAsync(DateTimeOffset.UtcNow - TimeSpan.FromDays(days));
                }
                catch (Exception ex) { Log.Warning(ex, "rule-firing audit prune failed"); }
                try { await Task.Delay(TimeSpan.FromHours(24)); }
                catch { break; }
            }
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Dock / Alt-Tab / window title-bar icon
            desktop.MainWindow.Icon = LoadWindowIcon("avares://EcoFlowMonitor.App/Assets/app-icon.png");

            // Setup tray icon
            SetupTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static WindowIcon? LoadWindowIcon(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "failed to load icon from {Uri}", uri);
            return null;
        }
    }

    private static Bitmap? LoadBitmap(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "failed to load bitmap from {Uri}", uri);
            return null;
        }
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        void OpenMain()
        {
            var w = desktop.MainWindow;
            if (w is null) return;
            // Show from tray even if minimised or previously hidden.
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.Topmost = true;  // briefly foreground — then drop it so it behaves like a normal window.
            w.Topmost = false;
        }

        var trayIcon = new TrayIcon
        {
            ToolTipText = "EcoFlow Monitor",
            IsVisible   = true,
            Icon        = LoadWindowIcon("avares://EcoFlowMonitor.App/Assets/tray-icon.png"),
            Menu = new NativeMenu
            {
                new NativeMenuItem("Open") { Command = new RelayCommand(OpenMain) },
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Exit") { Command = new RelayCommand(() => desktop.Shutdown()) },
            },
        };
        // Primary click (single click on Windows / macOS) opens the window.
        trayIcon.Clicked += (_, _) => OpenMain();

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
