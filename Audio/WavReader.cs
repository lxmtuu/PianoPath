using System;
using System.IO;

namespace PianoPath;

/// <summary>One decoded audio file: interleaved 16-bit samples plus the layout needed to play them.</summary>
internal sealed class WavData
{
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    /// <summary>Interleaved samples, one <c>short</c> per channel per frame.</summary>
    public required short[] Samples { get; init; }
    public int Frames => Samples.Length / Channels;
}

/// <summary>
/// Reads RIFF WAVE files the way the SFZ engine needs them: PCM 16/24/32-bit and IEEE float32, mono or
/// stereo, any sample rate (the synthesiser resamples while playing). Everything is converted to 16-bit
/// on load — sample libraries ship 24-bit masters, but the audible difference disappears under the
/// velocity scaling and effects, while the memory cost of keeping gigabyte sets in 24-bit does not.
/// Unknown chunks (LIST, fact, cue, bext…) are skipped; compressed formats are rejected with the format
/// tag in the message so a user knows what the file actually contains.
/// </summary>
internal static class WavReader
{
    public static WavData Read(string path)
    {
        using var stream = File.OpenRead(path);
        try { return Read(stream); }
        catch (InvalidDataException ex) { throw new InvalidDataException($"{Path.GetFileName(path)}: {ex.Message}"); }
    }

    public static WavData Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("not a RIFF file."); // "RIFF"
        reader.ReadUInt32(); // RIFF size; the chunk walk below finds the real extents
        if (reader.ReadUInt32() != 0x45564157) throw new InvalidDataException("not a WAVE file."); // "WAVE"
        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = reader.ReadUInt32(); var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1); // chunks are word-aligned
            if (id == 0x20746d66) // "fmt "
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); sampleRate = (int)reader.ReadUInt32();
                reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                // WAVE_FORMAT_EXTENSIBLE wraps the real format in the first two bytes of its GUID.
                if (format == 0xFFFE && size >= 26) { reader.ReadUInt16(); reader.ReadUInt32(); format = reader.ReadUInt16(); }
            }
            else if (id == 0x61746164) // "data"
            {
                data = reader.ReadBytes((int)Math.Min(size, stream.Length - stream.Position));
            }
            if (next > stream.Length) break;
            stream.Position = next;
        }
        if (format == 0 || data is null) throw new InvalidDataException("missing fmt or data chunk.");
        if (channels is < 1 or > 2) throw new InvalidDataException($"{channels} channels; only mono and stereo are supported.");
        if (sampleRate < 8000) throw new InvalidDataException($"implausible sample rate {sampleRate}.");
        var samples = format switch
        {
            1 when bits == 16 => FromPcm16(data),
            1 when bits == 24 => FromPcm24(data),
            1 when bits == 32 => FromPcm32(data),
            3 when bits == 32 => FromFloat32(data),
            1 => throw new InvalidDataException($"{bits}-bit PCM is not supported (16/24/32 are)."),
            _ => throw new InvalidDataException($"compressed or unknown format tag {format}; only PCM and float WAVE are supported.")
        };
        // A truncated final frame (cut-off download) is dropped rather than played as a click.
        if (samples.Length % channels != 0) Array.Resize(ref samples, samples.Length - samples.Length % channels);
        return new WavData { SampleRate = sampleRate, Channels = channels, Samples = samples };
    }

    private static short[] FromPcm16(byte[] data)
    {
        var samples = new short[data.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8);
        return samples;
    }

    private static short[] FromPcm24(byte[] data)
    {
        var samples = new short[data.Length / 3];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = data[i * 3] | data[i * 3 + 1] << 8 | data[i * 3 + 2] << 16;
            if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000); // sign-extend
            samples[i] = (short)(value >> 8);
        }
        return samples;
    }

    private static short[] FromPcm32(byte[] data)
    {
        var samples = new short[data.Length / 4];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(BitConverter.ToInt32(data, i * 4) >> 16);
        return samples;
    }

    private static short[] FromFloat32(byte[] data)
    {
        var samples = new short[data.Length / 4];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = BitConverter.ToSingle(data, i * 4);
            samples[i] = (short)Math.Clamp((int)Math.Round(value * 32767f), short.MinValue, short.MaxValue);
        }
        return samples;
    }
}
