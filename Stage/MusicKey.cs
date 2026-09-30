namespace PianoPath;

/// <summary>
/// The key a song is written in: the tonic, the mode, and therefore the signature drawn at the head of every
/// staff of the sheet layer.
///
/// <para>
/// <see cref="Accidentals"/> is the one number the drawing and the spelling both need: positive counts sharps,
/// negative counts flats and zero is C major / A minor, which writes nothing at all. A key never mixes the two
/// (no real signature does), so a single signed count is enough and no key in this file can be half sharp and
/// half flat.
/// </para>
///
/// <para>
/// The key of a song is <em>inferred</em> from how long each pitch class sounds, by correlating those weights
/// with the classic Krumhansl–Kessler major and minor profiles over all twelve tonics. A song that fits no key
/// well enough (a chromatic cluster, a drum-like loop of one note) keeps the plain C major spelling instead of
/// inventing a signature.
/// </para>
/// </summary>
internal readonly record struct MusicKey(int TonicPitchClass, bool IsMinor, int Accidentals)
{
    /// <summary>What a song that fits no key is written in: no sign at the head of the staff.</summary>
    internal static readonly MusicKey CMajor = new(0, false, 0);

    /// <summary>How well a song has to fit a key before its signature is drawn at all.</summary>
    internal const double FitThreshold = .5;

    /// <summary>The pitch class of each letter written on the staff: C D E F G A B.</summary>
    private static readonly int[] LetterBase = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The order a signature adds sharps (F C G D A E B) and flats (B E A D G C F), as letter indices.</summary>
    private static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6];
    private static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3];

    /// <summary>
    /// The signature of each major tonic, in the order C, C♯/D♭, D … : positive is that many sharps and negative
    /// that many flats, and the tonic spelling with the fewer accidentals wins (D♭ major rather than C♯ major).
    /// </summary>
    private static readonly int[] MajorSignatures = [0, -5, 2, -3, 4, -1, 6, 1, -4, 3, -2, 5];

    /// <summary>The probe-tone weights of a major and a minor key (Krumhansl–Kessler).</summary>
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    /// <summary>True when the signature is written with flats; C major writes none either way.</summary>
    internal bool UsesFlats => Accidentals < 0;

    /// <summary>How many signs the signature writes, 0 to 7.</summary>
    internal int Count => Math.Abs(Accidentals);

    /// <summary>
    /// The letters the signature alters, in the order it writes them — F C G D A E B for sharps and
    /// B E A D G C F for flats, which is also the order they are drawn at the head of the staff.
    /// </summary>
    internal IReadOnlyList<int> SignedLetters
    {
        get
        {
            var order = UsesFlats ? FlatOrder : SharpOrder;
            var letters = new int[Count];
            for (var index = 0; index < Count; index++) letters[index] = order[index];
            return letters;
        }
    }

    /// <summary>The alteration the signature gives a letter: +1 sharp, -1 flat, 0 as it is written naturally.</summary>
    internal int Signature(int letter) =>
        SignedLetters.Contains(letter) ? (UsesFlats ? -1 : 1) : 0;

    /// <summary>
    /// The written form of a pitch in this key: the letter it sits on, its octave, and the alteration that
    /// letter carries (-1, 0 or +1 relative to the natural letter, which is what a sign beside its head shows).
    /// A pitch the signature already writes is spelled with that letter and no sign; otherwise the spelling
    /// closest to a natural letter wins, and a tie leans the way the signature does — so B♭ is written on the
    /// B line in F major while A♯ is written on the A line in a sharp key.
    /// </summary>
    internal (int Letter, int Octave, int Alteration) Spell(int pitch)
    {
        var clamped = Math.Clamp(pitch, 0, 127);
        var pitchClass = clamped % 12;
        var letter = 0; var alteration = 0; var best = int.MaxValue;
        for (var candidate = 0; candidate < 7; candidate++)
        {
            var written = Nearest(pitchClass - LetterBase[candidate]);
            if (Math.Abs(written) > 1) continue;
            // A note the signature already writes is free; otherwise the sign nearest the natural letter wins,
            // and a sharp in a flat key (or the other way round) costs one more.
            var score = Math.Abs(written) * 2 + (written != 0 && (written > 0) != (Accidentals >= 0) ? 1 : 0);
            if (score >= best) continue;
            best = score; letter = candidate; alteration = written;
        }
        // The written pitch class is the sounding one by construction, so the octave follows from the letter.
        var octave = (clamped - LetterBase[letter] - alteration) / 12 - 1;
        return (letter, octave, alteration);
    }

    /// <summary>
    /// The key of a song, from the sounding time of each pitch class. An empty song, or one that fits no key
    /// well enough, is written without a signature.
    /// </summary>
    internal static MusicKey Infer(IReadOnlyList<NoteEvent> notes)
    {
        var weight = new double[12];
        var total = 0.0;
        foreach (var note in notes)
        {
            // A note that keeps sounding all song must not decide the key on its own, so the weight is capped
            // at a few seconds while every note still counts for something.
            var sounding = Math.Clamp(note.Duration, .05, 4);
            weight[Math.Clamp(note.Pitch, 0, 127) % 12] += sounding;
            total += sounding;
        }
        if (total <= 0) return CMajor;
        var major = Correlate(weight, MajorProfile, out var majorTonic);
        var minor = Correlate(weight, MinorProfile, out var minorTonic);
        if (Math.Max(major, minor) < FitThreshold) return CMajor;
        // A minor key shares the signature of the major key three semitones above its tonic.
        return major >= minor
            ? new MusicKey(majorTonic, false, MajorSignatures[majorTonic])
            : new MusicKey(minorTonic, true, MajorSignatures[(minorTonic + 3) % 12]);
    }

    /// <summary>The correlation of the sounding weights with a profile rotated to each of the twelve tonics.</summary>
    private static double Correlate(double[] weight, double[] profile, out int tonic)
    {
        tonic = 0; var best = double.MinValue;
        for (var candidate = 0; candidate < 12; candidate++)
        {
            var value = Correlation(weight, profile, candidate);
            if (value <= best) continue;
            best = value; tonic = candidate;
        }
        return best;
    }

    /// <summary>Pearson correlation of the weights with a profile read from <paramref name="tonic"/> upwards.</summary>
    private static double Correlation(double[] weight, double[] profile, int tonic)
    {
        var count = weight.Length;
        var meanWeight = weight.Sum() / count;
        var meanProfile = profile.Sum() / count;
        double covariance = 0, spreadWeight = 0, spreadProfile = 0;
        for (var index = 0; index < count; index++)
        {
            var w = weight[(tonic + index) % 12] - meanWeight;
            var p = profile[index] - meanProfile;
            covariance += w * p; spreadWeight += w * w; spreadProfile += p * p;
        }
        var spread = Math.Sqrt(spreadWeight * spreadProfile);
        return spread <= 0 ? 0 : covariance / spread;
    }

    /// <summary>A semitone distance folded into −6…+5, so −11 reads as +1 and +11 as −1.</summary>
    private static int Nearest(int semitones) => (semitones % 12 + 18) % 12 - 6;
}
