using System.IO.Compression;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The MusicXML reader, driven by scores written inline so a test shows the markup it means.
///
/// <para>
/// The fixtures are the ones <c>VerifyMusicXmlImport</c> feeds <c>--verify</c>; the expected values are
/// copied from that check, so the two suites disagree loudly if one of them is edited alone.
/// </para>
/// </summary>
public class MusicXmlReaderTests
{
    /// <summary>
    /// A piano bar of 2/4 at 60 bpm with two divisions per quarter, so a quarter note is exactly one
    /// second: the right hand plays C4 then an E4+G4 chord, the left hand enters after a backup, and
    /// the second measure doubles the tempo.
    /// </summary>
    private const string TwoHands = """
<score-partwise version="4.0">
  <work><work-title>Fixture Waltz</work-title></work>
  <identification><creator type="composer">Nobody</creator></identification>
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1">
    <measure number="1">
      <attributes><divisions>2</divisions><staves>2</staves><time><beats>2</beats><beat-type>4</beat-type></time></attributes>
      <direction><sound tempo="60"/></direction>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <note><pitch><step>E</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <note><chord/><pitch><step>G</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <backup><duration>4</duration></backup>
      <note><pitch><step>E</step><octave>3</octave></pitch><duration>2</duration><staff>2</staff></note>
      <note><pitch><step>G</step><octave>3</octave></pitch><duration>1</duration><staff>2</staff></note>
      <forward><duration>1</duration></forward>
    </measure>
    <measure number="2">
      <direction><sound tempo="120"/></direction>
      <note><pitch><step>D</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <backup><duration>2</duration></backup>
      <note><pitch><step>C</step><octave>3</octave></pitch><duration>2</duration><staff>2</staff></note>
      <note><rest/><duration>2</duration></note>
    </measure>
  </part>
</score-partwise>
""";

    [Fact]
    public void A_score_carries_its_title_composer_measure_count_and_time_signature()
    {
        var score = MusicXmlReader.Parse(TwoHands);

        Assert.Equal("Fixture Waltz", score.Title);
        Assert.Equal("Nobody", score.Composer);
        Assert.Equal(2, score.MeasureCount);
        Assert.Equal(2, score.BeatsPerBar);
    }

    [Fact]
    public void Divisions_tempo_chords_backup_and_forward_place_every_note_in_seconds()
    {
        var score = MusicXmlReader.Parse(TwoHands);
        var right = score.Notes.Where(note => note.Pitch >= 60).OrderBy(note => note.Start).Select(Describe).ToList();
        var left = score.Notes.Where(note => note.Pitch < 60).OrderBy(note => note.Start).Select(Describe).ToList();

        Assert.Equal(["60@0+1/t0", "64@1+1/t0", "67@1+1/t0", "62@2+0.5/t0"], right);
        Assert.Equal(["52@0+1/t0", "55@1+0.5/t0", "48@2+0.5/t0"], left);
    }

    [Fact]
    public void Two_staves_state_the_hand_split_themselves()
    {
        var score = MusicXmlReader.Parse(TwoHands);

        Assert.True(score.SplitFromStaves);
        Assert.Equal(57, score.HandSplitPitch);
    }

    [Fact]
    public void The_beat_grid_follows_the_time_signature_and_the_tempo_of_each_measure()
    {
        var score = MusicXmlReader.Parse(TwoHands);

        Assert.True(score.BeatTimes.Count >= 4, $"expected at least four beats, got {score.BeatTimes.Count}");
        Assert.Equal(0, score.BeatTimes[0], 9);
        Assert.Equal(1, score.BeatTimes[1], 9);
        Assert.Equal(2, score.BeatTimes[2], 9);
        // Measure 2 is twice as fast, so its beat is half as long.
        Assert.Equal(2.5, score.BeatTimes[3], 9);
    }

    [Fact]
    public void The_part_list_names_the_track_a_note_carries_and_the_score_hands_itself_over_as_a_song()
    {
        var score = MusicXmlReader.Parse(TwoHands);
        var song = score.ToSong();

        Assert.Equal("Piano", score.TrackNames[0]);
        Assert.Equal(score.Notes.Count, song.Notes.Count);
        Assert.Equal(2, song.BeatsPerBar);
    }

    [Fact]
    public void A_compound_signature_beats_in_threes_and_that_does_not_move_the_notes()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1"><measure number="1">
    <attributes><divisions>2</divisions><time><beats>6</beats><beat-type>8</beat-type></time></attributes>
    <direction><sound tempo="60"/></direction>
    <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration></note>
    <note><pitch><step>D</step><octave>4</octave></pitch><duration>2</duration></note>
    <note><pitch><step>E</step><octave>4</octave></pitch><duration>2</duration></note>
  </measure></part>
</score-partwise>
""");

        // Three quarters of music written in 6/8 are two dotted-quarter beats, and the grid keeps that
        // step past the bar so the metronome still has a beat to stand on.
        Assert.Equal(2, score.BeatsPerBar);
        Assert.True(score.BeatTimes.Count >= 3, $"expected at least three beats, got {score.BeatTimes.Count}");
        Assert.Equal(0, score.BeatTimes[0], 9);
        Assert.Equal(1.5, score.BeatTimes[1], 9);
        Assert.Equal(3, score.BeatTimes[2], 9);
        Assert.Equal(3, score.Notes.Count);
        Assert.Equal(2, score.Notes[^1].Start, 9);
    }

    [Fact]
    public void Two_parts_are_simultaneous_named_and_split_by_the_part_each_note_is_in()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Right</part-name></score-part><score-part id="P2"><part-name>Left</part-name></score-part></part-list>
  <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration></note></measure></part>
  <part id="P2"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><pitch><step>C</step><octave>3</octave></pitch><duration>1</duration></note></measure></part>
</score-partwise>
""");

        Assert.Equal(2, score.Notes.Count);
        Assert.Contains(score.Notes, note => note is { Pitch: 72, Track: 0 });
        Assert.Contains(score.Notes, note => note is { Pitch: 48, Track: 1 });
        Assert.Equal("Right", score.TrackNames[0]);
        Assert.Equal("Left", score.TrackNames[1]);
        Assert.Equal(60, score.HandSplitPitch);
    }

    [Fact]
    public void A_tempo_change_inside_a_measure_only_changes_time_after_its_position()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1">
    <measure number="1">
      <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
      <direction><sound tempo="60"/></direction>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>1</duration></note>
      <direction><sound tempo="120"/></direction>
      <note><pitch><step>D</step><octave>4</octave></pitch><duration>1</duration></note>
    </measure>
    <measure number="2"><note><pitch><step>E</step><octave>4</octave></pitch><duration>1</duration></note></measure>
  </part>
</score-partwise>
""");

        Assert.Equal("60@0+1/t0", Describe(score.Notes[0]));
        Assert.Equal("62@1+0.5/t0", Describe(score.Notes[1]));
        Assert.Equal("64@2.5+0.5/t0", Describe(score.Notes[2]));
        Assert.Equal(new[] { 0, 4 }, score.DownbeatIndices.ToArray());
    }

    [Fact]
    public void Tempo_marks_in_one_part_apply_to_simultaneous_parts()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Right</part-name></score-part><score-part id="P2"><part-name>Left</part-name></score-part></part-list>
  <part id="P1"><measure number="1">
    <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
    <direction><sound tempo="60"/></direction>
    <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration></note>
  </measure></part>
  <part id="P2"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><pitch><step>C</step><octave>3</octave></pitch><duration>1</duration></note>
  </measure></part>
</score-partwise>
""");

        Assert.Equal("48@0+1/t1", Describe(score.Notes[0]));
        Assert.Equal("72@0+1/t0", Describe(score.Notes[1]));
    }

    [Fact]
    public void A_divisions_change_rebases_subsequent_durations_without_moving_the_cursor()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1"><measure number="1">
    <attributes><divisions>2</divisions></attributes>
    <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration></note>
    <attributes><divisions>4</divisions></attributes>
    <note><pitch><step>D</step><octave>4</octave></pitch><duration>4</duration></note>
  </measure></part>
</score-partwise>
""");

        Assert.Equal("60@0+0.5/t0", Describe(score.Notes[0]));
        Assert.Equal("62@0.5+0.5/t0", Describe(score.Notes[1]));
    }

    [Fact]
    public void A_metronome_mark_converts_its_written_beat_unit_and_dots_to_quarter_bpm()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1"><measure number="1">
    <attributes><divisions>2</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
    <direction><direction-type><metronome><beat-unit>eighth</beat-unit><per-minute>120</per-minute></metronome></direction-type></direction>
    <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration></note>
    <direction><direction-type><metronome><beat-unit>quarter</beat-unit><beat-unit-dot/><per-minute>80</per-minute></metronome></direction-type></direction>
    <note><pitch><step>D</step><octave>4</octave></pitch><duration>2</duration></note>
  </measure></part>
</score-partwise>
""");

        Assert.Equal(1, score.Notes[0].Duration, 9);
        Assert.Equal(.5, score.Notes[1].Duration, 9);
        Assert.Equal(1, score.BeatTimes[1], 9);
        Assert.Equal(1.5, score.BeatTimes[2], 9);
    }

    [Fact]
    public void Mixed_meter_preserves_each_measure_start_for_the_sheet_and_metronome()
    {
        var score = MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1">
    <measure number="1">
      <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration></note>
    </measure>
    <measure number="2">
      <attributes><time><beats>3</beats><beat-type>4</beat-type></time></attributes>
      <note><pitch><step>D</step><octave>4</octave></pitch><duration>3</duration></note>
    </measure>
  </part>
</score-partwise>
""");

        Assert.Equal(4, score.BeatsPerBar);
        Assert.Equal(new[] { 0, 4 }, score.DownbeatIndices.ToArray());
        Assert.Equal(4, score.ToSong().BeatsPerBar);
        Assert.True(score.DownbeatIndices.SequenceEqual(score.ToSong().DownbeatIndices));
    }

    [Fact]
    public void Compressed_scores_are_rejected_when_the_decompressed_xml_exceeds_the_limit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-tests-oversized-{Guid.NewGuid():N}.mxl");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("oversized.musicxml").Open()))
                writer.Write(new string(' ', MusicXmlReader.MaxXmlCharacters + 1));

            Assert.Throws<InvalidDataException>(() => MusicXmlReader.ReadCompressed(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void A_score_that_would_expand_to_too_many_beats_is_refused_safely()
    {
        Assert.Throws<InvalidDataException>(() => MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><pitch><step>C</step><octave>4</octave></pitch><duration>300000</duration></note>
  </measure></part>
</score-partwise>
"""));
    }

    [Fact]
    public void Something_that_is_not_MusicXML_is_refused_instead_of_read_as_empty()
    {
        Assert.Throws<InvalidDataException>(() => MusicXmlReader.Parse("<html><body>not a score</body></html>"));
    }

    [Fact]
    public void A_score_with_no_notes_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => MusicXmlReader.Parse("""
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><rest/><duration>1</duration></note></measure></part>
</score-partwise>
"""));
    }

    private static string Describe(NoteEvent note) => $"{note.Pitch}@{note.Start:0.###}+{note.Duration:0.###}/t{note.Track}";
}
