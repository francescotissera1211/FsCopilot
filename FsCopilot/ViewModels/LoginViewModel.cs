namespace FsCopilot.ViewModels;

using System.Reactive;
using Accessibility;
using ReactiveUI;

public sealed class LoginViewModel : ReactiveObject
{
    private string _serverAddress;
    private string _username;
    private string _peerId;
    private ConnectionModeOption _connectionMode;
    private bool _shareGroundVehicles;
    private string _errorMessage = string.Empty;

    public event Action? Completed;

    public sealed record ConnectionModeOption(string Mode, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<ConnectionModeOption> ConnectionModeOptions { get; } =
    [
        new(ConnectionModes.Automatic, "Automatic"),
        new(ConnectionModes.Direct, "Direct only"),
        new(ConnectionModes.Relay, "Relay only")
    ];

    public string ServerAddress
    {
        get => _serverAddress;
        set => this.RaiseAndSetIfChanged(ref _serverAddress, value);
    }

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string PeerId
    {
        get => _peerId;
        set => this.RaiseAndSetIfChanged(ref _peerId, value);
    }

    public ConnectionModeOption ConnectionMode
    {
        get => _connectionMode;
        set => this.RaiseAndSetIfChanged(ref _connectionMode, value);
    }

    public bool ShareGroundVehicles
    {
        get => _shareGroundVehicles;
        set => this.RaiseAndSetIfChanged(ref _shareGroundVehicles, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string ConfigPath => ConnectionConfig.ConfigPath;

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

    public LoginViewModel()
    {
        var saved = ConnectionConfig.Load();
        _serverAddress = saved.ServerAddress;
        _username = saved.Username;
        _peerId = saved.PeerId;
        _shareGroundVehicles = saved.ShareGroundVehicles;
        var mode = ConnectionModes.Normalize(saved.ConnectionMode);
        _connectionMode = ConnectionModeOptions.First(o => o.Mode == mode);

        // Always enabled: a disabled button gives a screen reader user no reason. The command
        // checks the fields and says what is wrong instead.
        ConnectCommand = ReactiveCommand.Create(ExecuteConnect);
    }

    /// <summary>The first problem with the fields, or null when they can be saved.</summary>
    private string? Validate(string server, string name, string peerId)
    {
        if (string.IsNullOrWhiteSpace(server))
            return "Server address is empty.";
        if (server.Any(char.IsWhiteSpace) || server.Contains("://"))
            return "Server address: host name only, like p2p.fscopilot.com.";
        if (string.IsNullOrWhiteSpace(name))
            return "Username is empty.";
        // Join takes exactly 8 characters, so a shorter ID could never be joined.
        if (peerId.Length != 8 || !peerId.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            return "Peer ID must be 8 letters or digits.";
        return null;
    }

    private void ExecuteConnect()
    {
        var server = (ServerAddress ?? string.Empty).Trim();
        var name = (Username ?? string.Empty).Trim();
        var peerId = (PeerId ?? string.Empty).Trim().ToUpperInvariant();

        var problem = Validate(server, name, peerId);
        if (problem is not null)
        {
            ErrorMessage = problem;
            Announcer.Say(problem);
            return;
        }

        var saved = ConnectionConfig.Load();
        saved.ServerAddress = server;
        saved.Username = name;
        saved.PeerId = peerId;
        saved.ConnectionMode = ConnectionMode.Mode;
        saved.ShareGroundVehicles = ShareGroundVehicles;

        if (!saved.Save())
        {
            ErrorMessage = $"Could not save settings to {ConnectionConfig.ConfigPath}.";
            Announcer.Say(ErrorMessage);
            return;
        }

        ErrorMessage = string.Empty;
        Completed?.Invoke();
    }
}
