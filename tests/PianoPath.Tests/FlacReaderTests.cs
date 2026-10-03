using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The hand-rolled FLAC decoder against files produced by libFLAC (via libsndfile): each fixture pairs
/// a FLAC file with a WAV twin written from the same integer samples, so equality here means bit-exact
/// decoding of the real encoder's output — LPC and fixed predictors, Rice residuals, stereo
/// decorrelation and both bit depths the free piano libraries ship.
/// </summary>
public class FlacReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("tone16m", 44100, 1)]  // tonal mono 16-bit: LPC subframes
    [InlineData("noise16s", 22050, 2)] // noisy stereo 16-bit: high-order residuals, left/side candidates
    [InlineData("mix24s", 48000, 2)]   // correlated stereo 24-bit: mid/side, the studio-master format
    [InlineData("quiet24m", 48000, 1)] // silence then a ramp: constant and fixed subframes
    public void Flac_fixtures_decode_bit_exact_to_their_wav_twins(string name, int rate, int channels)
    {
        var flac = FlacReader.Read(Fixture(name + ".flac"));
        var wav = WavReader.Read(Fixture(name + ".wav"));

        Assert.Equal(rate, flac.SampleRate);
        Assert.Equal(channels, flac.Channels);
        Assert.Equal(wav.Samples, flac.Samples);
    }

    [Fact]
    public void Files_that_are_not_flac_are_refused_with_the_file_name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-not-flac-{Guid.NewGuid():N}.flac");
        File.WriteAllBytes(path, "RIFF not a flac file at all"u8.ToArray());
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => FlacReader.Read(path));
            Assert.Contains(Path.GetFileName(path), error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Truncated_files_are_refused_rather_than_returned_short()
    {
        var bytes = File.ReadAllBytes(Fixture("tone16m.flac"));
        var cut = bytes[..(bytes.Length / 2)];

        Assert.Throws<InvalidDataException>(() => FlacReader.Read(cut));
    }
}
