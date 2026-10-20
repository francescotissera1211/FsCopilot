namespace FsCopilot.Simulation;

using System.Text.Json;
using Connection;
using Network;
using static EnvironmentSync;

/// <summary>
/// Weather preset sync. The bridge JS (weather.js) reads which weather preset is selected through the sim's weather
/// view listener; the host sends the preset name to joiners, whose bridge selects the preset with the same name
/// (live weather matches live weather). Only presets are synced: the full weather data is not available to
/// instruments. Failures are logged; repeated failures switch weather sync off for the session.
/// </summary>
public class WeatherSync : IDisposable
{
    private const string BusType = "wx";
    private const int MaxFailures = 3;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SelectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(10);
    // The cockpit (and so the bridge JS) only runs in flight, not in menus, so allow time to get there.
    private const int PingsBeforeWarning = 9;
    private const int MaxPings = 30;

    private readonly SimClient _sim;
    private readonly INetwork _net;
    private readonly EnvironmentSync _env;
    private readonly CompositeDisposable _d = new();
    private readonly Lock _lock = new();

    private bool _bridgeSeen;
    private bool _bridgeWarned;
    private bool _readWarned;
    private bool _disabled;
    private int _failures;

    private string? _lastSentName;          // host
    private WeatherState? _hostPreset;      // joiner: what the host has selected
    private string? _localName;             // joiner: what we have selected
    private string? _selecting;             // joiner: preset name being applied
    private IDisposable? _selectTimeout;

    public WeatherSync(SimClient sim, INetwork net, EnvironmentSync env)
    {
        _sim = sim;
        _net = net;
        _env = env;

        net.RegisterPacket<WeatherState, WeatherState.Codec>();
        net.RegisterPacket<WeatherRequest, WeatherRequest.Codec>();

        _d.Add(sim.BusMessages(BusType).Subscribe(OnBridgeMessage, e => Log.Error(e, "[Weather] Bus error")));

        // Host: report its preset regularly. Joiner: check its own preset still matches the host's.
        _d.Add(Observable.Interval(CheckInterval)
            .Where(_ => Active)
            .Subscribe(_ => AskBridge()));

        _d.Add(env.SessionReady.Subscribe(_ => InitialSync()));
        _d.Add(sim.Aircraft.Subscribe(_ =>
            _d.Add(Observable.Interval(PingInterval)
                .Take(MaxPings)
                .TakeWhile(_ => !_bridgeSeen)
                .Subscribe(i => CheckBridge((int)i + 1)))));

        _d.Add(net.Stream<WeatherRequest>().Where(_ => _env.IsHost).Subscribe(_ =>
        {
            lock (_lock) _lastSentName = null; // resend even if unchanged
            AskBridge();
        }));

        _d.Add(net.Stream<WeatherState>().Where(_ => !_env.IsHost).Subscribe(OnHostWeather));
    }

    public void Dispose()
    {
        _d.Dispose();
        _selectTimeout?.Dispose();
    }

    private bool Active => !_disabled && _env.PeerCount > 0 && _env.HasAircraft;

    /// <summary>Resets per-session state; called on join/leave.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _disabled = false;
            _failures = 0;
            _lastSentName = _localName = _selecting = null;
            _hostPreset = null;
            _selectTimeout?.Dispose();
            _selectTimeout = null;
        }
    }

    private void InitialSync()
    {
        if (_disabled) return;
        if (_env.IsHost)
        {
            lock (_lock) _lastSentName = null;
            AskBridge();
        }
        else
        {
            AskBridge();
            try { _net.SendAll(new WeatherRequest(0)); }
            catch (Exception e) { Log.Error(e, "[Weather] Failed to send request"); }
        }
    }

    // The bridge JS announces itself once when it starts, which can happen before the app is listening,
    // so actively ask it until it answers.
    private void CheckBridge(int attempt)
    {
        _sim.SendBus(BusType, w => w.WriteString("op", "ping"));
        lock (_lock)
        {
            if (_bridgeSeen || _bridgeWarned || attempt < PingsBeforeWarning) return;
            _bridgeWarned = true;
        }
        _env.Write(SyncLevel.Warning,
            "Weather module in the bridge is not answering. If you are already in the cockpit, restart the sim so it loads the bridge installed by this app.");
    }

    private void AskBridge() => _sim.SendBus(BusType, w => w.WriteString("op", "get"));

    private void OnBridgeMessage(JsonElement msg)
    {
        try
        {
            switch (msg.String("op"))
            {
                case "hello":
                    OnHello(msg);
                    break;
                case "log":
                    Log.Information("[Weather] Bridge: {Message}", msg.String("m"));
                    break;
                case "state":
                    OnLocalState(msg);
                    break;
                case "result" when msg.String("what") == "select":
                    OnSelectResult(msg);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "[Weather] Failed to handle bridge message");
        }
    }

    private void OnHello(JsonElement msg)
    {
        var ok = msg.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        bool first;
        lock (_lock)
        {
            first = !_bridgeSeen;
            _bridgeSeen = true;
        }
        Log.Information("[Weather] Bridge hello (listener ok: {Ok})", ok);
        if (!first) return;
        if (!ok) _env.Write(SyncLevel.Warning, "Weather sync unavailable: the sim refused the weather listener");
        else if (_bridgeWarned) _env.Write(SyncLevel.Info, "Weather module in the bridge is answering now");
        if (_env.PeerCount > 0) InitialSync();
    }

    private void OnLocalState(JsonElement msg)
    {
        var name = msg.StringOrNull("name");
        var live = msg.TryGetProperty("live", out var l) && l.ValueKind == JsonValueKind.True;
        Log.Debug("[Weather] Selected preset: {Name} (live: {Live}, {Count} presets)", name ?? "none", live,
            msg.TryGetProperty("count", out var c) ? c.ToString() : "?");

        if (name == null)
        {
            if (_readWarned) return;
            _readWarned = true;
            _env.Write(SyncLevel.Warning, "Could not tell which weather preset is selected. Weather is not synced.");
            return;
        }

        if (_env.IsHost) OnHostLocalState(name, live);
        else OnJoinerLocalState(name);
    }

    private void OnHostLocalState(string name, bool live)
    {
        bool changed, send;
        lock (_lock)
        {
            changed = _lastSentName != null && _lastSentName != name;
            send = _lastSentName != name;
            _lastSentName = name;
        }
        if (!send || _env.PeerCount == 0) return;

        if (changed) _env.Write(SyncLevel.Info, $"Weather changed on host to {DisplayName(name, live)}, sending to other pilots");
        Log.Information("[Weather] Sending host preset {Name}", name);
        try { _net.SendAll(new WeatherState(name, live)); }
        catch (Exception e) { Log.Error(e, "[Weather] Failed to send weather"); }
    }

    private void OnJoinerLocalState(string name)
    {
        WeatherState? target;
        lock (_lock)
        {
            _localName = name;
            target = _hostPreset;
            if (target == null || _selecting != null || _disabled || target.Name == name) return;
        }
        Select(target, $"Weather differs from host ({DisplayName(name, false)}), switching to {DisplayName(target.Name, target.Live)}");
    }

    private void OnHostWeather(WeatherState state)
    {
        lock (_lock)
        {
            if (_disabled) return;
            _hostPreset = state;
            if (_selecting != null || state.Name == _localName) return;
        }
        Select(state, $"Host weather is {DisplayName(state.Name, state.Live)}, switching to it");
    }

    private void Select(WeatherState target, string message)
    {
        lock (_lock)
        {
            if (_selecting != null) return;
            _selecting = target.Name;
            _selectTimeout = Observable.Timer(SelectTimeout).Subscribe(_ => OnSelectFailed("the bridge did not answer"));
        }
        _env.Write(SyncLevel.Info, message);
        _sim.SendBus(BusType, w =>
        {
            w.WriteString("op", "select");
            w.WriteString("name", target.Name);
            w.WriteBoolean("live", target.Live);
        });
    }

    private void OnSelectResult(JsonElement msg)
    {
        var ok = msg.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            OnSelectFailed(msg.StringOrNull("err") ?? "unknown error");
            return;
        }

        string? name;
        lock (_lock)
        {
            name = _selecting;
            if (name == null) return;
            _localName = name;
            _selecting = null;
            _failures = 0;
            _selectTimeout?.Dispose();
            _selectTimeout = null;
        }
        Log.Information("[Weather] Selected {Name} ({Detail})", name, msg.StringOrNull("err") ?? "already selected");
        _env.Write(SyncLevel.Info, "Weather now matches the host");
    }

    private void OnSelectFailed(string reason)
    {
        bool disable;
        lock (_lock)
        {
            if (_selecting == null) return;
            _selecting = null;
            _selectTimeout?.Dispose();
            _selectTimeout = null;
            disable = ++_failures >= MaxFailures;
            if (disable) _disabled = true;
        }
        Log.Warning("[Weather] Preset change failed: {Reason}", reason);
        _env.Write(disable ? SyncLevel.Error : SyncLevel.Warning, disable
            ? $"Weather sync switched off for this session after {MaxFailures} failed attempts ({reason})"
            : $"Could not switch to the host's weather ({reason}), will retry");
    }

    // Built-in presets are named by localization keys such as TT:MENU.DATA_LIVE_WEATHER.
    private static string DisplayName(string name, bool live)
    {
        if (live) return "live weather";
        var i = name.LastIndexOf('.');
        return name.StartsWith("TT:") && i >= 0 ? name[(i + 1)..].Replace("DATA_", "").Replace('_', ' ').ToLowerInvariant() : name;
    }

    private record WeatherState(string Name, bool Live)
    {
        public class Codec : IPacketCodec<WeatherState>
        {
            public void Encode(WeatherState packet, BinaryWriter bw)
            {
                bw.Write(packet.Name);
                bw.Write(packet.Live);
            }

            public WeatherState Decode(BinaryReader br) => new(br.ReadString(), br.ReadBoolean());
        }
    }

    private record WeatherRequest(byte Reserved)
    {
        public class Codec : IPacketCodec<WeatherRequest>
        {
            public void Encode(WeatherRequest packet, BinaryWriter bw) => bw.Write(packet.Reserved);

            public WeatherRequest Decode(BinaryReader br) => new(br.ReadByte());
        }
    }
}
