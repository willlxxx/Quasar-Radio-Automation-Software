using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace quasar_radio;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static AuthService Auth { get; } = new AuthService();

    public App()
    {
        ConfigureServices();
        ConfigureLogging();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Singleton services (lifetime de aplicación)
        services.AddSingleton<AudioEngine>();
        services.AddSingleton(sp => sp.GetRequiredService<AudioEngine>().DspProcessor);
        services.AddSingleton<CartwallEngine>();
        services.AddSingleton<CommercialScheduler>();
        services.AddSingleton<MicEngine>();
        services.AddSingleton<SmartRotationEngine>();
        services.AddSingleton<SilenceDetector>();
        services.AddSingleton<StatisticsService>();
        services.AddSingleton<RecordingService>();
        services.AddSingleton<AuxPlayerEngine>();
        services.AddSingleton<AudioDatabaseService>();

        services.AddSingleton(Auth);

        // Transient services (creados cada vez que se solicitan)
        services.AddTransient<IcecastStreamingService>();
        services.AddTransient<ThemeViewModel>();
        services.AddTransient<AudioPlayerViewModel>();
        services.AddTransient<StreamingViewModel>();
        services.AddTransient<CartWallViewModel>();
        services.AddTransient<MainViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        Log.Information("Quasar Radio se está iniciando sin login obligatorio en la apertura.");
        Log.Information("La autenticación solo se exige para el panel de administración.");

        // Crear MainWindow con DI
        Log.Information("Resolviendo MainWindow desde DI...");
        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Log.Information("Mostrando MainWindow...");
        mainWindow.Show();
        mainWindow.Activate();
    }

    private static void ConfigureLogging()
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, "quasar_radio-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        Log.Information("Serilog inicializado correctamente.");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Fatal(exception, "Excepción no controlada en la aplicación.");
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Excepción no controlada del dispatcher de WPF.");
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Excepción de tarea no observada.");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Auth.Logout();
        Log.Information("Quasar Radio cerrando");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

