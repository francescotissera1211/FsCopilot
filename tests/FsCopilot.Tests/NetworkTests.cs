namespace FsCopilot.Tests;

using System.Net;
using FsCopilot.Network;

public class DirectTargetTests
{
    [Theory]
    [InlineData("10.0.0.2", "10.0.0.2:38510")]
    [InlineData("10.0.0.2:38600", "10.0.0.2:38600")]
    [InlineData(" 192.168.1.5 ", "192.168.1.5:38510")]
    [InlineData("[::1]:38600", "[::1]:38600")]
    [InlineData("::1", "[::1]:38510")]
    public void An_address_parses_with_the_default_port_when_none_is_typed(string typed, string expected)
    {
        Assert.True(DirectNetwork.TryParseTarget(typed, 38510, out var endpoint));
        Assert.Equal(IPEndPoint.Parse(expected), endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pilot.example.com")]
    [InlineData("pilot.example.com:38510")]
    public void A_host_name_is_left_for_DNS(string typed)
    {
        Assert.False(DirectNetwork.TryParseTarget(typed, 38510, out _));
    }
}

public class SchemaTests
{
    private record P01(int X);
    private record P02(string Y);
    private sealed class Codec<T> : IPacketCodec<T> where T : notnull
    {
        public void Encode(T packet, BinaryWriter bw) { }
        public T Decode(BinaryReader br) => default!;
    }

    [Fact]
    public void The_schema_is_one_digest_whatever_the_packet_count()
    {
        var codecs = new Codecs().Add<P01, Codec<P01>>().Add<P02, Codec<P02>>().Add<PeerTags, PeerTags.Codec>();
        Assert.Equal(64, codecs.Schema.Length);
    }

    [Fact]
    public void The_same_packet_table_gives_the_same_schema_and_a_different_one_does_not()
    {
        var a = new Codecs().Add<P01, Codec<P01>>().Add<P02, Codec<P02>>();
        var b = new Codecs().Add<P01, Codec<P01>>().Add<P02, Codec<P02>>();
        var c = new Codecs().Add<P02, Codec<P02>>().Add<P01, Codec<P01>>();
        Assert.Equal(a.Schema, b.Schema);
        Assert.NotEqual(a.Schema, c.Schema);
    }

    [Fact]
    public void Unreliable_packets_leave_room_for_the_relay_frame_byte()
    {
        // One byte of LiteNetLib's unreliable header and one of the v2 relay's frame type.
        Assert.Equal(Transport.MtuFloor - 2, Transport.MaxUnreliablePayload);
    }
}

public class PeerAddressesTests
{
    [Fact]
    public void The_address_list_round_trips()
    {
        var codec = new PeerAddresses.Codec();
        var sent = new PeerAddresses([new("ABCDEFGH", "10.0.0.2:38510"), new("IJKLMNOP", "[::1]:38600")]);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) codec.Encode(sent, bw);
        ms.Position = 0;
        var received = codec.Decode(new BinaryReader(ms));
        Assert.Equal(sent.Entries, received.Entries);
    }

    [Fact]
    public void A_hostile_count_is_refused()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) bw.Write(PeerAddresses.MaxEntries + 1);
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => new PeerAddresses.Codec().Decode(new BinaryReader(ms)));
    }
}

public class FirewallTests
{
    private const string Exe = @"C:\Games\FsCopilot\FsCopilot.exe";

    [Fact]
    public void Only_block_rules_for_this_executable_are_picked()
    {
        var netsh = $"""
            Rule Name:                            FS Copilot Direct
            Enabled:                              Yes
            Direction:                            In
            Action:                               Allow
            Program:                              {Exe}

            Rule Name:                            fscopilot.exe
            Enabled:                              Yes
            Direction:                            In
            Action:                               Block
            Program:                              {Exe}

            Rule Name:                            Something else
            Action:                               Block
            Program:                              C:\Other\other.exe
            """;

        Assert.Equal(["fscopilot.exe"], Firewall.BlockRulesFor(netsh, Exe).ToArray());
    }

    [Fact]
    public void A_listing_it_cannot_read_matches_nothing()
    {
        // netsh's field names are localised; nothing matched means nothing deleted.
        var netsh = $"Regelname: x\nAktion: Blockieren\nProgramm: {Exe}\n";
        Assert.Empty(Firewall.BlockRulesFor(netsh, Exe));
    }
}

public class ConnectionModeTests
{
    [Theory]
    [InlineData("Automatic", "Automatic")]
    [InlineData("direct", "Direct")]
    [InlineData("RELAY", "Relay")]
    [InlineData("IpDirect", "IpDirect")]
    [InlineData("", "Automatic")]
    [InlineData(null, "Automatic")]
    [InlineData("nonsense", "Automatic")]
    public void A_saved_mode_reads_back_as_a_known_one(string? saved, string expected) =>
        Assert.Equal(expected, ConnectionModes.Normalize(saved));
}
