using System.IO;
using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>
/// The REC format that comes out ready to upload: one MP4 holding the stage's frames as H.264 and the engine's
/// own samples as AAC, written through a Media Foundation sink writer, so there is nothing left to mux
/// afterwards.
///
/// <para>
/// The session feeds it like any other recorder — a frame buffer per frame, repeated when the render cannot
/// keep up — and it is also an <see cref="IAudioTrack"/>, so when <c>Record audio</c> is on the session hands
/// the engine's tap straight to it instead of opening a WAV. That tap runs on the audio thread while frames
/// arrive on the UI thread, so every call into the writer is taken under one lock.
/// </para>
///
/// <para>
/// Two kinds of machine cannot write this format cleanly, and neither is allowed to kill the take: a media
/// stack with no writer at all cannot record MP4 (the constructor says so and the session reports it), while a
/// stack that has the H.264 encoder but refuses the AAC stream records the video and notes that the sound
/// stayed out (<see cref="AudioDropped"/>).
/// </para>
/// </summary>
internal sealed class Mp4Recorder : IFrameRecorder, IAudioTrack
{
    /// <summary>The sample rate the engine renders at, and so the rate the AAC encoder is given.</summary>
    internal const int SampleRate = 44100;

    /// <summary>Two channels: the engine renders stereo, and so does the file.</summary>
    internal const int Channels = 2;

    /// <summary>The audio bitrate the AAC encoder is asked for: 192 kbps, transparent for a sampled piano.</summary>
    internal const int AudioBytesPerSecond = 24000;

    private readonly Lock _lock = new();
    private readonly byte[] _nv12;
    private readonly byte[] _audioBytes = new byte[8192];
    private Mf.IMFSinkWriter? _writer;
    private int _videoStream = -1;
    private int _audioStream = -1;
    private long _audioFrames;
    private bool _mediaStarted;
    private bool _finalized;

    /// <summary>
    /// Opens the file and starts the encoders. <paramref name="withAudio"/> asks for the audio stream as well,
    /// which is only ever true when the engine has a SoundFont to play.
    /// </summary>
    internal Mp4Recorder(string path, int width, int height, int frameRate, bool withAudio)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(Loc.T("An MP4 recording needs a file to go into."), nameof(path));
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        Width = width; Height = height; FrameRate = frameRate; OutputPath = path;
        _nv12 = new byte[Nv12Frame.Size(width, height)];
        try
        {
            var hr = Mf.MFStartup(Mf.MF_VERSION, 0);
            if (hr != Mf.S_OK) throw new InvalidOperationException(Loc.F("The media stack would not start ({0}).", Mf.Describe(hr)));
            _mediaStarted = true;
            if (!withAudio) { Open(encodersForAudio: false); return; }
            try { Open(encodersForAudio: true); }
            catch (Exception)
            {
                // The video is worth more than the sound: the writer is opened again without the audio stream.
                AudioDropped = true;
                _writer = null; _videoStream = -1; _audioStream = -1;
                Open(encodersForAudio: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Pixels across; part of <see cref="IFrameRecorder"/>.</summary>
    public int Width { get; }

    /// <summary>Pixels down; part of <see cref="IFrameRecorder"/>.</summary>
    public int Height { get; }

    /// <summary>Frames a second the file plays at; part of <see cref="IFrameRecorder"/>.</summary>
    public int FrameRate { get; }

    /// <summary>Frames handed to the encoder so far; part of <see cref="IFrameRecorder"/>.</summary>
    public int FrameCount { get; private set; }

    /// <summary>The size the file has grown to; part of <see cref="IFrameRecorder"/>.</summary>
    public long BytesWritten
    {
        get { try { return new FileInfo(OutputPath).Length; } catch { return 0; } }
    }

    /// <summary>MP4 has no 2 GB ceiling to run into; part of <see cref="IFrameRecorder"/>.</summary>
    public bool IsNearSizeLimit => false;

    /// <summary>Where the recording is going; part of <see cref="IFrameRecorder"/>.</summary>
    public string OutputPath { get; }

    /// <summary>
    /// Bytes one frame takes. The encoder wants NV12, but the conversion reads the stage's own pixels, so the
    /// session is asked for the packed 32-bit buffer the PNG sequence uses — the alpha is simply dropped when
    /// the colours are averaged.
    /// </summary>
    public int FrameBytes => Width * Height * 4;

    /// <summary>True because the stage hands over its own pixels, alpha byte and all; the MP4 keeps no alpha.</summary>
    public bool HasAlpha => true;

    /// <summary>True when the file has an audio stream: the engine's samples go into it, not into a WAV.</summary>
    internal bool HasAudio => _audioStream >= 0;

    /// <summary>True when the audio stream was asked for and refused, so this take has no sound.</summary>
    internal bool AudioDropped { get; private set; }

    /// <summary>Seconds of audio handed to the encoder so far; part of <see cref="IAudioTrack"/>.</summary>
    public double Seconds
    {
        get { lock (_lock) return _audioFrames / (double)SampleRate; }
    }

    /// <summary>The time and length of one video frame, in the 100-nanosecond units a sample is stamped with.</summary>
    internal static (long Time, long Duration) FrameTime(int index, int frameRate)
    {
        var start = index * 10_000_000L / frameRate;
        var end = (index + 1) * 10_000_000L / frameRate;
        return (start, end - start);
    }

    /// <summary>The time and length of a block of audio, stamped the same way.</summary>
    internal static (long Time, long Duration) AudioTime(long frames, int blockFrames)
    {
        var start = frames * 10_000_000L / SampleRate;
        var end = (frames + blockFrames) * 10_000_000L / SampleRate;
        return (start, end - start);
    }

    /// <summary>
    /// The bitrate the H.264 encoder is asked for: seven per cent of the frame size times the frame rate, but
    /// never less than 2 Mbps — a small stage would otherwise come out mush — and never more than 24 Mbps.
    /// </summary>
    internal static int BitrateFor(int width, int height, int frameRate) =>
        (int)Math.Clamp(width * (long)height * frameRate * 7 / 100, 2_000_000, 24_000_000);

    /// <summary>
    /// Writes one frame, or <paramref name="repeat"/> copies of it when the render fell behind the clock, so a
    /// slow machine produces a file that plays at the right speed instead of a fast-forward.
    /// </summary>
    public void WriteFrame(byte[] frame, int repeat = 1)
    {
        if (frame.Length != FrameBytes) throw new ArgumentException(Loc.F("Expected a {0}×{1} BGRA frame ({2} bytes).", Width, Height, FrameBytes), nameof(frame));
        lock (_lock)
        {
            if (_finalized) return;
            Nv12Frame.FromBgra(frame, Width, Height, _nv12);
            for (var copy = 0; copy < repeat; copy++)
            {
                var (time, duration) = FrameTime(FrameCount, FrameRate);
                var hr = WriteSample(_videoStream, _nv12, _nv12.Length, time, duration);
                if (hr < 0) throw new InvalidOperationException(Loc.F("The encoder stopped taking frames ({0}).", Mf.Describe(hr)));
                FrameCount++;
            }
        }
    }

    /// <summary>Appends the engine's own samples to the audio stream; part of <see cref="IAudioTrack"/>.</summary>
    public void Append(short[] samples, int count)
    {
        if (!HasAudio || samples.Length == 0) return;
        count = Math.Clamp(count, 0, samples.Length);
        count -= count % Channels;
        if (count == 0) return;
        lock (_lock)
        {
            if (_finalized) return;
            for (var offset = 0; offset < count; )
            {
                var block = Math.Min(_audioBytes.Length / 2, count - offset);
                block -= block % Channels;
                Buffer.BlockCopy(samples, offset * 2, _audioBytes, 0, block * 2);
                var (time, duration) = AudioTime(_audioFrames, block / Channels);
                var hr = WriteSample(_audioStream, _audioBytes, block * 2, time, duration);
                if (hr < 0) throw new InvalidOperationException(Loc.F("The encoder stopped taking audio ({0}).", Mf.Describe(hr)));
                _audioFrames += block / Channels;
                offset += block;
            }
        }
    }

    /// <summary>Appends silence so the track keeps step with the recording clock; part of <see cref="IAudioTrack"/>.</summary>
    public void AppendSilence(long frames)
    {
        if (!HasAudio || frames <= 0) return;
        var block = Math.Min(Math.Max(1, frames), 4096);
        var silent = new short[block * Channels];
        for (var written = 0L; written < frames; written += block)
            Append(silent, (int)Math.Min(block, frames - written) * Channels);
    }

    /// <summary>Closes the file: the last frames, the index and the two stream headers are written here.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (!_finalized)
            {
                _finalized = true;
                try { if (FrameCount > 0 || _audioFrames > 0) _writer?.FinalizeFile(); } catch { }
            }
            if (_mediaStarted) { try { Mf.MFShutdown(); } catch { } _mediaStarted = false; }
        }
    }

    /// <summary>
    /// Creates the writer and its streams. The video stream is mandatory — without it there is nothing to
    /// record — while the audio stream is only added when asked for.
    /// </summary>
    private void Open(bool encodersForAudio)
    {
        var hr = Mf.MFCreateSinkWriterFromURL(OutputPath, IntPtr.Zero, null, out var writer);
        if (hr < 0) throw new InvalidOperationException(Loc.F("No MP4 writer is available on this machine ({0}).", Mf.Describe(hr)));
        _writer = writer;
        _videoStream = AddVideoStream();
        if (encodersForAudio) _audioStream = AddAudioStream();
        hr = writer.BeginWriting();
        if (hr < 0) throw new InvalidOperationException(Loc.F("The MP4 writer would not start writing ({0}).", Mf.Describe(hr)));
    }

    /// <summary>Adds the H.264 stream and declares the NV12 frames this side hands it.</summary>
    private int AddVideoStream()
    {
        var output = Mf.MFCreateMediaType(out var target);
        if (output != Mf.S_OK) throw new InvalidOperationException(Loc.F("A media type could not be prepared ({0}).", Mf.Describe(output)));
        target.SetGUIDKey(Mf.MajorType, Mf.VideoMajorType);
        target.SetGUIDKey(Mf.SubType, Mf.H264);
        target.SetUINT32Key(Mf.AverageBitrate, BitrateFor(Width, Height, FrameRate));
        target.SetUINT64Key(Mf.FrameSize, Mf.Pack(Height, Width));
        target.SetUINT64Key(Mf.FrameRateKey, Mf.Pack(FrameRate, 1));
        target.SetUINT64Key(Mf.PixelAspectRatio, Mf.Pack(1, 1));
        target.SetUINT32Key(Mf.InterlaceMode, Mf.InterlaceProgressive);
        target.SetUINT32Key(Mf.AllSamplesIndependent, 1);
        var hr = _writer!.AddStream(target, out var index);
        if (hr < 0) throw new InvalidOperationException(Loc.F("The MP4 writer has no H.264 encoder for this size ({0}).", Mf.Describe(hr)));
        var input = Mf.MFCreateMediaType(out var video);
        if (input != Mf.S_OK) throw new InvalidOperationException(Loc.F("A media type could not be prepared ({0}).", Mf.Describe(input)));
        video.SetGUIDKey(Mf.MajorType, Mf.VideoMajorType);
        video.SetGUIDKey(Mf.SubType, Mf.Nv12);
        video.SetUINT64Key(Mf.FrameSize, Mf.Pack(Height, Width));
        video.SetUINT64Key(Mf.FrameRateKey, Mf.Pack(FrameRate, 1));
        video.SetUINT64Key(Mf.PixelAspectRatio, Mf.Pack(1, 1));
        video.SetUINT32Key(Mf.InterlaceMode, Mf.InterlaceProgressive);
        hr = _writer.SetInputMediaType(index, video, null);
        if (hr < 0) throw new InvalidOperationException(Loc.F("The H.264 encoder refused the frames this stage produces ({0}).", Mf.Describe(hr)));
        return index;
    }

    /// <summary>
    /// Adds the AAC stream and declares the 16-bit stereo PCM this side hands it. A refusal here is not fatal:
    /// the caller opens the file again without the sound, and <see cref="AudioDropped"/> says so.
    /// </summary>
    private int AddAudioStream()
    {
        if (Mf.MFCreateMediaType(out var target) != Mf.S_OK) throw new InvalidOperationException(Loc.T("A media type could not be prepared."));
        target.SetGUIDKey(Mf.MajorType, Mf.AudioMajorType);
        target.SetGUIDKey(Mf.SubType, Mf.Aac);
        target.SetUINT32Key(Mf.AudioSamplesPerSecond, SampleRate);
        target.SetUINT32Key(Mf.AudioChannels, Channels);
        target.SetUINT32Key(Mf.AudioAverageBytesPerSecond, AudioBytesPerSecond);
        target.SetUINT32Key(Mf.AudioBitsPerSample, 16);
        var hr = _writer!.AddStream(target, out var index);
        if (hr < 0) throw new InvalidOperationException(Loc.F("The MP4 writer has no AAC encoder ({0}).", Mf.Describe(hr)));
        if (Mf.MFCreateMediaType(out var input) != Mf.S_OK) throw new InvalidOperationException(Loc.T("A media type could not be prepared."));
        input.SetGUIDKey(Mf.MajorType, Mf.AudioMajorType);
        input.SetGUIDKey(Mf.SubType, Mf.Pcm);
        input.SetUINT32Key(Mf.AudioSamplesPerSecond, SampleRate);
        input.SetUINT32Key(Mf.AudioChannels, Channels);
        input.SetUINT32Key(Mf.AudioBitsPerSample, 16);
        input.SetUINT32Key(Mf.AudioBlockAlignment, Channels * 2);
        input.SetUINT32Key(Mf.AudioAverageBytesPerSecond, SampleRate * Channels * 2);
        hr = _writer.SetInputMediaType(index, input, null);
        if (hr < 0) throw new InvalidOperationException(Loc.F("The AAC encoder refused the engine's samples ({0}).", Mf.Describe(hr)));
        return index;
    }

    /// <summary>Copies a block of bytes into a media buffer, stamps it and hands it to the writer.</summary>
    private int WriteSample(int streamIndex, byte[] bytes, int count, long time, long duration)
    {
        var hr = Mf.MFCreateMemoryBuffer(count, out var buffer);
        if (hr < 0) return hr;
        hr = buffer.Lock(out var pointer, out _, out _);
        if (hr < 0) return hr;
        Marshal.Copy(bytes, 0, pointer, count);
        buffer.Unlock();
        buffer.SetCurrentLength(count);
        hr = Mf.MFCreateSample(out var sample);
        if (hr < 0) return hr;
        sample.AddBuffer(buffer);
        sample.SetSampleTime(time);
        sample.SetSampleDuration(duration);
        return _writer!.WriteSample(streamIndex, sample);
    }
}
