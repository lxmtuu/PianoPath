using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The SFZ instrument reader and the WAV decoder behind it — the pure-parsing half of the SFZ engine,
/// checked here because a mapping read wrong (a velocity window off by one, a 24-bit sample decoded
/// with the wrong sign) picks the wrong recording for a keystroke, and none of that needs a Windows
/// sound device to prove. Playback itself (voices, pedals, release samples) runs under
/// <c>VerifySfzEngine</c> on the Windows job.
/// </summary>
public class SfzReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"keyflow-tests-sfz-{Guid.NewGuid():N}");

    public SfzReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp cleanup is best effort */ }
    }

    private string WriteWav(string name, int channels, int bits, int frames, Func<int, double> wave)
    {
        var path = Path.Combine(_dir, name);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        var bytesPerSample = bits / 8; var dataSize = frames * channels * bytesPerSample;
        w.Write("RIFF"u8.ToArray()); w.Write((uint)(36 + dataSize)); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16u); w.Write((ushort)(bits == 32 ? 3 : 1)); w.Write((ushort)channels);
        w.Write(22050u); w.Write((uint)(22050 * channels * bytesPerSample)); w.Write((ushort)(channels * bytesPerSample)); w.Write((ushort)bits);
        w.Write("data"u8.ToArray()); w.Write((uint)dataSize);
        for (var i = 0; i < frames; i++)
            for (var c = 0; c < channels; c++)
            {
                var value = wave(i);
                if (bits == 16) w.Write((short)Math.Round(value * 32767));
                else if (bits == 24) { var s = (int)Math.Round(value * 8388607); w.Write((byte)s); w.Write((byte)(s >> 8)); w.Write((byte)(s >> 16)); }
                else w.Write((float)value);
            }
        return path;
    }

    [Fact]
    public void Wav_reader_decodes_16_24_and_float_samples_alike()
    {
        var p16 = WriteWav("p16.wav", 1, 16, 100, i => (i - 50) / 64.0);
        var p24 = WriteWav("p24.wav", 1, 24, 100, i => (i - 50) / 64.0);
        var p32 = WriteWav("p32.wav", 2, 32, 100, i => (i - 50) / 64.0);

        var d16 = WavReader.Read(p16); var d24 = WavReader.Read(p24); var d32 = WavReader.Read(p32);

        Assert.Equal(22050, d16.SampleRate);
        Assert.Equal(1, d16.Channels);
        Assert.Equal(100, d16.Frames);
        Assert.Equal(2, d32.Channels);
        Assert.Equal(100, d32.Frames);
        for (var i = 0; i < 100; i++)
        {
            // All three encodings should land on the same 16-bit value within one least-significant bit.
            Assert.InRange(Math.Abs(d24.Samples[i] - d16.Samples[i]), 0, 1);
            Assert.InRange(Math.Abs(d32.Samples[i * 2] - d16.Samples[i]), 0, 1);
            Assert.Equal(d32.Samples[i * 2], d32.Samples[i * 2 + 1]);
        }
    }

    /// <summary>Like <see cref="WriteWav"/> but with the WAVE_FORMAT_EXTENSIBLE header studio exports use.</summary>
    private string WriteExtensibleWav(string name, int bits, int frames, Func<int, double> wave)
    {
        var path = Path.Combine(_dir, name);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        var bytesPerSample = bits / 8; var dataSize = frames * bytesPerSample;
        w.Write("RIFF"u8.ToArray()); w.Write((uint)(60 + dataSize)); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(40u); w.Write((ushort)0xFFFE); w.Write((ushort)1);
        w.Write(22050u); w.Write((uint)(22050 * bytesPerSample)); w.Write((ushort)bytesPerSample); w.Write((ushort)bits);
        w.Write((ushort)22);            // cbSize
        w.Write((ushort)bits);          // wValidBitsPerSample
        w.Write(4u);                    // dwChannelMask
        w.Write((ushort)(bits == 32 ? 3 : 1)); // SubFormat GUID, first two bytes = the real format tag
        w.Write((ushort)0); w.Write((ushort)0x10); w.Write((ushort)0x80);
        w.Write((byte)0); w.Write((byte)0xaa); w.Write((byte)0); w.Write((byte)0x38); w.Write((byte)0x9b); w.Write((byte)0x71);
        w.Write("data"u8.ToArray()); w.Write((uint)dataSize);
        for (var i = 0; i < frames; i++)
        {
            var value = wave(i);
            if (bits == 24) { var s = (int)Math.Round(value * 8388607); w.Write((byte)s); w.Write((byte)(s >> 8)); w.Write((byte)(s >> 16)); }
            else w.Write((float)value);
        }
        return path;
    }

    [Fact]
    public void Wav_reader_unwraps_the_extensible_header_studio_exports_use()
    {
        var plain = WavReader.Read(WriteWav("plain24.wav", 1, 24, 100, i => (i - 50) / 64.0));
        var extensible = WavReader.Read(WriteExtensibleWav("ext24.wav", 24, 100, i => (i - 50) / 64.0));
        var extFloat = WavReader.Read(WriteExtensibleWav("extf32.wav", 32, 100, _ => .5));

        Assert.Equal(plain.Samples, extensible.Samples); // the wrapper changes the header, not the audio
        Assert.Equal(1, extensible.Channels);
        Assert.Equal(22050, extensible.SampleRate);
        Assert.All(extFloat.Samples, s => Assert.Equal(16384, s));
    }

    [Fact]
    public void Wav_reader_rejects_compressed_files_with_the_format_tag()
    {
        var path = WriteWav("plain.wav", 1, 16, 10, _ => 0);
        var bytes = File.ReadAllBytes(path);
        bytes[20] = 85; // format tag 85 = MP3 inside WAVE
        File.WriteAllBytes(path, bytes);

        var error = Assert.Throws<InvalidDataException>(() => WavReader.Read(path));

        Assert.Contains("85", error.Message);
    }

    [Fact]
    public void Regions_inherit_global_group_and_define_values_under_their_own_opcodes()
    {
        WriteWav("a.wav", 1, 16, 200, _ => .1);
        WriteWav("b.wav", 1, 16, 200, _ => .1);
        File.WriteAllText(Path.Combine(_dir, "incl.txt"), "<region> sample=b.wav key=c4 volume=-6\n");
        File.WriteAllText(Path.Combine(_dir, "main.sfz"),
            "// comment line\n#define $TOP 79\n<control> note_offset=12\n<global> ampeg_release=2 unknown_opcode=ignored\n" +
            "<group> lokey=60 hikey=$TOP lovel=1 hivel=79 pitch_keycenter=69 seq_length=2 seq_position=2\n" +
            "<region> sample=a.wav hivel=127\n#include \"incl.txt\"\n");

        var instrument = SfzReader.Read(Path.Combine(_dir, "main.sfz"));

        Assert.Equal("main", instrument.Name);
        Assert.Equal(12, instrument.NoteOffset);
        Assert.Equal(2, instrument.Regions.Count);
        var first = instrument.Regions[0];
        Assert.Equal(60, first.LoKey);
        Assert.Equal(79, first.HiKey);     // from #define through the group
        Assert.Equal(1, first.LoVel);
        Assert.Equal(127, first.HiVel);    // the region's own opcode wins over the group
        Assert.Equal(69, first.Root);
        Assert.Equal(2, first.SeqLength);
        Assert.Equal(2, first.SeqPosition);
        Assert.Equal(2, first.ReleaseSeconds, 3);
        var second = instrument.Regions[1];
        Assert.Equal(60, second.LoKey);    // key=c4 in the include
        Assert.Equal(60, second.HiKey);
        Assert.Equal(-6, second.VolumeDb, 3);
        Assert.Equal(2, second.ReleaseSeconds, 3);
    }

    [Fact]
    public void Sample_paths_honour_default_path_spaces_and_backslashes()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "My Samples"));
        WriteWav(Path.Combine("My Samples", "grand a4.wav"), 2, 16, 200, _ => .1);
        File.WriteAllText(Path.Combine(_dir, "spaced.sfz"),
            "<control> default_path=My Samples\\\n<region> sample=grand a4.wav key=69\n");

        var instrument = SfzReader.Read(Path.Combine(_dir, "spaced.sfz"));

        Assert.Single(instrument.Regions);
        Assert.Equal(2, instrument.Regions[0].Sample.Channels);
        Assert.EndsWith("grand a4.wav", instrument.Regions[0].SamplePath);
    }

    [Fact]
    public void Opcodes_under_unknown_headers_do_not_leak_into_the_next_region()
    {
        WriteWav("t.wav", 1, 16, 200, _ => .1);
        File.WriteAllText(Path.Combine(_dir, "aria.sfz"),
            "<curve> curve_index=7 v000=1 volume=-40\n<region> sample=t.wav key=60\n" +
            "<sample> name=x volume=-40 loop_mode=loop_continuous\n<region> sample=t.wav key=61\n");

        var instrument = SfzReader.Read(Path.Combine(_dir, "aria.sfz"));

        Assert.Equal(2, instrument.Regions.Count);
        Assert.Equal(0, instrument.Regions[0].VolumeDb, 3); // the <curve> block's volume stays its own
        Assert.Equal(0, instrument.Regions[1].VolumeDb, 3); // the <sample> block's opcodes stay its own
        Assert.False(instrument.Regions[1].Loop);
    }

    [Fact]
    public void A_missing_sample_or_an_empty_file_fails_loud()
    {
        File.WriteAllText(Path.Combine(_dir, "missing.sfz"), "<region> sample=nope.wav\n");
        File.WriteAllText(Path.Combine(_dir, "empty.sfz"), "<global> ampeg_release=1\n");

        Assert.Throws<FileNotFoundException>(() => SfzReader.Read(Path.Combine(_dir, "missing.sfz")));
        Assert.Throws<InvalidDataException>(() => SfzReader.Read(Path.Combine(_dir, "empty.sfz")));
    }

    [Fact]
    public void Release_trigger_round_robin_and_loop_opcodes_survive_parsing()
    {
        WriteWav("tone.wav", 1, 16, 2000, _ => .1);
        File.WriteAllText(Path.Combine(_dir, "rel.sfz"),
            "<group> trigger=release rt_decay=6\n<region> sample=tone.wav lokey=a0 hikey=c8\n" +
            "<group> trigger=attack\n<region> sample=tone.wav key=50 loop_mode=loop_continuous loop_start=100 loop_end=1900 tune=25 transpose=-12\n");

        var instrument = SfzReader.Read(Path.Combine(_dir, "rel.sfz"));

        var release = instrument.Regions[0];
        Assert.True(release.TriggerRelease);
        Assert.Equal(6, release.RtDecayDb, 3);
        Assert.Equal(21, release.LoKey);   // a0 by name
        Assert.Equal(108, release.HiKey);  // c8 by name
        var looped = instrument.Regions[1];
        Assert.False(looped.TriggerRelease);
        Assert.True(looped.Loop);
        Assert.Equal(100, looped.LoopStart);
        Assert.Equal(1900, looped.LoopEnd);
        Assert.Equal(25, looped.TuneCents, 3);
        Assert.Equal(-12, looped.Transpose);
    }
}
