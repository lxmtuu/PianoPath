using System;
using System.IO;

namespace PianoPath;

/// <summary>
/// Decodes FLAC files into the same 16-bit <see cref="WavData"/> the WAV reader produces, because the
/// best free piano libraries ship their samples losslessly compressed. The whole format is implemented
/// by hand — constant, verbatim, fixed and LPC subframes, Rice-coded residuals in both 4- and 5-bit
/// flavours, every stereo decorrelation mode and any bit depth up to 32 — and the output is bit-exact
/// against libFLAC for the material the sample sets contain. Frame CRCs are skipped: the downloader
/// already verifies each file's hash, so decode speed wins over a second checksum.
/// </summary>
internal static class FlacReader
{
    public static WavData Read(string path)
    {
        try { return Read(File.ReadAllBytes(path)); }
        catch (InvalidDataException ex) { throw new InvalidDataException($"{Path.GetFileName(path)}: {ex.Message}"); }
    }

    public static WavData Read(byte[] data)
    {
        if (data.Length < 42 || data[0] != 'f' || data[1] != 'L' || data[2] != 'a' || data[3] != 'C')
            throw new InvalidDataException("not a FLAC file.");

        // Metadata blocks: STREAMINFO is always first, the rest (padding, seektable, tags) are skipped.
        var pos = 4;
        int sampleRate = 0, channels = 0, streamBits = 0; long totalFrames = 0;
        while (true)
        {
            if (pos + 4 > data.Length) throw new InvalidDataException("truncated metadata.");
            var last = (data[pos] & 0x80) != 0; var type = data[pos] & 0x7F;
            var size = data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3];
            pos += 4;
            if (type == 0 && size >= 34)
            {
                var info = new BitReader(data, pos);
                info.Read(16); info.Read(16); info.Read(24); info.Read(24); // block sizes, frame sizes
                sampleRate = (int)info.Read(20); channels = (int)info.Read(3) + 1; streamBits = (int)info.Read(5) + 1;
                totalFrames = (long)info.Read(4) << 32 | info.Read(32); // 36-bit sample count
            }
            pos += size;
            if (pos > data.Length) throw new InvalidDataException("truncated metadata.");
            if (last) break;
        }
        if (sampleRate == 0 || channels == 0) throw new InvalidDataException("missing STREAMINFO.");
        if (channels > 2) throw new InvalidDataException($"{channels} channels; only mono and stereo are supported.");
        if (totalFrames * channels > int.MaxValue) throw new InvalidDataException("file is too long to load into memory.");

        var samples = new short[(int)Math.Max(totalFrames * channels, channels)];
        var written = 0L;
        var reader = new BitReader(data, pos);
        int[] left = [], right = [];
        while (reader.BytesLeft >= 10 && (totalFrames == 0 || written < totalFrames))
        {
            // Frame header: sync, strategy, coded block size / rate / channel mode / depth, frame number.
            if (reader.Read(14) != 0x3FFE) throw new InvalidDataException("lost frame sync.");
            reader.Read(2); // reserved bit + blocking strategy
            var bsCode = (int)reader.Read(4); var srCode = (int)reader.Read(4);
            var mode = (int)reader.Read(4); var ssCode = (int)reader.Read(3); reader.Read(1);
            var first = (int)reader.Read(8); // UTF-8 coded frame number: the length prefix tells how many follow
            for (var n = first; first >= 0xC0 && (n & 0x40) != 0; n <<= 1) reader.Read(8);
            var blockSize = bsCode switch
            {
                1 => 192, >= 2 and <= 5 => 576 << (bsCode - 2), 6 => (int)reader.Read(8) + 1,
                7 => (int)reader.Read(16) + 1, >= 8 => 256 << (bsCode - 8),
                _ => throw new InvalidDataException("reserved block size code.")
            };
            if (srCode == 12) reader.Read(8); else if (srCode is 13 or 14) reader.Read(16);
            reader.Read(8); // header CRC-8
            var bits = ssCode switch
            {
                0 => streamBits, 1 => 8, 2 => 12, 4 => 16, 5 => 20, 6 => 24, 7 => 32,
                _ => throw new InvalidDataException("reserved sample size code.")
            };
            if (left.Length < blockSize) { left = new int[blockSize]; right = new int[blockSize]; }

            // Subframes; the side channel of a decorrelated pair carries one extra bit of depth.
            if (mode < 8)
            {
                if (mode + 1 != channels) throw new InvalidDataException("channel mode does not match STREAMINFO.");
                ReadSubframe(ref reader, left, blockSize, bits);
                if (channels == 2) ReadSubframe(ref reader, right, blockSize, bits);
            }
            else if (mode is 8 or 9 or 10)
            {
                ReadSubframe(ref reader, left, blockSize, bits + (mode == 9 ? 1 : 0));
                ReadSubframe(ref reader, right, blockSize, bits + (mode == 9 ? 0 : 1));
                for (var i = 0; i < blockSize; i++)
                {
                    int l = left[i], s = right[i];
                    if (mode == 8) right[i] = l - s;                     // left + side
                    else if (mode == 9) left[i] = right[i] + l;          // side + right
                    else { var mid = (l << 1) | (s & 1); left[i] = (mid + s) >> 1; right[i] = (mid - s) >> 1; } // mid + side
                }
            }
            else throw new InvalidDataException("reserved channel mode.");
            reader.AlignToByte();
            reader.Read(16); // frame CRC-16

            // Convert to 16-bit interleaved; >> 8 for 24-bit matches the WAV reader's conversion exactly.
            var take = totalFrames > 0 ? (int)Math.Min(blockSize, totalFrames - written) : blockSize;
            var need = (written + take) * channels;
            if (need > samples.Length) Array.Resize(ref samples, (int)Math.Min(Math.Max(need, samples.Length * 2L), int.MaxValue));
            var shift = bits - 16;
            var at = (int)(written * channels);
            for (var i = 0; i < take; i++)
            {
                var l = shift >= 0 ? left[i] >> shift : left[i] << -shift;
                samples[at++] = (short)Math.Clamp(l, short.MinValue, short.MaxValue);
                if (channels == 2)
                {
                    var r = shift >= 0 ? right[i] >> shift : right[i] << -shift;
                    samples[at++] = (short)Math.Clamp(r, short.MinValue, short.MaxValue);
                }
            }
            written += take;
        }
        if (totalFrames > 0 && written < totalFrames) throw new InvalidDataException("file ends before the declared sample count.");
        if (samples.Length != written * channels) Array.Resize(ref samples, (int)(written * channels));
        return new WavData { SampleRate = sampleRate, Channels = channels, Samples = samples };
    }

    private static void ReadSubframe(ref BitReader reader, int[] samples, int blockSize, int depth)
    {
        if (reader.Read(1) != 0) throw new InvalidDataException("bad subframe padding bit.");
        var type = (int)reader.Read(6);
        var wasted = 0;
        if (reader.Read(1) == 1) { wasted = 1; while (reader.Read(1) == 0) wasted++; }
        depth -= wasted;
        if (type == 0) // constant
        {
            var value = reader.ReadSigned(depth);
            for (var i = 0; i < blockSize; i++) samples[i] = value;
        }
        else if (type == 1) // verbatim
        {
            for (var i = 0; i < blockSize; i++) samples[i] = reader.ReadSigned(depth);
        }
        else if (type is >= 8 and <= 12) // fixed predictor, orders 0-4
        {
            var order = type - 8;
            for (var i = 0; i < order; i++) samples[i] = reader.ReadSigned(depth);
            ReadResidual(ref reader, samples, blockSize, order);
            for (var i = order; i < blockSize; i++)
                samples[i] += order switch
                {
                    1 => samples[i - 1],
                    2 => 2 * samples[i - 1] - samples[i - 2],
                    3 => 3 * samples[i - 1] - 3 * samples[i - 2] + samples[i - 3],
                    4 => 4 * samples[i - 1] - 6 * samples[i - 2] + 4 * samples[i - 3] - samples[i - 4],
                    _ => 0
                };
        }
        else if (type >= 32) // LPC, order 1-32
        {
            var order = (type & 0x1F) + 1;
            for (var i = 0; i < order; i++) samples[i] = reader.ReadSigned(depth);
            var precision = (int)reader.Read(4) + 1;
            var shift = reader.ReadSigned(5);
            var coefficients = new int[order];
            for (var i = 0; i < order; i++) coefficients[i] = reader.ReadSigned(precision);
            ReadResidual(ref reader, samples, blockSize, order);
            for (var i = order; i < blockSize; i++)
            {
                long prediction = 0; // 24-bit samples times 15-bit coefficients overflow 32 bits mid-sum
                for (var j = 0; j < order; j++) prediction += (long)coefficients[j] * samples[i - 1 - j];
                samples[i] += (int)(prediction >> shift);
            }
        }
        else throw new InvalidDataException($"reserved subframe type {type}.");
        if (wasted > 0)
            for (var i = 0; i < blockSize; i++) samples[i] <<= wasted;
    }

    private static void ReadResidual(ref BitReader reader, int[] samples, int blockSize, int order)
    {
        var method = (int)reader.Read(2);
        if (method > 1) throw new InvalidDataException("reserved residual coding method.");
        var paramBits = method == 0 ? 4 : 5;
        var escape = (1 << paramBits) - 1;
        var partitionOrder = (int)reader.Read(4);
        var partitions = 1 << partitionOrder;
        var index = order;
        for (var p = 0; p < partitions; p++)
        {
            var count = (blockSize >> partitionOrder) - (p == 0 ? order : 0);
            var parameter = (int)reader.Read(paramBits);
            if (parameter == escape)
            {
                var raw = (int)reader.Read(5);
                for (var i = 0; i < count; i++) samples[index++] = raw == 0 ? 0 : reader.ReadSigned(raw);
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    var quotient = 0u;
                    while (reader.Read(1) == 0) quotient++;
                    var value = quotient << parameter | (parameter > 0 ? reader.Read(parameter) : 0);
                    samples[index++] = (value & 1) != 0 ? -(int)(value >> 1) - 1 : (int)(value >> 1); // zig-zag
                }
            }
        }
    }

    /// <summary>Big-endian bit cursor over the file bytes with a 64-bit refill buffer.</summary>
    private struct BitReader(byte[] data, int position)
    {
        private readonly byte[] _data = data;
        private int _next = position;
        private ulong _cache;
        private int _cached;

        public readonly int BytesLeft => _data.Length - _next + (_cached >> 3);

        public uint Read(int count) // count is 1..32
        {
            while (_cached < count)
            {
                if (_next >= _data.Length) throw new InvalidDataException("unexpected end of file.");
                _cache = _cache << 8 | _data[_next++];
                _cached += 8;
            }
            _cached -= count;
            return (uint)(_cache >> _cached & (count == 32 ? uint.MaxValue : (1u << count) - 1));
        }

        public int ReadSigned(int count)
        {
            var value = Read(count);
            return (value & 1u << (count - 1)) != 0 ? (int)(value - (count == 32 ? 0 : 1ul << count)) : (int)value;
        }

        public void AlignToByte() => _cached -= _cached & 7;
    }
}
