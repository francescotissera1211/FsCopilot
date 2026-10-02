namespace FsCopilot;

using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Connection;
using Network;
using Simulation;
using Splat;
using ViewModels;
using Views;

public class App : Application
{
    private readonly CancellationTokenSource _appCts = new();

    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromMilliseconds(500);

    /// <summary>Set by Settings in the main window: start a fresh instance once this one has left.</summary>
    public static bool RestartRequested { get; set; }

    /// <summary>Passed to the restarted instance: the settings were just saved, skip the window.</summary>
    private const string SkipSettingsArg = "--skip-settings";
    
    public static readonly string Version =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0] ?? "unknown";
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) =>
            {
                _appCts.Cancel();

                // Before the sockets drop, so panels can tell a quit from a crash.
                Locator.Current.GetService<PanelServer>()?.Shutdown();

                Locator.Current.GetService<ShareSwitch>()?.StopAll();
                Locator.Current.GetService<ViewModels.ShareViewModel>()?.Dispose();
                Locator.Current.GetService<Audio.AtcHost>()?.Dispose();
                Locator.Current.GetService<Audio.AtcReceiver>()?.Dispose();

                var net = Locator.Current.GetService<INetwork>();
                net?.Disconnect();
                net?.DrainDisconnect(DisconnectGrace);

                Locator.Current.GetService<MasterSwitch>()?.TakeControl();
                // Closing the traffic connection removes every AI object it created.
                Locator.Current.GetService<SimTraffic>()?.Dispose();
                Locator.Current.GetService<Settings>()?.Dispose();

                if (RestartRequested) Restart(desktop.Args ?? []);
            };

            Accessibility.TextEcho.Install();
            
            var args = desktop.Args ?? [];
            var dev = args.Contains("--dev", StringComparer.OrdinalIgnoreCase);
            var skipInstall = args.Contains("--skip-install", StringComparer.OrdinalIgnoreCase);
            var skipSettings = args.Contains(SkipSettingsArg, StringComparer.OrdinalIgnoreCase);
            
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var loginVm = Locator.Current.GetService<LoginViewModel>()!;
            var login = new LoginWindow { DataContext = loginVm };

            loginVm.Completed += () =>
            {
                try
                {
                    if (!skipInstall && Installer.RequiresInstallation)
                    {
                        var vm = Locator.Current.GetService<SetupViewModel>()!;
                        var setup = new SetupWindow { DataContext = vm };

                        vm.Completed += () =>
                        {
                            CreateWindow(desktop, dev);
                            setup.Close();
                        };

                        desktop.MainWindow = setup;
                        setup.Show();
                    }
                    else
                    {
                        CreateWindow(desktop, dev);
                        _ = CheckForUpdatesAsync(Locator.Current.GetService<Updater>()!, _appCts.Token);
                    }

                    login.Close();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[App] Exception in login completion handler");
                }
            };

            if (skipSettings) loginVm.Continue();
            else desktop.MainWindow = login;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Starts a new instance with the same arguments, after this one has said goodbye on the
    /// network, so the new one is not refused as a duplicate peer ID.
    /// </summary>
    private static void Restart(string[] args)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is null) return;
            var start = new System.Diagnostics.ProcessStartInfo(path) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false };
            foreach (var a in args.Where(a => !a.Equals(SkipSettingsArg, StringComparison.OrdinalIgnoreCase))) start.ArgumentList.Add(a);
            start.ArgumentList.Add(SkipSettingsArg);
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception e)
        {
            Log.Error(e, "[App] Restart failed");
        }
    }

    private static void CreateWindow(IClassicDesktopStyleApplicationLifetime desktop, bool dev)
    {
        var window = desktop.MainWindow = !dev 
            ? new MainWindow { DataContext = Locator.Current.GetService<MainViewModel>() }
            : new DevelopWindow { DataContext = Locator.Current.GetService<DevelopViewModel>() };
        window.Show();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private async Task CheckForUpdatesAsync(Updater updater, CancellationToken ct)
    {
        var release = await updater.CheckForUpdateAsync(Version, ct);
        if (release is null)
            return;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null } desktop)
        {
            var dialog = new UpdateAvailableWindow(new UpdateAvailableViewModel(Version, release.TagName, release.HtmlUrl));

            await dialog.ShowDialog(desktop.MainWindow);
        }
    }
}