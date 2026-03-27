using Avalonia;
using EcoFlowMonitor;

namespace EcoFlowMonitor;

class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        const string mutexName = "EcoFlowMonitor_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var msg = $"UNHANDLED EXCEPTION: {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}";
            Console.Error.WriteLine(msg);
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoFlowMonitor", "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var msg = $"UNOBSERVED TASK EXCEPTION: {e.Exception?.GetType().Name}: {e.Exception?.Message}\n{e.Exception?.StackTrace}";
            Console.Error.WriteLine(msg);
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoFlowMonitor", "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
