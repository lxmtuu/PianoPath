namespace PianoPath.Tests;

/// <summary>
/// The hand-split guess, which decides where the left hand ends and the right one begins when the user
/// lets the app choose. The expectations are the ones <c>VerifyHandSplitInference</c> asserts.
/// </summary>
public class HandSplitTests
{
    private static List<NoteEvent> Notes(params (int Pitch, double Duration)[] entries) =>
        [.. entries.Select(entry => new NoteEvent { Pitch = entry.Pitch, Duration = entry.Duration })];

    private static List<NoteEvent> Range(int from, int count, double duration) =>
        [.. Enumerable.Range(from, count).Select(pitch => new NoteEvent { Pitch = pitch, Duration = duration })];

    [Fact]
    public void A_wide_gap_between_the_hands_splits_at_middle_c()
    {
        var twoHands = Notes([.. Range(36, 13, .5).Select(note => (note.Pitch, note.Duration)),
                              .. Range(67, 18, .5).Select(note => (note.Pitch, note.Duration))]);

        Assert.Equal(HandSplit.MiddleC, HandSplit.Infer(twoHands, 55));
    }

    [Fact]
    public void The_answer_does_not_depend_on_how_often_it_is_computed()
    {
        var twoHands = Notes([.. Range(36, 13, .5).Select(note => (note.Pitch, note.Duration)),
                              .. Range(67, 18, .5).Select(note => (note.Pitch, note.Duration))]);

        Assert.Equal(HandSplit.Infer(twoHands, 55), HandSplit.Infer(twoHands, 55));
    }

    [Fact]
    public void A_tight_two_hand_arrangement_still_splits_at_middle_c()
    {
        var separated = Notes([.. Range(40, 11, 1.0).Select(note => (note.Pitch, note.Duration)),
                               .. Range(70, 11, 1.0).Select(note => (note.Pitch, note.Duration))]);

        Assert.Equal(HandSplit.MiddleC, HandSplit.Infer(separated, 60));
    }

    [Fact]
    public void A_song_that_lives_in_one_hand_keeps_the_split_the_user_chose()
    {
        var oneHand = Range(72, 8, .5);

        Assert.Equal(55, HandSplit.Infer(oneHand, 55));
    }

    [Theory]
    [InlineData(55, 55)]
    [InlineData(500, 108)]   // above the playable range
    [InlineData(5, 21)]      // below it
    public void An_empty_song_keeps_the_chosen_split_clamped_to_the_playable_range(double fallback, int expected)
    {
        Assert.Equal(expected, HandSplit.Infer([], fallback));
    }

    [Fact]
    public void A_stray_short_note_does_not_create_a_left_hand_out_of_nothing()
    {
        var stray = Notes([.. Range(60, 5, 1.0).Select(note => (note.Pitch, note.Duration)), (30, .04)]);

        Assert.Equal(62, HandSplit.Infer(stray, 62));
    }

    [Fact]
    public void A_slow_bass_under_a_fast_melody_still_counts_as_two_hands()
    {
        // Weighted by sounding time, not by note count: four long bass notes outweigh sixteen short ones.
        var weighted = Notes([.. Range(45, 4, 2.0).Select(note => (note.Pitch, note.Duration)),
                              .. Range(76, 16, .25).Select(note => (note.Pitch, note.Duration))]);

        Assert.Equal(HandSplit.MiddleC, HandSplit.Infer(weighted, 55));
    }

    [Fact]
    public void Hands_that_overlap_through_a_smooth_run_cannot_be_told_apart()
    {
        var overlapping = Range(48, 20, .5);

        Assert.Equal(58, HandSplit.Infer(overlapping, 58));
    }

    [Fact]
    public void Four_empty_semitones_are_too_few_to_read_as_a_hand_separation()
    {
        var narrowGap = Notes([.. Range(50, 6, 1.0).Select(note => (note.Pitch, note.Duration)),
                               .. Range(60, 6, 1.0).Select(note => (note.Pitch, note.Duration))]);

        Assert.Equal(54, HandSplit.Infer(narrowGap, 54));
    }

    [Fact]
    public void Five_empty_semitones_are_enough()
    {
        var justEnough = Notes([.. Range(50, 6, 1.0).Select(note => (note.Pitch, note.Duration)),
                                .. Range(61, 6, 1.0).Select(note => (note.Pitch, note.Duration))]);

        Assert.Equal(HandSplit.MiddleC, HandSplit.Infer(justEnough, 54));
    }
}

/// <summary>How a written time signature is felt, which is what the beat grid and the sheet both need.</summary>
public class MeterTests
{
    [Theory]
    [InlineData(4, 4, 4, 1)]    // four quarter beats
    [InlineData(3, 4, 3, 1)]    // three
    [InlineData(2, 2, 2, 1)]    // two half ones
    [InlineData(3, 8, 3, 1)]    // three eighths: a simple meter beats on the beat-type itself
    [InlineData(5, 4, 5, 1)]    // an irregular simple meter keeps its written beats
    [InlineData(6, 8, 2, 3)]    // compound: two dotted-quarter beats, not six eighths
    [InlineData(9, 8, 3, 3)]    // three
    [InlineData(12, 8, 4, 3)]   // four
    public void A_signature_is_felt_in_the_grouping_a_score_beams_by(int numerator, int denominator, int beats, int unitsPerBeat)
    {
        Assert.Equal((beats, unitsPerBeat), Meter.Of(numerator, denominator));
    }

    [Theory]
    [InlineData(1, 4, 1.0)]     // a quarter beat is one quarter note long
    [InlineData(1, 8, 0.5)]     // an eighth beat is half of one
    [InlineData(3, 8, 1.5)]     // a dotted quarter, which is what 6/8 beats on
    [InlineData(1, 2, 2.0)]     // a half-note beat
    public void One_felt_beat_is_measured_in_quarter_notes(int unitsPerBeat, int denominator, double quarters)
    {
        Assert.Equal(quarters, Meter.BeatInQuarters(unitsPerBeat, denominator), 9);
    }

    [Fact]
    public void A_degenerate_signature_cannot_produce_zero_beats()
    {
        Assert.True(Meter.Of(0, 4).Beats >= 1);
        Assert.True(Meter.BeatInQuarters(1, 0) > 0);
    }
}
