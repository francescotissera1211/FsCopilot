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

        // Land in the join box, and say what a sighted pilot sees at a glance: the app, your
        // code, who has the controls and whether anything is wrong.
        Opened += (_, _) =>
        {
            CodeBox.Focus();
            if (DataContext is not MainViewModel vm) return;
            var state = string.IsNullOrEmpty(vm.ErrorMessage) ? "Ready." : vm.ErrorMessage;
            Announcer.Say($"FS Copilot {vm.Version}. {vm.ClientCodeDescription}. {state} Focus is on the client code to join.");
        };
    }
}
