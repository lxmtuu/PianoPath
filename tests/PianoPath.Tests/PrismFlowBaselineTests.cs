using System.Text.Json.Nodes;
using Xunit;

namespace PianoPath.Tests;

public class PrismFlowBaselineTests
{
    private static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "prism-flow-p0");

    [Fact]
    public void Shipping_reader_matches_every_manifest_note_and_keeps_physical_note_off()
    {
        var scene = PrismFlowBaseline.Load(FixtureDirectory);
        Assert.Equal(26, scene.Notes.Count);
        Assert.Equal(28, scene.Manifest.DurationSeconds);
        Assert.Equal(24, scene.Notes.Max(n => n.End));
        Assert.Contains(scene.Notes, n => n.Pitch == 21 && n.Track == 1);
        Assert.Contains(scene.Notes, n => n.Pitch == 108 && n.Velocity == 110);
        Assert.Equal(2, scene.Notes.Count(n => n.Pitch == 60 && n.Start >= 18));
        // CC64 is in the explicit fixture timeline, not sustained duration in MidiSong.
        Assert.All(scene.Notes.Where(n => n.Start == 12.25), n => Assert.Equal(13, n.End));
        Assert.Equal(4, scene.Notes.Count(n => n.Start == 12.25));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void Cursor_dispatches_every_event_once_without_wall_clock_or_frame_count_dependence(int fps)
    {
        var scene = PrismFlowBaseline.Load(FixtureDirectory);
        var cursor = scene.CreateCursor();
        var strikes = new List<NoteEvent>();
        var pedals = new List<(bool Down, double Level)>();
        for (var frame = 0; frame <= 28 * fps; frame++)
            cursor.AdvanceTo(frame / (double)fps, strikes.Add, (down, level) => pedals.Add((down, level)));
        cursor.AdvanceTo(28, strikes.Add, (down, level) => pedals.Add((down, level)));
        Assert.Equal(scene.Notes.Count, strikes.Count);
        Assert.Equal(scene.Notes.ToArray(), strikes.ToArray());
        Assert.Equal(new[] { (true, 1.0), (false, 0.0) }, pedals.ToArray());
    }

    [Fact]
    public void Pedal_is_still_down_after_physical_release_and_resets_only_at_16_seconds()
    {
        var scene = PrismFlowBaseline.Load(FixtureDirectory);
        var cursor = scene.CreateCursor();
        bool pedal = false;
        cursor.AdvanceTo(13.5, _ => { }, (down, _) => pedal = down);
        Assert.True(pedal);
        Assert.DoesNotContain(scene.Notes, n => n.Start <= 13.5 && n.End > 13.5);
        cursor.AdvanceTo(16, _ => { }, (down, _) => pedal = down);
        Assert.False(pedal);
    }

    [Fact]
    public void Backward_seek_requires_new_cursor_and_new_cursor_replays_from_zero()
    {
        var scene = PrismFlowBaseline.Load(FixtureDirectory);
        var cursor = scene.CreateCursor();
        cursor.AdvanceTo(20, _ => { }, (_, _) => { });
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.AdvanceTo(1, _ => { }, (_, _) => { }));
        var strikes = new List<NoteEvent>();
        scene.CreateCursor().AdvanceTo(1, strikes.Add, (_, _) => { });
        Assert.Single(strikes);
        Assert.Equal(21, strikes[0].Pitch);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("note")]
    [InlineData("controller")]
    [InlineData("checkpoint")]
    public void Invalid_or_drifted_input_is_rejected(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "prism-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(FixtureDirectory, "baseline.mid"), Path.Combine(directory, "baseline.mid"));
            var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "manifest.json")))!;
            switch (mutation)
            {
                case "checksum": doc["midiSha256"] = "bad"; break;
                case "note": doc["notes"]![0]!["velocity"] = 41; break;
                case "controller": doc["controls"]![0]!["controller"] = 123; break;
                case "checkpoint": doc["checkpoints"]![0]!["id"] = "../escape"; break;
            }
            File.WriteAllText(Path.Combine(directory, "manifest.json"), doc.ToJsonString());
            Assert.Throws<InvalidDataException>(() => PrismFlowBaseline.Load(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
