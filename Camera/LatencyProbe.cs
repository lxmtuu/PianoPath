namespace PianoPath;

/// <summary>How much the picture moved between one camera frame and the one before, and when the newer one arrived.</summary>
/// <param name="Time">Seconds, on the same clock as the note times given to <see cref="LatencyProbe"/>.</param>
/// <param name="Energy">0 for a still picture; larger for more movement (see <see cref="MotionEnergy"/>).</param>
internal readonly record struct MotionSample(double Time, double Energy);

/// <summary>How a measurement ended.</summary>
internal enum LatencyOutcome
{
    /// <summary>A delay was found and it is not a coincidence.</summary>
    Measured,
    /// <summary>Too few separate notes were played inside the recorded picture.</summary>
    NotEnoughNotes,
    /// <summary>The recording of the picture is too short or too sparse to look for a delay in.</summary>
    NotEnoughPicture,
    /// <summary>The picture hardly moved at all: the camera does not see the hands, or the keys.</summary>
    NoMotion,
    /// <summary>There was movement, but nothing that follows the notes clearly enough to call it a delay. Try again.</summary>
    Unclear,
}

/// <summary>The result of <see cref="LatencyProbe.Estimate"/>.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="DelaySeconds">How long after a key was pressed the camera's picture showed it; 0 unless measured.</param>
/// <param name="Confidence">0..1: the share of look-alike tries (the same notes moved to other times) that the real notes beat.</param>
/// <param name="Notes">Separate notes the measurement used.</param>
internal readonly record struct LatencyEstimate(LatencyOutcome Outcome, double DelaySeconds, double Confidence, int Notes)
{
    internal bool IsMeasured => Outcome == LatencyOutcome.Measured;
}

/// <summary>
/// How late the camera's picture is, found from the player's own notes: press about twenty keys, and the picture moves a little
/// after each one. The movement of the picture is averaged over the moments after every note, for each possible delay from 0
/// to 400 ms, and the delay at which the picture moves most is the camera's.
/// <para>
/// A delay is only believed when it stands out: the same notes are moved to a hundred other times in the recording and the
/// real ones must beat nearly all of those, the first half of the notes and the second half must agree, and no other delay may
/// explain the playing nearly as well. A player who moves
/// a lot between notes cannot fool it, only make it say "unclear".
/// </para>
/// </summary>
internal static class LatencyProbe
{
    internal const double MaxDelay = 0.4;
    internal const double Step = 0.005;
    internal const int MinNotes = 8;
    internal const double ChordWindow = 0.06;
    internal const int Tries = 100;
    internal const double RequiredConfidence = 0.99;
    internal const double HalvesMayDifferBy = 0.06;
    internal const double MinSpan = 3;
    /// <summary>Another delay, at least 100 ms away, may rise above the picture's usual movement by at most this share of what the best one does.</summary>
    internal const double SecondPeakShare = 0.6;
    private const double PeakSeparation = 0.1;
    private const int Lags = (int)(MaxDelay / Step + 0.5) + 1;
    private const int Smoothing = 2;              // half-width of the average over neighbouring delays, in steps

    /// <param name="motion">The picture's movement over time, in any order; samples that are not numbers are ignored.</param>
    /// <param name="noteOns">When keys were pressed, seconds on the same clock as the motion.</param>
    internal static LatencyEstimate Estimate(IReadOnlyList<MotionSample> motion, IReadOnlyList<double> noteOns)
    {
        var samples = motion.Where(m => double.IsFinite(m.Time) && double.IsFinite(m.Energy)).OrderBy(m => m.Time).ToArray();
        // a repeated time would divide by zero when two samples are blended
        samples = [.. samples.Where((m, i) => i == 0 || m.Time > samples[i - 1].Time)];
        var span = samples.Length < 2 ? 0 : samples[^1].Time - MaxDelay - samples[0].Time;
        if (samples.Length < 10 || span < MinSpan) return new(LatencyOutcome.NotEnoughPicture, 0, 0, 0);

        // one event for a chord, and only notes whose next 400 ms were recorded
        var events = new List<double>();
        foreach (var time in noteOns.Where(double.IsFinite).Order())
        {
            if (events.Count > 0 && time - events[^1] < ChordWindow) continue;
            if (time < samples[0].Time || time > samples[^1].Time - MaxDelay) continue;
            events.Add(time);
        }
        if (events.Count < MinNotes) return new(LatencyOutcome.NotEnoughNotes, 0, 0, events.Count);

        var energies = samples.Select(s => Math.Max(0, s.Energy)).ToArray();
        var times = samples.Select(s => s.Time).ToArray();
        var mean = energies.Average();
        var variance = energies.Sum(e => (e - mean) * (e - mean)) / energies.Length;
        if (variance < 1e-12) return new(LatencyOutcome.NoMotion, 0, 0, events.Count);

        var curve = Curve(times, energies, events);
        var peak = Peak(curve);
        var beaten = 0;
        for (var k = 1; k <= Tries; k++)
        {
            // the same notes, moved somewhere else in the recording, in steps that spread over all of it
            var shift = 0.37 + k * (span - 0.74) / (Tries + 1);
            var moved = events.Select(t => times[0] + ((t - times[0]) + shift) % span).ToArray();
            if (curve[peak] > Curve(times, energies, moved).Max()) beaten++;
        }
        var confidence = beaten / (double)Tries;
        var first = Peak(Curve(times, energies, events.Where((_, i) => i % 2 == 0).ToArray()));
        var second = Peak(Curve(times, energies, events.Where((_, i) => i % 2 == 1).ToArray()));
        // two delays that explain the playing about equally well (a steady beat, or a camera that stutters) are no answer
        var other = double.NegativeInfinity;
        for (var lag = 0; lag < Lags; lag++) if (Math.Abs(lag - peak) * Step > PeakSeparation) other = Math.Max(other, curve[lag]);
        var rival = curve[peak] > mean ? (other - mean) / (curve[peak] - mean) : double.PositiveInfinity;
        if (confidence < RequiredConfidence || curve[peak] <= mean || Math.Abs(first - second) * Step > HalvesMayDifferBy || rival >= SecondPeakShare)
            return new(LatencyOutcome.Unclear, 0, confidence, events.Count);

        var delay = peak * Step;
        if (peak > 0 && peak < Lags - 1)                                            // between steps: the top of the parabola through the three points
        {
            double a = curve[peak - 1], b = curve[peak], c = curve[peak + 1];
            var bend = a - 2 * b + c;
            if (bend < 0) delay += Step * 0.5 * (a - c) / bend;
        }
        return new(LatencyOutcome.Measured, Math.Clamp(delay, 0, MaxDelay), confidence, events.Count);
    }

    /// <summary>The picture's movement averaged over the moments after each note, for every delay, lightly smoothed.</summary>
    private static double[] Curve(double[] times, double[] energies, IReadOnlyList<double> events)
    {
        var raw = new double[Lags];
        foreach (var time in events)
            for (var lag = 0; lag < Lags; lag++) raw[lag] += EnergyAt(times, energies, time + lag * Step);
        for (var lag = 0; lag < Lags; lag++) raw[lag] /= events.Count;
        var smooth = new double[Lags];
        for (var lag = 0; lag < Lags; lag++)
        {
            int from = Math.Max(0, lag - Smoothing), to = Math.Min(Lags - 1, lag + Smoothing);
            double sum = 0;
            for (var i = from; i <= to; i++) sum += raw[i];
            smooth[lag] = sum / (to - from + 1);
        }
        return smooth;
    }

    private static int Peak(double[] curve)
    {
        var best = 0;
        for (var i = 1; i < curve.Length; i++) if (curve[i] > curve[best]) best = i;
        return best;
    }

    /// <summary>The movement at <paramref name="time"/>, blended between the two frames it falls between.</summary>
    private static double EnergyAt(double[] times, double[] energies, double time)
    {
        if (time <= times[0]) return energies[0];
        if (time >= times[^1]) return energies[^1];
        int lo = 0, hi = times.Length - 1;
        while (hi - lo > 1)
        {
            var middle = (lo + hi) / 2;
            if (times[middle] <= time) lo = middle; else hi = middle;
        }
        var share = (time - times[lo]) / (times[hi] - times[lo]);
        return energies[lo] + (energies[hi] - energies[lo]) * share;
    }
}
