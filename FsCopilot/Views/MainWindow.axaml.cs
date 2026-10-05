namespace FsCopilot.Views;

using Accessibility;
using ViewModels;

public partial class MainWindow : Window
{
    private const double RowSpacing = 10;

    private readonly double _baseHeight;
    private readonly Border _notesCard;

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

        _baseHeight = Height;

        // Increase window height when profile notes are visible (SamiSaleh98)
        _notesCard = this.FindControl<Border>("NotesCard")!;
        _notesCard.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty || e.Property == IsVisibleProperty) UpdateHeight();
        };
    }

    private void UpdateHeight() =>
        Height = _baseHeight + (_notesCard.IsVisible ? _notesCard.Bounds.Height + RowSpacing : 0);

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
