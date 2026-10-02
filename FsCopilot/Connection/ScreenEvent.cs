namespace FsCopilot.Connection;

/// <summary>
/// Screen level message exchanged with the bridge: discovery, remote pointer tests and hover focus.
/// </summary>
/// <param name="Action">info | focus | hit | moved | clicked</param>
/// <param name="Instrument">Screen address reported by the bridge: "panel:instrument:gauge".</param>
/// <param name="X">Pointer position: normalized [0..1] for pointer events, panel left for info.</param>
/// <param name="Y">Pointer position: normalized [0..1] for pointer events, panel top for info.</param>
/// <param name="W">Screen width in page pixels (0 when unknown).</param>
/// <param name="H">Screen height in page pixels (0 when unknown).</param>
/// <param name="Interactive">True when the aircraft instrument reports isInteractive.</param>
/// <param name="Detail">Free form description (page, cfg, wasm, keys, hit element) for the developer UI.</param>
/// <param name="Title">Panel/document title reported by the bridge, e.g. "VCockpit53 - WasmInstrument".</param>
/// <param name="Gauge">Gauge name of a Wasm display (DU, ISIS, FCU, ...), empty for html instruments.</param>
/// <param name="Guid">Instrument GUID assigned by the simulator - informational only, not stable.</param>
/// <param name="Time">Creation timestamp in epoch milliseconds: the display order is derived from it.</param>
public record ScreenEvent(
    string Action,
    string Instrument,
    double X,
    double Y,
    double W,
    double H,
    bool Interactive,
    string Detail,
    string Title,
    string Gauge,
    string Guid,
    long Time)
{
    /// <summary>
    /// Index of this display inside its group (same wasm module + gauge, ordered by creation time).
    /// Derived by the host app: panel names such as "VCockpit53" change between sessions and when
    /// add-ons patch the panel configuration, so they cannot be used to identify a display.
    /// </summary>
    public int Order { get; init; }

    public override string ToString() =>
        $"{Action,-8} {Gauge,-6} #{(Order > 0 ? Order : 0),-2} {Instrument,-30} {Detail}";
}
