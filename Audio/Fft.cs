using System;

namespace PianoPath;

/// <summary>
/// A plain iterative radix-2 FFT over separate real/imaginary arrays — exactly what the partitioned
/// convolution reverb needs and nothing more. The length is fixed per instance so the bit-reversal
/// table and twiddle factors are computed once; <see cref="Inverse"/> folds the 1/N scale in, so a
/// Forward/Inverse round trip returns the input.
/// </summary>
internal sealed class Fft
{
    private readonly int _length;
    private readonly int[] _reversed;
    private readonly float[] _cos;
    private readonly float[] _sin;

    public Fft(int length)
    {
        if (length < 2 || (length & (length - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.", nameof(length));
        _length = length;
        _reversed = new int[length];
        var bits = int.TrailingZeroCount(length);
        for (var i = 0; i < length; i++)
        {
            var reversed = 0;
            for (var bit = 0; bit < bits; bit++) if ((i & 1 << bit) != 0) reversed |= 1 << (bits - 1 - bit);
            _reversed[i] = reversed;
        }
        _cos = new float[length / 2];
        _sin = new float[length / 2];
        for (var i = 0; i < length / 2; i++)
        {
            _cos[i] = (float)Math.Cos(2 * Math.PI * i / length);
            _sin[i] = (float)Math.Sin(2 * Math.PI * i / length);
        }
    }

    public void Forward(float[] real, float[] imaginary) => Transform(real, imaginary, inverse: false);

    public void Inverse(float[] real, float[] imaginary)
    {
        Transform(real, imaginary, inverse: true);
        var scale = 1f / _length;
        for (var i = 0; i < _length; i++) { real[i] *= scale; imaginary[i] *= scale; }
    }

    private void Transform(float[] real, float[] imaginary, bool inverse)
    {
        for (var i = 0; i < _length; i++)
        {
            var j = _reversed[i];
            if (j <= i) continue;
            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }
        for (var size = 2; size <= _length; size <<= 1)
        {
            var half = size >> 1;
            var step = _length / size;
            for (var start = 0; start < _length; start += size)
            {
                for (var pair = 0; pair < half; pair++)
                {
                    var angle = pair * step;
                    var wr = _cos[angle];
                    var wi = inverse ? _sin[angle] : -_sin[angle];
                    int a = start + pair, b = a + half;
                    var tr = real[b] * wr - imaginary[b] * wi;
                    var ti = real[b] * wi + imaginary[b] * wr;
                    real[b] = real[a] - tr; imaginary[b] = imaginary[a] - ti;
                    real[a] += tr; imaginary[a] += ti;
                }
            }
        }
    }
}
