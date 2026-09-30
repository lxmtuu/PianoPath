using System.IO;
using System.Text;

namespace PianoPath;

/// <summary>
/// A streaming RIFF/WAVE writer for the recording's audio track: the 44-byte canonical header goes down first
/// with zero sizes, the PCM is appended as it is rendered, and closing the file seeks back to write the two
/// sizes that can only be known at the end.
///
/// <para>
/// Only the format the engine produces is written — 16-bit signed PCM, one or two channels — because that is
/// what the SoundFont synthesiser renders and what <c>waveOut</c> plays, so the recorded file holds exactly the
/// samples the machine heard. <see cref="Header"/> is public to the assembly so the checks can compare the
/// bytes it writes with the specification instead of with the file it just produced.
/// </para>
/// </summary>
internal sealed class WavWriter : IAudioTrack
{
    /// <summary>Bytes of the canonical PCM header: RIFF, fmt (16), data.</summary>
    internal const int HeaderBytes = 44;

    private readonly FileStream _stream;
    private readonly byte[] _buffer = new byte[8192];
    /// <summary>No more samples are accepted: the take was closed, or a write failed.</summary>
    private bool _closed;
    /// <summary>The stream is gone; <see cref="Dispose"/> is a no-op from here on.</summary>
    private bool _disposed;
    /// <summary>
    /// A block failed on the way to disk, so <see cref="DataBytes"/> no longer describes what the file
    /// holds and the sizes are left as they were written: a header that promises samples which are not
    /// there is worse than a header of zeroes.
    /// </summary>
    private bool _sizesUnreliable;

    private WavWriter(string path, int sampleRate, int channels)
    {
        Path = path; SampleRate = sampleRate; Channels = channels;
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var header = Header(sampleRate, channels, 0);
        _stream.Write(header, 0, header.Length);
    }

    /// <summary>Full path of the file being written.</summary>
    internal string Path { get; }

    internal int SampleRate { get; }

    internal int Channels { get; }

    /// <summary>Frames (samples per channel) appended so far.</summary>
    internal long Frames { get; private set; }

    /// <summary>Bytes of PCM written, i.e. what the data chunk declares.</summary>
    internal long DataBytes => Frames * Channels * 2;

    /// <summary>True once the file has been closed and its sizes patched.</summary>
    internal bool IsClosed => _disposed;

    /// <summary>
    /// Opens a file for recording, or returns <c>null</c> when it cannot be written (the folder is gone, the
    /// name is taken by a locked file). A recording that cannot keep its audio must not stop the video.
    /// </summary>
    internal static WavWriter? TryCreate(string path, int sampleRate = 44100, int channels = 2)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            return new WavWriter(path, Math.Max(1, sampleRate), Math.Clamp(channels, 1, 2));
        }
        catch { return null; }
    }

    /// <summary>
    /// The canonical 44-byte header of a PCM WAVE file of <paramref name="dataBytes"/> bytes: the checks compare
    /// this with the byte layout of the format, and the writer uses it for the sizes it fills in at the end.
    /// </summary>
    internal static byte[] Header(int sampleRate, int channels, long dataBytes)
    {
        var bytes = new byte[HeaderBytes];
        var blockAlign = (short)(channels * 2);
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((uint)Math.Clamp(36 + dataBytes, 0, uint.MaxValue));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16u);
        writer.Write((short)1);                       // 1 = uncompressed PCM
        writer.Write((short)channels);
        writer.Write((uint)sampleRate);
        writer.Write((uint)(sampleRate * blockAlign)); // bytes per second
        writer.Write(blockAlign);
        writer.Write((short)16);                      // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((uint)Math.Clamp(dataBytes, 0, uint.MaxValue));
        return bytes;
    }

    /// <summary>Seconds of audio held so far, which is what the recording's clock is compared with.</summary>
    public double Seconds => SampleRate <= 0 ? 0 : Frames / (double)SampleRate;

    /// <summary>
    /// Appends interleaved samples; <paramref name="count"/> is the number of <see cref="short"/> values, not
    /// frames. Appending after the file is closed is ignored, so a late block from the audio thread during
    /// shutdown cannot throw on the recording's way out.
    /// </summary>
    public void Append(short[] samples, int count)
    {
        if (_closed || samples.Length == 0) return;
        count = Math.Clamp(count, 0, samples.Length);
        count -= count % Channels;      // a partial frame is not written; the next block starts a whole one
        if (count == 0) return;
        try
        {
            var bytes = count * 2;
            if (bytes <= _buffer.Length)
            {
                Buffer.BlockCopy(samples, 0, _buffer, 0, bytes);
                _stream.Write(_buffer, 0, bytes);
            }
            else
            {
                for (var offset = 0; offset < count; offset += _buffer.Length / 2)
                {
                    var chunk = Math.Min(_buffer.Length / 2, count - offset);
                    Buffer.BlockCopy(samples, offset * 2, _buffer, 0, chunk * 2);
                    _stream.Write(_buffer, 0, chunk * 2);
                }
            }
            Frames += count / Channels;
        }
        catch
        {
            // A disk that filled up mid-recording ends the audio track, not the session: the blocks that
            // already landed stay in the file, and this one stops the track rather than throwing on the
            // audio thread. Dispose closes the file on the way out and leaves the two sizes as they were
            // written — a block that failed may have landed in part, so "how many bytes are in there" is
            // no longer something this writer knows.
            _closed = true;
            _sizesUnreliable = true;
        }
    }

    /// <summary>Appends a stretch of silence, used to keep the track in step with the video clock.</summary>
    public void AppendSilence(long frames)
    {
        var block = Math.Min(Math.Max(0, frames), 4096);
        if (block == 0) return;
        var silent = new short[block * Channels];
        for (var written = 0L; written < frames; written += block)
            Append(silent, (int)Math.Min(block, frames - written) * Channels);
    }

    /// <summary>Writes the final sizes, flushes and closes; safe to call more than once.</summary>
    /// <remarks>
    /// The close happens on every path, a failed write included: a take that ended early because the disk
    /// filled up must not keep its file handle (and the lock on the file) for the rest of the process, which
    /// is what a guard on <see cref="_closed"/> alone would do — that flag says "no more samples", not
    /// "the file is gone".
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _closed = true;
        try
        {
            if (!_sizesUnreliable)
            {
                var sizes = Header(SampleRate, Channels, DataBytes);
                _stream.Seek(4, SeekOrigin.Begin);
                _stream.Write(sizes, 4, 4);                        // RIFF chunk size
                _stream.Seek(HeaderBytes - 4, SeekOrigin.Begin);
                _stream.Write(sizes, HeaderBytes - 4, 4);          // data chunk size
                _stream.Flush();
            }
        }
        catch
        {
            // The file keeps the zero sizes it started with; the samples themselves are already on disk.
        }
        finally { try { _stream.Dispose(); } catch { } }
    }
}
