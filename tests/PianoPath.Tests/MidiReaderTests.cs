namespace PianoPath.Tests;

/// <summary>
/// The Standard MIDI File reader, checked against bytes written to the specification (see
/// <see cref="MidiFixture"/>) rather than against a file some application happened to save.
///
/// <para>
/// These mirror the MIDI assertions of <c>VerifyMidiImport</c> in the in-app suite. They are repeated
/// here on purpose: that suite only runs on Windows, so before this project a MIDI regression could
/// not fail a build until a Windows runner picked it up.
/// </para>
/// </summary>
public class MidiReaderTests
{
    [Fact]
    public void Format1_reads_every_track_and_drops_the_percussion_channel()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.Equal(3, song.Notes.Count);
            Assert.DoesNotContain(song.Notes, note => note.Pitch == 36);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Format1_keeps_pitch_track_and_velocity_of_each_note()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one-meta");
        try
        {
            var middleC = MidiReader.ReadSong(path).Notes.Single(note => note.Pitch == 60);

            Assert.Equal(0, middleC.Track);
            Assert.Equal(100, middleC.Velocity);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Default_tempo_maps_480_ticks_to_half_a_second()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one-ticks");
        try
        {
            var notes = MidiReader.ReadSong(path).Notes;
            var middleC = notes.Single(note => note.Pitch == 60);

            Assert.Equal(0, middleC.Start, 3);
            Assert.Equal(0.5, middleC.Duration, 3);
            Assert.Equal(0.25, notes.Single(note => note.Pitch == 64).Duration, 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_tempo_change_rescales_only_the_notes_after_it()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one-tempo");
        try
        {
            // The change to 240 BPM lands one beat in, so a beat after it is a quarter of a second.
            var changed = MidiReader.ReadSong(path).Notes.Single(note => note.Pitch == 67);

            Assert.Equal(0.5, changed.Start, 3);
            Assert.Equal(0.25, changed.Duration, 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_beat_grid_follows_the_tempo_map()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one-beats");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.True(song.BeatTimes.Count >= 3, $"expected at least three beats, got {song.BeatTimes.Count}");
            Assert.Equal(0.5, song.BeatTimes[1], 3);
            Assert.Equal(0.75, song.BeatTimes[2], 3);
            Assert.Equal(4, song.BeatsPerBar);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Track_name_meta_events_are_exposed_per_track()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one-names");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.True(song.TrackNames.TryGetValue(0, out var name));
            Assert.Equal("Lead", name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_note_only_reader_stays_compatible()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatOne(), "format-one-notes-only");
        try
        {
            Assert.Equal(3, MidiReader.Read(path).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Format2_lays_its_patterns_end_to_end_instead_of_stacking_them()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatTwo(), "format-two");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.Equal(2, song.Notes.Count);
            Assert.Equal(60, song.Notes[0].Pitch);
            Assert.Equal(0, song.Notes[0].Start, 3);
            // The second pattern starts where the first ended, not on top of it.
            Assert.Equal(67, song.Notes[1].Pitch);
            Assert.Equal(0.5, song.Notes[1].Start, 3);
            Assert.Equal(0.5, song.Notes[1].Duration, 3);
            Assert.Equal(1, song.Notes[1].Track);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Format2_keeps_the_beat_grid_running_across_the_patterns()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.FormatTwo(), "format-two-beats");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.Equal(new double[] { 0, 0.5 }, song.BeatTimes);
            Assert.True(song.TrackNames.TryGetValue(1, out var name));
            Assert.Equal("Lead", name);
            Assert.Equal(2, song.TrackNames.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_Smpte_division_reads_ticks_as_absolute_time()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.Smpte(), "smpte");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.Equal(2, song.Notes.Count);
            // 500 ticks at 25 frames × 40 ticks is half a second whatever the tempo says.
            Assert.Equal(0, song.Notes[0].Start, 3);
            Assert.Equal(0.5, song.Notes[0].Duration, 3);
            Assert.Equal(1, song.Notes[1].Start, 3);
            Assert.Equal(0.5, song.Notes[1].Duration, 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_Smpte_file_takes_its_beats_from_the_tempo_map()
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.Smpte(), "smpte-beats");
        try
        {
            var song = MidiReader.ReadSong(path);

            Assert.Equal(5, song.BeatTimes.Count);
            Assert.Equal(0.5, song.BeatTimes[1], 3);
            Assert.Equal(1, song.BeatTimes[2], 3);
            // After the tempo change a beat is a quarter of a second.
            Assert.Equal(1.25, song.BeatTimes[3], 3);
            Assert.Equal(1.5, song.BeatTimes[4], 3);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(3, 480)]        // a format this reader does not know
    [InlineData(1, 0xE520)]     // an SMPTE frame rate that is not one of the four
    [InlineData(1, 0xE700)]     // an SMPTE division that counts no ticks in a frame
    public void A_header_the_reader_cannot_use_is_refused_not_read_as_noise(int format, int division)
    {
        var path = MidiFixture.WriteToTempFile(MidiFixture.HeaderOnly(format, division), $"bad-{format}-{division}");
        try
        {
            Assert.Throws<InvalidDataException>(() => MidiReader.ReadSong(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_file_that_is_not_a_MIDI_file_at_all_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-tests-not-midi-{Guid.NewGuid():N}.mid");
        File.WriteAllText(path, "this is not a MIDI file");
        try
        {
            Assert.Throws<InvalidDataException>(() => MidiReader.ReadSong(path));
        }
        finally { File.Delete(path); }
    }
}

/// <summary>The binary search the falling notes and the playhead share, over notes sorted by start.</summary>
public class NoteTimelineTests
{
    private static List<NoteEvent> NotesAt(params double[] starts) =>
        [.. starts.Select((start, index) => new NoteEvent { Pitch = 60 + index, Start = start, Duration = 0.25 })];

    [Fact]
    public void Finds_the_first_note_at_or_after_a_time()
    {
        var notes = NotesAt(0, 0.25, 0.5);

        Assert.Equal(2, NoteTimeline.FirstIndexAtOrAfter(notes, 0.5));
    }

    [Fact]
    public void A_time_past_the_last_note_points_one_past_the_end()
    {
        var notes = NotesAt(0, 0.25, 0.5);

        Assert.Equal(notes.Count, NoteTimeline.FirstIndexAtOrAfter(notes, 9));
    }

    [Fact]
    public void A_time_before_the_first_note_points_at_the_start()
    {
        var notes = NotesAt(0, 0.25, 0.5);

        Assert.Equal(0, NoteTimeline.FirstIndexAtOrAfter(notes, -1));
    }

    [Fact]
    public void An_empty_song_has_nothing_to_find()
    {
        Assert.Equal(0, NoteTimeline.FirstIndexAtOrAfter(NotesAt(), 0));
    }
}
