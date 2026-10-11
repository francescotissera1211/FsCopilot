namespace FsCopilot.Simulation;

using System.Globalization;
using System.Security.Cryptography;
using Connection;
using Network;

public class Coordinator : IDisposable
{
    private readonly INetwork _net;
    private readonly MasterSwitch _masterSwitch;
    private readonly SimClient _sim;
    private readonly CompositeDisposable _d = new();
    private CompositeDisposable _cSubs = new();
    private HashSet<string> _ignore = [];
    // Current local value of every state-like profile variable, for the load-in snapshot.
    private readonly ConcurrentDictionary<string, StateLink> _states = new();
    private readonly EnvironmentSync _env;

    /// <summary>Names of the variables the loaded profile syncs (used to avoid double-syncing them elsewhere).</summary>
    public ICollection<string> ProfileVariables => _states.Keys;

    public Coordinator(SimClient sim, INetwork net, MasterSwitch masterSwitch, EnvironmentSync env)
    {
        _env = env;
        _net = net;
        _masterSwitch = masterSwitch;
        _sim = sim;
        var sw = Stopwatch.StartNew();

        Span<byte> sessionBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(sessionBytes);
        var sessionId = BitConverter.ToUInt64(sessionBytes);

        net.RegisterPacket<Update, Update.Codec>();
        net.RegisterPacket<Interact, InteractCodec>();
        net.RegisterPacket<SnapshotRequest, SnapshotRequest.Codec>();
        net.RegisterPacket<Snapshot, Snapshot.Codec>();

        _sim.Register<Physics>();
        _sim.Register<Surfaces>();
        _net.RegisterPacket<Physics, Physics.Codec>();
        _net.RegisterPacket<Surfaces, Surfaces.Codec>();

        _d.Add(sim.Aircraft.Take(1).Subscribe(_ => AddLink((ref Physics physics) =>
        {
            physics.SessionId = sessionId;
            physics.TimeMs = (uint)sw.ElapsedMilliseconds;
        })));

        _d.Add(sim.Aircraft.Take(1).Subscribe(_ => AddLink((ref Surfaces surfaces) =>
        {
            surfaces.SessionId = sessionId;
            surfaces.TimeMs = (uint)sw.ElapsedMilliseconds;
        })));

        _d.Add(_sim.Interactions
            .Where(i => !_ignore.Contains(i.Instrument))
            .Subscribe(interact => _net.SendAll(interact)));
        _d.Add(_net.Stream<Interact>()
            .Subscribe(update => _sim.Set(update)));

        // Load-in: the joiner (not in control) asks; the pilot in control sends every profile state it knows.
        _d.Add(env.SessionReady
            .Where(_ => !_masterSwitch.IsMaster)
            .Subscribe(_ =>
            {
                try { _net.SendAll(new SnapshotRequest(0)); }
                catch (Exception e) { Log.Error(e, "[Coordinator] Failed to request snapshot"); }
            }));
        _d.Add(_net.Stream<SnapshotRequest>()
            .Where(_ => _masterSwitch.IsMaster)
            .Subscribe(_ => SendSnapshot()));
        _d.Add(_net.Stream<Snapshot>()
            .Where(_ => !_masterSwitch.IsMaster)
            .Subscribe(ApplySnapshot));
    }

    private void SendSnapshot()
    {
        var items = _states
            .Select(kv => (Name: kv.Key, Value: kv.Value.Current()))
            .Where(x => x.Value != null)
            .Select(x => new Update(x.Name, x.Value!))
            .ToArray();
        if (items.Length == 0) return;
        Log.Information("[Coordinator] Sending snapshot of {Count} profile states", items.Length);
        try { _net.SendAll(new Snapshot(items)); }
        catch (Exception e) { Log.Error(e, "[Coordinator] Failed to send snapshot"); }
    }

    private void ApplySnapshot(Snapshot snapshot)
    {
        var applied = 0;
        foreach (var update in snapshot.Items)
        {
            if (!_states.TryGetValue(update.Name, out var link)) continue;
            var current = link.Current();
            if (current != null && SameValue(current, update.Value)) continue;
            try
            {
                Apply(link.Def, update.Value, current);
                applied++;
            }
            catch (Exception e) { Log.Error(e, "[Coordinator] Error applying snapshot value {Name}", update.Name); }
        }
        Log.Information("[Coordinator] Snapshot: {Applied} of {Count} profile states differed and were applied",
            applied, snapshot.Items.Length);
        if (applied > 0)
            _env.Write(EnvironmentSync.SyncLevel.Info, $"Matched {applied} cockpit settings to the pilot in control");
    }

    private static bool SameValue(object a, object b)
    {
        if (a is string || b is string) return Equals(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture));
        try { return Math.Abs(Convert.ToDouble(a, CultureInfo.InvariantCulture) - Convert.ToDouble(b, CultureInfo.InvariantCulture)) < 1e-6; }
        catch (Exception) { return Equals(a, b); }
    }

    public void Dispose()
    {
        _d.Dispose();
        _cSubs.Dispose();
    }

    public void Load(Definitions definitions)
    {
        if (!_cSubs.IsDisposed) _cSubs.Dispose();
        _ignore.Clear();
        _states.Clear();
        _cSubs = new();
        foreach (var def in definitions) AddLink(def);
        foreach (var i in definitions.Ignore) _ignore.Add(i);
    }

    private void AddLink<TPacket>(RefAction<TPacket> modify)
        where TPacket : unmanaged
    {
        _d.Add(_sim.Stream<TPacket>()
            .Sample(TimeSpan.FromMilliseconds(50), new EventLoopScheduler()) // 20 fps
            .Where(_ => _masterSwitch.IsMaster)
            .Subscribe(update =>
            {
                modify(ref update);
                try { _net.SendAll(update, true); }
                catch (Exception e) { Log.Error(e, "[Coordinator] Error while sending packet {Packet}", typeof(TPacket).Name); }
            }, ex => { Log.Fatal(ex, "[Coordinator] Error while processing a message from sim"); }));

        _d.Add(_net.Stream<TPacket>()
            .Where(_ => !_masterSwitch.IsMaster)
            .Subscribe(update =>
            {
                try { _sim.Set(update); }
                catch (Exception e) { Log.Error(e, "[Coordinator] Error processing packet {Packet}", typeof(TPacket).Name); }
            }, ex => { Log.Fatal(ex, "[Coordinator] Error while processing a message from client"); }));
    }

    private void AddLink(Definition def)
    {
        var master = !def.Shared;
        object? currentValue = null;
        var getVar = def.Get;

        // H: events and K: events are actions, not states, so they are not part of the load-in snapshot.
        if (getVar.Length > 2 && getVar[1] == ':' && getVar[0] != 'H' && getVar[0] != 'K')
            _states[getVar] = new(def, () => currentValue);

        var simRx = _sim.Stream(getVar, def.Units);
        if (master) simRx = simRx.Sample(TimeSpan.FromMilliseconds(30), DefaultScheduler.Instance); // 33 fps
        simRx = simRx
            .Do(value => currentValue = value)
            .Where(_ => !master || _masterSwitch.IsMaster);
        if (getVar[0] == 'H') simRx = simRx.Delay(TimeSpan.FromMilliseconds(500));
        if (!master) simRx = simRx.Where(_ => !Skip.Should(getVar));

        _cSubs.Add(simRx
            .Subscribe(value =>
            {
                if (!master && def.Skip != null) Skip.Next(def.Skip);
                _net.SendAll(new Update(getVar, value), unreliable: master);
                Log.Verbose("[PACKET] SENT {Name} {Value}", getVar, value);
            }));

        _cSubs.Add(_net.Stream<Update>()
            .Where(update => update.Name == getVar)
            .Do(update => Log.Verbose("[PACKET] RECV {Name} {Value}", getVar, update.Value))
            .Where(_ => !master || !_masterSwitch.IsMaster)
            .Subscribe(update => Apply(def, update.Value, currentValue)));
    }

    private void Apply(Definition def, object value, object? currentValue)
    {
        var getVar = def.Get;
        if (!def.Shared)
        {
            var set = def.ParseSet(value, currentValue ?? value, out var units, out var values);
            if (values.Length == 0) return;
            _sim.Set(set, units, values);
        }
        else
        {
            var expression = def.Set(value, currentValue ?? value);
            if (expression.Contains(">K:#"))
            {
                var set = def.ParseSet(value, currentValue ?? value, out var units, out var values);
                if (values.Length == 0) return;
                Skip.Next(getVar);
                _sim.Set(set, units, values);
            }
            else
            {
                if ((expression.Contains(">K:") || expression.Contains(">B:"))
                    && expression.Contains("TOGGLE", StringComparison.OrdinalIgnoreCase)
                    && value.Equals(currentValue)) return;
                Skip.Next(getVar);
                _sim.Execute(expression);
            }
        }
    }

    private record StateLink(Definition Def, Func<object?> Current);

    private record SnapshotRequest(byte Reserved)
    {
        public class Codec : IPacketCodec<SnapshotRequest>
        {
            public void Encode(SnapshotRequest packet, BinaryWriter bw) => bw.Write(packet.Reserved);

            public SnapshotRequest Decode(BinaryReader br) => new(br.ReadByte());
        }
    }

    private record Snapshot(Update[] Items)
    {
        public class Codec : IPacketCodec<Snapshot>
        {
            private static readonly Update.Codec Item = new();

            public void Encode(Snapshot packet, BinaryWriter bw)
            {
                bw.Write(packet.Items.Length);
                foreach (var item in packet.Items) Item.Encode(item, bw);
            }

            public Snapshot Decode(BinaryReader br)
            {
                var items = new Update[br.ReadInt32()];
                for (var i = 0; i < items.Length; i++) items[i] = Item.Decode(br);
                return new(items);
            }
        }
    }

    private record Update(string Name, object Value)
    {
        public class Codec : IPacketCodec<Update>
        {
            public void Encode(Update packet, BinaryWriter bw)
            {
                bw.Write(packet.Name);
                var v = packet.Value;
                var type = Type.GetTypeCode(v.GetType());
                bw.Write((byte)type);
                switch (type)
                {
                    case TypeCode.String: bw.Write((string)v); break;
                    case TypeCode.Int32: bw.Write((int)v); break;
                    case TypeCode.Int64: bw.Write((long)v); break;
                    case TypeCode.Double: bw.Write((double)v); break;
                    case TypeCode.Single: bw.Write((float)v); break;
                    case TypeCode.Boolean: bw.Write((bool)v); break;
                    case TypeCode.Int16: bw.Write((short)v); break;
                    case TypeCode.UInt16: bw.Write((ushort)v); break;
                    case TypeCode.UInt32: bw.Write((uint)v); break;
                    case TypeCode.UInt64: bw.Write((ulong)v); break;
                    case TypeCode.Decimal: bw.Write((decimal)v); break;
                    case TypeCode.SByte: bw.Write((sbyte)v); break;
                    case TypeCode.Byte: bw.Write((byte)v); break;
                    default: throw new NotSupportedException( $"Data.Value type '{v.GetType().FullName}' is not supported by codec");
                }
            }

            public Update Decode(BinaryReader br)
            {
                var name = br.ReadString();
                var t = (TypeCode)br.ReadByte();
                object value = t switch
                {
                    TypeCode.String => br.ReadString(),
                    TypeCode.Int32 => br.ReadInt32(),
                    TypeCode.Int64 => br.ReadInt64(),
                    TypeCode.Double => br.ReadDouble(),
                    TypeCode.Single => br.ReadSingle(),
                    TypeCode.Boolean => br.ReadBoolean(),
                    TypeCode.Int16 => br.ReadInt16(),
                    TypeCode.UInt16 => br.ReadUInt16(),
                    TypeCode.UInt32 => br.ReadUInt32(),
                    TypeCode.UInt64 => br.ReadUInt64(),
                    TypeCode.Decimal => br.ReadDecimal(),
                    TypeCode.SByte => br.ReadSByte(),
                    TypeCode.Byte => br.ReadByte(),
                    TypeCode.DateTime => DateTime.FromBinary(br.ReadInt64()),
                    _ => throw new NotSupportedException($"Неизвестный TypeCode={t}")
                };

                return new(name, value);
            }
        }
    }

    private class InteractCodec : IPacketCodec<Interact>
    {
        public void Encode(Interact packet, BinaryWriter bw)
        {
            bw.Write(packet.Instrument);
            bw.Write(packet.Event);
            bw.Write(packet.Id);
            bw.Write(packet.Value != null);
            if (packet.Value != null) bw.Write(packet.Value);
        }

        public Interact Decode(BinaryReader br)
        {
            var instrument = br.ReadString();
            var @event = br.ReadString();
            var id = br.ReadString();
            var hasValue = br.ReadBoolean();
            var value = hasValue ? br.ReadString() : null;
            return new(instrument, @event, id, value);
        }
    }

    delegate void RefAction<T>(ref T value);
}
