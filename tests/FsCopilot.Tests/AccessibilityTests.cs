namespace FsCopilot.Tests;

using FsCopilot.Accessibility;
using FsCopilot.Simulation;
using FsCopilot.ViewModels;
using Avalonia.Media;

public class TextEchoTests
{
    [Theory]
    [InlineData('A', "A")]
    [InlineData(' ', "space")]
    [InlineData('.', "dot")]
    [InlineData(':', "colon")]
    [InlineData('-', "dash")]
    public void A_character_is_named_as_a_screen_reader_names_it(char c, string spoken) =>
        Assert.Equal(spoken, TextEcho.Name(c));

    [Fact]
    public void The_character_at_the_cursor_or_blank_past_the_end()
    {
        Assert.Equal("B", TextEcho.CharAt("AB.CD", 1));
        Assert.Equal("dot", TextEcho.CharAt("AB.CD", 2));
        Assert.Equal("blank", TextEcho.CharAt("AB.CD", 5));
    }

    [Fact]
    public void Backspace_names_what_it_deleted()
    {
        var before = new TextEcho.Snapshot("AB.CD", 3, 3, 3);
        Assert.Equal("dot", TextEcho.Deleted(before, word: false));
        Assert.Equal(string.Empty, TextEcho.Deleted(new TextEcho.Snapshot("AB", 0, 0, 0), word: false));
    }

    [Fact]
    public void Ctrl_Backspace_names_the_word()
    {
        var before = new TextEcho.Snapshot("join pilot", 10, 10, 10);
        Assert.Equal("pilot", TextEcho.Deleted(before, word: true));
    }

    [Fact]
    public void Shift_selection_reads_the_moving_end_not_the_anchor()
    {
        // CaretIndex stays at the anchor while Shift is held; SelectionEnd moves.
        var before = new TextEcho.Snapshot("B.CD", 0, 0, 0);
        var after = new TextEcho.Snapshot("B.CD", 0, 0, 1);
        Assert.Equal("B selected", TextEcho.Selection(before, after));
        Assert.Equal("B unselected", TextEcho.Selection(after, before));
    }

    [Fact]
    public void Ctrl_Right_reads_the_word_from_the_cursor()
    {
        Assert.Equal("pilot", TextEcho.WordAt("join pilot", 5));
        Assert.Equal("blank", TextEcho.WordAt("join", 4));
    }
}

public class SpokenTextTests
{
    [Fact]
    public void A_code_is_spelled()
    {
        Assert.Equal("Q F Q Y", MainViewModel.Spell("QFQY"));
    }

    [Fact]
    public void An_onboard_row_names_the_pilot_and_keeps_the_changing_figures_in_its_description()
    {
        var item = new ConnectionItem("ABCD1234");
        item.Update("Alpha", 87, 0f, isDirect: false);
        Assert.Equal("Alpha, A B C D 1 2 3 4, relay, excellent", item.Description);
        Assert.Equal("Ping 87 ms, loss 0.0%", item.Details);
        item.Update("Alpha", 450, 12f, isDirect: true);
        Assert.Equal("Alpha, A B C D 1 2 3 4, direct, poor", item.Description);
    }

    [Fact]
    public void A_sync_log_entry_says_its_severity_in_words()
    {
        var time = new DateTime(2026, 10, 20, 16, 43, 0);
        Assert.Equal("Warning: Weather sync unavailable, 16:43",
            new MainViewModel.SyncLogItem(time, "Weather sync unavailable", Brushes.Yellow, EnvironmentSync.SyncLevel.Warning).Spoken);
        Assert.Equal("Error: Connection lost, 16:43",
            new MainViewModel.SyncLogItem(time, "Connection lost", Brushes.Red, EnvironmentSync.SyncLevel.Error).Spoken);
        Assert.Equal("Left the session, 16:43",
            new MainViewModel.SyncLogItem(time, "Left the session", Brushes.Gray, EnvironmentSync.SyncLevel.Info).Spoken);
    }
}

public class ProfileTests
{
    private static string Definitions => Path.Combine(AppContext.BaseDirectory, "Definitions");

    [Fact]
    public void Notes_come_from_the_profile_then_its_modules_with_empty_ones_skipped()
    {
        Directory.CreateDirectory(Path.Combine(Definitions, "modules"));
        File.WriteAllText(Path.Combine(Definitions, "modules", "zz_test_module.yaml"),
            "notes:\n  - \"Module note\"\nshared:\n  - get: L:ZZ_TEST\n");
        File.WriteAllText(Path.Combine(Definitions, "zz_test_profile.yaml"),
            "include:\n  - modules/zz_test_module.yaml\nnotes:\n  - \"One\"\n  - \"\"\n  - \"Two\"\npointer:\n  - DisplayUnits\nmaster:\n  - get: A:PLANE ALTITUDE, Feet\n");

        var defs = Simulation.Definitions.Load("zz_test_profile");

        Assert.Equal(["One", "Two", "Module note"], defs.Notes);
        Assert.Equal(["DisplayUnits"], defs.Pointer);
        Assert.Equal(2, defs.Count);
    }

    [Fact]
    public void Every_bundled_profile_loads()
    {
        var problems = Directory.GetFiles(Definitions, "*.yaml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not "screens" && !name!.StartsWith("zz_test"))
            .Where(name => Simulation.Definitions.Load(name!).Count == 0)
            .ToArray();
        Assert.Empty(problems);
    }
}
