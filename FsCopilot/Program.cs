namespace FsCopilot;

using System.Globalization;
using System.Reflection;
using Connection;
using Microsoft.Extensions.DependencyInjection;
using Network;
using ReactiveUI.Avalonia;
using ReactiveUI.Avalonia.Splat;
using Serilog;
using Serilog.Events;
using Simulation;
using ViewModels;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var isDev = args.Any(a => string.Equals(a, "--dev", StringComparison.OrdinalIgnoreCase));
        var isDebug = args.Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0] ?? "unknown";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                path: "log",
                rollingInterval: RollingInterval.Infinite,
                rollOnFileSizeLimit: false,
                shared: true,
                retainedFileCountLimit: null,
                fileSizeLimitBytes: null,
                outputTemplate: "[{Timestamp:HH:mm:ss.fff}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: isDev || isDebug ? LogEventLevel.Verbose : LogEventLevel.Debug
            )
            .CreateLogger();

        try
        {
            Log.Information("[Application] Loaded {Version} version", version);
            BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Application] Something went wrong");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp([]);

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        var isDev = args.Any(a => string.Equals(a, "--dev", StringComparison.OrdinalIgnoreCase));
        // var isExperimental = args.Any(a => string.Equals(a, "--experimental", StringComparison.OrdinalIgnoreCase));
        // Transport and server overrides (Johnsmz13): --p2p forces direct links only, --relay forces
        // relay only, --server <host> overrides the server saved in the login window (xray447).
        var relayNetwork = args.Any(a => string.Equals(a, "--relay", StringComparison.OrdinalIgnoreCase));
        var p2pNetwork = args.Any(a => string.Equals(a, "--p2p", StringComparison.OrdinalIgnoreCase));
        string? serverOverride = null;
        var serverIndex = Array.FindIndex(args, a =>
                string.Equals(a, "--server", StringComparison.OrdinalIgnoreCase));
        if (serverIndex >= 0 && serverIndex + 1 < args.Length)
        {
            serverOverride = args[serverIndex + 1];
        }

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUIWithMicrosoftDependencyResolver(
                services =>
                {
                    services.AddSingleton(new SimClient(!isDev ? "FS Copilot" : "FS Copilot DEV"));
                    services.AddSingleton<SetupViewModel>();
                    services.AddSingleton<LoginViewModel>();
                    services.AddSingleton(new Updater("http://p2p.fscopilot.com:2320"));

                    if (!isDev)
                    {
                        services.AddSingleton<INetwork>(sp =>
                        {
                            var cfg = ConnectionConfig.Load();
                            var host = serverOverride ?? cfg.ServerAddress;
                            if (p2pNetwork) return new P2PNetwork(host, cfg.PeerId, cfg.Username);
                            if (relayNetwork) return new RelayNetwork(host, cfg.PeerId, cfg.Username);
                            return new HybridNetwork(host, cfg.PeerId, cfg.Username);
                        });
                        services.AddSingleton<MasterSwitch>();
                        services.AddSingleton<Coordinator>();
                        services.AddSingleton(sp =>
                        {
                            var cfg = ConnectionConfig.Load();
                            return new MainViewModel(
                                cfg.PeerId,
                                cfg.Username,
                                sp.GetRequiredService<INetwork>(),
                                sp.GetRequiredService<SimClient>(),
                                sp.GetRequiredService<MasterSwitch>(),
                                sp.GetRequiredService<Coordinator>(),
                                sp.GetRequiredService<Updater>()
                            );
                        });
                    }
                    else
                    {
                        services.AddSingleton<DevelopViewModel>();
                    }
                },
                null)
            .RegisterReactiveUIViewsFromEntryAssembly()
            .WithInterFont()
            .LogToTrace();
    }
}
