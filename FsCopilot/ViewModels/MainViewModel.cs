namespace FsCopilot.ViewModels;

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using Accessibility;
using Connection;
using Network;
using ReactiveUI;
using Simulation;

public class MainViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _d = new();

    private string _aircraft = string.Empty;
    private string _connectionCode = string.Empty;
    private bool _isBusy;
    private bool _connected;
    private bool _showTakeControl;
    private bool _newProfileAvailable;
    private bool _isMaster = true;
    private string _lastSpokenError = string.Empty;
    private DateTime _leftAt = DateTime.MinValue;
    private ViewErrors _errors = ViewErrors.None;

    private string Aircraft
    {
        set
        {
            _aircraft = value;
            this.RaisePropertyChanged(nameof(ErrorMessage));
        }
    }

    private ViewErrors Errors
    {
        get => _errors;
        set
        {
            _errors = value;
            this.RaisePropertyChanged(nameof(ErrorMessage));
            SpeakError();
        }
    }

    /// <summary>A new problem is spoken once; the same one is not repeated while it lasts.</summary>
    private void SpeakError()
    {
        var message = ErrorMessage;
        if (message == _lastSpokenError) return;
        _lastSpokenError = message;
        if (!string.IsNullOrEmpty(message)) Announcer.Say(message);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool Connected
    {
        get => _connected;
        set => this.RaiseAndSetIfChanged(ref _connected, value);
    }

    public bool ShowTakeControl
    {
        get => _showTakeControl;
        set => this.RaiseAndSetIfChanged(ref _showTakeControl, value);
    }

    public string ConnectionCode
    {
        get => _connectionCode;
        set => this.RaiseAndSetIfChanged(ref _connectionCode, value);
    }

    public bool NewProfileAvailable
    {
        get => _newProfileAvailable;
        set
        {
            if (value && !_newProfileAvailable)
                Announcer.Say("A newer profile for this aircraft is available. Use the Download profile button.");
            this.RaiseAndSetIfChanged(ref _newProfileAvailable, value);
        }
    }

    /// <summary>Who flies the aircraft, in words: the Take Control button alone does not say.</summary>
    public string ControlStatus => _isMaster ? "You have the controls" : "Your co-pilot has the controls";

    public string ClientCodeDescription => $"Your client code: {Spell(PeerId)}";

    public string PeerId { get; init; }
    public string ClientName { get; init; }

    public string Version => App.Version;
    public string ErrorMessage =>
        _errors.HasFlag(ViewErrors.Failed) ? "Failed to connect." :
        _errors.HasFlag(ViewErrors.NotRunning) ? "Microsoft Flight Simulator is not running!" :
        _errors.HasFlag(ViewErrors.NotLoadedBridge) ? "Bridge package is not loaded. Check your Community folder." :
        _errors.HasFlag(ViewErrors.BridgeMismatch) ? "Bridge version mismatch. Update Community package." :
        _errors.HasFlag(ViewErrors.NotSupported) ? $"No profile available for the {_aircraft}." :
        _errors.HasFlag(ViewErrors.Rejected) ? "Both sides must use the same FS Copilot version." :
        _errors.HasFlag(ViewErrors.Conflict) ? "Conflict detected with YourControls package." :
        _errors.HasFlag(ViewErrors.PanelChannel) ? "Panel channel unavailable - ports 9020-9024 are in use." :
        string.Empty;

    public ObservableCollection<ConnectionItem> Connections { get; set; } = [];
    public ReactiveCommand<Unit, Unit> CopyCodeCommand { get; }

    /// <summary>Raised by <see cref="CopyCodeCommand"/>; the window owns the clipboard.</summary>
    public event Func<string, Task>? CopyRequested;
    public ShareViewModel Share { get; }
    public ReactiveCommand<Unit, Unit> JoinCommand { get; }
    public ReactiveCommand<Unit, Unit> LeaveCommand { get; }
    public ReactiveCommand<Unit, Unit> TakeControlCommand { get; }
    public ReactiveCommand<Unit, Unit> DownloadProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetPacketLossCommand { get; }

    public MainViewModel(string peerId,
        string name,
        INetwork net,
        SimClient sim,
        MasterSwitch masterSwitch,
        Coordinator coordinator,
        Updater updater,
        PanelServer panels,
        ShareViewModel share)
    {
        ClientName = name;
        PeerId = peerId;
        Share = share;

        sim.Aircraft
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(aircraft => Aircraft = aircraft)
            .DisposeWith(_d);

        var definitions = new BehaviorSubject<Definitions?>(null);
       sim.Aircraft
            .Select(Definitions.Load)
            .Subscribe(defs => definitions.OnNext(defs))
            .DisposeWith(_d);

        definitions.Subscribe(defs => Errors = defs is { Count: 0 }
                ? _errors | ViewErrors.NotSupported
                : _errors & ~ViewErrors.NotSupported).DisposeWith(_d);

        definitions
            .Where(defs => defs != null)
            .Subscribe(defs => coordinator.Load(defs!))
            .DisposeWith(_d);

        definitions
            .Where(defs => defs != null)
            .SelectMany(defs => Observable.FromAsync(ct => updater.Check(defs!.Name, ct))
                .Select(updatedAt => updatedAt != null && updatedAt > defs!.UpdatedAt))
            .Subscribe(updateAvailable => NewProfileAvailable =  updateAvailable).DisposeWith(_d);

        sim.Connected
            .Sample(TimeSpan.FromMilliseconds(250))
            .Select(connected => !connected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(notRunning => Errors = notRunning
                ? _errors | ViewErrors.NotRunning
                : _errors & ~ViewErrors.NotRunning)
            .DisposeWith(_d);

        sim.WasmReady
            .Sample(TimeSpan.FromMilliseconds(250))
            .Select(connected => !connected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(notLoaded => Errors = notLoaded
                ? _errors | ViewErrors.NotLoadedBridge
                : _errors & ~ViewErrors.NotLoadedBridge)
            .DisposeWith(_d);

        sim.WasmVersionMismatch
            .Sample(TimeSpan.FromMilliseconds(250))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(notLoaded => Errors = notLoaded
                ? _errors | ViewErrors.BridgeMismatch
                : _errors & ~ViewErrors.BridgeMismatch)
            .DisposeWith(_d);

        sim.Conflict
            .Sample(TimeSpan.FromMilliseconds(250))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(conflict => Errors = conflict
                ? _errors | ViewErrors.Conflict
                : _errors & ~ViewErrors.Conflict)
            .DisposeWith(_d);

        panels.BindFailed
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(failed => Errors = failed
                ? _errors | ViewErrors.PanelChannel
                : _errors & ~ViewErrors.PanelChannel)
            .DisposeWith(_d);

        // A handshake may still fail, so it is not shown, counted or announced.
        var connectedPeers = net.Peers
            .Select(peers => (ICollection<Peer>)peers.Where(p => p.Connected).ToArray());

        connectedPeers
            .Sample(TimeSpan.FromMilliseconds(250))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateConnections)
            .DisposeWith(_d);

        connectedPeers
            .Select(p => p.Count)
            .DistinctUntilChanged()
            .Scan(
                seed: (Prev: 0, Curr: 0),
                accumulator: (state, curr) => (Prev: state.Curr, Curr: curr))
            .Subscribe(x =>
            {
                if (x.Curr > x.Prev) UiSounds.Play("connect");
                else if (x.Curr < x.Prev) UiSounds.Play("disconnect");
            })
            .DisposeWith(_d);

        masterSwitch.Master
            .Sample(TimeSpan.FromMilliseconds(250))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isMaster =>
            {
                ShowTakeControl = !isMaster;
                var changed = isMaster != _isMaster;
                _isMaster = isMaster;
                this.RaisePropertyChanged(nameof(ControlStatus));
                if (changed && Connected) Announcer.Say(ControlStatus + ".");
            })
            .DisposeWith(_d);

        JoinCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (IsBusy) return;
            var code = (ConnectionCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length != 8)
            {
                Announcer.Say("Enter the 8-character client code of the pilot you want to join.");
                return;
            }
            ConnectionCode = code;
            Announcer.Say($"Joining {Spell(code)}.");

            Errors &= ~ViewErrors.Failed;
            IsBusy = true;
            ConnectionResult result;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

                masterSwitch.Join();
                result = await net.Connect(code, cts.Token);
            }
            finally
            {
                IsBusy = false;;
            }

            // Join() made us slave before the attempt.
            if (result != ConnectionResult.Success) masterSwitch.TakeControl();

            if (result == ConnectionResult.Success)
                Announcer.Say($"Joined {Spell(code)}.");
            if (result == ConnectionResult.Failed)
            {
                Errors |= ViewErrors.Failed;
                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => Errors &= ~ViewErrors.Failed);
            }
            else if (result == ConnectionResult.Rejected)
            {
                Errors |= ViewErrors.Rejected;
                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => Errors &= ~ViewErrors.Rejected);
            }
        });

        LeaveCommand = ReactiveCommand.Create(() =>
        {
            net.Disconnect();
            masterSwitch.TakeControl();
            // Leaving is not an outage: unlock panels instead of holding for a return.
            coordinator.EndSync();
            _leftAt = DateTime.UtcNow;
            Announcer.Say("You left the session.");
        });

        TakeControlCommand = ReactiveCommand.Create(masterSwitch.TakeControl);

        ResetPacketLossCommand = ReactiveCommand.Create(() =>
        {
            net.ResetPacketLoss();
            Announcer.Say("Packet loss statistics reset.");
        });

        CopyCodeCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (CopyRequested is { } copy) await copy(PeerId);
            Announcer.Say($"Client code {Spell(PeerId)} copied to the clipboard.");
        });

        DownloadProfileCommand = ReactiveCommand.CreateFromTask(async ct =>
        {
            if (definitions.Value == null) return;
            var name = definitions.Value.Name;
            var cfg = await updater.Download(name, ct);
            if (cfg == null)
            {
                Announcer.Say("The profile could not be downloaded. The profile server may be unreachable.");
                return;
            }
            var defs = Definitions.Save(name, cfg);
            definitions.OnNext(defs);
            Announcer.Say($"Profile for {name} updated.");
        });
    }

    public void Dispose() => _d.Dispose();

    [Flags]
    private enum ViewErrors : byte
    {
        None            = 0b_0000_0000,
        Failed          = 0b_0000_0001,
        NotRunning      = 0b_0000_0010,
        NotSupported    = 0b_0000_0100,
        Rejected        = 0b_0000_1000,
        Conflict        = 0b_0001_0000,
        NotLoadedBridge = 0b_0010_0000,
        BridgeMismatch  = 0b_0100_0000,
        PanelChannel    = 0b_1000_0000
    }

    /// <summary>
    /// Rows are updated in place rather than rebuilt every quarter second, so a screen reader
    /// reading the list does not lose its place while ping and loss change.
    /// </summary>
    private void UpdateConnections(ICollection<Peer> peers)
    {
        var current = peers.Select(p => p.PeerId).ToHashSet();

        foreach (var gone in Connections.Where(c => !current.Contains(c.PeerId)).ToArray())
        {
            Connections.Remove(gone);
            // After our own Leave, "you left" has been said; every peer going is no news.
            if (gone.Announced && DateTime.UtcNow - _leftAt > TimeSpan.FromSeconds(3))
                Announcer.Say($"{gone.Name} left the session.");
        }

        foreach (var peer in peers)
        {
            var name = string.IsNullOrWhiteSpace(peer.Name) ? "Unknown" : peer.Name;
            var isDirect = peer.Transport == Peer.TransportKind.Direct;
            var item = Connections.FirstOrDefault(c => c.PeerId == peer.PeerId);
            if (item is null)
            {
                item = new ConnectionItem(peer.PeerId);
                Connections.Add(item);
            }
            item.Update(name, peer.Ping, peer.PacketLoss, isDirect);

            // A peer's name arrives a moment after its link; announce once it is known, or
            // after a few seconds by code, rather than "Unknown joined".
            if (!item.Announced && (name != "Unknown" || DateTime.UtcNow - item.FirstSeen > TimeSpan.FromSeconds(4)))
            {
                item.Announced = true;
                var who = name != "Unknown" ? name : $"Pilot {Spell(peer.PeerId)}";
                Announcer.Say($"{who} joined the session, {(isDirect ? "direct link" : "through the relay")}.");
            }
        }

        for (var i = 0; i < Connections.Count; i++) Connections[i].HasSeparatorAfter = i < Connections.Count - 1;
        Connected = Connections.Any();
    }

    /// <summary>A code spelled out, so a screen reader reads "Q F Q Y" rather than a word.</summary>
    internal static string Spell(string code) => string.Join(' ', code.ToCharArray());
}
