namespace FsCopilot.Simulation;

using System.Diagnostics.CodeAnalysis;
using Connection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Glass displays whose pointer and input are shared between the pilots.
///
/// Wasm based screens (e.g. the iniBuilds A380 DU panels) all report the same instrument identifier
/// ("WasmInstrument") and gauge name ("DU"), so a screen is identified by its panel: the bridge
/// reports addresses such as "VCockpit53:WasmInstrument:DU" and a configuration entry only needs the
/// distinguishing part ("VCockpit53").
/// </summary>
public class Screens
{
    private const string FileName = "screens.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly List<Screen> _items;

    private Screens(List<Screen> items, bool shareClicks)
    {
        _items = items;
        ShareClicks = shareClicks;
    }

    public static readonly Screens Empty = new([], false);

    public IReadOnlyList<Screen> Items => _items;

    /// <summary>
    /// True when clicks (mousedown/mouseup/click) of the shared displays are forwarded to the other
    /// pilot. Default is false: only the pointer position is shared and the simulator generates the
    /// click from it on the receiving side (see "clicks" in screens.yaml).
    /// </summary>
    public bool ShareClicks { get; }

    /// <summary>
    /// Numbers the screens reported by the bridge inside their display family (same gauge, e.g. all
    /// "DU" displays of the A380). The absolute panel number changes between sessions (other
    /// packages register panels too: VCockpit52..60 in one session, VCockpit29..37 in the next), but
    /// the order inside a family does not. Display identity therefore comes from that relative index
    /// and never from the panel name.
    /// </summary>
    public static List<ScreenEvent> Ordered(IEnumerable<ScreenEvent> discovered)
    {
        var list = discovered
            .Where(e => e.Action.Equals("info", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Instrument))
            .GroupBy(e => e.Instrument, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var result = new List<ScreenEvent>(list.Count);
        foreach (var group in list.GroupBy(e => e.Gauge ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(e => PanelNumber(e.Instrument))
                .ThenBy(e => e.Instrument, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < ordered.Count; i++) result.Add(ordered[i] with { Order = i + 1 });
        }

        return result.OrderBy(e => e.Instrument, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Numeric part of the panel name ("VCockpit30:WasmInstrument:DU" -> 30).</summary>
    private static int PanelNumber(string instrument)
    {
        var panel = instrument.Split(':')[0];
        var digits = new string(panel.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    /// <summary>
    /// Machine independent key of a display, used in the packets exchanged between the pilots:
    /// "DU#2" for wasm displays (gauge + index inside the group) or the instrument identifier for
    /// html instruments. Panel names are never part of it - they differ between the two machines.
    /// </summary>
    public static string PortableKey(ScreenEvent screen) =>
        !string.IsNullOrWhiteSpace(screen.Gauge) && screen.Order > 0
            ? $"{screen.Gauge}#{screen.Order}"
            : InstrumentId(screen.Instrument);

    /// <summary>Instrument identifier part of an address ("VCockpit53:WasmInstrument:DU" -> WasmInstrument).</summary>
    public static string InstrumentId(string? instrument)
    {
        var parts = (instrument ?? string.Empty).Split(':');
        return parts.Length > 1 ? parts[^1] : instrument ?? string.Empty;
    }

    /// <summary>Matches the configured entries against the discovered screens.</summary>
    public Resolution Resolve(IEnumerable<ScreenEvent> discovered)
    {
        var screens = Ordered(discovered);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ScreenMatch>(_items.Count);
        var addresses = new List<string>(_items.Count);

        foreach (var item in _items)
        {
            var match = screens.FirstOrDefault(e => !used.Contains(e.Instrument) && item.Matches(e));
            if (match is null)
            {
                entries.Add(new ScreenMatch(item, null));
                continue;
            }

            used.Add(match.Instrument);
            addresses.Add(match.Instrument);
            entries.Add(new ScreenMatch(item, match.Instrument));
        }

        return new Resolution(addresses.ToArray(), entries);
    }

    /// <summary>
    /// Loads the screens configured for the given aircraft (entry matched by substring, so
    /// "inibuilds-a380" also matches a livery specific name).
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties
                       | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ScreensConfig))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties
                       | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ScreenConfig))]
    public static Screens Load(string? aircraft)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(aircraft)) return Empty;

            var path = Path.Combine(AppContext.BaseDirectory, "Definitions", FileName);
            if (!File.Exists(path)) return Empty;

            var config = Deserializer.Deserialize<ScreensConfig[]>(File.ReadAllText(path));
            var entry = config?.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.Aircraft) &&
                aircraft.Contains(c.Aircraft, StringComparison.OrdinalIgnoreCase));

            if (entry?.Screens is null) return Empty;

            var items = entry.Screens
                .Where(s => !string.IsNullOrWhiteSpace(s.Match) || !string.IsNullOrWhiteSpace(s.Gauge) ||
                            !string.IsNullOrWhiteSpace(s.Guid) || s.Order is not null)
                .Select(s => new Screen(
                    !string.IsNullOrWhiteSpace(s.Name) ? s.Name! : (s.Match ?? s.Gauge ?? "screen"),
                    s.Match,
                    s.Gauge,
                    s.Guid,
                    s.Order))
                .ToList();

            return items.Count == 0 ? Empty : new Screens(items, entry.Clicks ?? false);
        }
        catch (Exception e)
        {
            Log.Error(e, "[Screens] Failed to load {File}", FileName);
            return Empty;
        }
    }

    public override string ToString() => string.Join(", ", _items);
}

/// <summary>
/// A single shared display. Any of the given keys may identify it; they are checked in this order:
/// <list type="bullet">
/// <item>guid: instrument GUID (informational, may change with the aircraft version)</item>
/// <item>gauge + order: the n-th display of that gauge group (stable across reloads and add-ons)</item>
/// <item>match: substring of the screen address / title / detail (e.g. a panel name or a gauge)</item>
/// </list>
/// </summary>
public record Screen(string Name, string? Match, string? Gauge, string? Guid, int? Order)
{
    public bool Matches(ScreenEvent e)
    {
        if (!string.IsNullOrWhiteSpace(Guid) && string.Equals(Guid, e.Guid, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(Gauge) && !e.Gauge.Contains(Gauge, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Order is { } order) return e.Order == order;

        if (!string.IsNullOrWhiteSpace(Match))
            return e.Instrument.Contains(Match, StringComparison.OrdinalIgnoreCase) ||
                   e.Title.Contains(Match, StringComparison.OrdinalIgnoreCase) ||
                   e.Detail.Contains(Match, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(Gauge);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Gauge)) parts.Add($"gauge={Gauge}");
        if (Order is { } order) parts.Add($"order={order}");
        if (!string.IsNullOrWhiteSpace(Match)) parts.Add($"match={Match}");
        if (!string.IsNullOrWhiteSpace(Guid)) parts.Add($"guid={Guid}");
        return $"{Name} ({string.Join(", ", parts)})";
    }
}

/// <summary>A configured display together with the address it resolved to (null when not found).</summary>
public sealed record ScreenMatch(Screen Screen, string? Address);

/// <summary>Result of matching the configuration against the screens reported by the bridge.</summary>
public sealed record Resolution(string[] Addresses, IReadOnlyList<ScreenMatch> Entries);
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties
                            | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ScreensConfig
{
    [YamlMember(Alias = "aircraft")]
    public string? Aircraft { get; set; }

    /// <summary>
    /// Share clicks (mousedown/mouseup/click) of the configured displays with the other pilot.
    /// Default false: only the pointer position travels between the pilots and the simulator
    /// generates the click itself.
    /// </summary>
    [YamlMember(Alias = "clicks")]
    public bool? Clicks { get; set; }

    [YamlMember(Alias = "screens")]
    public ScreenConfig[]? Screens { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties
                            | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ScreenConfig
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>Substring of the screen address, title or details (panel name, gauge, ...).</summary>
    [YamlMember(Alias = "match")]
    public string? Match { get; set; }

    /// <summary>Gauge name of the display group, e.g. "DU" for the A380 display units.</summary>
    [YamlMember(Alias = "gauge")]
    public string? Gauge { get; set; }

    /// <summary>One based index of the display inside its gauge group (creation order).</summary>
    [YamlMember(Alias = "order")]
    public int? Order { get; set; }

    /// <summary>Instrument GUID reported by the simulator (informational, may change).</summary>
    [YamlMember(Alias = "guid")]
    public string? Guid { get; set; }
}
