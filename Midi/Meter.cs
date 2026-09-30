namespace PianoPath;

/// <summary>
/// How a time signature is felt, as opposed to how it is written.
///
/// <para>
/// A simple meter beats on the beat-type itself: 4/4 is four quarter beats, 3/4 is three and 2/2 is two half
/// ones, and 3/8 is three eighths. A <em>compound</em> meter — six, nine or twelve of a beat-type — is felt in
/// threes instead: 6/8 is two beats of a dotted quarter rather than six eighths, 9/8 is three and 12/8 four.
/// That is the grouping a score beams its notes and counts its rests by, so it is the grouping the beat grid
/// has to carry, or the sheet groups a bar of 6/8 in twos and the metronome clicks where nobody counts.
/// </para>
/// </summary>
internal static class Meter
{
    /// <summary>
    /// The beats of one bar and how many beat-type units each of them lasts, for a written signature:
    /// (4, 4) → four quarter beats, (6, 8) → two dotted-quarter ones, (5, 4) → five quarters.
    /// </summary>
    internal static (int Beats, int UnitsPerBeat) Of(int numerator, int denominator)
    {
        var units = numerator > 3 && numerator % 3 == 0 ? 3 : 1;
        return (Math.Max(1, numerator / units), units);
    }

    /// <summary>How long one felt beat of that signature lasts, counted in quarter notes.</summary>
    internal static double BeatInQuarters(int unitsPerBeat, int denominator) => unitsPerBeat * 4.0 / Math.Max(1, denominator);
}
