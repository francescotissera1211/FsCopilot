namespace FsCopilot.Views;

using Accessibility;
using ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.CopyRequested += code => Clipboard?.SetTextAsync(code) ?? Task.CompletedTask;
        };

        // Land in the join box and say the app and your code. Problems are not summed up here:
        // the simulator checks report a quarter second later and speak for themselves.
        Opened += (_, _) =>
        {
            CodeBox.Focus();
            // After a moment, for when this is the first window (a restart from Settings): said
            // at once, no screen reader has looked at the window yet to hear it.
            if (DataContext is MainViewModel vm)
                Avalonia.Threading.DispatcherTimer.RunOnce(
                    () => Announcer.Say(vm.OpeningLine()), TimeSpan.FromMilliseconds(700));
        };
    }

    /// <summary>
    /// Connection settings again. Server, ID and connection type are fixed when the network
    /// starts, so saving restarts FS Copilot (which leaves any session); Escape cancels.
    /// </summary>
    private async void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var vm = new LoginViewModel { SaveLabel = "Save and restart" };
        var dialog = new LoginWindow { DataContext = vm, CloseOnEscape = true };
        vm.Completed += () =>
        {
            App.RestartRequested = true;
            Announcer.Say("Saved. Restarting.");
            dialog.Close();
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };
        await dialog.ShowDialog(this);
    }
}
