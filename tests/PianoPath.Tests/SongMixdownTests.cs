using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the voice half of an offline take (Video/ArVideoAudio.cs, roadmap §14.10 AR‑S7): the piano track
/// covers exactly the ticks of the take at 44.1 kHz — sample-exact on even and awkward frame rates alike —
/// and every tick's note events fire with that tick's span.
/// </summary>
public class SongMixdownTests
{
    [Fact]
    public void AnEvenTakeLandsSampleExactOnTheTakeEnd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-mix-{Guid.NewGuid():N}.wav");
        try
        {
            var plan = ArVideoRenderProfile.Plan(640, 360, 0.5, 12, 1, 200);
            var synth = new TickingSynth();
            using (var wav = WavWriter.TryCreate(path)!)
                Assert.Equal(22050, ArVideoMixdown.MixTake(new ArVideoAudio(synth, _ => []), plan));
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(WavWriter.HeaderBytes + 22050 * 4, bytes.Length);
            Assert.Equal(BitConverter.ToInt32(bytes, 40), 22050 * 4);            // the close patched the data size
            Assert.All(bytes.Skip(WavWriter.HeaderBytes), b => Assert.Equal(0, b)); // no events: a quiet take stays quiet
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void AnAwkwardRateStillLandsSampleExact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-mix-{Guid.NewGuid():N}.wav");
        try
        {
            var plan = ArVideoRenderProfile.Plan(640, 360, 7d / 13, 13, 1, 200);
            Assert.Equal(7, plan.Frames);
            var targetEnd = ArVideoMixdown.FramesThrough(6, 13);                  // cumulative rounding, never per-tick
            using (var wav = WavWriter.TryCreate(path)!)
                Assert.Equal(targetEnd, ArVideoMixdown.MixTake(new ArVideoAudio(new TickingSynth(), _ => []), plan));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void EventsFireWithTheirTickAndSoundFromIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-mix-{Guid.NewGuid():N}.wav");
        try
        {
            var synth = new TickingSynth();
            var script = new Dictionary<int, ArVideoNoteEvent[]>
            {
                [1] = [new ArVideoNoteEvent(60, 110, true)],
                [4] = [new ArVideoNoteEvent(60, 0, false)],
            };
            var plan = ArVideoRenderProfile.Plan(640, 360, 0.5, 12, 1, 200);
            using (var wav = WavWriter.TryCreate(path)!)
                ArVideoMixdown.MixTake(new ArVideoAudio(synth, tick => script.GetValueOrDefault(tick, []), wav), plan);
            Assert.Equal(new[] { "on:60@0.87", "off:60" }, synth.Log);       // events arrive, in take order
            var pcm = File.ReadAllBytes(path)[WavWriter.HeaderBytes..];
            // Tick 0's samples stay silent, tick 1's very first samples sing: the press lands with its tick.
            short At(long frame, int channel) => BitConverter.ToInt16(pcm, (int)(frame * 2 + channel) * 2);
            Assert.True(At(100, 0) == 0 && At(3674, 1) == 0 && Math.Abs((int)At(3676, 0)) > 400,
                $"Tick 0 must be silent and tick 1's first sample must sing ({At(100, 0)}, {At(3674, 1)}, {At(3676, 0)}); the take runs 12 fps, 3675 samples a tick.");
            var held = Math.Abs((int)At(5000, 0)) + Math.Abs((int)At(9000, 1));
            Assert.True(held > 800, $"The held tick keeps sounding ({held}).");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    /// <summary>A deterministic square-wave-ish voice: note-on starts a steady tone, note-off stops it dead.</summary>
    private sealed class TickingSynth : IPianoSynthesizer
    {
        private int _sounding;
        internal readonly List<string> Log = [];
        internal int SoundingTicks => _sounding;

        public string Name => "ticking test synth";
        public int ActiveVoiceCount => _sounding;
        public double DamperLevel => 0;

        public void NoteOn(int channel, int pitch, int velocity) { _sounding++; Log.Add($"on:{pitch}@{velocity / 127d:0.00}"); }
        public void NoteOff(int channel, int pitch) { _sounding = Math.Max(0, _sounding - 1); Log.Add($"off:{pitch}"); }
        public void ProcessMidi(int channel, int command, int data1, int data2) { }
        public void AllNotesOff(int? channel = null) { _sounding = 0; }

        public void Render(float[] interleaved, int frames)
        {
            for (var i = 0; i < frames * 2; i++) interleaved[i] = _sounding > 0 ? 0.4f : 0f;
        }
    }
}
