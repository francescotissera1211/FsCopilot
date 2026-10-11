namespace FsCopilot.Simulation;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Connection;
using Network;
using static EnvironmentSync;

/// <summary>
/// Experimental weather sync. The bridge JS (weather.js) reads the host's current weather preset through the
/// sim's weather view listener; the preset is sent to joiners, whose bridge applies it as a temporary custom
/// preset. Every step has a failsafe: missing bridge support, unreadable weather and failed applies are logged,
/// and repeated failures switch weather sync off for the session instead of retrying forever.
/// </summary>
public class WeatherSync : IDisposable
{
    private const string BusType = "wx";
    private const int ChunkSize = 300;
    private const int MaxFailures = 3;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(25);

    private readonly SimClient _sim;
    private readonly INetwork _net;
    private readonly EnvironmentSync _env;
    private readonly CompositeDisposable _d = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string?[]> _chunks = new();

    private int _seq;
    private bool _bridgeSeen;
    private bool _bridgeWarned;
    private bool _readWarned;
    private bool _diagLogged;
    private bool _disabled;
    private int _failures;

    // Host
    private string? _lastSentHash;

    // Joiner
    private string? _hostHash;
    private string? _hostPreset;
    private string? _appliedHash;
    private string? _localBaseline;
    private bool _awaitBaseline;
    private IDisposable? _applyTimeout;

    public WeatherSync(SimClient sim, INetwork net, EnvironmentSync env)
    {
        _sim = sim;
        _net = net;
        _env = env;

        net.RegisterPacket<WeatherState, WeatherState.Codec>();
        net.RegisterPacket<WeatherRequest, WeatherRequest.Codec>();

        _d.Add(sim.BusMessages(BusType).Subscribe(OnBridgeMessage, e => Log.Error(e, "[Weather] Bus error")));

        _d.Add(Observable.Interval(CheckInterval)
            .Where(_ => Active)
            .Subscribe(_ => AskBridge()));

        _d.Add(env.PeersChanged.Where(c => c > 0).Throttle(TimeSpan.FromSeconds(3)).Subscribe(_ => InitialSync()));
        _d.Add(sim.Aircraft.Throttle(TimeSpan.FromSeconds(5)).Subscribe(_ =>
        {
            if (_env.PeerCount > 0) InitialSync();
            _d.Add(Observable.Timer(BridgeTimeout).Subscribe(_ => CheckBridge()));
        }));

        _d.Add(net.Stream<WeatherRequest>().Where(_ => _env.IsHost).Subscribe(_ => AskBridge()));

        _d.Add(net.Stream<WeatherState>().Where(_ => !_env.IsHost).Subscribe(OnHostWeather));
    }

    public void Dispose()
    {
        _d.Dispose();
        _applyTimeout?.Dispose();
    }

    private bool Active => !_disabled && _env.PeerCount > 0 && _env.HasAircraft;

    /// <summary>Resets per-session state; called on join/leave.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _disabled = false;
            _failures = 0;
            _lastSentHash = _hostHash = _hostPreset = _appliedHash = _localBaseline = null;
            _awaitBaseline = false;
            _applyTimeout?.Dispose();
            _applyTimeout = null;
        }
    }

    private void InitialSync()
    {
        if (_disabled) return;
        if (_env.IsHost)
        {
            AskBridge();
        }
        else
        {
            try { _net.SendAll(new WeatherRequest(0)); }
            catch (Exception e) { Log.Error(e, "[Weather] Failed to send request"); }
        }
    }

    private void CheckBridge()
    {
        lock (_lock)
        {
            if (_bridgeSeen || _bridgeWarned) return;
            _bridgeWarned = true;
        }
        _env.Write(SyncLevel.Warning,
            "Weather sync unavailable: the installed bridge has no weather support. Install the bridge bundled with this build.");
    }

    private void AskBridge() => _sim.SendBus(BusType, w => w.WriteString("op", "get"));

    #region Bridge -> app

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
                case "result":
                    OnApplyResult(msg);
                    break;
                case "chunk":
                    var text = Collect(msg);
                    if (text == null) break;
                    if (msg.String("k") == "diag") OnDiag(text);
                    else if (msg.String("k") == "state") OnLocalState(text);
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
        if (!ok && first)
            _env.Write(SyncLevel.Warning, "Weather sync unavailable: the sim refused the weather listener");
    }

    private void OnDiag(string text)
    {
        if (_diagLogged) return;
        _diagLogged = true;
        Log.Debug("[Weather] Payload structure: {Diag}", text);
    }

    private void OnLocalState(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var source = root.String("source");
        var preset = root.TryGetProperty("preset", out var p) && p.ValueKind == JsonValueKind.Object ? p.GetRawText() : null;

        if (preset == null)
        {
            if (_readWarned) return;
            _readWarned = true;
            _env.Write(SyncLevel.Warning, "Could not read the current weather from the sim (live weather?). Weather is not synced.");
            return;
        }

        var hash = Hash(preset);
        if (!_diagLogged) Log.Debug("[Weather] Current preset from {Source}: {Preset}", source, preset);

        if (_env.IsHost) OnHostLocalState(hash, preset, source);
        else OnJoinerLocalState(hash);
    }

    private void OnHostLocalState(string hash, string preset, string source)
    {
        // Sent on every check, not only on change: joiners skip a preset they already applied, and this way a
        // joiner whose apply failed retries without needing its own request.
        bool changed;
        lock (_lock)
        {
            changed = _lastSentHash != null && _lastSentHash != hash;
            _lastSentHash = hash;
        }
        if (_env.PeerCount == 0) return;

        if (changed) _env.Write(SyncLevel.Info, "Weather changed on host, sending to other pilots");
        Log.Information("[Weather] Sending host weather {Hash} ({Source}, {Length} chars)", hash[..8], source, preset.Length);
        try { _net.SendAll(new WeatherState(hash, preset)); }
        catch (Exception e) { Log.Error(e, "[Weather] Failed to send weather"); }
    }

    private void OnJoinerLocalState(string hash)
    {
        string? preset = null;
        lock (_lock)
        {
            if (_awaitBaseline)
            {
                // First reading after our apply: whatever the sim made of the host preset is the new normal.
                _awaitBaseline = false;
                _localBaseline = hash;
                return;
            }
            if (_localBaseline != null && hash != _localBaseline && _hostPreset != null && _applyTimeout == null)
                preset = _hostPreset;
        }
        if (preset == null) return;

        _env.Write(SyncLevel.Warning, "Local weather was changed, restoring host weather");
        Apply(preset);
    }

    private void OnApplyResult(JsonElement msg)
    {
        var ok = msg.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        var how = msg.String("how");
        var err = msg.StringOrNull("err");

        bool disable = false;
        lock (_lock)
        {
            if (_applyTimeout == null) return; // late result after timeout; ignore
            _applyTimeout.Dispose();
            _applyTimeout = null;

            if (ok)
            {
                _failures = 0;
                _appliedHash = _hostHash;
                _awaitBaseline = true;
            }
            else
            {
                disable = ++_failures >= MaxFailures;
                if (disable) _disabled = true;
            }
        }

        if (ok)
        {
            _env.Write(SyncLevel.Info, how == "temp" ? "Weather set to host weather" : "Weather set to host's preset by name");
            if (err != null) Log.Warning("[Weather] Applied with fallback: {Error}", err);
            _d.Add(Observable.Timer(TimeSpan.FromSeconds(4)).Subscribe(_ => AskBridge()));
        }
        else
        {
            Log.Warning("[Weather] Apply failed: {Error}", err);
            _env.Write(disable ? SyncLevel.Error : SyncLevel.Warning, disable
                ? $"Weather sync switched off for this session after {MaxFailures} failed attempts"
                : "Could not apply host weather, will retry");
        }
    }

    #endregion

    #region Network -> bridge

    private void OnHostWeather(WeatherState state)
    {
        lock (_lock)
        {
            if (_disabled) return;
            _hostHash = state.Hash;
            _hostPreset = state.Preset;
            if (state.Hash == _appliedHash || _applyTimeout != null) return;
        }
        Log.Information("[Weather] Received host weather {Hash}", state.Hash[..8]);
        Apply(state.Preset);
    }

    private void Apply(string preset)
    {
        lock (_lock)
        {
            if (_applyTimeout != null) return;
            _applyTimeout = Observable.Timer(ApplyTimeout).Subscribe(_ => OnApplyTimeout());
        }

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(preset));
        var n = Math.Max(1, (b64.Length + ChunkSize - 1) / ChunkSize);
        var s = Interlocked.Increment(ref _seq);
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < n; i++)
            {
                var d = b64.Substring(i * ChunkSize, Math.Min(ChunkSize, b64.Length - i * ChunkSize));
                var index = i;
                _sim.SendBus(BusType, w =>
                {
                    w.WriteString("op", "chunk");
                    w.WriteString("k", "apply");
                    w.WriteNumber("s", s);
                    w.WriteNumber("i", index);
                    w.WriteNumber("n", n);
                    w.WriteString("d", d);
                });
                // The bus is a single client-data slot; give the WASM a frame or two to forward each chunk.
                await Task.Delay(40);
            }
        });
    }

    private void OnApplyTimeout()
    {
        bool disable;
        lock (_lock)
        {
            if (_applyTimeout == null) return;
            _applyTimeout = null;
            disable = ++_failures >= MaxFailures;
            if (disable) _disabled = true;
        }
        _env.Write(disable ? SyncLevel.Error : SyncLevel.Warning, disable
            ? $"Weather sync switched off for this session after {MaxFailures} failed attempts"
            : "Bridge did not confirm the weather change, will retry");
    }

    #endregion

    private string? Collect(JsonElement msg)
    {
        var key = $"{msg.String("k")}:{msg.GetProperty("s").GetInt32()}";
        var i = msg.GetProperty("i").GetInt32();
        var n = msg.GetProperty("n").GetInt32();
        if (n is <= 0 or > 1000 || i < 0 || i >= n) return null;

        lock (_lock)
        {
            if (!_chunks.TryGetValue(key, out var parts))
            {
                // Drop stale partial messages; a lost chunk is recovered by the next periodic read.
                if (_chunks.Count > 8) _chunks.Clear();
                _chunks[key] = parts = new string?[n];
            }
            parts[i] = msg.String("d");
            if (parts.Any(part => part == null)) return null;
            _chunks.Remove(key);
            return Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(parts)));
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private record WeatherState(string Hash, string Preset)
    {
        public class Codec : IPacketCodec<WeatherState>
        {
            public void Encode(WeatherState packet, BinaryWriter bw)
            {
                bw.Write(packet.Hash);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    gz.Write(Encoding.UTF8.GetBytes(packet.Preset));
                bw.Write(ms.Length > int.MaxValue ? 0 : (int)ms.Length);
                bw.Write(ms.GetBuffer(), 0, (int)ms.Length);
            }

            public WeatherState Decode(BinaryReader br)
            {
                var hash = br.ReadString();
                var length = br.ReadInt32();
                using var gz = new GZipStream(new MemoryStream(br.ReadBytes(length)), CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                return new(hash, reader.ReadToEnd());
            }
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
