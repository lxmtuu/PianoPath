using System;

namespace PianoPath;

/// <summary>The stereo room stage of the pipeline; the classic hall and the convolver are swappable.</summary>
internal interface IStereoReverb
{
    bool Enabled { get; set; }
    /// <summary>Wet-effect multiplier, where 1 preserves the room's established default mix.</summary>
    float Amount { get; set; }
    /// <summary>Processes interleaved stereo in place; output stays within [-1, 1].</summary>
    void Process(float[] interleaved, int frames);
}

/// <summary>
/// Real-time convolution reverb: uniform partitioned overlap-save over a hand-rolled FFT, so a
/// seconds-long impulse response costs a fixed, small amount of work per block. The impulse response
/// is resampled to the engine rate, normalised to unit energy and cut into 512-frame partitions whose
/// spectra are multiplied against a ring of input spectra — one forward and one inverse FFT per channel
/// per 512 frames regardless of tail length. The wet path arrives one partition (~12 ms) after the dry
/// signal, which plays the role of the pre-delay every real hall has. Bypass behaves like the classic
/// hall's: the dry signal is never touched, a ringing tail fades over ~120 ms, then state clears once.
/// </summary>
internal sealed class ConvolutionReverb : IStereoReverb
{
    private const int Block = 512;
    private const int FftSize = Block * 2;
    /// <summary>Impulse responses longer than this are truncated to bound the per-block work.</summary>
    private const double MaxSeconds = 6;

    private readonly Fft _fft = new(FftSize);
    private readonly float _wet;
    private volatile float _amount = 1f;
    private readonly double _fadeStep;
    private readonly int _partitions;
    private readonly float[][][] _impulseRe; // [channel][partition][bin]
    private readonly float[][][] _impulseIm;
    private readonly float[][][] _inputRe;   // ring of past input spectra, one slot per partition
    private readonly float[][][] _inputIm;
    private readonly float[][] _time;        // [channel][FftSize]: previous block then the current one
    private readonly float[][] _wetBlock;    // [channel][Block]: wet output being handed out
    private readonly float[] _workRe = new float[FftSize];
    private readonly float[] _workIm = new float[FftSize];
    private int _slot;
    private int _fill;
    private double _fade;
    private bool _ringing;
    private volatile bool _enabled = true;

    public bool Enabled { get => _enabled; set => _enabled = value; }
    public float Amount { get => _amount; set => _amount = Math.Clamp(value, 0f, 1.5f); }

    public ConvolutionReverb(WavData impulse, int sampleRate, float wet)
    {
        _wet = wet;
        _fadeStep = 1 / (sampleRate * 0.12);
        var (left, right) = PrepareImpulse(impulse, sampleRate);
        _partitions = Math.Max(1, (left.Length + Block - 1) / Block);
        _impulseRe = new float[2][][]; _impulseIm = new float[2][][];
        _inputRe = new float[2][][]; _inputIm = new float[2][][];
        _time = [new float[FftSize], new float[FftSize]];
        _wetBlock = [new float[Block], new float[Block]];
        for (var channel = 0; channel < 2; channel++)
        {
            var source = channel == 0 ? left : right;
            _impulseRe[channel] = new float[_partitions][]; _impulseIm[channel] = new float[_partitions][];
            _inputRe[channel] = new float[_partitions][]; _inputIm[channel] = new float[_partitions][];
            for (var partition = 0; partition < _partitions; partition++)
            {
                var re = new float[FftSize]; var im = new float[FftSize];
                var copy = Math.Min(Block, source.Length - partition * Block);
                for (var i = 0; i < copy; i++) re[i] = source[partition * Block + i];
                _fft.Forward(re, im);
                _impulseRe[channel][partition] = re; _impulseIm[channel][partition] = im;
                _inputRe[channel][partition] = new float[FftSize]; _inputIm[channel][partition] = new float[FftSize];
            }
        }
    }

    public void Process(float[] interleaved, int frames)
    {
        var enabled = _enabled;
        var amount = _amount;
        if (enabled) { _fade = 1; _ringing = true; }
        else if (!_ringing) return;
        for (var i = 0; i < frames; i++)
        {
            if (!enabled && _fade > 0) _fade = Math.Max(0, _fade - _fadeStep);
            var gain = _wet * amount * (float)_fade;
            for (var channel = 0; channel < 2; channel++)
            {
                var dry = interleaved[i * 2 + channel];
                _time[channel][Block + _fill] = enabled ? dry : 0; // a fading bypass feeds no new energy
                interleaved[i * 2 + channel] = Math.Clamp(dry + _wetBlock[channel][_fill] * gain, -1f, 1f);
            }
            if (++_fill == Block) { Cycle(); _fill = 0; }
        }
        if (!enabled && _fade == 0) { Clear(); _ringing = false; }
    }

    /// <summary>One overlap-save step: spectrum in, multiply-accumulate across partitions, last half out.</summary>
    private void Cycle()
    {
        for (var channel = 0; channel < 2; channel++)
        {
            var time = _time[channel];
            var slotRe = _inputRe[channel][_slot]; var slotIm = _inputIm[channel][_slot];
            Array.Copy(time, slotRe, FftSize);
            Array.Clear(slotIm);
            _fft.Forward(slotRe, slotIm);
            Array.Clear(_workRe); Array.Clear(_workIm);
            for (var partition = 0; partition < _partitions; partition++)
            {
                var inRe = _inputRe[channel][(_slot - partition + _partitions) % _partitions];
                var inIm = _inputIm[channel][(_slot - partition + _partitions) % _partitions];
                var irRe = _impulseRe[channel][partition]; var irIm = _impulseIm[channel][partition];
                for (var bin = 0; bin < FftSize; bin++)
                {
                    _workRe[bin] += inRe[bin] * irRe[bin] - inIm[bin] * irIm[bin];
                    _workIm[bin] += inRe[bin] * irIm[bin] + inIm[bin] * irRe[bin];
                }
            }
            _fft.Inverse(_workRe, _workIm);
            Array.Copy(_workRe, Block, _wetBlock[channel], 0, Block); // the first half is circular overlap
            Array.Copy(time, Block, time, 0, Block);                  // current input becomes the history half
        }
        _slot = (_slot + 1) % _partitions;
    }

    private void Clear()
    {
        for (var channel = 0; channel < 2; channel++)
        {
            Array.Clear(_time[channel]); Array.Clear(_wetBlock[channel]);
            for (var partition = 0; partition < _partitions; partition++)
            {
                Array.Clear(_inputRe[channel][partition]); Array.Clear(_inputIm[channel][partition]);
            }
        }
        _fill = 0; _slot = 0;
    }

    /// <summary>Mono or stereo in any sample rate, out as per-channel floats at the engine rate, unit energy.</summary>
    private static (float[] Left, float[] Right) PrepareImpulse(WavData impulse, int sampleRate)
    {
        if (impulse.Frames < 1) throw new ArgumentException("The impulse response is empty.", nameof(impulse));
        var frames = Math.Min((int)(impulse.Frames * (double)sampleRate / impulse.SampleRate), (int)(sampleRate * MaxSeconds));
        frames = Math.Max(frames, 1);
        var left = new float[frames]; var right = new float[frames];
        double energy = 0;
        for (var i = 0; i < frames; i++)
        {
            // Linear interpolation is plenty for a reverb tail.
            var position = i * (double)impulse.SampleRate / sampleRate;
            var index = Math.Min((int)position, impulse.Frames - 1);
            var next = Math.Min(index + 1, impulse.Frames - 1);
            var fraction = (float)(position - index);
            for (var channel = 0; channel < 2; channel++)
            {
                var pick = Math.Min(channel, impulse.Channels - 1);
                var a = impulse.Samples[index * impulse.Channels + pick] / 32768f;
                var b = impulse.Samples[next * impulse.Channels + pick] / 32768f;
                var value = a + (b - a) * fraction;
                if (channel == 0) left[i] = value; else right[i] = value;
            }
            energy += (left[i] * (double)left[i] + right[i] * (double)right[i]) * .5;
        }
        var scale = energy > 0 ? (float)(1 / Math.Sqrt(energy)) : 1f;
        for (var i = 0; i < frames; i++) { left[i] *= scale; right[i] *= scale; }
        return (left, right);
    }
}
