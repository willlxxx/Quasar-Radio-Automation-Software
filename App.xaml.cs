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

        // ViewModels de modulo: singletons. La consola y las ventanas de modulo
        // (UI-2) tienen que ver el MISMO estado; si son transient, cada ventana
        // montaria su propia copia del playlist y del stream.
        services.AddSingleton<ThemeViewModel>();
        services.AddSingleton<AudioPlayerViewModel>();
        services.AddSingleton<StreamingViewModel>();
        services.AddSingleton<CartWallViewModel>();
        services.AddSingleton(sp => new LibraryViewModel(
            sp.GetRequiredService<AudioDatabaseService>(),
            sp.GetRequiredService<SmartRotationEngine>(),
            sp.GetRequiredService<AudioPlayerViewModel>().Playlist,
            sp.GetRequiredService<IModuleNavigator>()));

        // Navegacion de modulos (UI-2): una sola ventana por modulo, no modal,
        // con la geometria recordada en %APPDATA%\QuasarRadio\layout.json.
        services.AddSingleton<IModuleNavigator>(sp => new ModuleNavigator(sp));

        // Las ventanas de modulo son transient: una vez cerradas WPF no permite
        // volver a mostrarlas, asi que el navigator pide una nueva en cada Open.
        services.AddTransient<LibraryManagementWindow>();
        services.AddTransient<ScheduleWindow>();
        services.AddTransient<DspWindow>();
        services.AddTransient<StreamingWindow>();
        services.AddTransient<ReportsWindow>();

        // Unico MainViewModel vivo: MainWindow es singleton, asi que este tambien.
        services.AddSingleton<MainViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        ThemeService.RestoreSaved();
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

