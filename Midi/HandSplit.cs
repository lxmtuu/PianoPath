using System;

namespace PianoPath;

/// <summary>
/// Guesses the hand split point of a song from the way its notes are spread over the keyboard.
///
/// <para>
/// The rule is a one-dimensional two-means clustering of the notes, weighted by how long each pitch
/// sounds, so a slow accompaniment of long low chords counts as much as a fast melodic line. A split is
/// only used when it lies in a real gap between the hands (at least <see cref="MinimumGap"/> empty semitones)
/// and both hands carry at least <see cref="MinimumShare"/> of that sounding time; a song that is
/// really one hand, or whose hands overlap, keeps the split the user chose. Inside the gap the score is
/// flat, so ties resolve to the candidate closest to middle C — the same note the default split uses,
/// which keeps the result deterministic and familiar.
/// </para>
///
/// <para>
/// The result is meant to be stored per song (see <see cref="SongLibrary"/>): inferring once and
/// keeping the value is what stops the split from jumping around between openings.
/// </para>
/// </summary>
internal static class HandSplit
{
    /// <summary>Hands never sit outside this range, so a stray extreme note cannot define a hand.</summary>
    internal const int LowestSplit = 40;
    internal const int HighestSplit = 84;
    /// <summary>Middle C: the tie-breaker and the familiar default split.</summary>
    internal const int MiddleC = 60;
    /// <summary>Each hand needs at least this share of the sounding time for a split to be believed.</summary>
    internal const double MinimumShare = .10;
    /// <summary>Empty semitones that must sit between the highest left-hand note and the lowest right-hand one.</summary>
    internal const int MinimumGap = 5;

    /// <summary>
    /// The pitch the right hand should start at, or <paramref name="fallback"/> when the song does not
    /// look two-handed.
    /// </summary>
    internal static int Infer(IReadOnlyList<NoteEvent> notes, double fallback)
    {
        var keep = (int)Math.Clamp(Math.Round(fallback), 21, 108);
        if (notes is null || notes.Count == 0) return keep;

        // Sounding time per pitch: a whole note says more about the shape of a piece than a grace note.
        var weight = new double[128];
        var total = 0.0;
        foreach (var note in notes)
        {
            if (note.Pitch is < 0 or > 127) continue;
            var time = Math.Max(note.Duration, .05);
            weight[note.Pitch] += time; total += time;
        }
        if (total <= 0) return keep;

        // Prefix sums let every candidate split be scored in constant time.
        double[] mass = new double[129], sum = new double[129], squares = new double[129];
        for (var pitch = 0; pitch < 128; pitch++)
        {
            mass[pitch + 1] = mass[pitch] + weight[pitch];
            sum[pitch + 1] = sum[pitch] + weight[pitch] * pitch;
            squares[pitch + 1] = squares[pitch] + weight[pitch] * pitch * pitch;
        }
        static double Spread(double mass, double sum, double squares) => mass <= 0 ? 0 : squares - sum * sum / mass;

        // The occupied pitches just outside a candidate split, so the gap between the hands can be measured:
        // below[split] is the highest sounding pitch under the split, above[split] the lowest one at or over it.
        var below = new int[129]; var above = new int[128];
        var seen = -1;
        for (var pitch = 0; pitch < 128; pitch++) { if (weight[pitch] > 0) seen = pitch; below[pitch + 1] = seen; }
        seen = -1;
        for (var pitch = 127; pitch >= 0; pitch--) { if (weight[pitch] > 0) seen = pitch; above[pitch] = seen; }

        int? bestSplit = null;
        var bestScore = double.MaxValue;
        for (var split = LowestSplit; split <= HighestSplit; split++)
        {
            var lowMass = mass[split]; var highMass = total - lowMass;
            if (lowMass < total * MinimumShare || highMass < total * MinimumShare) continue;
            // A split only means "two hands" when a real gap sits between the lowest right-hand note and the
            // highest left-hand one; through a smooth run of notes the two-means score would still improve,
            // which says nothing about hands.
            if (below[split] < 0 || above[split] < 0 || above[split] - below[split] - 1 < MinimumGap) continue;
            var score = Spread(lowMass, sum[split], squares[split]) + Spread(highMass, sum[128] - sum[split], squares[128] - squares[split]);
            // A flat region between two clusters keeps the candidate nearest middle C, so the answer does not
            // depend on the order the candidates are tried in.
            if (bestSplit is null || score < bestScore - 1e-9
                || (Math.Abs(score - bestScore) <= 1e-9 && Math.Abs(split - MiddleC) < Math.Abs(bestSplit.Value - MiddleC)))
            {
                bestSplit = split; bestScore = score;
            }
        }
        return bestSplit is null ? keep : Math.Clamp(bestSplit.Value, 21, 108);
    }
}
