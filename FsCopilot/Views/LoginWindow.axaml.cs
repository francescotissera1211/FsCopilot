namespace FsCopilot.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        // Start in the first field so a screen reader lands on something useful.
        Opened += (_, _) => ServerBox.Focus();
    }
}
