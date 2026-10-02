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
            if (DataContext is MainViewModel vm) Announcer.Say($"FS Copilot {vm.Version}. {vm.ClientCodeDescription}.");
        };
    }
}
