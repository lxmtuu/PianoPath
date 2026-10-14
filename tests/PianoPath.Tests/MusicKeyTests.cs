using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The key a song is written in: the Krumhansl–Kessler guess that decides which signature is drawn at the
/// head of both staves of the sheet layer, and the spelling that follows from it.
///
/// <para>
/// The file is pure — no WPF, no WinMM, no Vortice — so it can be checked on any machine; before this
/// suite its only coverage was the Windows-only <c>VerifySheetLayer</c> half of <c>--verify</c>. Every
/// expectation here repeats one that <c>VerifySheetLayer</c> asserts, so a change to the inference now
/// fails the Linux job too instead of waiting for a Windows runner to be free.
/// </para>
/// </summary>
public class MusicKeyTests
{
    /// <summary>Ten notes of the major scale on <paramref name="tonic"/>, with the tonic triad at the end.</summary>
    private static List<NoteEvent> MajorScale(int tonic) =>
        [.. new[] { 0, 2, 4, 5, 7, 9, 11, 0, 4, 7 }.Select(step => new NoteEvent { Pitch = 60 + tonic + step, Duration = 1 })];

    /// <summary>The natural minor scale on <paramref name="tonic"/>, same shape as <see cref="MajorScale"/>.</summary>
    private static List<NoteEvent> MinorScale(int tonic) =>
        [.. new[] { 0, 2, 3, 5, 7, 8, 10, 0, 3, 7 }.Select(step => new NoteEvent { Pitch = 60 + tonic + step, Duration = 1 })];

    [Fact]
    public void An_empty_song_is_written_without_a_signature()
    {
        var key = MusicKey.Infer([]);

        Assert.Equal(MusicKey.CMajor, key);
        Assert.Equal(0, key.Accidentals);
        Assert.False(key.UsesFlats);
        Assert.Equal(0, key.Count);
    }

    [Fact]
    public void C_major_gets_no_signature_at_all()
    {
        var key = MusicKey.Infer(MajorScale(0));

        Assert.Equal(0, key.TonicPitchClass);
        Assert.False(key.IsMinor);
        Assert.Equal(0, key.Accidentals);
    }

    [Theory]
    [InlineData(0, 0)]      // C major
    [InlineData(7, 1)]      // G major — one sharp
    [InlineData(2, 2)]      // D major — two sharps
    [InlineData(5, -1)]     // F major — one flat
    [InlineData(10, -2)]    // B♭ major — two flats
    public void A_major_scale_is_read_back_as_its_own_key(int tonic, int accidentals)
    {
        var key = MusicKey.Infer(MajorScale(tonic));

        Assert.Equal(tonic, key.TonicPitchClass);
        Assert.False(key.IsMinor);
        Assert.Equal(accidentals, key.Accidentals);
    }

    [Fact]
    public void A_minor_song_reports_the_minor_tonic_and_the_major_signature_it_shares()
    {
        // "A minor-key scale takes the signature of the major key three semitones above its tonic:
        //  one sharp for E, none for A."
        var aMinor = MusicKey.Infer(MinorScale(9));
        var eMinor = MusicKey.Infer(MinorScale(4));

        Assert.Equal(9, aMinor.TonicPitchClass);
        Assert.True(aMinor.IsMinor);
        Assert.Equal(0, aMinor.Accidentals);      // A minor writes the empty signature of C major
        Assert.Equal(4, eMinor.TonicPitchClass);
        Assert.True(eMinor.IsMinor);
        Assert.Equal(1, eMinor.Accidentals);      // E minor writes G major's single sharp
    }

    [Fact]
    public void A_song_that_fits_no_key_keeps_the_plain_c_major_spelling()
    {
        // Every pitch class the same length: the correlation is zero for all twelve tonics, so nothing
        // may be invented and the song is written without a signature.
        var chromatic = new List<NoteEvent>();
        for (var pitch = 60; pitch < 72; pitch++) chromatic.Add(new NoteEvent { Pitch = pitch, Duration = 1 });

        var key = MusicKey.Infer(chromatic);

        Assert.Equal(MusicKey.CMajor, key);
        Assert.Equal(0, key.Accidentals);
    }

    [Fact]
    public void A_loop_of_one_note_is_written_in_the_key_of_that_note()
    {
        // Not the no-fit case: both profiles peak on their own tonic, so a single sounding pitch class
        // carries the correlation past FitThreshold and the loop is written in that note's key. C — whose
        // signature is empty — is the one where that is invisible.
        var onC = MusicKey.Infer([.. Enumerable.Repeat(new NoteEvent { Pitch = 60, Duration = 1 }, 9)]);
        var onFSharp = MusicKey.Infer([.. Enumerable.Repeat(new NoteEvent { Pitch = 66, Duration = 1 }, 9)]);

        Assert.Equal(MusicKey.CMajor, onC);
        Assert.Equal(6, onFSharp.TonicPitchClass);
        Assert.Equal(6, onFSharp.Accidentals);    // F♯ major, six sharps
    }

    [Fact]
    public void A_long_held_note_cannot_outvote_the_melody()
    {
        // The four-second cap inside Infer exists for this song: forty seconds of melody decide the key,
        // and a note held underneath it counts for four. Without the cap the drone's own pitch class would
        // carry six hundred seconds of weight and the piece would come out in F major.
        var melody = new List<NoteEvent>();
        for (var repeat = 0; repeat < 4; repeat++) melody.AddRange(MajorScale(7));
        var keyOfTheMelody = MusicKey.Infer(melody);
        var withShortDrone = MusicKey.Infer([new NoteEvent { Pitch = 65, Duration = 4 }, .. melody]);
        var withLongDrone = MusicKey.Infer([new NoteEvent { Pitch = 65, Duration = 600 }, .. melody]);

        Assert.Equal(7, keyOfTheMelody.TonicPitchClass);
        Assert.Equal(keyOfTheMelody, withShortDrone);
        Assert.Equal(keyOfTheMelody, withLongDrone);
    }

    [Fact]
    public void Signed_letters_follow_the_order_a_signature_writes_them()
    {
        var gMajor = MusicKey.Infer(MajorScale(7));     // one sharp
        var fMajor = MusicKey.Infer(MajorScale(5));     // one flat

        Assert.Equal(new[] { 3 }, gMajor.SignedLetters.ToArray());   // F is letter 3, counting C D E F
        Assert.Equal(new[] { 6 }, fMajor.SignedLetters.ToArray());   // B is letter 6
        Assert.Equal(1, gMajor.Signature(3));
        Assert.Equal(0, gMajor.Signature(6));
        Assert.Equal(-1, fMajor.Signature(6));
        Assert.Equal(0, fMajor.Signature(3));
    }

    [Fact]
    public void Spelling_leans_the_way_the_signature_does()
    {
        // "F major should write B♭ on the B line and C major A♯ on the A line."
        var cMajor = MusicKey.CMajor;
        var fMajor = MusicKey.Infer(MajorScale(5));
        var dMajor = MusicKey.Infer(MajorScale(2));

        Assert.Equal((6, 4, -1), fMajor.Spell(70));
        Assert.Equal((5, 4, 1), cMajor.Spell(70));
        // "A note the key already writes is spelled without a sign — G♮ in F major — while a chromatic
        //  note leans the way the signature does: F♯ in D major, G♭ in F major."
        Assert.Equal((4, 4, 0), fMajor.Spell(67));
        Assert.Equal((3, 4, 1), dMajor.Spell(66));
        Assert.Equal((4, 4, -1), fMajor.Spell(66));
    }

    [Fact]
    public void Every_pitch_spells_back_to_the_pitch_that_was_played()
    {
        int[] letterBase = [0, 2, 4, 5, 7, 9, 11];
        var keys = new List<MusicKey> { MusicKey.CMajor };
        for (var tonic = 0; tonic < 12; tonic++)
        {
            keys.Add(MusicKey.Infer(MajorScale(tonic)));
            keys.Add(MusicKey.Infer(MinorScale(tonic)));
        }

        foreach (var key in keys)
        {
            for (var pitch = 0; pitch < 128; pitch++)
            {
                var (letter, octave, alteration) = key.Spell(pitch);
                Assert.InRange(letter, 0, 6);
                Assert.InRange(alteration, -1, 1);
                Assert.Equal(pitch, (octave + 1) * 12 + letterBase[letter] + alteration);
            }
        }
    }

    [Fact]
    public void A_pitch_outside_the_midi_range_is_clamped_instead_of_thrown()
    {
        var key = MusicKey.Infer(MajorScale(7));

        Assert.Equal(key.Spell(0), key.Spell(-5));
        Assert.Equal(key.Spell(127), key.Spell(200));
    }
}
