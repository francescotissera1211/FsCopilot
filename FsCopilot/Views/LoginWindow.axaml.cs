namespace FsCopilot.Views;

using Accessibility;
using Avalonia.Input;
using ViewModels;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        // Start in the first field, and say the ID: it is filled in already (made on first run, then kept).
        Opened += (_, _) =>
        {
            ServerBox.Focus();
            // After a moment: said the instant the app's first window opens, it reaches no
            // screen reader, which has not looked at the window yet.
            if (DataContext is LoginViewModel vm)
                Avalonia.Threading.DispatcherTimer.RunOnce(
                    () => Announcer.Say($"Connection settings. Your peer ID is {vm.PeerIdSpelled}."),
                    TimeSpan.FromMilliseconds(700));
        };
    }

    /// <summary>Opened from the main window: Escape closes without saving.</summary>
    public bool CloseOnEscape { get; init; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (CloseOnEscape && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
