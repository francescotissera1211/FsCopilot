namespace FsCopilot.Simulation;

using Connection;

/// <summary>
/// Resolves the configured shared displays (Definitions/screens.yaml) against the screens reported by
/// the bridge and pushes the result back to it, so the bridge knows which displays carry a pointer
/// and can label the remote cursor. Independent of the network layer, therefore also usable from the
/// Develop window while testing with a single machine.
/// </summary>
public sealed class ScreenSync : IDisposable
{
    private readonly SimClient _sim;
    private readonly CompositeDisposable _d = new();
    private readonly Dictionary<string, ScreenEvent> _discovered = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _portable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _local = new(StringComparer.OrdinalIgnoreCase);

    private Screens _config = Screens.Empty;
    private string _published = string.Empty;
    private int _retries;

    public ScreenSync(SimClient sim)
    {
        _sim = sim;

        _d.Add(sim.Aircraft
            .DistinctUntilChanged()
            .Subscribe(aircraft =>
            {
                _config = Screens.Load(aircraft);
                _discovered.Clear();
                _addresses.Clear();
                _published = string.Empty;
                _retries = 20;
                ShareClicks = _config.ShareClicks;

                if (_config.Items.Count == 0)
                {
                    Log.Debug("[Screens] No shared displays configured for {Aircraft}", aircraft);
                    return;
                }

                Log.Information("[Screens] {Aircraft}: {Count} shared display(s), clicks {Mode}",
                    aircraft, _config.Items.Count, ShareClicks ? "shared" : "left to the simulator");
                Publish();
            }));

        _d.Add(sim.Screens.Subscribe(ev =>
        {
            if (!ev.Action.Equals("info", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(ev.Instrument)) return;

            _discovered[ev.Instrument] = ev;
            Publish();
        }));

        // The bridge reports every display once, shortly after the panel has been created. An app
        // started later (or a panel created after the aircraft was loaded) would miss that report,
        // so ask for it again until the configured displays show up.
        _d.Add(sim.Connected.Where(connected => connected).Subscribe(_ => _retries = 20));

        _d.Add(Observable.Interval(TimeSpan.FromSeconds(3)).Subscribe(_ =>
        {
            if (_retries <= 0 || _config.Items.Count == 0 || _addresses.Count >= _config.Items.Count) return;

            _retries--;
            Log.Debug("[Screens] {Found} of {Expected} displays found - asking the bridge again",
                _addresses.Count, _config.Items.Count);
            _sim.SendScreen("info", "*");
        }));
    }

    /// <summary>Addresses of the displays currently shared with the other pilot.</summary>
    public IReadOnlyCollection<string> Addresses => _addresses;

    /// <summary>
    /// True when clicks of the shared displays are forwarded to the other pilot. False (default)
    /// means only the pointer position is shared: the simulator generates the click from it.
    /// </summary>
    public bool ShareClicks { get; private set; }

    /// <summary>True when the given screen address belongs to one of the shared displays.</summary>
    public bool IsShared(string? instrument) => instrument is not null && _addresses.Contains(instrument);

    /// <summary>
    /// Decides whether an interaction of the simulator has to travel to the other pilot:
    /// <list type="bullet">
    /// <item>pointer moves: only for the shared displays (a shared cursor)</item>
    /// <item>glass displays (Wasm panels, addressed as "panel:instrument:gauge"): only the press
    /// (mousedown) of a shared display travels - mouseup and the click that follows are generated
    /// by the simulator on the receiving side and must never be forwarded (not even for the glass
    /// displays that are not shared)</item>
    /// <item>html instruments (the EFB for instance): unchanged, a click is replayed by element id</item>
    /// </list>
    /// </summary>
    public bool Forward(Interact interact)
    {
        var shared = IsShared(interact.Instrument);

        if (interact.Event == "mousemove") return shared;
        if (ShareClicks) return true;

        if (IsGlassDisplay(interact.Instrument))
        {
            if (shared && interact.Event == "mousedown") return true;

            if (interact.Event is "mousedown" or "mouseup" or "click")
                Log.Debug("[Screens] {Event} of {Instrument} not shared (the simulator generates it)",
                    interact.Event, interact.Instrument);

            return false;
        }

        return true;
    }

    /// <summary>
    /// True for Wasm displays: the bridge addresses them as "panel:instrument:gauge" (e.g.
    /// "VCockpit30:WasmInstrument:DU"), while html instruments have no gauge part.
    /// </summary>
    private static bool IsGlassDisplay(string? instrument)
    {
        var parts = (instrument ?? string.Empty).Split(':');
        return parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]);
    }

    /// <summary>
    /// Rewrites a local interaction into the machine independent form that is sent to the peer:
    /// panel names differ between the two machines (VCockpit36 here, VCockpit30 there), so a display
    /// travels as "gauge#index" (e.g. DU#2) or as its instrument identifier.
    /// </summary>
    public Interact ToPeer(Interact interact) =>
        _portable.TryGetValue(interact.Instrument, out var key)
            ? interact with { Instrument = key }
            : interact with { Instrument = Screens.InstrumentId(interact.Instrument) };

    /// <summary>
    /// Rewrites an interaction received from the peer into the local address of that display.
    /// Returns null when this machine cannot resolve the display, so nothing is clicked by accident:
    /// a "DU#2" style key is only usable once the local displays have been discovered (the retry loop
    /// in the constructor takes care of that).
    /// </summary>
    public Interact? FromPeer(Interact interact)
    {
        if (_local.TryGetValue(interact.Instrument, out var address))
            return interact with { Instrument = address };

        // plain instrument identifiers (html instruments) stay addressable without discovery,
        // portable display keys cannot be resolved yet -> drop them
        return interact.Instrument.Contains('#') ? null : interact;
    }

    public void Dispose() => _d.Dispose();

    private void Publish()
    {
        var ordered = Screens.Ordered(_discovered.Values);
        RebuildPortable(ordered);

        if (_config.Items.Count == 0) return;

        var resolution = _config.Resolve(ordered);

        _addresses.Clear();
        foreach (var address in resolution.Addresses) _addresses.Add(address);

        // Push when the resolved set changes, and also when a new display showed up (a panel created
        // later has not seen the previous push yet).
        var signature = string.Join("|", resolution.Addresses) + "@" + _discovered.Count;
        if (signature == _published) return;
        _published = signature;

        foreach (var entry in resolution.Entries)
        {
            if (entry.Address is not null)
                Log.Information("[Screens] {Screen} -> {Address}", entry.Screen, entry.Address);
            else if (_discovered.Count > 0)
                Log.Warning("[Screens] {Screen}: no matching display among the {Count} reported - fix screens.yaml",
                    entry.Screen, _discovered.Count);
            // nothing reported yet: the retry loop below asks the bridge again, no need to shout
        }

        if (resolution.Addresses.Length > 0) _sim.SendScreens(resolution.Addresses, ShareClicks);
    }

    /// <summary>Builds the local address &lt;-&gt; portable key translation tables.</summary>
    private void RebuildPortable(IEnumerable<ScreenEvent> ordered)
    {
        _portable.Clear();
        _local.Clear();

        foreach (var screen in ordered)
        {
            var key = Screens.PortableKey(screen);

            // Only gauge-less instruments may collide (several pages hosting the same html
            // instrument, e.g. two crashReportA380 panels): disambiguate with the position inside
            // the family, which is derived from the panel order and therefore identical on both
            // machines, instead of silently dropping the display.
            if (_local.ContainsKey(key) && screen.Order > 0)
            {
                var unique = $"{key}#{screen.Order}";
                Log.Debug("[Screens] {Key} is reported by {First} and {Second}: using {Unique}",
                    key, _local[key], screen.Instrument, unique);
                key = unique;
            }

            _portable[screen.Instrument] = key;
            _local[key] = screen.Instrument;
        }
    }
}
