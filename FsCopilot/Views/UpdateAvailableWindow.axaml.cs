namespace FsCopilot.Views;

using ViewModels;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(UpdateAvailableViewModel vm)
    {
        InitializeComponent();

        DataContext = vm;

        vm.CloseRequested += () => Close();

        // The versions sit in a grid a screen reader only finds by exploring; put them where
        // focus lands, on the button that acts on them.
        Avalonia.Automation.AutomationProperties.SetHelpText(OpenButton,
            $"Version {vm.LatestVersion} is available, you have version {vm.CurrentVersion}. " +
            "Opens the release page in your browser. Escape closes this message.");
        Opened += (_, _) => OpenButton.Focus();
    }
}
