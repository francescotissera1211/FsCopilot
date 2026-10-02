namespace FsCopilot.Accessibility;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

/// <summary>
/// Speaks what the cursor does in every text box, the way a screen reader does in a native edit
/// field: the character under the cursor after Left, Right, Home and End, the word after
/// Ctrl+Left/Right, the deleted character after Backspace, the new character after Delete, the
/// whole field after Up/Down, and what Shift extends the selection over.
/// <para>
/// A screen reader can only do this itself through the UI Automation Text pattern, and Avalonia
/// (11.3 and 12.1 alike) gives a TextBox the Value pattern alone, so the cursor moves in silence.
/// Typed characters are not echoed here: the screen reader hears those keys itself.
/// </para>
/// </summary>
public static class TextEcho
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // Tunnel: read the field before the TextBox acts on the key, speak once it has.
        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        var before = new Snapshot(box.Text ?? string.Empty, box.CaretIndex, box.SelectionStart, box.SelectionEnd);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        Func<Snapshot, string?>? speak = e.Key switch
        {
            Key.Back => after => after.Text == before.Text ? null : Deleted(before, ctrl),
            Key.Delete => after => after.Text == before.Text ? null : CharAt(after.Text, after.Caret),
            Key.Left or Key.Right or Key.Home or Key.End when shift => after => Selection(before, after),
            Key.Left or Key.Right when ctrl => after => WordAt(after.Text, after.Caret),
            Key.Left or Key.Right or Key.Home or Key.End => after => CharAt(after.Text, after.Caret),
            Key.Up or Key.Down => after => after.Text.Length == 0 ? "blank" : after.Text,
            Key.A when ctrl => after => after.SelectionLength > 0 ? $"{after.Text} selected" : null,
            _ => null
        };
        if (speak is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var after = new Snapshot(box.Text ?? string.Empty, box.CaretIndex, box.SelectionStart, box.SelectionEnd);
            var text = speak(after);
            if (!string.IsNullOrEmpty(text)) Announcer.Say(text, interrupt: true);
        }, DispatcherPriority.Background);
    }

    private readonly record struct Snapshot(string Text, int Caret, int SelectionStart, int SelectionEnd)
    {
        public int SelectionLength => Math.Abs(SelectionEnd - SelectionStart);
        public string Selected => Text.Substring(Math.Min(SelectionStart, SelectionEnd), SelectionLength);
    }

    private static string Deleted(Snapshot before, bool word)
    {
        if (before.SelectionLength > 0) return Spoken(before.Selected);
        if (before.Caret <= 0) return string.Empty;
        if (!word) return Name(before.Text[before.Caret - 1]);
        var start = before.Caret;
        while (start > 0 && char.IsWhiteSpace(before.Text[start - 1])) start--;
        while (start > 0 && !char.IsWhiteSpace(before.Text[start - 1])) start--;
        return Spoken(before.Text[start..before.Caret]);
    }

    private static string Selection(Snapshot before, Snapshot after)
    {
        if (after.SelectionLength == before.SelectionLength) return string.Empty;
        // The moving end of a selection is SelectionEnd; CaretIndex stays at the anchor while
        // Shift is held, so the caret cannot say what was taken in or let go.
        var lo = Math.Min(before.SelectionEnd, after.SelectionEnd);
        var hi = Math.Max(before.SelectionEnd, after.SelectionEnd);
        var span = Spoken(after.Text[lo..hi]);
        return after.SelectionLength > before.SelectionLength ? $"{span} selected" : $"{span} unselected";
    }

    private static string CharAt(string text, int caret) => caret >= 0 && caret < text.Length ? Name(text[caret]) : "blank";

    private static string WordAt(string text, int caret)
    {
        if (caret >= text.Length) return "blank";
        var end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
        return Spoken(text[caret..end].TrimEnd());
    }

    /// <summary>A single character in words, as a screen reader names it.</summary>
    private static string Name(char c) => c switch
    {
        ' ' => "space",
        '.' => "dot",
        ',' => "comma",
        '-' => "dash",
        '_' => "underline",
        ':' => "colon",
        ';' => "semicolon",
        '/' => "slash",
        '\\' => "backslash",
        '@' => "at",
        '#' => "number",
        '\'' => "tick",
        '"' => "quote",
        _ => c.ToString()
    };

    private static string Spoken(string text) => text.Length == 1 ? Name(text[0]) : text.Length == 0 ? "blank" : text;
}
