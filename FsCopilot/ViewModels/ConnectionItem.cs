namespace FsCopilot.ViewModels;

using ReactiveUI;

/// <summary>One pilot in the Onboard list.</summary>
public sealed class ConnectionItem(string peerId) : ReactiveObject
{
    private string _name = string.Empty;
    private int _ping;
    private float _packetLoss;
    private bool _isDirect;
    private bool _hasSeparatorAfter;

    /// <summary>When the row first appeared, and whether its arrival has been announced.</summary>
    public DateTime FirstSeen { get; } = DateTime.UtcNow;
    public bool Announced { get; set; }

    public string PeerId { get; } = peerId;
    public string Name { get => _name; private set => this.RaiseAndSetIfChanged(ref _name, value); }
    public int Ping { get => _ping; private set => this.RaiseAndSetIfChanged(ref _ping, value); }
    public float PacketLoss { get => _packetLoss; private set => this.RaiseAndSetIfChanged(ref _packetLoss, value); }
    public bool IsDirect { get => _isDirect; private set => this.RaiseAndSetIfChanged(ref _isDirect, value); }
    public bool HasSeparatorAfter { get => _hasSeparatorAfter; set => this.RaiseAndSetIfChanged(ref _hasSeparatorAfter, value); }

    public void Update(string name, int ping, float packetLoss, bool isDirect)
    {
        Name = name;
        Ping = ping;
        PacketLoss = packetLoss;
        IsDirect = isDirect;
        this.RaisePropertyChanged(nameof(QualityLevel));
        this.RaisePropertyChanged(nameof(Quality));
        this.RaisePropertyChanged(nameof(Description));
        this.RaisePropertyChanged(nameof(Details));
    }

    public string Quality => QualityLevel switch
    {
        1 => "excellent",
        2 => "good",
        3 => "fair",
        4 => "poor",
        _ => "bad"
    };

    /// <summary>
    /// The row's name for a screen reader. Ping and loss stay out of it: they change every
    /// quarter second, and a changing name is re-read while the row has focus.
    /// </summary>
    public string Description =>
        $"{Name}, {MainViewModel.Spell(PeerId)}, {(IsDirect ? "direct" : "relay")}, {Quality}";

    /// <summary>The figures, read as the row's description.</summary>
    public string Details => $"Ping {Ping} ms, loss {PacketLoss:F1}%";

    public int QualityLevel
    {
        get
        {
            // Quality: 1=excellent, 5=poor. Combines ping and packet loss.
            var level = 1;

            // Ping-based levels
            if (Ping > 800) level = Math.Max(level, 5);
            else if (Ping > 400) level = Math.Max(level, 4);
            else if (Ping > 200) level = Math.Max(level, 3);
            else if (Ping > 100) level = Math.Max(level, 2);

            // Packet loss degrades quality further
            if (PacketLoss > 20f) level = Math.Max(level, 5);
            else if (PacketLoss > 10f) level = Math.Max(level, 4);
            else if (PacketLoss > 5f) level = Math.Max(level, 3);
            else if (PacketLoss > 2f) level = Math.Max(level, 2);

            return level;
        }
    }
}
