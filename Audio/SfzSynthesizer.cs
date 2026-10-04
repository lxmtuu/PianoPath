using System;
using System.Collections.Generic;
using System.Linq;

namespace PianoPath;

/// <summary>
/// Sample playback for the Salamander Grand SFZ: velocity layers pick the right recording per keystroke,
/// round-robins alternate takes, and <c>trigger=release</c> regions add damper-fall noise when a string is
/// actually damped (attenuated by <c>rt_decay</c> for notes that had already rung out). The continuous
/// damper model handles CC 64 half-pedalling and repedal catch; <see cref="DamperLevel"/> drives the shared
/// sympathetic-resonance bank. Audio renders straight to float32 for the low-latency pipeline.
/// </summary>
internal sealed class SfzSynthesizer : IPianoSynthesizer
{
    private const int VoiceLimit = 96;
    private readonly SfzInstrumentData _instrument;
    private readonly SfzRegion[] _attackRegions;
    private readonly SfzRegion[] _releaseRegions;
    /// <summary>Regions fired by the pedal itself (<c>on_locc64</c>): the mechanical thump of it going down or up.</summary>
    private readonly SfzRegion[] _pedalNoiseRegions;
    /// <summary>Last raw CC 64 value per channel, for edge-detecting the pedal-noise windows.</summary>
    private readonly int[] _lastCc64 = new int[16];
    /// <summary>Seeded so <c>lorand</c> take-picking cannot break the deterministic verification renders.</summary>
    private readonly Random _random = new(12345);
    private readonly int _sampleRate;
    private readonly object _gate = new();
    private readonly List<Voice> _voices = [];
    /// <summary>Damper position per channel, 0..1 from CC 64 — free, catch and fully engaged.</summary>
    private readonly double[] _damper = new double[16];
    private const double DamperFree = .25;
    private const double DamperCatch = .5;
    private readonly bool[] _sostenuto = new bool[16];
    private readonly bool[] _soft = new bool[16];
    /// <summary>Per-key round-robin counters; NoteOn advances them so <c>seq_position</c> regions alternate.</summary>
    private readonly int[] _roundRobin = new int[128];
    /// <summary>When and how hard each key last went down — release samples need both (<c>rt_decay</c>).</summary>
    private readonly double[] _noteOnClock = new double[128];
    private readonly int[] _noteOnVelocity = new int[128];
    /// <summary>Seconds rendered so far; the timebase for how long a note was held.</summary>
    private double _clock;
    private double[] _mixLeft = [];
    private double[] _mixRight = [];

    private sealed class Voice
    {
        public required SfzRegion Region;
        public int Channel, Pitch;
        public double Position, Step, Gain, PanLeft, PanRight;
        public double Attack, Decay, Sustain, Release;
        public double Age, ReleaseAge = -1, ReleaseLevel;
        /// <summary>Half-pedalling: 1 is a normal release, larger values stretch it (damper riding high).</summary>
        public double ReleaseScale = 1;
        public bool KeyHeld = true, SustainHeld, SostenutoHeld;
        /// <summary>True for a damper-fall (<c>trigger=release</c>) voice: it plays out once and ignores pedals.</summary>
        public bool IsRelease;
        public double FilterLeft, FilterRight;
        public double Envelope()
        {
            if (ReleaseAge >= 0) return ReleaseLevel * Math.Exp(-ReleaseAge * 7.0 / (Release * ReleaseScale));
            if (Attack > 0 && Age < Attack) return Age / Attack;
            var decayAge = Age - Attack;
            if (Decay > 0 && decayAge < Decay) return 1 + (Sustain - 1) * decayAge / Decay;
            return Sustain;
        }
        public void ReleaseKey() { if (ReleaseAge >= 0) return; ReleaseLevel = Math.Max(.01, Envelope()); ReleaseAge = 0; }
        /// <summary>The damper moved mid-release: rebase the decay at the current loudness, no click.</summary>
        public void RetimeRelease(double scale)
        {
            if (ReleaseAge < 0) { ReleaseScale = scale; return; }
            if (Math.Abs(scale - ReleaseScale) < .01) return;
            ReleaseLevel = Math.Max(.01, Envelope()); ReleaseAge = 0; ReleaseScale = scale;
        }
    }

    public SfzSynthesizer(SfzInstrumentData instrument, int sampleRate = 44100)
    {
        _instrument = instrument;
        _sampleRate = sampleRate;
        _attackRegions = instrument.Regions.Where(r => !r.TriggerRelease && !r.IsCc64Trigger).ToArray();
        _releaseRegions = instrument.Regions.Where(r => r.TriggerRelease && !r.IsCc64Trigger).ToArray();
        _pedalNoiseRegions = instrument.Regions.Where(r => r.IsCc64Trigger).ToArray();
    }

    public string Name => _instrument.Name;
    public int ActiveVoiceCount { get { lock (_gate) return _voices.Count; } }
    /// <summary>How many distinct sample windows the instrument maps — shown on the Settings page.</summary>
    public int RegionCount => _instrument.Regions.Count;

    public void ProcessMidi(int channel, int command, int data1, int data2)
    {
        channel &= 15;
        switch (command & 0xF0)
        {
            case 0x90: NoteOn(channel, data1, data2); break;
            case 0x80: NoteOff(channel, data1); break;
            case 0xB0:
                if (data1 == 67) lock (_gate) _soft[channel] = data2 >= 64;
                if (data1 == 64) { SetDamper(channel, data2); PlayPedalNoise(channel, data2); }
                else if (data1 == 66) SetSostenuto(channel, data2 >= 64);
                else if (data1 == 120 || data1 == 123) AllNotesOff(channel);
                break;
        }
    }

    public void NoteOn(int channel, int pitch, int velocity)
    {
        if (velocity <= 0) { NoteOff(channel, pitch); return; }
        pitch = Math.Clamp(pitch, 0, 127);
        lock (_gate)
        {
            var key = Math.Clamp(pitch + _instrument.NoteOffset, 0, 127);
            var take = _roundRobin[pitch]++;
            _noteOnClock[pitch] = _clock; _noteOnVelocity[pitch] = velocity;
            foreach (var region in _attackRegions)
            {
                if (key < region.LoKey || key > region.HiKey || velocity < region.LoVel || velocity > region.HiVel) continue;
                if (region.SeqLength > 1 && take % region.SeqLength + 1 != region.SeqPosition) continue;
                StartVoice(region, channel & 15, pitch, key, velocity, extraGain: 1);
            }
        }
    }

    /// <summary>Adds one playing voice for a matched region; the caller holds the gate.</summary>
    private void StartVoice(SfzRegion region, int channel, int pitch, int key, int velocity, double extraGain)
    {
        if (_voices.Count >= VoiceLimit) StealVoice();
        // amp_veltrack still controls the depth of the response; a softened curve keeps real piano
        // velocities in the middle of the range present without changing raw velocity-layer selection.
        var track = region.AmpVeltrack; var curve = Math.Pow(velocity / 127.0, 1.45);
        var velocityGain = Math.Clamp(1 - track + track * curve, 0, 1);
        var pan = region.Pan / 100.0;
        var semitones = key - region.Root + region.Transpose + region.TuneCents / 100.0;
        _voices.Add(new Voice
        {
            Region = region, Channel = channel, Pitch = pitch,
            Position = Math.Min(region.Offset, Math.Max(0, region.Sample.Frames - 2)),
            Step = region.Sample.SampleRate / (double)_sampleRate * Math.Pow(2, semitones / 12.0),
            Gain = Math.Pow(10, region.VolumeDb / 20.0) * velocityGain * extraGain,
            PanLeft = Math.Sqrt((1 - pan) * .5), PanRight = Math.Sqrt((1 + pan) * .5),
            Attack = Math.Clamp(region.AttackSeconds, .0005, 20), Decay = Math.Clamp(region.DecaySeconds, .0005, 20),
            Sustain = Math.Clamp(region.SustainLevel, .001, 1), Release = Math.Clamp(region.ReleaseSeconds, .015, 20),
            IsRelease = region.TriggerRelease || region.IsCc64Trigger, KeyHeld = !region.TriggerRelease && !region.IsCc64Trigger
        });
    }

    public void NoteOff(int channel, int pitch)
    {
        pitch = Math.Clamp(pitch, 0, 127);
        lock (_gate)
        {
            var damper = _damper[channel & 15];
            var damped = false;
            foreach (var voice in _voices.Where(v => v.Channel == (channel & 15) && v.Pitch == pitch && v.KeyHeld))
            {
                voice.KeyHeld = false;
                if (voice.SostenutoHeld) continue;
                if (damper >= DamperCatch) { voice.SustainHeld = true; continue; }
                voice.ReleaseScale = ReleaseScaleFor(damper);
                voice.ReleaseKey();
                damped = true;
            }
            // The damper only falls when nothing keeps the string open — that is when the noise happens.
            if (damped) TriggerReleaseSamples(channel & 15, pitch);
        }
    }

    /// <summary>
    /// Plays the <c>trigger=release</c> regions for a string whose damper just fell. <c>rt_decay</c>
    /// attenuates the noise for notes that had long decayed — a string barely vibrating barely thumps.
    /// The caller holds the gate.
    /// </summary>
    /// <summary>Fires <c>on_locc64</c> one-shots when the raw pedal value crosses into a region's window.</summary>
    private void PlayPedalNoise(int channel, int value)
    {
        if (_pedalNoiseRegions.Length == 0) { _lastCc64[channel] = value; return; }
        lock (_gate)
        {
            var previous = _lastCc64[channel]; _lastCc64[channel] = value;
            if (previous == value) return;
            var take = _random.NextDouble();
            foreach (var region in _pedalNoiseRegions)
            {
                if (value < region.OnLoCc64 || value > region.OnHiCc64) continue;
                if (previous >= region.OnLoCc64 && previous <= region.OnHiCc64) continue; // no edge: still inside the window
                if (take < region.LoRand || take >= region.HiRand) continue;              // lorand/hirand picks one take
                // A fresh pedal action replaces the tail of the previous one (the source sets say polyphony=1).
                foreach (var voice in _voices)
                    if (voice.Channel == channel && voice.Region.IsCc64Trigger) { voice.Release = Math.Min(voice.Release, .1); voice.ReleaseScale = 1; voice.ReleaseKey(); }
                StartVoice(region, channel, pitch: -1, key: region.Root, velocity: 127, extraGain: 1);
            }
        }
    }

    private void TriggerReleaseSamples(int channel, int pitch)
    {
        if (_releaseRegions.Length == 0) return;
        var velocity = _noteOnVelocity[pitch]; if (velocity <= 0) return;
        var held = Math.Max(0, _clock - _noteOnClock[pitch]);
        var key = Math.Clamp(pitch + _instrument.NoteOffset, 0, 127);
        foreach (var region in _releaseRegions)
        {
            if (key < region.LoKey || key > region.HiKey || velocity < region.LoVel || velocity > region.HiVel) continue;
            StartVoice(region, channel, pitch, key, velocity, extraGain: Math.Pow(10, -region.RtDecayDb * held / 20.0));
        }
    }

    private static double ReleaseScaleFor(double damper) =>
        damper <= DamperFree ? 1 : 1 + (Math.Min(damper, DamperCatch) - DamperFree) / (DamperCatch - DamperFree) * 5;

    /// <summary>Continuous CC 64 with a free, half-pedal and fully-damped zone.</summary>
    private void SetDamper(int channel, int value)
    {
        lock (_gate)
        {
            var level = Math.Clamp(value, 0, 127) / 127.0;
            if (Math.Abs(_damper[channel] - level) < .001) return;
            _damper[channel] = level;
            if (level >= DamperCatch)
            {
                foreach (var voice in _voices)
                {
                    if (voice.Channel != channel || voice.KeyHeld || voice.SustainHeld || voice.SostenutoHeld || voice.IsRelease) continue;
                    if (voice.ReleaseAge < 0) continue;
                    var caught = voice.Envelope();
                    if (caught < .02) continue; // a string that has already faded out cannot be caught again
                    voice.SustainHeld = true; voice.ReleaseAge = -1;
                    voice.Sustain = Math.Clamp(Math.Min(voice.Sustain, caught), .01, 1);
                    voice.Age = Math.Max(voice.Age, voice.Attack + voice.Decay);
                }
                return;
            }
            var scale = ReleaseScaleFor(level);
            var dampedPitches = new List<int>();
            foreach (var voice in _voices)
            {
                if (voice.Channel != channel || voice.IsRelease) continue;
                if (voice.SustainHeld)
                {
                    voice.SustainHeld = false;
                    if (voice.KeyHeld || voice.SostenutoHeld) continue;
                    voice.ReleaseScale = scale; voice.ReleaseKey();
                    if (!dampedPitches.Contains(voice.Pitch)) dampedPitches.Add(voice.Pitch);
                }
                else if (!voice.KeyHeld && !voice.SostenutoHeld && voice.ReleaseAge >= 0) voice.RetimeRelease(scale);
            }
            // Dampers falling back onto open strings thump once per string, not once per layered voice.
            foreach (var pitch in dampedPitches) TriggerReleaseSamples(channel, pitch);
        }
    }

    public double DamperLevel
    {
        get { lock (_gate) { var level = 0.0; foreach (var value in _damper) level = Math.Max(level, value); return level; } }
    }

    private void SetSostenuto(int channel, bool down)
    {
        lock (_gate)
        {
            if (_sostenuto[channel] == down) return;
            _sostenuto[channel] = down;
            if (down)
            {
                foreach (var voice in _voices.Where(v => v.Channel == channel && v.KeyHeld)) voice.SostenutoHeld = true;
            }
            else foreach (var voice in _voices.Where(v => v.Channel == channel && v.SostenutoHeld))
            {
                voice.SostenutoHeld = false;
                if (voice.KeyHeld || voice.SustainHeld) continue;
                if (_damper[channel] >= DamperCatch) { voice.SustainHeld = true; continue; }
                voice.ReleaseScale = ReleaseScaleFor(_damper[channel]); voice.ReleaseKey();
            }
        }
    }

    public void AllNotesOff(int? channel = null)
    {
        lock (_gate) foreach (var voice in _voices.Where(v => channel is null || v.Channel == channel))
        {
            voice.KeyHeld = false; voice.SustainHeld = false; voice.SostenutoHeld = false; voice.ReleaseScale = 1; voice.ReleaseKey();
        }
    }

    /// <summary>Drops the oldest voice already releasing, or the oldest overall, to honour a Note On.</summary>
    private void StealVoice()
    {
        for (var i = 0; i < _voices.Count; i++) if (_voices[i].ReleaseAge >= 0) { _voices.RemoveAt(i); return; }
        if (_voices.Count > 0) _voices.RemoveAt(0);
    }

    public void Render(float[] interleaved, int frames)
    {
        lock (_gate)
        {
            RenderMix(frames);
            for (var i = 0; i < frames; i++)
            {
                interleaved[i * 2] = SoftClip(_mixLeft[i]); interleaved[i * 2 + 1] = SoftClip(_mixRight[i]);
            }
            _clock += frames / (double)_sampleRate;
        }
    }

    private void RenderMix(int frames)
    {
        if (_mixLeft.Length < frames) { _mixLeft = new double[frames]; _mixRight = new double[frames]; }
        var left = _mixLeft; var right = _mixRight;
        Array.Clear(left, 0, frames); Array.Clear(right, 0, frames);
        var sampleSeconds = 1.0 / _sampleRate;
        for (var v = _voices.Count - 1; v >= 0; v--)
        {
            var voice = _voices[v]; var region = voice.Region; var audio = region.Sample;
            var envelope = 1.0;
            var soft = _soft[voice.Channel] && !voice.IsRelease; var softGain = soft ? .72 : 1.0;
            var filterAlpha = soft ? 1 - Math.Exp(-2 * Math.PI * 4200 / _sampleRate) : 1.0;
            var channels = audio.Channels; var samples = audio.Samples; var lastFrame = audio.Frames - 1;
            for (var i = 0; i < frames; i++)
            {
                if (region.Loop && voice.Position >= region.LoopEnd)
                    voice.Position = region.LoopStart + (voice.Position - region.LoopEnd) % (region.LoopEnd - region.LoopStart);
                if (voice.Position >= lastFrame) break;
                var frame = (int)voice.Position; var fraction = voice.Position - frame;
                var baseIndex = frame * channels;
                double sampleL, sampleR;
                if (channels == 2)
                {
                    sampleL = (samples[baseIndex] + (samples[baseIndex + 2] - samples[baseIndex]) * fraction) / 32768.0;
                    sampleR = (samples[baseIndex + 1] + (samples[baseIndex + 3] - samples[baseIndex + 1]) * fraction) / 32768.0;
                }
                else
                {
                    sampleL = (samples[baseIndex] + (samples[baseIndex + 1] - samples[baseIndex]) * fraction) / 32768.0;
                    sampleR = sampleL;
                }
                if (soft)
                {
                    voice.FilterLeft += filterAlpha * (sampleL - voice.FilterLeft);
                    voice.FilterRight += filterAlpha * (sampleR - voice.FilterRight);
                    sampleL = voice.FilterLeft; sampleR = voice.FilterRight;
                }
                envelope = voice.Envelope(); var gain = envelope * voice.Gain * softGain;
                left[i] += sampleL * gain * voice.PanLeft; right[i] += sampleR * gain * voice.PanRight;
                voice.Position += voice.Step; voice.Age += sampleSeconds; if (voice.ReleaseAge >= 0) voice.ReleaseAge += sampleSeconds;
            }
            if (envelope < .001 || voice.Position >= lastFrame || voice.ReleaseAge >= 0 && voice.ReleaseAge > voice.Release * voice.ReleaseScale * 1.1) _voices.RemoveAt(v);
        }
    }

    /// <summary>A gentle tanh limiter keeps the per-voice sum within the float audio range.</summary>
    private static float SoftClip(double sample) => (float)Math.Tanh(sample * 1.45);
}
