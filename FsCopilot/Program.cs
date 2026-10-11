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
    /// <summary>
    /// The relay and STUN host. The fork speaks relay protocol v2 (see <see cref="RelayNetwork"/>),
    /// which upstream's <c>p2p.fscopilot.com</c> does not, so this is the fork's own, built from
    /// this tree (record/self-hosted-relay); <c>--relay host</c> overrides it for a local one.
    /// </summary>
    private const string RelayHost = "fscrelay.ihsan.dev";

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
        // For a harness driving this build from outside - BenchControl, or the exerciser in
        // its own repo: two instances on one machine need ids it can name before either starts.
        var peerId = Option(args, "--peer-id") ?? Random.String(8);
        var name = Environment.UserName;
        var trafficOptions = TrafficOptions.Parse(args);
        // Development switches for the network: `--relay host` to use a relay of your own (the
        // one-machine bed runs one on localhost), `--no-direct` to force every link through it.
        var relayHost = Option(args, "--relay") ?? RelayHost;
        var direct = !args.Any(a => string.Equals(a, "--no-direct", StringComparison.OrdinalIgnoreCase));
        if (relayHost != RelayHost) Log.Warning("[Application] Relay host overridden: {Host}", relayHost);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUIWithMicrosoftDependencyResolver(
                services =>
                {
                    services.AddSingleton(Settings.Load());
                    services.AddSingleton(trafficOptions);
                    var simClient = new SimClient(!isDev ? "FS Copilot" : "FS Copilot DEV");
                    services.AddSingleton(simClient);
                    services.AddSingleton(new SimTraffic(!isDev ? "FS Copilot (Traffic)" : "FS Copilot DEV (Traffic)"));
                    var panelServer = new PanelServer();
                    if (isDev)
                    {
                        panelServer.EnableDevEcho();
                        // Dev mode has no Coordinator, so wire the profile's pointer list
                        // straight through - which panels sync is profile-driven either way.
                        simClient.Aircraft
                            .Select(Definitions.Load)
                            .Subscribe(defs => panelServer.Configure(defs.Pointer));
                    }
                    services.AddSingleton(panelServer);
                    services.AddSingleton<SetupViewModel>();
                    services.AddSingleton(new Updater("http://p2p.fscopilot.com:2320"));
                    
                    if (!isDev)
                    {
                        services.AddSingleton<INetwork>(new HybridNetwork(relayHost, peerId, name, direct));
                        services.AddSingleton<MasterSwitch>();
                        services.AddSingleton<Coordinator>();
                        // Registers the sharing packets; constructed after Coordinator so the
                        // packet table is the same on every peer.
                        services.AddSingleton(sp =>
                        {
                            sp.GetRequiredService<Coordinator>();
                            return new ShareSwitch(peerId, sp.GetRequiredService<INetwork>());
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
                            return new MainViewModel(
                                peerId,
                                name,
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
