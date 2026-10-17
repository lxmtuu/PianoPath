using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The convolution reverb against ground truth computed the slow way: the FFT against a naive DFT, and
/// the partitioned overlap-save engine against direct time-domain convolution — the one-block delay of
/// the wet path and the untouched dry path are part of the contract. The built-in impulse responses are
/// pinned as deterministic, stereo, unit-energy and decaying, so a regression in the generators cannot
/// silently change how the piano sounds.
/// </summary>
public class ConvolutionReverbTests
{
    [Fact]
    public void Fft_matches_a_naive_dft()
    {
        const int n = 64;
        var rng = new Random(11);
        var real = new float[n]; var imaginary = new float[n];
        for (var i = 0; i < n; i++) { real[i] = (float)(rng.NextDouble() * 2 - 1); imaginary[i] = (float)(rng.NextDouble() * 2 - 1); }
        var expectedReal = new double[n]; var expectedImaginary = new double[n];
        for (var k = 0; k < n; k++)
            for (var t = 0; t < n; t++)
            {
                var angle = -2 * Math.PI * k * t / n;
                expectedReal[k] += real[t] * Math.Cos(angle) - imaginary[t] * Math.Sin(angle);
                expectedImaginary[k] += real[t] * Math.Sin(angle) + imaginary[t] * Math.Cos(angle);
            }
        new Fft(n).Forward(real, imaginary);
        for (var k = 0; k < n; k++)
        {
            Assert.True(Math.Abs(real[k] - expectedReal[k]) < 1e-3, $"Re[{k}]: {real[k]} vs {expectedReal[k]}");
            Assert.True(Math.Abs(imaginary[k] - expectedImaginary[k]) < 1e-3, $"Im[{k}]: {imaginary[k]} vs {expectedImaginary[k]}");
        }
    }

    [Fact]
    public void Fft_round_trip_returns_the_input()
    {
        const int n = 1024;
        var rng = new Random(23);
        var real = new float[n]; var imaginary = new float[n];
        for (var i = 0; i < n; i++) real[i] = (float)(rng.NextDouble() * 2 - 1);
        var original = (float[])real.Clone();
        var fft = new Fft(n);
        fft.Forward(real, imaginary);
        fft.Inverse(real, imaginary);
        for (var i = 0; i < n; i++)
        {
            Assert.True(Math.Abs(real[i] - original[i]) < 1e-5);
            Assert.True(Math.Abs(imaginary[i]) < 1e-5);
        }
    }

    [Fact]
    public void Dirac_impulse_response_is_a_pure_one_block_delay()
    {
        // Convolving with a dirac reproduces the input, so the wet path must be the dry signal exactly
        // one 512-frame partition later at the wet gain — and the dry path itself must be untouched.
        var dirac = new WavData { SampleRate = 44100, Channels = 2, Samples = new short[] { 32767, 32767 } };
        var reverb = new ConvolutionReverb(dirac, 44100, wet: .5f);
        const int frames = 2000;
        var rng = new Random(7);
        var input = new float[frames * 2];
        for (var i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() * .6 - .3);
        var buffer = (float[])input.Clone();
        reverb.Process(buffer, frames);
        for (var i = 0; i < frames * 2; i++)
        {
            var expected = i < 512 * 2 ? 0f : input[i - 512 * 2] * .5f;
            Assert.True(Math.Abs(buffer[i] - input[i] - expected) < 2e-3, $"sample {i}: wet {buffer[i] - input[i]} vs {expected}");
        }
    }

    [Fact]
    public void Room_amount_scales_only_the_wet_return_and_zero_is_dry()
    {
        var dirac = new WavData { SampleRate = 44100, Channels = 2, Samples = new short[] { 32767, 32767 } };
        var input = new float[1400 * 2];
        input[0] = input[1] = .4f;
        var reverb = new ConvolutionReverb(dirac, 44100, wet: .5f) { Amount = .5f };
        reverb.Process(input, 1400);
        Assert.InRange(Math.Abs(input[512 * 2] - .4f - .1f), 0, .002);
        Assert.InRange(Math.Abs(input[512 * 2 + 1] - .4f - .1f), 0, .002);

        var dryOnly = new float[1400 * 2];
        dryOnly[0] = dryOnly[1] = .4f;
        reverb = new ConvolutionReverb(dirac, 44100, wet: .5f) { Amount = 0 };
        reverb.Process(dryOnly, 1400);
        Assert.Equal(.4f, dryOnly[512 * 2]);
        Assert.Equal(.4f, dryOnly[512 * 2 + 1]);
    }

    [Fact]
    public void Partitioned_convolution_matches_direct_time_domain_convolution()
    {
        // A 1 200-frame impulse spans three partitions, one of them partial, so the spectrum ring and the
        // tail accumulation are all exercised; the slow direct convolution is the ground truth.
        const int impulseFrames = 1200, frames = 2560, delay = 512;
        const float wet = .4f;
        var rng = new Random(31);
        var samples = new short[impulseFrames * 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)rng.Next(-16000, 16001);
        var impulse = new WavData { SampleRate = 44100, Channels = 2, Samples = samples };
        // Normalise exactly the way the engine does: shorts to floats, then unit energy across channels.
        var ir = new float[2][];
        ir[0] = new float[impulseFrames]; ir[1] = new float[impulseFrames];
        double energy = 0;
        for (var i = 0; i < impulseFrames; i++)
        {
            ir[0][i] = samples[i * 2] / 32768f; ir[1][i] = samples[i * 2 + 1] / 32768f;
            energy += (ir[0][i] * (double)ir[0][i] + ir[1][i] * (double)ir[1][i]) * .5;
        }
        var scale = (float)(1 / Math.Sqrt(energy));
        for (var i = 0; i < impulseFrames; i++) { ir[0][i] *= scale; ir[1][i] *= scale; }
        var input = new float[frames * 2];
        for (var i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() * .5 - .25);
        var buffer = (float[])input.Clone();
        new ConvolutionReverb(impulse, 44100, wet).Process(buffer, frames);
        for (var n = 0; n < frames; n++)
            for (var channel = 0; channel < 2; channel++)
            {
                double expected = 0;
                for (var k = 0; k <= Math.Min(n - delay, impulseFrames - 1); k++)
                    expected += ir[channel][k] * input[(n - delay - k) * 2 + channel];
                var actual = buffer[n * 2 + channel] - input[n * 2 + channel];
                Assert.True(Math.Abs(actual - expected * wet) < 3e-3, $"frame {n} ch {channel}: {actual} vs {expected * wet}");
            }
    }

    [Fact]
    public void Chunked_processing_is_identical_to_one_shot_processing()
    {
        // The engine hands the reverb whatever block sizes the device wants; awkward chunk boundaries
        // must not change a single sample relative to one big call.
        var impulse = ReverbImpulse.Soundboard(44100);
        const int frames = 3000;
        var rng = new Random(47);
        var input = new float[frames * 2];
        for (var i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() * .5 - .25);
        var oneShot = (float[])input.Clone();
        new ConvolutionReverb(impulse, 44100, wet: .5f).Process(oneShot, frames);
        var chunked = (float[])input.Clone();
        var reverb = new ConvolutionReverb(impulse, 44100, wet: .5f);
        var sizes = new[] { 1, 3, 17, 100, 491, 512, 700 };
        var offset = 0; var step = 0;
        while (offset < frames)
        {
            var take = Math.Min(sizes[step++ % sizes.Length], frames - offset);
            var slice = new float[take * 2];
            Array.Copy(chunked, offset * 2, slice, 0, take * 2);
            reverb.Process(slice, take);
            Array.Copy(slice, 0, chunked, offset * 2, take * 2);
            offset += take;
        }
        Assert.Equal(oneShot, chunked);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Generated_responses_are_deterministic_stereo_unit_energy_and_decaying(int rate)
    {
        foreach (var (impulse, again, minSeconds, maxSeconds) in new[]
                 {
                     (ReverbImpulse.ConcertHall(rate), ReverbImpulse.ConcertHall(rate), 2.0, 4.0),
                     (ReverbImpulse.Soundboard(rate), ReverbImpulse.Soundboard(rate), .05, .5),
                 })
        {
            Assert.Equal(2, impulse.Channels);
            Assert.Equal(rate, impulse.SampleRate);
            Assert.Equal(impulse.Samples, again.Samples); // same seeds, same response, every launch
            Assert.InRange(impulse.Frames / (double)rate, minSeconds, maxSeconds);
            double energy = 0, early = 0, late = 0;
            var half = impulse.Frames / 2;
            for (var i = 0; i < impulse.Frames; i++)
            {
                var l = impulse.Samples[i * 2] / 32768.0; var r = impulse.Samples[i * 2 + 1] / 32768.0;
                var e = (l * l + r * r) * .5;
                energy += e;
                if (i < half) early += e; else late += e;
            }
            Assert.InRange(energy, .8, 1.1); // unit energy, give or take 16-bit quantisation
            Assert.True(late < early * .25, $"the tail must decay: early {early}, late {late}");
        }
    }

    [Fact]
    public void Mono_and_resampled_impulses_are_accepted()
    {
        // A user's IR can be mono and at a different rate; a dirac at 22 050 Hz still lands close to a
        // one-block echo after resampling to 44 100 Hz.
        var mono = new WavData { SampleRate = 22050, Channels = 1, Samples = new short[] { 32767, 0, 0, 0 } };
        var reverb = new ConvolutionReverb(mono, 44100, wet: .5f);
        var buffer = new float[1200 * 2];
        buffer[0] = buffer[1] = .5f;
        reverb.Process(buffer, 1200);
        for (var channel = 0; channel < 2; channel++)
        {
            var peak = 0f; var at = -1;
            for (var i = 1; i < 1200; i++)
                if (Math.Abs(buffer[i * 2 + channel]) > peak) { peak = Math.Abs(buffer[i * 2 + channel]); at = i; }
            Assert.Equal(512, at); // the dirac survives resampling at the head of the response
            Assert.True(peak > .1f, $"channel {channel} wet peak {peak} is too small");
        }
    }
}
