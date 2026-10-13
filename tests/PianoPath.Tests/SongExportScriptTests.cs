using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks how a song becomes the tick script of an offline take (Video/ArVideoExportScript.cs, roadmap
/// §14.10 AR‑S7): onsets land on the tick that contains them, releases always trail their press, and a
/// shared tick releases first so a re-press survives.
/// </summary>
public class SongExportScriptTests
{
    [Fact]
    public void ANoteLandsOnTheTicksItsSecondsCover()
    {
        var script = ArVideoExportScript.MapNotes([(0.30, 0.55, 60, 110)], frames: 12, frameRate: 12);
        Assert.Equal(new[] { new ArVideoNoteEvent(60, 110, true) }, script[3]);   // floor(3.6) — the press's tick
        Assert.Equal(new[] { new ArVideoNoteEvent(60, 0, false) }, script[6]);    // floor(6.6) — the release's tick
        Assert.Equal(2, script.Count);
    }

    [Fact]
    public void ASharedTickReleasesBeforeItPresses()
    {
        var script = ArVideoExportScript.MapNotes([(0.50, 0.60, 60, 90), (0.62, 1.10, 60, 110)], frames: 24, frameRate: 12);
        var onTickSeven = script[7];                                              // release of note 1 (7.2) and press of note 2 (7.44)
        Assert.Equal(new[] { new ArVideoNoteEvent(60, 0, false), new ArVideoNoteEvent(60, 110, true) }, onTickSeven);
    }

    [Fact]
    public void SubTickNotesStillSoundForOneTick()
    {
        var script = ArVideoExportScript.MapNotes([(0.10, 0.11, 60, 90)], frames: 12, frameRate: 12);
        Assert.Equal(new[] { new ArVideoNoteEvent(60, 90, true) }, script[1]);
        Assert.Equal(new[] { new ArVideoNoteEvent(60, 0, false) }, script[2]);    // one tick is the shortest voice
    }

    [Fact]
    public void NotesOutsideTheTakeAreClipped()
    {
        var script = ArVideoExportScript.MapNotes(
            [(-1.0, -0.5, 50, 90), (99.0, 100.0, 60, 110), (0.0, 99.0, 72, 100)], frames: 6, frameRate: 12);
        Assert.Single(script);                                                     // only note 3 starts inside
        Assert.Equal(new[] { new ArVideoNoteEvent(72, 100, true) }, script[0]);    // its release lies past the take, so the note simply rings into the tail
    }

    [Fact]
    public void AVelocityOutsideMidiRangeIsReconciled()
    {
        var script = ArVideoExportScript.MapNotes([(0.0, 0.5, 60, 900)], frames: 12, frameRate: 12);
        Assert.Equal(127, script[0][0].Velocity);
    }

    [Fact]
    public void AnUnscriptedTickFiresNothing()
    {
        Assert.Empty(ArVideoExportScript.EventsForTick(new Dictionary<int, ArVideoNoteEvent[]>(), 4));
    }
}
