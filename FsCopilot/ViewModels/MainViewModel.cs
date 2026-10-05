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
    private string _selfId = string.Empty;
    private string? _holder;
    private string? _holderToAnnounce;
    private DateTime _holderSince;
    private string _lastSpokenError = string.Empty;
    private bool _greeted;
    private DateTime _leftAt = DateTime.MinValue;
    private string _notes = string.Empty;
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
        // Until the window has said its opening line, a problem waits to be said with it.
        if (!_greeted) return;
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
                Announcer.Say("Newer profile available.");
            this.RaiseAndSetIfChanged(ref _newProfileAvailable, value);
        }
    }

    /// <summary>Who flies the aircraft, by name: the Take Control button alone does not say.</summary>
    public string ControlStatus => _holder == _selfId ? "You have control" : $"{HolderName()} has control";

    public string ClientCodeDescription => $"Your code: {Spell(PeerId)}";

    /// <summary>The window's first words: the app, your code, and any problem there is now.</summary>
    public string OpeningLine()
    {
        _greeted = true;
        _lastSpokenError = ErrorMessage;
        var problem = string.IsNullOrEmpty(ErrorMessage) ? string.Empty : " " + ErrorMessage;
        return $"FS Copilot {Version}. {ClientCodeDescription}.{problem}";
    }

    /// <summary>The holder's name, or "Your co-pilot" until it is known (or for an older build).</summary>
    private string HolderName()
    {
        var name = _holder is null ? null : Connections.FirstOrDefault(c => c.PeerId == _holder)?.Name;
        return string.IsNullOrEmpty(name) || name == "Unknown" ? "Your co-pilot" : name;
    }

    /// <summary>
    /// Says who has control once there is a session to say it in and, for another pilot, once
    /// their name has arrived (or four seconds have passed).
    /// </summary>
    private void AnnounceHolder()
    {
        if (_holderToAnnounce is null || _holderToAnnounce != _holder) return;
        // Leaving hands you the controls; after "Left the session" that is no news.
        if (DateTime.UtcNow - _leftAt < TimeSpan.FromSeconds(3)) { _holderToAnnounce = null; return; }
        // After the arrival, not before it: wait while a pilot's joining is still unannounced.
        if (!Connected || Connections.Any(c => !c.Announced)) return;
        var other = _holder != _selfId;
        if (other && HolderName() == "Your co-pilot" && DateTime.UtcNow - _holderSince < TimeSpan.FromSeconds(4)) return;
        _holderToAnnounce = null;
        Announcer.Say(ControlStatus + ".");
    }

    /// <summary>Pilot instructions from the "notes" section of the loaded aircraft profile.</summary>
    public string Notes
    {
        get => _notes;
        private set
        {
            // A sighted pilot sees the card appear; say so, and the reader finds it under its heading.
            if (!string.IsNullOrEmpty(value) && value != _notes) Announcer.Say("Profile notes available.");
            this.RaiseAndSetIfChanged(ref _notes, value);
        }
    }

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
            .Select(defs => defs == null ? string.Empty : string.Join("\n\n", defs.Notes))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(notes => Notes = notes)
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
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isMaster => ShowTakeControl = !isMaster)
            .DisposeWith(_d);

        _selfId = masterSwitch.SelfId;
        masterSwitch.Holder
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(holder =>
            {
                _holder = holder;
                _holderSince = DateTime.UtcNow;
                _holderToAnnounce = holder;
                this.RaisePropertyChanged(nameof(ControlStatus));
                AnnounceHolder();
            })
            .DisposeWith(_d);

        JoinCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (IsBusy) return;
            var code = (ConnectionCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length != 8)
            {
                Announcer.Say("Enter an 8-character code.");
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
            _leftAt = DateTime.UtcNow;
            Announcer.Say("Left the session.");
            net.Disconnect();
            masterSwitch.TakeControl();
            // Leaving is not an outage: unlock panels instead of holding for a return.
            coordinator.EndSync();
        });

        TakeControlCommand = ReactiveCommand.Create(masterSwitch.TakeControl);

        ResetPacketLossCommand = ReactiveCommand.Create(() =>
        {
            net.ResetPacketLoss();
            Announcer.Say("Stats reset.");
        });

        CopyCodeCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (CopyRequested is { } copy) await copy(PeerId);
            Announcer.Say("Code copied.");
        });

        DownloadProfileCommand = ReactiveCommand.CreateFromTask(async ct =>
        {
            if (definitions.Value == null) return;
            var name = definitions.Value.Name;
            var cfg = await updater.Download(name, ct);
            if (cfg == null)
            {
                Announcer.Say("Profile download failed.");
                return;
            }
            var defs = Definitions.Save(name, cfg);
            definitions.OnNext(defs);
            Announcer.Say("Profile updated.");
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
                Announcer.Say($"{gone.Name} left.");
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
                Announcer.Say($"{who} joined, {(isDirect ? "direct" : "relay")}.");
            }
        }

        for (var i = 0; i < Connections.Count; i++) Connections[i].HasSeparatorAfter = i < Connections.Count - 1;
        Connected = Connections.Any();

        // Names arrive after links: the status line and a pending "has control" catch up here.
        this.RaisePropertyChanged(nameof(ControlStatus));
        AnnounceHolder();
    }

    /// <summary>A code spelled out, so a screen reader reads "Q F Q Y" rather than a word.</summary>
    internal static string Spell(string code) => string.Join(' ', code.ToCharArray());
}
