namespace FsCopilot.Simulation;

using Connection;
using Network;

public class MasterSwitch : IDisposable
{
    // SetMaster carries the network peer id, so every peer can say who has control. It used to
    // be a random id per process, which told the others nothing; a build that still sends one
    // is shown as "your co-pilot".
    private readonly string _peerId;

    private readonly BehaviorSubject<bool> _master = new(true);
    private readonly BehaviorSubject<string?> _holder;
    private readonly CompositeDisposable _d = new();

    public bool IsMaster => _master.Value;
    public IObservable<bool> Master => _master;

    /// <summary>
    /// The peer id of whoever has control: this peer's own id, another peer's, or null right
    /// after joining until the holder says who it is.
    /// </summary>
    public IObservable<string?> Holder => _holder;

    public string SelfId => _peerId;

    public MasterSwitch(SimClient sim, INetwork net, string peerId)
    {
        _peerId = peerId;
        _holder = new(peerId);
        net.RegisterPacket<SetMaster, SetMaster.Codec>();

        _d.Add(net.Stream<SetMaster>()
            .Subscribe(setMaster =>
            {
                var newMaster = setMaster.Peer == _peerId;
                if (newMaster != IsMaster) _master.OnNext(newMaster);
                _holder.OnNext(setMaster.Peer);
            }));

        // A peer that joins after control last changed has never heard who has it; the holder
        // says so again whenever the crew grows.
        _d.Add(net.Peers
            .Select(peers => peers.Count(p => p.Connected))
            .DistinctUntilChanged()
            .Buffer(2, 1)
            .Where(pair => pair.Count == 2 && pair[1] > pair[0] && IsMaster)
            .Subscribe(_ => net.SendAll(new SetMaster(_peerId))));
        
        _d.Add(_master
            .Subscribe(isMaster => sim.SetControl(isMaster ? BehaviorControl.Master : BehaviorControl.Slave)));
        
        _d.Add(_master.DistinctUntilChanged()
            .Where(isMaster => isMaster)
            .Subscribe(_ => net.SendAll(new SetMaster(_peerId))));
        
        _d.Add(sim.Stream("K:TOGGLE_LAUNCH_BAR_SWITCH", string.Empty)
            .Do(_ => Log.Information("Launch bar toggle detected"))
            .Subscribe(_ => TakeControl()));
        
        // _d.Add(sim.Config.Where(c => c.Undefined).Subscribe(_ => sim.Set(new SimConfig(false, _master.Value))));
        // _d.Add(_master.Subscribe(val => sim.Set(new SimConfig(false, val))));
        // _d.Add(sim.Config.Where(c => !c.Undefined).Subscribe(c =>
        // {
        //     if (c.Control) TakeControl();
        // }));
    }

    public void TakeControl()
    {
        _master.OnNext(true);
        _holder.OnNext(_peerId);
    }

    //todo Temp solution. We should use ClientConnected event from Peer2Peer class 
    public void Join()
    {
        _master.OnNext(false);
        _holder.OnNext(null);
    }

    public void Dispose() => _d.Dispose();

    private record SetMaster(string Peer)
    {
        public class Codec : IPacketCodec<SetMaster>
        {
            public void Encode(SetMaster packet, BinaryWriter bw) => bw.Write(packet.Peer);

            public SetMaster Decode(BinaryReader br) => new(br.ReadString());
        }
    }
}