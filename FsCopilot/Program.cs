namespace FsCopilot;

using System.Globalization;
using System.Reflection;
using Audio;
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
    // The relay and STUN host is the server saved in the login window (xray447), default
    // p2p.fscopilot.com, overridable with --server <host> or --relay <host>. This build speaks
    // xiprox's relay protocol v2, which p2p.fscopilot.com does not serve: direct links work
    // against it, relayed links need a v2 relay entered there.

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

    /// <summary>The value after <paramref name="flag"/>, or null when it is absent or last.</summary>
    private static string? Option(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        var isDev = args.Any(a => string.Equals(a, "--dev", StringComparison.OrdinalIgnoreCase));
        // var isExperimental = args.Any(a => string.Equals(a, "--experimental", StringComparison.OrdinalIgnoreCase));
        // Server, transport and identity overrides. --server <host> (Johnsmz13) and --relay <host>
        // (xiprox) both override the server saved in the login window (xray447). A bare --relay with
        // no host after it keeps Johnsmz13's meaning, relay links only; --p2p is direct links only.
        // --peer-id lets a harness name both instances of a one-machine test before they start.
        var relayIndex = Array.FindIndex(args, a => string.Equals(a, "--relay", StringComparison.OrdinalIgnoreCase));
        var relayHost = relayIndex >= 0 && relayIndex + 1 < args.Length && !args[relayIndex + 1].StartsWith("--")
            ? args[relayIndex + 1]
            : null;
        var relayNetwork = relayIndex >= 0 && relayHost is null;
        var p2pNetwork = args.Any(a => string.Equals(a, "--p2p", StringComparison.OrdinalIgnoreCase));
        var serverOverride = relayHost ?? Option(args, "--server");
        var peerIdOverride = Option(args, "--peer-id");
        var trafficOptions = TrafficOptions.Parse(args);
        // xiprox's development switch: --no-direct forces every link through the relay.
        var direct = !args.Any(a => string.Equals(a, "--no-direct", StringComparison.OrdinalIgnoreCase));
        if (serverOverride is not null) Log.Warning("[Application] Server overridden: {Host}", serverOverride);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUIWithMicrosoftDependencyResolver(
                services =>
                {
                    services.AddSingleton(Settings.Load());
                    services.AddSingleton(trafficOptions);
                    services.AddSingleton(new SimClient(!isDev ? "FS Copilot" : "FS Copilot DEV"));
                    var panelServer = new PanelServer();
                    services.AddSingleton(panelServer);
                    services.AddSingleton(new SimTraffic(!isDev ? "FS Copilot (Traffic)" : "FS Copilot DEV (Traffic)"));
                    services.AddSingleton<SetupViewModel>();
                    services.AddSingleton<LoginViewModel>();
                    services.AddSingleton(new Updater("http://p2p.fscopilot.com:2320"));

                    if (!isDev)
                    {
                        services.AddSingleton<INetwork>(sp =>
                        {
                            var cfg = ConnectionConfig.Load();
                            var host = serverOverride ?? cfg.ServerAddress;
                            var peerId = peerIdOverride ?? cfg.PeerId;
                            if (p2pNetwork) return new P2PNetwork(host, peerId, cfg.Username);
                            if (relayNetwork) return new RelayNetwork(host, peerId, cfg.Username);
                            return new HybridNetwork(host, peerId, cfg.Username, direct);
                        });
                        services.AddSingleton<MasterSwitch>();
                        services.AddSingleton<Coordinator>();
                        // Registers the sharing packets; constructed after Coordinator so the
                        // packet table is the same on every peer.
                        services.AddSingleton(sp =>
                        {
                            sp.GetRequiredService<Coordinator>();
                            return new ShareSwitch(peerIdOverride ?? ConnectionConfig.Load().PeerId,
                                sp.GetRequiredService<INetwork>());
                        });
                        services.AddSingleton<TrafficReceiver>();
                        services.AddSingleton<TrafficHost>();
                        services.AddSingleton<AtcHost>();
                        services.AddSingleton<AtcReceiver>();
                        services.AddSingleton<ShareViewModel>();
                        services.AddSingleton(sp =>
                        {
                            sp.GetRequiredService<ShareSwitch>();
                            sp.GetRequiredService<TrafficHost>();
                            var cfg = ConnectionConfig.Load();
                            return new MainViewModel(
                                peerIdOverride ?? cfg.PeerId,
                                cfg.Username,
                                sp.GetRequiredService<INetwork>(),
                                sp.GetRequiredService<SimClient>(),
                                sp.GetRequiredService<MasterSwitch>(),
                                sp.GetRequiredService<Coordinator>(),
                                sp.GetRequiredService<Updater>(),
                                sp.GetRequiredService<PanelServer>(),
                                sp.GetRequiredService<ShareViewModel>()
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
