using System;

namespace PianoPath;

/// <summary>
/// Sympathetic string resonance for the sustain pedal. When the damper rail lifts on a real grand, every
/// string is free to vibrate, so each played note excites the undamped strings around it and the whole
/// instrument "opens up" — the signature halo a sampled piano loses because its samples were recorded one
/// note at a time. This stage recreates it physically rather than with reverb: one waveguide per string
/// (a fractional-delay comb tuned to the string's pitch, with a lowpass in the loop for the darkening of
/// higher partials), fed by the dry mix only in proportion to how far the pedal is down. Strings ring for
/// seconds in the bass and around a second in the treble, and releasing the pedal chokes the bank quickly,
/// exactly like felt falling back on the strings. The bank sleeps (and costs nothing) while the pedal is up.
/// </summary>
internal sealed class StringResonance
{
    /// <summary>A0 — the lowest string on the piano.</summary>
    private const int FirstKey = 21;
    /// <summary>C6 — strings above this are so short their sympathetic contribution is inaudible.</summary>
    private const int LastKey = 84;
    /// <summary>How much of the dry mix excites the bank; the sum over 64 strings stays a subtle halo.</summary>
    private const double InputGain = .035;

    private sealed class PianoString
    {
        public float[] Delay = Array.Empty<float>();
        public int Write;
        /// <summary>Loop length in samples, fractional so every string is tuned exactly.</summary>
        public double Length;
        /// <summary>Per-loop gain giving the string its decay time once the pedal holds it open.</summary>
        public double Feedback;
        /// <summary>One-pole lowpass coefficient inside the loop; dulls the string a little every pass.</summary>
        public double DampAlpha;
        public double DampState;
    }

    private readonly PianoString[] _strings;
    private readonly double _sendAlpha;
    private readonly int _sampleRate;
    private double _send;
    private int _quietSamples;
    private bool _sleeping = true;

    /// <summary>When off, <see cref="Process"/> leaves the mix bit-identical to the dry signal.</summary>
    public bool Enabled { get; set; } = true;

    public StringResonance(int sampleRate)
    {
        _sampleRate = sampleRate;
        // The pedal send is smoothed over a few milliseconds so a stomped pedal does not click.
        _sendAlpha = 1 - Math.Exp(-2 * Math.PI * 30.0 / sampleRate);
        _strings = new PianoString[LastKey - FirstKey + 1];
        for (var key = FirstKey; key <= LastKey; key++)
        {
            var frequency = 440.0 * Math.Pow(2, (key - 69) / 12.0);
            var length = sampleRate / frequency;
            // Open-string ring time: long, dark bass strings sing for seconds; short treble strings do not.
            var t60 = 6.0 - (key - FirstKey) / (double)(LastKey - FirstKey) * 4.8;
            var feedback = Math.Min(Math.Pow(10, -3.0 * (length / sampleRate) / t60), .98);
            var cutoff = Math.Min(4 * frequency, 8000);
            _strings[key - FirstKey] = new PianoString
            {
                Delay = new float[(int)Math.Ceiling(length) + 2],
                Length = length,
                Feedback = feedback,
                DampAlpha = 1 - Math.Exp(-2 * Math.PI * cutoff / sampleRate)
            };
        }
    }

    /// <summary>
    /// Adds the resonance of the open strings to one interleaved stereo block. <paramref name="damper"/> is
    /// the current pedal position 0..1: below the quarter mark the dampers sit on the strings and nothing is
    /// fed or sustained, and above it the bank both receives more energy and rings longer, up to fully open.
    /// </summary>
    public void Process(float[] interleaved, int frames, double damper)
    {
        if (!Enabled) { if (!_sleeping) Reset(); return; }
        var target = Math.Clamp((damper - .25) / .75, 0, 1);
        if (_sleeping)
        {
            if (target <= 0) return;
            _sleeping = false;
        }
        var peak = 0.0;
        for (var i = 0; i < frames; i++)
        {
            _send += (target - _send) * _sendAlpha;
            var send = _send;
            var input = (interleaved[i * 2] + interleaved[i * 2 + 1]) * .5 * InputGain * send;
            // Lifting the pedal lets the felt fall back: the loop gain collapses and the bank dies fast.
            var hold = .25 + .75 * send;
            var wet = 0.0;
            foreach (var piano in _strings)
            {
                var buffer = piano.Delay;
                var position = piano.Write - piano.Length;
                if (position < 0) position += buffer.Length;
                var index = (int)position; var fraction = position - index;
                var next = index + 1; if (next >= buffer.Length) next = 0;
                var sample = buffer[index] + (buffer[next] - buffer[index]) * fraction;
                piano.DampState += piano.DampAlpha * (sample - piano.DampState);
                buffer[piano.Write] = (float)(piano.DampState * piano.Feedback * hold + input);
                if (++piano.Write >= buffer.Length) piano.Write = 0;
                wet += sample;
            }
            if (wet > peak) peak = wet; else if (-wet > peak) peak = -wet;
            interleaved[i * 2] = Math.Clamp(interleaved[i * 2] + (float)wet, -1f, 1f);
            interleaved[i * 2 + 1] = Math.Clamp(interleaved[i * 2 + 1] + (float)wet, -1f, 1f);
        }
        // Once the pedal is up and the strings have faded, go back to sleep so idle playing costs nothing.
        if (target <= 0 && peak < 1e-5) { _quietSamples += frames; if (_quietSamples > _sampleRate) Reset(); }
        else _quietSamples = 0;
    }

    /// <summary>Silences every string and suspends processing until the pedal feeds the bank again.</summary>
    private void Reset()
    {
        foreach (var piano in _strings) { Array.Clear(piano.Delay, 0, piano.Delay.Length); piano.DampState = 0; piano.Write = 0; }
        _send = 0; _quietSamples = 0; _sleeping = true;
    }
}
