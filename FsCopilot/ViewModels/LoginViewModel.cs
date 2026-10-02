namespace FsCopilot.ViewModels;

using System.Reactive;
using ReactiveUI;

public sealed class LoginViewModel : ReactiveObject
{
    private string _serverAddress;
    private string _username;
    private string _peerId;
    private string _errorMessage = string.Empty;

    public event Action? Completed;

    public string ServerAddress
    {
        get => _serverAddress;
        set
        {
            this.RaiseAndSetIfChanged(ref _serverAddress, value);
            this.RaisePropertyChanged(nameof(CanConnect));
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            this.RaiseAndSetIfChanged(ref _username, value);
            this.RaisePropertyChanged(nameof(CanConnect));
        }
    }

    public string PeerId
    {
        get => _peerId;
        set
        {
            this.RaiseAndSetIfChanged(ref _peerId, value);
            this.RaisePropertyChanged(nameof(CanConnect));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool CanConnect =>
        !string.IsNullOrWhiteSpace(ServerAddress) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(PeerId) &&
        PeerId.Length >= 4;

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

    public LoginViewModel()
    {
        var saved = ConnectionConfig.Load();
        _serverAddress = saved.ServerAddress;
        _username = saved.Username;
        _peerId = saved.PeerId;

        var canExecute = this.WhenAnyValue(
            x => x.CanConnect);

        ConnectCommand = ReactiveCommand.Create(
            ExecuteConnect,
            canExecute);
    }

    private void ExecuteConnect()
    {
        if (!CanConnect) return;

        var saved = ConnectionConfig.Load();
        saved.ServerAddress = ServerAddress.Trim();
        saved.Username = Username.Trim();
        saved.PeerId = PeerId.Trim().ToUpperInvariant();
        if (saved.PeerId.Length > 8)
            saved.PeerId = saved.PeerId[..8];

        saved.Save();
        Completed?.Invoke();
    }
}