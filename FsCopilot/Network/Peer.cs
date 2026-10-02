namespace FsCopilot.Network;

public record struct Peer(
    string PeerId, 
    string Name, 
    int Ping,
    float PacketLoss,
    Peer.TransportKind Transport)
{
    public enum TransportKind { Direct, Relay }
}
