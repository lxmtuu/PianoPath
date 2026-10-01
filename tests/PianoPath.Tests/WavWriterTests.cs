using Xunit;

using System.Text;

namespace PianoPath.Tests;

/// <summary>
/// The WAV writer that carries the audio of a recording. It is checked here rather than only in
/// <c>VerifyRecordingAudioTrack</c> because a header the writer gets wrong is a file no player opens,
/// and that does not need a Windows sound device to prove.
/// </summary>
public class WavWriterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"keyflow-tests-wav-{Guid.NewGuid():N}.wav");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* a test that never created it has nothing to delete */ }
    }

    [Fact]
    public void Opening_a_track_starts_empty_with_the_format_already_chosen()
    {
        using var wav = WavWriter.TryCreate(_path);

        Assert.NotNull(wav);
        Assert.Equal(44100, wav!.SampleRate);
        Assert.Equal(2, wav.Channels);
        Assert.Equal(0, wav.Frames);
        Assert.Equal(0, wav.DataBytes);
        Assert.False(wav.IsClosed);
    }

    [Fact]
    public void An_empty_track_that_is_closed_leaves_a_header_only_file_with_zero_sizes()
    {
        // The header is written to a buffered stream, so the file on disk only appears once the track is
        // closed - which is also when the two size fields get their final values.
        WavWriter.TryCreate(_path)!.Dispose();

        var bytes = File.ReadAllBytes(_path);
        Assert.Equal(WavWriter.HeaderBytes, bytes.Length);
        Assert.Equal(36u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal(0u, BitConverter.ToUInt32(bytes, WavWriter.HeaderBytes - 4));
    }

    [Fact]
    public void Appending_interleaved_samples_counts_frames_not_values()
    {
        using var wav = WavWriter.TryCreate(_path)!;
        var samples = new short[] { 100, -100, 200, -200, 300, -300, 400, -400 };

        wav.Append(samples, samples.Length);

        Assert.Equal(4, wav.Frames);
        Assert.Equal(16, wav.DataBytes);
    }

    [Fact]
    public void A_block_that_ends_inside_a_frame_keeps_whole_frames_only()
    {
        using var wav = WavWriter.TryCreate(_path)!;
        var samples = new short[] { 100, -100, 200, -200, 300, -300, 400, -400 };
        wav.Append(samples, samples.Length);

        wav.Append(samples, 3);      // one and a half stereo frames: the half is dropped

        Assert.Equal(5, wav.Frames);
    }

    [Fact]
    public void Silence_advances_the_track_by_the_frames_it_writes()
    {
        using var wav = WavWriter.TryCreate(_path)!;
        wav.AppendSilence(11);

        Assert.Equal(11, wav.Frames);
        Assert.Equal(44, wav.DataBytes);
        Assert.Equal(11 / 44100.0, wav.Seconds, 9);
    }

    [Fact]
    public void Disposing_patches_both_sizes_in_the_header_and_closes_the_file()
    {
        var wav = WavWriter.TryCreate(_path)!;
        wav.AppendSilence(11);
        wav.Dispose();

        Assert.True(wav.IsClosed);
        var bytes = File.ReadAllBytes(_path);
        Assert.Equal(44 + 11 * 4, bytes.Length);
        Assert.Equal(36 + 44u, BitConverter.ToUInt32(bytes, 4));       // RIFF chunk size
        Assert.Equal(44u, BitConverter.ToUInt32(bytes, WavWriter.HeaderBytes - 4));   // data chunk size
    }

    [Fact]
    public void Appending_after_the_track_is_closed_is_ignored_instead_of_throwing()
    {
        var wav = WavWriter.TryCreate(_path)!;
        wav.AppendSilence(4);
        wav.Dispose();

        wav.Append([1, 2, 3, 4], 4);

        Assert.Equal(4, wav.Frames);
    }

    [Fact]
    public void The_canonical_header_describes_uncompressed_stereo_pcm()
    {
        var header = WavWriter.Header(44100, 2, 1000);

        Assert.Equal(WavWriter.HeaderBytes, header.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(header, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(header, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(header, 36, 4));
        Assert.Equal(1, BitConverter.ToInt16(header, 20));            // 1 = uncompressed PCM
        Assert.Equal(2, BitConverter.ToInt16(header, 22));
        Assert.Equal(44100u, BitConverter.ToUInt32(header, 24));
        Assert.Equal(44100u * 4, BitConverter.ToUInt32(header, 28));  // bytes per second
        Assert.Equal(16, BitConverter.ToInt16(header, 34));           // bits per sample
        Assert.Equal(1000u, BitConverter.ToUInt32(header, 40));
    }

    [Fact]
    public void A_path_that_cannot_be_written_gives_up_instead_of_throwing_at_the_caller()
    {
        Assert.Null(WavWriter.TryCreate("   "));
    }

    [Fact]
    public void A_mono_track_is_clamped_to_one_channel_and_a_stereo_one_to_two()
    {
        using var mono = WavWriter.TryCreate(_path, 22050, 1)!;

        Assert.Equal(1, mono.Channels);
        Assert.Equal(22050, mono.SampleRate);
    }
}
