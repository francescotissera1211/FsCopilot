namespace FsCopilot.Connection;

/// <summary>
/// Interaction with an instrument of the cockpit.
/// </summary>
/// <param name="Instrument">Instrument identifier the event belongs to.</param>
/// <param name="Event">DOM event type: mouseup, mousemove, mousedown, input, keypress, keydown.</param>
/// <param name="Id">Stable element id (legacy addressing, used for input/keyboard events).</param>
/// <param name="Value">Event payload for input/keyboard events.</param>
/// <param name="X">Normalized [0..1] pointer position inside the screen, null when the event is element based.</param>
/// <param name="Y">Normalized [0..1] pointer position inside the screen, null when the event is element based.</param>
/// <param name="From">Name of the pilot the interaction came from (used to label the remote pointer).</param>
public record Interact(string Instrument, string Event, string Id, string? Value, double? X = null, double? Y = null, string? From = null);
