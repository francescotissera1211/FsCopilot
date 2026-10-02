using Avalonia.Input;

namespace FsCopilot.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        // Focus the action so Enter or Space works at once, and say where the user is.
        Opened += (_, _) =>
        {
            PrimaryButton.Focus();
            if (DataContext is ViewModels.SetupViewModel vm)
                Avalonia.Threading.DispatcherTimer.RunOnce(() => Accessibility.Announcer.Say($"FS Copilot setup. {vm.Subtitle.TrimEnd('.', ' ')}. {vm.PrimaryButtonText} button."), TimeSpan.FromMilliseconds(700));
        };
    }

    private void DragArea_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}