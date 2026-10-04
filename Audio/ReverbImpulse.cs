using System;

namespace PianoPath;

/// <summary>
/// Builds the impulse responses the convolution reverb ships with, so the app needs no IR download and
/// no bundled binary: a concert hall and a close soundboard, both synthesised deterministically from
/// seeded noise with the structure of measured rooms — sparse early reflections, then a diffuse tail
/// whose highs die faster than its lows (one decay time per band), with decorrelated channels over a
/// shared core so the image is wide but stays solid in the middle. Each response is normalised to unit
/// energy; the convolution engine applies the wet level on top.
/// </summary>
internal static class ReverbImpulse
{
    /// <summary>A ~2.8 s hall: 12 ms of soundboard-like body, early reflections to 72 ms, then the tail.</summary>
    public static WavData ConcertHall(int sampleRate)
    {
        var frames = (int)(sampleRate * 2.8);
        var left = new double[frames];
        var right = new double[frames];

        // The piano's own cabinet answers first: a dense, dark burst in the first 12 milliseconds.
        var shared = new Random(20261004); var ownLeft = new Random(911); var ownRight = new Random(417);
        var body = (int)(sampleRate * .012);
        double bodyLpLeft = 0, bodyLpRight = 0;
        for (var i = 1; i < body; i++)
        {
            var envelope = Math.Exp(-i / (sampleRate * .004));
            var core = Gaussian(shared);
            bodyLpLeft += .25 * (.8 * core + .2 * Gaussian(ownLeft) - bodyLpLeft);
            bodyLpRight += .25 * (.8 * core + .2 * Gaussian(ownRight) - bodyLpRight);
            left[i] += bodyLpLeft * envelope * .3;
            right[i] += bodyLpRight * envelope * .3;
        }

        // Early reflections: discrete arrivals from the stage walls, alternating sides, getting softer.
        foreach (var (milliseconds, gain, pan) in new (double, double, double)[]
                 {
                     (11.3, .52, -.40), (17.9, .46, .45), (23.1, .40, -.20), (29.7, .37, .30), (36.4, .33, -.50),
                     (43.9, .30, .15), (52.6, .27, -.30), (61.8, .24, .40), (71.5, .21, -.10),
                 })
        {
            var at = (int)(milliseconds / 1000 * sampleRate);
            left[at] += gain * Math.Sqrt((1 - pan) * .5);
            right[at] += gain * Math.Sqrt((1 + pan) * .5);
            left[at + 1] += gain * .3 * Math.Sqrt((1 - pan) * .5); // a touch of smear so arrivals do not click
            right[at + 1] += gain * .3 * Math.Sqrt((1 + pan) * .5);
        }

        // The diffuse tail: three bands with their own RT60s — a hall keeps its bass the longest.
        AddTail(left, right, sampleRate, startSeconds: .055, gain: .02, coherence: .35,
            lowRt60: 2.9, midRt60: 2.2, highRt60: 1.25, shared, ownLeft, ownRight);

        return Normalise(left, right, sampleRate);
    }

    /// <summary>A ~0.18 s soundboard: the close, almost-dry body of the instrument without the room.</summary>
    public static WavData Soundboard(int sampleRate)
    {
        var frames = (int)(sampleRate * .18);
        var left = new double[frames];
        var right = new double[frames];
        var shared = new Random(16200); var ownLeft = new Random(2); var ownRight = new Random(3);
        AddTail(left, right, sampleRate, startSeconds: .0005, gain: .5, coherence: .7,
            lowRt60: .16, midRt60: .13, highRt60: .09, shared, ownLeft, ownRight);
        return Normalise(left, right, sampleRate);
    }

    /// <summary>Filtered-noise tail: per-band exponential decays over a short fade-in, channels decorrelated.</summary>
    private static void AddTail(double[] left, double[] right, int sampleRate, double startSeconds, double gain,
        double coherence, double lowRt60, double midRt60, double highRt60, Random shared, Random ownLeft, Random ownRight)
    {
        var start = (int)(startSeconds * sampleRate);
        var lowK = OnePole(250, sampleRate); var highK = OnePole(3500, sampleRate);
        double lowLeft = 0, highLeft = 0, lowRight = 0, highRight = 0;
        for (var i = start; i < left.Length; i++)
        {
            var seconds = (i - start) / (double)sampleRate;
            var fadeIn = Math.Min(1, seconds / .012);
            var core = Gaussian(shared);
            var sampleLeft = coherence * core + (1 - coherence) * Gaussian(ownLeft);
            var sampleRight = coherence * core + (1 - coherence) * Gaussian(ownRight);
            lowLeft += lowK * (sampleLeft - lowLeft); highLeft += highK * (sampleLeft - highLeft);
            lowRight += lowK * (sampleRight - lowRight); highRight += highK * (sampleRight - highRight);
            var envelopeLow = Math.Exp(-6.91 * seconds / lowRt60);
            var envelopeMid = Math.Exp(-6.91 * seconds / midRt60);
            var envelopeHigh = Math.Exp(-6.91 * seconds / highRt60);
            left[i] += gain * fadeIn * (lowLeft * envelopeLow + (highLeft - lowLeft) * envelopeMid + (sampleLeft - highLeft) * envelopeHigh);
            right[i] += gain * fadeIn * (lowRight * envelopeLow + (highRight - lowRight) * envelopeMid + (sampleRight - highRight) * envelopeHigh);
        }
    }

    private static double OnePole(double cutoffHz, int sampleRate) => 1 - Math.Exp(-2 * Math.PI * cutoffHz / sampleRate);

    private static double Gaussian(Random random)
    {
        // Box-Muller; NextDouble is in [0,1) so flip it to (0,1] for the log.
        var u = 1 - random.NextDouble();
        var v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    /// <summary>Scales to unit RMS energy and converts to the 16-bit sample container the readers use.</summary>
    private static WavData Normalise(double[] left, double[] right, int sampleRate)
    {
        double energy = 0;
        for (var i = 0; i < left.Length; i++) energy += (left[i] * left[i] + right[i] * right[i]) * .5;
        var scale = energy > 0 ? 1 / Math.Sqrt(energy) : 1;
        // Keep every sample inside 16-bit range; unit energy spread over seconds leaves peaks far below 1.
        var samples = new short[left.Length * 2];
        for (var i = 0; i < left.Length; i++)
        {
            samples[i * 2] = (short)Math.Clamp(Math.Round(left[i] * scale * 32767), short.MinValue, short.MaxValue);
            samples[i * 2 + 1] = (short)Math.Clamp(Math.Round(right[i] * scale * 32767), short.MinValue, short.MaxValue);
        }
        return new WavData { SampleRate = sampleRate, Channels = 2, Samples = samples };
    }
}
