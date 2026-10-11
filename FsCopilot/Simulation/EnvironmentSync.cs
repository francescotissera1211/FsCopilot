namespace FsCopilot.Simulation;

using Connection;
using Network;

/// <summary>
/// Keeps the free-flight environment (zulu date/time, pause, sim rate) in parity between clients.
/// The session creator (the client that did not press Join) is authoritative for date and time.
/// Pause (including the Esc menu) is synced in both directions. Sim rate is locked to 1x on every client
/// while connected. <see cref="SessionReady"/> drives the initial sync of everything on load-in.
/// </summary>
public class EnvironmentSync : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxDrift = TimeSpan.FromSeconds(30);
    // Right after load-in the clocks are matched closely, not just within the periodic tolerance.
    private static readonly TimeSpan InitialMaxDrift = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VerifyDelay = TimeSpan.FromSeconds(3);
    // The sim flips pause flags while an aircraft loads; don't sync those.
    private static readonly TimeSpan LoadSettle = TimeSpan.FromSeconds(15);
    // Re-announce readiness a few times: the other side may still be loading the first time.
    private static readonly TimeSpan[] ReadyBursts =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45)];
    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(3);
    private const int MaxSilentCorrections = 3;

    // Pause_EX1 flags
    private const uint PauseFull = 1;
    private const uint PauseActive = 4;
    private const uint PauseMenu = 8;

    private readonly SimClient _sim;
    private readonly INetwork _net;
    private readonly CompositeDisposable _d = new();
    private readonly ReplaySubject<SyncLogEntry> _log = new(50);
    private readonly Lock _lock = new();

    private volatile bool _isHost = true;
    private volatile bool _leaving;
    private volatile bool _hasAircraft;
    private volatile int _peerCount;

    private double? _zuluSeconds;
    private double? _dayOfYear;
    private double? _year;
    private double _simRate = 1;
    private bool _paused;

    private bool? _pauseEcho;
    private DateTime _pauseEchoAt;
    private DateTime _aircraftLoadedAt = DateTime.MinValue;
    private PauseState? _lastRemotePause;
    private bool _pauseIgnoredLogged;
    private IDisposable? _pendingCorrection;
    private int _consecutiveCorrections;
    private bool _initialTimePending;
    // The bridge only answers once a cockpit is running, so one miss during load-in doesn't rule it out.
    private int _bridgeTimeFailures;
    private const int MaxBridgeTimeFailures = 2;
    private bool _rateLogged;
    private readonly Subject<Unit> _sessionReady = new();

    public IObservable<SyncLogEntry> Log => _log;
    public bool IsHost => _isHost;
    public bool HasAircraft => _hasAircraft;
    public int PeerCount => _peerCount;
    public IObservable<int> PeersChanged { get; }

    /// <summary>
    /// Fires a few times after a peer connects or an aircraft loads while connected. Every sync component sends
    /// (host) or requests (joiner) its full state on it, so load-in gets everything without waiting for the
    /// periodic checks. Handlers must be idempotent.
    /// </summary>
    public IObservable<Unit> SessionReady => _sessionReady;

    public EnvironmentSync(SimClient sim, INetwork net)
    {
        _sim = sim;
        _net = net;

        net.RegisterPacket<TimeState, TimeState.Codec>();
        net.RegisterPacket<TimeRequest, TimeRequest.Codec>();
        net.RegisterPacket<PauseState, PauseState.Codec>();

        _d.Add(sim.Stream("A:ZULU TIME", "Seconds").Subscribe(v => _zuluSeconds = Convert.ToDouble(v)));
        _d.Add(sim.Stream("A:ZULU DAY OF YEAR", "Number").Subscribe(v => _dayOfYear = Convert.ToDouble(v)));
        _d.Add(sim.Stream("A:ZULU YEAR", "Number").Subscribe(v => _year = Convert.ToDouble(v)));
        _d.Add(sim.Connected.Where(c => !c).Subscribe(_ =>
        {
            _hasAircraft = false;
            _zuluSeconds = _dayOfYear = _year = null;
        }));

        _d.Add(sim.Aircraft.Subscribe(_ =>
        {
            _hasAircraft = true;
            _aircraftLoadedAt = DateTime.UtcNow;
            if (_peerCount > 0) AnnounceReady("aircraft loaded");
        }));

        var peersChanged = new Subject<int>();
        PeersChanged = peersChanged;
        _d.Add(net.Peers
            .Select(p => p.Count)
            .DistinctUntilChanged()
            .Scan((Prev: 0, Curr: 0), (s, c) => (s.Curr, c))
            .Subscribe(x =>
            {
                OnPeersChanged(x.Prev, x.Curr);
                peersChanged.OnNext(x.Curr);
            }));

        // Host: periodic parity broadcast. Joiners compare against their own clock on receipt.
        _d.Add(Observable.Interval(CheckInterval)
            .Where(_ => _isHost && _peerCount > 0)
            .Subscribe(_ => BroadcastTime()));

        _d.Add(net.Stream<TimeRequest>()
            .Where(_ => _isHost)
            .Subscribe(_ => BroadcastTime()));

        _d.Add(SessionReady.Subscribe(_ =>
        {
            if (_isHost)
            {
                BroadcastTime();
                if (_paused) SendPause(true, PauseReason.Pause);
            }
            else
            {
                lock (_lock) _initialTimePending = true;
                RequestSync("load-in");
            }
        }));

        _d.Add(net.Stream<TimeState>()
            .Where(_ => !_isHost)
            .Subscribe(OnTimeState));

        _d.Add(sim.SystemEvent("Pause_EX1").Subscribe(OnLocalPause));
        _d.Add(net.Stream<PauseState>().Subscribe(OnRemotePause));

        // One step per tick so a stale rate reading can't overshoot (e.g. 2x -> 0.5x -> 2x).
        _d.Add(sim.Stream("A:SIMULATION RATE", "Number").Subscribe(v => _simRate = Convert.ToDouble(v)));
        _d.Add(Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ => EnforceSimRate()));
    }

    public void Dispose()
    {
        _d.Dispose();
        _pendingCorrection?.Dispose();
    }

    private void AnnounceReady(string reason)
    {
        Serilog.Log.Information("[EnvSync] Session ready ({Reason}), syncing state", reason);
        foreach (var delay in ReadyBursts)
            _d.Add(Observable.Timer(delay).Where(_ => _peerCount > 0).Subscribe(_ => _sessionReady.OnNext(Unit.Default)));
    }

    /// <summary>Called when this client joins someone else's session; the other side becomes authoritative.</summary>
    public void Join()
    {
        _isHost = false;
        _leaving = false;
    }

    /// <summary>Called when the join attempt failed or the user pressed Leave.</summary>
    public void Leave()
    {
        _leaving = true;
        _isHost = true;
        lock (_lock)
        {
            _pendingCorrection?.Dispose();
            _pendingCorrection = null;
            _consecutiveCorrections = 0;
        }
    }

    private void OnPeersChanged(int prev, int curr)
    {
        _peerCount = curr;

        if (curr > prev)
        {
            _leaving = false;
            if (_isHost) Write(SyncLevel.Info, "Peer connected, sending sim state to clients");
            else Write(SyncLevel.Info, "Connected, requesting sim state from host");
            AnnounceReady("peer connected");
        }
        else if (curr == 0 && prev > 0)
        {
            if (_leaving)
            {
                Write(SyncLevel.Info, "Left the session");
                return;
            }

            if (_hasAircraft && !_paused)
            {
                ApplyPause(true);
                Write(SyncLevel.Error, "Connection lost - sim paused so you can reconnect");
            }
            else
            {
                Write(SyncLevel.Error, "Connection lost");
            }
        }
    }

    #region Time

    private bool TryGetLocalTime(out DateTime time)
    {
        time = default;
        if (_zuluSeconds is not { } secs || _dayOfYear is not { } doy || _year is not { } year) return false;
        if (year < 1 || doy < 1) return false;
        try
        {
            time = new DateTime((int)year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddDays(doy - 1)
                .AddSeconds(secs);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private void BroadcastTime()
    {
        if (!_hasAircraft || !TryGetLocalTime(out var now)) return;
        try { _net.SendAll(new TimeState(now.Ticks, _paused)); }
        catch (Exception e) { Serilog.Log.Error(e, "[EnvSync] Failed to send time state"); }
    }

    private void RequestSync(string reason)
    {
        Serilog.Log.Information("[EnvSync] Requesting time sync ({Reason})", reason);
        try { _net.SendAll(new TimeRequest(0)); }
        catch (Exception e) { Serilog.Log.Error(e, "[EnvSync] Failed to send time request"); }
    }

    private void OnTimeState(TimeState state)
    {
        if (!_hasAircraft || !TryGetLocalTime(out var local)) return;

        var host = new DateTime(state.ZuluTicks, DateTimeKind.Utc);
        var drift = host - local;

        lock (_lock)
        {
            var initial = _initialTimePending;
            _initialTimePending = false;
            if (drift.Duration() <= (initial ? InitialMaxDrift : MaxDrift))
            {
                if (_consecutiveCorrections > 0)
                    Write(SyncLevel.Info, $"Time in sync with host (drift {FormatDrift(drift)})");
                _consecutiveCorrections = 0;
                return;
            }

            if (_pendingCorrection != null) return;
            if (state.Paused || _paused) return; // clocks are frozen; correct once both run again

            _consecutiveCorrections++;
            if (_consecutiveCorrections <= MaxSilentCorrections)
            {
                Write(SyncLevel.Warning,
                    $"Time drift {FormatDrift(drift)} (host {host:yyyy-MM-dd HH:mm:ss}Z, local {local:yyyy-MM-dd HH:mm:ss}Z), correcting");
            }
            if (_consecutiveCorrections == MaxSilentCorrections + 1)
            {
                Write(SyncLevel.Error,
                    "MSFS keeps rejecting the time change. Check that the flight does not use live/real time. Still retrying quietly.");
            }

            var receivedAt = Stopwatch.StartNew();
            if (_bridgeTimeFailures < MaxBridgeTimeFailures)
            {
                // The sim's own time control (through the bridge) sets seconds directly and applies at once.
                // Verify it took; if not, fall back to the key events below for the rest of the session.
                var target = host + TimeSpan.FromMilliseconds(100);
                SetTimeViaBridge(target);
                _pendingCorrection = Observable.Timer(VerifyDelay).Subscribe(_ =>
                {
                    lock (_lock) _pendingCorrection = null;
                    if (!TryGetLocalTime(out var now)) return;
                    var expected = host + receivedAt.Elapsed;
                    if ((expected - now).Duration() <= InitialMaxDrift)
                    {
                        Write(SyncLevel.Info, $"Set time to {expected:yyyy-MM-dd HH:mm:ss}Z");
                        lock (_lock) _bridgeTimeFailures = 0;
                        return;
                    }
                    lock (_lock) _bridgeTimeFailures++;
                    Serilog.Log.Warning("[EnvSync] Bridge time set did not take (local {Local:O}, expected {Expected:O}); trying key events",
                        now, expected);
                    OnTimeState(state with { ZuluTicks = expected.Ticks });
                });
                return;
            }

            // ZULU_*_SET only has minute resolution. Apply exactly at the next whole host minute and zero
            // the seconds, so the remaining error is just network latency.
            var toBoundary = TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(host.Ticks % TimeSpan.TicksPerMinute);
            if (toBoundary < TimeSpan.FromSeconds(2)) toBoundary += TimeSpan.FromMinutes(1);

            _pendingCorrection = Observable.Timer(toBoundary).Subscribe(_ =>
            {
                lock (_lock) _pendingCorrection = null;
                if (_isHost || _paused) return;
                var target = host + receivedAt.Elapsed;
                target = new DateTime(target.Ticks - target.Ticks % TimeSpan.TicksPerMinute
                                      + (target.Second >= 30 ? TimeSpan.TicksPerMinute : 0), DateTimeKind.Utc);
                ApplyTime(target);
            });
        }
    }

    private void SetTimeViaBridge(DateTime target)
    {
        var sent = _sim.SendBus("wx", w =>
        {
            w.WriteString("op", "time");
            w.WriteNumber("sec", (int)target.TimeOfDay.TotalSeconds);
            w.WriteNumber("day", target.Day);
            w.WriteNumber("month", target.Month);
            w.WriteNumber("year", target.Year);
        });
        Serilog.Log.Information("[EnvSync] Setting zulu time {Target:O} through the bridge (sent: {Sent})", target, sent);
    }

    private void ApplyTime(DateTime target)
    {
        _sim.Trigger("ZULU_YEAR_SET", target.Year);
        _sim.Trigger("ZULU_DAY_SET", target.DayOfYear);
        _sim.Trigger("ZULU_HOURS_SET", target.Hour);
        _sim.Trigger("ZULU_MINUTES_SET", target.Minute);
        _sim.Trigger("CLOCK_SECONDS_ZERO");
        Serilog.Log.Information("[EnvSync] Applied zulu time {Target:O}", target);
        if (_consecutiveCorrections <= MaxSilentCorrections)
            Write(SyncLevel.Info, $"Set time to {target:yyyy-MM-dd HH:mm}Z");
    }

    private static string FormatDrift(TimeSpan drift)
    {
        var sign = drift < TimeSpan.Zero ? "-" : "+";
        var d = drift.Duration();
        return d.TotalHours >= 1 ? $"{sign}{(int)d.TotalHours}h{d.Minutes:00}m" :
            d.TotalMinutes >= 1 ? $"{sign}{(int)d.TotalMinutes}m{d.Seconds:00}s" :
            $"{sign}{d.TotalSeconds:0}s";
    }

    #endregion

    #region Pause

    private void OnLocalPause(uint flags)
    {
        Serilog.Log.Debug("[EnvSync] Pause_EX1 flags {Flags}", flags);
        var reason = (flags & PauseMenu) != 0 ? PauseReason.Menu
            : (flags & PauseFull) != 0 ? PauseReason.Pause
            : (flags & PauseActive) != 0 ? PauseReason.ActivePause
            : PauseReason.None;
        var paused = reason != PauseReason.None;
        bool changed;
        lock (_lock)
        {
            changed = paused != _paused;
            _paused = paused;
            if (_pauseEcho == paused && DateTime.UtcNow - _pauseEchoAt < EchoWindow)
            {
                // This change was caused by a remote pause we applied; don't bounce it back.
                _pauseEcho = null;
                return;
            }
        }

        if (!changed || _peerCount == 0) return;
        if (DateTime.UtcNow - _aircraftLoadedAt < LoadSettle)
        {
            Serilog.Log.Information("[EnvSync] Not syncing pause change during aircraft load ({Reason})", reason);
            return;
        }
        Write(SyncLevel.Info, reason switch
        {
            PauseReason.Menu => "You opened the menu, pausing other pilots",
            PauseReason.None => "You resumed the sim",
            _ => "You paused the sim"
        });
        SendPause(paused, reason);
    }

    private void SendPause(bool paused, PauseReason reason)
    {
        try { _net.SendAll(new PauseState(paused, (byte)reason)); }
        catch (Exception e) { Serilog.Log.Error(e, "[EnvSync] Failed to send pause state"); }
    }

    private void OnRemotePause(PauseState state)
    {
        lock (_lock)
        {
            // Apply even if we believe we're already in that state: our view of the local pause flags can be
            // stale (the sim doesn't always report every change), and setting the same pause twice is harmless.
            if (state == _lastRemotePause && state.Paused == _paused) return;
            _lastRemotePause = state;
            _pauseEcho = state.Paused;
            _pauseEchoAt = DateTime.UtcNow;
        }

        // Every remote pause kind is applied as a full pause; the Esc menu itself can't be opened remotely.
        ApplyPause(state.Paused);
        Write(SyncLevel.Info, (PauseReason)state.Reason switch
        {
            PauseReason.Menu => "Other pilot opened the menu - sim paused",
            PauseReason.None => "Sim resumed by other pilot",
            _ => "Sim paused by other pilot"
        });
    }

    // Sets the pause through SimConnect and through the bridge (in-sim calculator code), then checks the sim's
    // reported pause state and tries PAUSE_ON/PAUSE_OFF if PAUSE_SET was ignored.
    private void ApplyPause(bool pause)
    {
        var value = pause ? 1 : 0;
        _sim.Trigger("PAUSE_SET", value);
        _sim.Execute($"{value} (>K:PAUSE_SET)");

        _d.Add(Observable.Timer(VerifyDelay).Subscribe(_ =>
        {
            if (_paused == pause) return;
            Serilog.Log.Warning("[EnvSync] PAUSE_SET {Value} did not take, trying {Event}", value, pause ? "PAUSE_ON" : "PAUSE_OFF");
            lock (_lock)
            {
                _pauseEcho = pause;
                _pauseEchoAt = DateTime.UtcNow;
            }
            var evt = pause ? "PAUSE_ON" : "PAUSE_OFF";
            _sim.Trigger(evt);
            _sim.Execute($"(>K:{evt})");

            _d.Add(Observable.Timer(VerifyDelay).Subscribe(_ =>
            {
                if (_paused == pause || _pauseIgnoredLogged) return;
                _pauseIgnoredLogged = true;
                Write(SyncLevel.Warning, pause
                    ? "MSFS did not pause when the other pilot paused. Pause manually; pause sync may not work in this sim version."
                    : "MSFS did not resume when the other pilot resumed. Resume manually.");
            }));
        }));
    }

    private enum PauseReason : byte { None, Pause, ActivePause, Menu }

    #endregion

    #region Sim rate

    private void EnforceSimRate()
    {
        if (_peerCount == 0 || !_hasAircraft) return;
        var rate = _simRate;

        if (Math.Abs(rate - 1) < 0.01)
        {
            _rateLogged = false;
            return;
        }

        if (!_rateLogged)
        {
            Write(SyncLevel.Warning, $"Sim rate {rate:0.##}x is not allowed in shared flight, resetting to 1x");
            _rateLogged = true;
        }

        _sim.Trigger(rate > 1 ? "SIM_RATE_DECR" : "SIM_RATE_INCR");
    }

    #endregion

    public void Write(SyncLevel level, string message)
    {
        switch (level)
        {
            case SyncLevel.Error: Serilog.Log.Error("[EnvSync] {Message}", message); break;
            case SyncLevel.Warning: Serilog.Log.Warning("[EnvSync] {Message}", message); break;
            default: Serilog.Log.Information("[EnvSync] {Message}", message); break;
        }
        _log.OnNext(new(DateTime.Now, level, message));
    }

    public enum SyncLevel { Info, Warning, Error }

    public record SyncLogEntry(DateTime Time, SyncLevel Level, string Message);

    private record TimeState(long ZuluTicks, bool Paused)
    {
        public class Codec : IPacketCodec<TimeState>
        {
            public void Encode(TimeState packet, BinaryWriter bw)
            {
                bw.Write(packet.ZuluTicks);
                bw.Write(packet.Paused);
            }

            public TimeState Decode(BinaryReader br) => new(br.ReadInt64(), br.ReadBoolean());
        }
    }

    private record TimeRequest(byte Reserved)
    {
        public class Codec : IPacketCodec<TimeRequest>
        {
            public void Encode(TimeRequest packet, BinaryWriter bw) => bw.Write(packet.Reserved);

            public TimeRequest Decode(BinaryReader br) => new(br.ReadByte());
        }
    }

    private record PauseState(bool Paused, byte Reason)
    {
        public class Codec : IPacketCodec<PauseState>
        {
            public void Encode(PauseState packet, BinaryWriter bw)
            {
                bw.Write(packet.Paused);
                bw.Write(packet.Reason);
            }

            public PauseState Decode(BinaryReader br) => new(br.ReadBoolean(), br.ReadByte());
        }
    }
}
