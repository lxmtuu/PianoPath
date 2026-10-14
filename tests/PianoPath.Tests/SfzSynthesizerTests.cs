using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The SFZ synthesiser's voice bookkeeping, driven with one synthetic sample so a test can state
/// which voices survive a render block instead of listening for them.
/// </summary>
public class SfzSynthesizerTests
{
    private static WavData Tone(int frames = 44100)
    {
        var samples = new short[frames];
        Array.Fill(samples, (short)12000);
        return new WavData { SampleRate = 44100, Channels = 1, Samples = samples };
    }

    private static SfzSynthesizer Synth(params SfzRegion[] regions) =>
        new(new SfzInstrumentData { Name = "Fixture", Regions = regions });

    private static SfzRegion Region(WavData sample, double attackSeconds = .001, bool triggerRelease = false) => new()
    {
        Sample = sample,
        SamplePath = "tone.wav",
        AttackSeconds = attackSeconds,
        TriggerRelease = triggerRelease,
    };

    [Fact]
    public void A_slow_attack_voice_survives_its_first_small_render_blocks()
    {
        // A 2 s attack rendered in 64-frame blocks (a low-latency ASIO size) is only at ~0.0007 of
        // its envelope after the first block; culling quiet voices there used to silence the note.
        var synth = Synth(Region(Tone(), attackSeconds: 2));
        synth.NoteOn(0, 60, 96);

        var block = new float[64 * 2];
        for (var i = 0; i < 16; i++) synth.Render(block, 64);

        Assert.Equal(1, synth.ActiveVoiceCount);
    }

    [Fact]
    public void A_released_voice_still_dies_once_its_release_has_rung_out()
    {
        var synth = Synth(Region(Tone()));
        synth.NoteOn(0, 60, 96);
        synth.NoteOff(0, 60);

        var block = new float[512 * 2];
        for (var i = 0; i < 200 && synth.ActiveVoiceCount > 0; i++) synth.Render(block, 512);

        Assert.Equal(0, synth.ActiveVoiceCount);
    }

    [Fact]
    public void Lifting_the_sostenuto_pedal_plays_the_damper_fall_it_releases()
    {
        var synth = Synth(Region(Tone()), Region(Tone(), triggerRelease: true));
        synth.NoteOn(0, 60, 96);
        synth.ProcessMidi(0, 0xB0, 66, 127); // sostenuto down: catches the held key
        synth.NoteOff(0, 60);                // the string stays open, so no damper falls yet
        Assert.Equal(1, synth.ActiveVoiceCount);

        synth.ProcessMidi(0, 0xB0, 66, 0);   // sostenuto up: the damper falls now

        // The released string plus the release sample its fall triggers.
        Assert.Equal(2, synth.ActiveVoiceCount);
    }
}
