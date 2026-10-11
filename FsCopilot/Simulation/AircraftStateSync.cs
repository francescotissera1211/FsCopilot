namespace FsCopilot.Simulation;

using System.Globalization;
using Connection;
using Network;
using static EnvironmentSync;

/// <summary>
/// Syncs aircraft state that every aircraft has, independent of aircraft profiles:
/// fuel tank levels and flaps from the pilot in control, payload stations and walkaround items
/// (chocks, covers) in both directions. Everything is also sent in full on load-in.
/// Variables the loaded profile already syncs are skipped so they aren't driven twice.
/// </summary>
public class AircraftStateSync : IDisposable
{
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(3);

    private static readonly string[] FuelTanks =
    [
        "CENTER", "CENTER2", "CENTER3", "LEFT MAIN", "RIGHT MAIN", "LEFT AUX", "RIGHT AUX",
        "LEFT TIP", "RIGHT TIP", "EXTERNAL1", "EXTERNAL2"
    ];

    // A:COVER ON indexes as reported by MSFS 2024 developers; others are logged by number.
    private static readonly Dictionary<int, string> CoverNames = new()
    {
        [0] = "wheel chocks",
        [1] = "engine covers",
        [2] = "pitot covers"
    };

    private readonly SimClient _sim;
    private readonly INetwork _net;
    private readonly MasterSwitch _master;
    private readonly EnvironmentSync _env;
    private readonly Coordinator _coordinator;
    private readonly CompositeDisposable _d = new();
    private readonly Item[] _items;

    public AircraftStateSync(SimClient sim, INetwork net, MasterSwitch master, EnvironmentSync env, Coordinator coordinator)
    {
        _sim = sim;
        _net = net;
        _master = master;
        _env = env;
        _coordinator = coordinator;

        net.RegisterPacket<StateValues, StateValues.Codec>();
        net.RegisterPacket<StateRequest, StateRequest.Codec>();

        var items = new List<Item>();
        foreach (var tank in FuelTanks)
            items.Add(new($"A:FUEL TANK {tank} LEVEL", "Percent Over 100", Mode.PilotInControl, 0.002, Kind.Value, $"fuel {tank.ToLowerInvariant()}"));
        items.Add(new("A:FLAPS HANDLE INDEX", "Number", Mode.PilotInControl, 0.5, Kind.Value, "flaps"));
        for (var i = 1; i <= 15; i++)
            items.Add(new($"A:PAYLOAD STATION WEIGHT:{i}", "Pounds", Mode.Shared, 0.5, Kind.Value, $"payload station {i}"));
        for (var i = 0; i <= 7; i++)
            items.Add(new($"A:COVER ON:{i}", "Bool", Mode.Shared, 0.5, Kind.Cover,
                CoverNames.TryGetValue(i, out var n) ? n : $"cover {i}", i));
        _items = items.ToArray();

        foreach (var item in _items)
            _d.Add(sim.Stream(item.Name, item.Units).Subscribe(v => OnLocalValue(item, v)));

        // Pilot in control: stream continuous values (fuel burns) at a modest rate, only when they change.
        _d.Add(Observable.Interval(SendInterval)
            .Where(_ => _master.IsMaster && _env.PeerCount > 0)
            .Subscribe(_ => SendChanged(i => i.Mode == Mode.PilotInControl, full: false)));

        _d.Add(env.SessionReady.Subscribe(_ =>
        {
            if (_master.IsMaster) SendChanged(_ => true, full: true);
            else
            {
                try { _net.SendAll(new StateRequest(0)); }
                catch (Exception e) { Log.Error(e, "[AircraftState] Failed to send request"); }
            }
        }));
        _d.Add(net.Stream<StateRequest>().Where(_ => _master.IsMaster).Subscribe(_ => SendChanged(_ => true, full: true)));
        _d.Add(net.Stream<StateValues>().Subscribe(OnRemoteValues));
    }

    public void Dispose() => _d.Dispose();

    private bool InProfile(Item item) => _coordinator.ProfileVariables.Contains(item.Name);

    private void OnLocalValue(Item item, object raw)
    {
        double value;
        try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
        catch (Exception) { return; }

        bool send;
        lock (item)
        {
            var first = item.Current == null;
            item.Current = value;
            if (first || item.Mode != Mode.Shared) return;

            // Shared items are sent as soon as the local pilot changes them, unless the change is the echo of a
            // value we just applied from the other side.
            if (item.Echo is { } echo && Math.Abs(echo - value) < item.Tolerance && DateTime.UtcNow - item.EchoAt < EchoWindow)
            {
                item.Echo = null;
                item.LastSent = value;
                return;
            }
            send = item.LastSent is not { } last || Math.Abs(last - value) >= item.Tolerance;
            if (send) item.LastSent = value;
        }

        if (!send || _env.PeerCount == 0 || InProfile(item)) return;
        if (item.Kind == Kind.Cover)
            _env.Write(SyncLevel.Info, $"You {(value >= 0.5 ? "placed" : "removed")} the {item.Label}");
        Send([(item.Name, value)], snapshot: false);
    }

    private void SendChanged(Func<Item, bool> filter, bool full)
    {
        var values = new List<(string, double)>();
        foreach (var item in _items.Where(filter))
        {
            if (InProfile(item)) continue;
            lock (item)
            {
                if (item.Current is not { } current) continue;
                if (!full && item.LastSent is { } last && Math.Abs(last - current) < item.Tolerance) continue;
                item.LastSent = current;
                values.Add((item.Name, current));
            }
        }
        if (values.Count > 0) Send(values, snapshot: full);
    }

    private void Send(IReadOnlyList<(string Name, double Value)> values, bool snapshot)
    {
        if (snapshot) Log.Information("[AircraftState] Sending {Count} aircraft state values (load-in)", values.Count);
        try { _net.SendAll(new StateValues(values.Select(v => new StateValue(v.Name, v.Value)).ToArray(), snapshot)); }
        catch (Exception e) { Log.Error(e, "[AircraftState] Failed to send values"); }
    }

    private void OnRemoteValues(StateValues packet)
    {
        var applied = new List<Item>();
        foreach (var remote in packet.Values)
        {
            var item = _items.FirstOrDefault(i => i.Name == remote.Name);
            if (item == null || InProfile(item)) continue;
            // Values from the pilot in control only apply to the others; shared items apply anywhere.
            if (item.Mode == Mode.PilotInControl && _master.IsMaster) continue;

            lock (item)
            {
                if (item.Current is { } current && Math.Abs(current - remote.Value) < item.Tolerance) continue;
                item.Echo = remote.Value;
                item.EchoAt = DateTime.UtcNow;
                item.LastSent = remote.Value;
            }
            try
            {
                ApplyValue(item, remote.Value);
                applied.Add(item);
            }
            catch (Exception e) { Log.Error(e, "[AircraftState] Failed to apply {Name}", item.Name); }
        }
        if (applied.Count == 0) return;

        foreach (var cover in applied.Where(i => i.Kind == Kind.Cover))
        {
            var placed = packet.Values.First(v => v.Name == cover.Name).Value >= 0.5;
            _env.Write(SyncLevel.Info, $"Other pilot {(placed ? "placed" : "removed")} the {cover.Label}");
        }
        if (packet.Snapshot)
        {
            var other = applied.Where(i => i.Kind != Kind.Cover).Select(i => i.Label.Split(' ')[0]).Distinct().ToArray();
            if (other.Length > 0) _env.Write(SyncLevel.Info, $"Matched {string.Join(", ", other)} to the other pilot");
        }
        Log.Debug("[AircraftState] Applied {Names}", string.Join(", ", applied.Select(i => i.Name)));
    }

    private void ApplyValue(Item item, double value)
    {
        if (item.Kind == Kind.Cover)
        {
            // Setting A:COVER ON directly reportedly has no effect; COVER_SET takes (state, cover index).
            var state = value >= 0.5 ? 1 : 0;
            _sim.Trigger("COVER_SET", state, item.Index);
            _sim.Execute($"{state} {item.Index} (>K:2:COVER_SET)");
            return;
        }
        _sim.SetVar(item.Name, item.Units, value);
    }

    private enum Mode { PilotInControl, Shared }

    private enum Kind { Value, Cover }

    private sealed class Item(string name, string units, Mode mode, double tolerance, Kind kind, string label, int index = 0)
    {
        public string Name { get; } = name;
        public string Units { get; } = units;
        public Mode Mode { get; } = mode;
        public double Tolerance { get; } = tolerance;
        public Kind Kind { get; } = kind;
        public string Label { get; } = label;
        public int Index { get; } = index;

        public double? Current;
        public double? LastSent;
        public double? Echo;
        public DateTime EchoAt;
    }

    private record StateValue(string Name, double Value);

    private record StateValues(StateValue[] Values, bool Snapshot)
    {
        public class Codec : IPacketCodec<StateValues>
        {
            public void Encode(StateValues packet, BinaryWriter bw)
            {
                bw.Write(packet.Snapshot);
                bw.Write(packet.Values.Length);
                foreach (var v in packet.Values)
                {
                    bw.Write(v.Name);
                    bw.Write(v.Value);
                }
            }

            public StateValues Decode(BinaryReader br)
            {
                var snapshot = br.ReadBoolean();
                var values = new StateValue[br.ReadInt32()];
                for (var i = 0; i < values.Length; i++) values[i] = new(br.ReadString(), br.ReadDouble());
                return new(values, snapshot);
            }
        }
    }

    private record StateRequest(byte Reserved)
    {
        public class Codec : IPacketCodec<StateRequest>
        {
            public void Encode(StateRequest packet, BinaryWriter bw) => bw.Write(packet.Reserved);

            public StateRequest Decode(BinaryReader br) => new(br.ReadByte());
        }
    }
}
