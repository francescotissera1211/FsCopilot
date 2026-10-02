namespace FsCopilot.Network;

public record struct Peer(
    string PeerId, 
    string Name, 
    int Ping,
    float PacketLoss,
    Peer.TransportKind Transport,
    bool Connected)
{
    public enum TransportKind { Direct, Relay }
}
