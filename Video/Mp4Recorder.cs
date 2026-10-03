using System.IO;
using System.Runtime.ExceptionServices;
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

    /// <summary>Frames that may wait for the encoder before further ones are folded into the newest as repeats.</summary>
    private const int MaxQueuedFrames = 4;

    /// <summary>A frame on its way to the encoder: its pixels, the timeline slot it starts at and how many slots it fills.</summary>
    private sealed class PendingFrame(byte[] bgra, int index, int repeat)
    {
        internal readonly byte[] Bgra = bgra;
        internal readonly int Index = index;
        internal int Repeat = repeat;
    }

    private readonly Lock _lock = new();
    private readonly byte[] _nv12;
    private readonly object _queueGate = new();
    private readonly Queue<PendingFrame> _queue = new();
    private readonly Stack<byte[]> _spare = new();
    private PendingFrame? _tail;
    private Thread? _worker;
    private bool _closing;
    private Exception? _workerError;
    private Exception? _openFailure;
    private readonly byte[] _audioBytes = new byte[8192];
    private Mf.IMFSinkWriter? _writer;
    private int _videoStream = -1;
    private int _audioStream = -1;
    private long _audioFrames;
    private volatile bool _finalized;
    private readonly Action<string>? _step;

    /// <summary>
    /// Opens the file and starts the encoders. <paramref name="withAudio"/> asks for the audio stream as well,
    /// which is only ever true when the engine has a SoundFont to play. <paramref name="step"/>, when given,
    /// hears every stage of the way — the AAC probe, the writer opening, each stream going in, the writer
    /// starting — so the verification's child process can leave a log that names the call a machine stopped at
    /// rather than the call it was going to make next.
    /// </summary>
    internal Mp4Recorder(string path, int width, int height, int frameRate, bool withAudio, Action<string>? step = null)
    {
        _step = step;
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(Loc.T("An MP4 recording needs a file to go into."), nameof(path));
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        Width = width; Height = height; FrameRate = frameRate; OutputPath = path;
        _nv12 = new byte[Nv12Frame.Size(width, height)];
        // Every call into the media stack — starting it, probing the encoders, opening the writer, writing every
        // sample, closing the file — happens on one thread of its own. A sink writer is only usable from the
        // apartment it was made in: called from the window's thread it answers E_NOINTERFACE, and the file is
        // left without an index. The constructor waits here until that thread has the take open (or has failed).
        using var ready = new ManualResetEventSlim(false);
        _worker = new Thread(() => RunTake(withAudio, ready)) { IsBackground = true, Name = "MP4 encoder" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
        ready.Wait();
        if (_openFailure is { } failure)
        {
            _worker.Join();
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>The encoder thread's whole life: open the take, report that it is open, write frames, close the file.</summary>
    private void RunTake(bool withAudio, ManualResetEventSlim ready)
    {
        try
        {
            try { OpenTake(withAudio); }
            catch (Exception ex) { _openFailure = ex; return; }
            finally { ready.Set(); }
            EncodeFrames();
        }
        finally { CloseFile(); }
    }

    /// <summary>Starts the media stack, asks whether the sound can go in, and opens the writer in the shape that answer allows.</summary>
    private void OpenTake(bool withAudio)
    {
        _step?.Invoke("NOTE MP4 encoder: starting the media stack for the take.");
        var hr = Mf.MediaStartup();
        if (hr != Mf.S_OK) throw new InvalidOperationException(Loc.F("The media stack would not start ({0}).", Mf.Describe(hr)));
        _step?.Invoke("NOTE MP4 encoder: the media stack is up for the take.");
        // Whether the sound can go in at all is asked of a throwaway writer first: a stream that is added
        // and then refused would leave this writer unable to be told to drop it again, and the user's file
        // would have to be opened a second time. The answer decides the shape of the take before it starts.
        _step?.Invoke("NOTE MP4 encoder: asking a throwaway writer whether this machine can encode AAC.");
        var wantsAudio = withAudio && CanEncodeAudio(_step);
        _step?.Invoke(wantsAudio
            ? "NOTE MP4 encoder: the AAC probe says yes, so the take carries the sound."
            : "NOTE MP4 encoder: the AAC probe says no, so the take carries the picture only.");
        AudioDropped = withAudio && !wantsAudio;
        Open(encodersForAudio: wantsAudio);
    }

    /// <summary>
    /// Opens a take on a thread of its own, with <paramref name="limit"/> to come back — the way the window opens
    /// an MP4 recording, since everything in <see cref="Mp4Recorder"/> is native code and a machine whose media
    /// stack wedges inside one of its calls would otherwise freeze the window itself. Returns null when the take
    /// could not be opened; <paramref name="stopped"/> then says whether the machine stopped answering (the
    /// media stack) or refused the take (<paramref name="failure"/>, with the sentence to show the user).
    /// </summary>
    internal static Mp4Recorder? TryOpen(string path, int width, int height, int frameRate, bool withAudio,
        TimeSpan limit, out Exception? failure, out bool stopped)
    {
        Mp4Recorder? recorder = null;
        Exception? error = null;
        var opened = HangGuard.Run(() =>
        {
            try { recorder = new Mp4Recorder(path, width, height, frameRate, withAudio); }
            catch (Exception ex) { error = ex; }
        }, limit);
        failure = error;
        stopped = !opened;
        return stopped ? null : recorder;
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

    /// <summary>What closing the file answered (negative when the index and last frames were not written); null before it is closed.</summary>
    internal int? FinalizeResult { get; private set; }

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
    /// Hands one frame to the encoder thread, or <paramref name="repeat"/> copies of it when the render fell
    /// behind the clock, so a slow machine produces a file that plays at the right speed instead of a
    /// fast-forward. The colour conversion and the encoder run on that thread, never on the caller's: when the
    /// encoder cannot keep up, the frames beyond a short queue are folded into the newest one as repeats, so
    /// the window never waits for the codec and the file still keeps time. A failure on the encoder thread is
    /// raised from the next call here.
    /// </summary>
    public void WriteFrame(byte[] frame, int repeat = 1)
    {
        if (frame.Length != FrameBytes) throw new ArgumentException(Loc.F("Expected a {0}×{1} BGRA frame ({2} bytes).", Width, Height, FrameBytes), nameof(frame));
        if (repeat < 1) return;
        lock (_queueGate)
        {
            if (_workerError is { } error) throw new InvalidOperationException(error.Message, error);
            if (_finalized || _closing) return;
            if (_queue.Count >= MaxQueuedFrames && _tail is not null)
            {
                _tail.Repeat += repeat;
            }
            else
            {
                var buffer = _spare.Count > 0 ? _spare.Pop() : new byte[FrameBytes];
                Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);
                _tail = new PendingFrame(buffer, FrameCount, repeat);
                _queue.Enqueue(_tail);
                Monitor.Pulse(_queueGate);
            }
            FrameCount += repeat;
        }
    }

    /// <summary>The encoder thread: takes frames off the queue, converts them to NV12 and writes them, until the take closes.</summary>
    private void EncodeFrames()
    {
        while (true)
        {
            PendingFrame item;
            lock (_queueGate)
            {
                while (_queue.Count == 0)
                {
                    if (_closing) return;
                    Monitor.Wait(_queueGate);
                }
                item = _queue.Dequeue();
            }
            try
            {
                Nv12Frame.FromBgra(item.Bgra, Width, Height, _nv12);
                lock (_lock)
                {
                    if (_finalized) return;
                    for (var copy = 0; copy < item.Repeat; copy++)
                    {
                        var (time, duration) = FrameTime(item.Index + copy, FrameRate);
                        var hr = WriteSample(_videoStream, _nv12, _nv12.Length, time, duration);
                        if (hr < 0) throw new InvalidOperationException(Loc.F("The encoder stopped taking frames ({0}).", Mf.Describe(hr)));
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_queueGate) { _workerError = ex; _queue.Clear(); _tail = null; }
                return;
            }
            lock (_queueGate) _spare.Push(item.Bgra);
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
        // The frames still queued are written first, so the take ends where the recording did, and the encoder
        // thread closes the file itself: the sink writer was made on a thread of the media stack's own
        // apartment, and calling it from the window's thread is refused (E_NOINTERFACE) and leaves a file with
        // no index that no player opens. A codec that wedges is given a few seconds, not forever: the take is
        // then abandoned rather than the window.
        lock (_queueGate) { _closing = true; Monitor.PulseAll(_queueGate); }
        if (_worker is { } worker && !worker.Join(TimeSpan.FromSeconds(10))) { _finalized = true; return; }
        if (FinalizeResult is < 0 and var failed)
            throw new IOException(Loc.F("The MP4 file could not be finished ({0}).", Mf.Describe(failed)));
    }

    /// <summary>Closes the file: the last frames, the index and the stream headers are written here, on the encoder's thread.</summary>
    private void CloseFile()
    {
        lock (_lock)
        {
            if (_finalized) return;
            _finalized = true;
            try { if (FrameCount > 0 || _audioFrames > 0) FinalizeResult = _writer?.FinalizeFile(); } catch (Exception ex) { FinalizeResult = ex.HResult; }
            // The collector would release the writer at some later point; the file has to be closed now,
            // both for the user who wants to open it and for anyone reading the take back.
            Release();
            // The media stack is left running: it is started once for the whole process (see Mf.MediaStartup).
        }
    }

    /// <summary>Lets go of the writer and of the file it holds, so the take is closed with the recording.</summary>
    private void Release()
    {
        var writer = _writer;
        _writer = null; _videoStream = -1; _audioStream = -1;
        if (writer is null) return;
        try { Marshal.ReleaseComObject(writer); } catch { }
    }

    /// <summary>
    /// Creates the writer and its streams. The video stream is mandatory — without it there is nothing to
    /// record — while the audio stream is only added when asked for and only fails softly.
    /// </summary>
    private void Open(bool encodersForAudio)
    {
        var hr = Mf.MFCreateSinkWriterFromURL(OutputPath, IntPtr.Zero, null, out var writer);
        if (hr < 0) throw new InvalidOperationException(Loc.F("No MP4 writer is available on this machine ({0}).", Mf.Describe(hr)));
        _step?.Invoke("NOTE MP4 encoder: the take's sink writer is open.");
        _writer = writer;
        _videoStream = AddVideoStream();
        _step?.Invoke("NOTE MP4 encoder: the take's H.264 stream is in.");
        if (encodersForAudio) { _audioStream = AddAudioStream(); _step?.Invoke("NOTE MP4 encoder: the take's AAC stream is in."); }
        hr = writer.BeginWriting();
        if (hr < 0) throw new InvalidOperationException(Loc.F("The MP4 writer would not start writing ({0}).", Mf.Describe(hr)));
        _step?.Invoke($"NOTE MP4 encoder: the take's writer began writing at {Width}×{Height}.");
    }

    /// <summary>
    /// The H.264 stream the writer is asked to produce, described exactly as a take asks for it. The
    /// verification builds this same description and hands it to a writer without writing a frame: the sink
    /// writer has to find an encoder for it before it will take the stream, so the answer is also the answer to
    /// whether this machine can make a take at all.
    /// </summary>
    internal static Mf.IMFMediaType? VideoTarget(int width, int height, int frameRate)
    {
        if (Mf.MFCreateMediaType(out var target) != Mf.S_OK) return null;
        target.SetGUIDKey(Mf.MajorType, Mf.VideoMajorType);
        target.SetGUIDKey(Mf.SubType, Mf.H264);
        target.SetUINT32Key(Mf.AverageBitrate, BitrateFor(width, height, frameRate));
        target.SetUINT64Key(Mf.FrameSize, Mf.Pack(height, width));
        target.SetUINT64Key(Mf.FrameRateKey, Mf.Pack(frameRate, 1));
        target.SetUINT64Key(Mf.PixelAspectRatio, Mf.Pack(1, 1));
        target.SetUINT32Key(Mf.InterlaceMode, Mf.InterlaceProgressive);
        target.SetUINT32Key(Mf.AllSamplesIndependent, 1);
        return target;
    }

    /// <summary>Adds the H.264 stream and declares the NV12 frames this side hands it.</summary>
    private int AddVideoStream()
    {
        var target = VideoTarget(Width, Height, FrameRate)
            ?? throw new InvalidOperationException(Loc.F("A media type could not be prepared ({0}).", Mf.Describe(Mf.MF_E_OUT_OF_MEMORY)));
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
    /// The AAC stream the writer is asked to produce. The encoder derives the block alignment and the byte rate
    /// from these attributes.
    /// </summary>
    private static Mf.IMFMediaType? AudioTarget()
    {
        if (Mf.MFCreateMediaType(out var target) != Mf.S_OK) return null;
        target.SetGUIDKey(Mf.MajorType, Mf.AudioMajorType);
        target.SetGUIDKey(Mf.SubType, Mf.Aac);
        target.SetUINT32Key(Mf.AudioSamplesPerSecond, SampleRate);
        target.SetUINT32Key(Mf.AudioChannels, Channels);
        target.SetUINT32Key(Mf.AudioBitsPerSample, 16);
        target.SetUINT32Key(Mf.AudioAverageBytesPerSecond, AudioBytesPerSecond);
        return target;
    }

    /// <summary>
    /// The 16-bit stereo PCM this side hands the encoder, in the smallest set of attributes the AAC encoder
    /// documents. Spelling out the block alignment, the byte rate or "all samples independent" on top of these
    /// makes it refuse the type outright — a CI run came back with MF_E_INVALIDMEDIATYPE for exactly that.
    /// </summary>
    private static Mf.IMFMediaType? AudioSource()
    {
        if (Mf.MFCreateMediaType(out var source) != Mf.S_OK) return null;
        source.SetGUIDKey(Mf.MajorType, Mf.AudioMajorType);
        source.SetGUIDKey(Mf.SubType, Mf.Pcm);
        source.SetUINT32Key(Mf.AudioSamplesPerSecond, SampleRate);
        source.SetUINT32Key(Mf.AudioChannels, Channels);
        source.SetUINT32Key(Mf.AudioBitsPerSample, 16);
        return source;
    }

    /// <summary>
    /// Whether this machine can put the engine's samples into an MP4 at all, asked on a throwaway file so the
    /// user's own take is only ever opened once and only in a shape the machine has already accepted.
    /// </summary>
    private static bool CanEncodeAudio(Action<string>? step)
    {
        var probe = Path.Combine(Path.GetTempPath(), "keyflow-mp4-probe-" + Guid.NewGuid().ToString("N") + ".mp4");
        Mf.IMFSinkWriter? writer = null;
        try
        {
            if (Mf.MFCreateSinkWriterFromURL(probe, IntPtr.Zero, null, out writer) < 0) return false;
            step?.Invoke("NOTE MP4 encoder: the AAC probe writer is open.");
            var target = AudioTarget(); var source = AudioSource();
            if (target is null || source is null) return false;
            if (writer.AddStream(target, out var index) < 0) return false;
            step?.Invoke("NOTE MP4 encoder: the AAC probe took the stream.");
            return writer.SetInputMediaType(index, source, null) >= 0;
        }
        catch { return false; }
        finally
        {
            if (writer is not null) { try { Marshal.ReleaseComObject(writer); } catch { } }
            try { File.Delete(probe); } catch { }
        }
    }

    /// <summary>Adds the AAC stream and declares the PCM this side hands it; the probe has already proved it fits.</summary>
    private int AddAudioStream()
    {
        var target = AudioTarget() ?? throw new InvalidOperationException(Loc.T("A media type could not be prepared."));
        var hr = _writer!.AddStream(target, out var index);
        if (hr < 0) throw new InvalidOperationException(Loc.F("The MP4 writer has no AAC encoder ({0}).", Mf.Describe(hr)));
        var source = AudioSource() ?? throw new InvalidOperationException(Loc.T("A media type could not be prepared."));
        hr = _writer.SetInputMediaType(index, source, null);
        if (hr < 0) throw new InvalidOperationException(Loc.F("The AAC encoder refused the engine's samples ({0}).", Mf.Describe(hr)));
        return index;
    }

    /// <summary>Copies a block of bytes into a media buffer, stamps it and hands it to the writer.</summary>
    private int WriteSample(int streamIndex, byte[] bytes, int count, long time, long duration)
        => WriteSampleTo(_writer!, streamIndex, bytes, count, time, duration, _step);

    /// <summary>
    /// The step every sample goes through: one media buffer, one copy, one sample, stamped, written. The
    /// encoder-free AVI probe in <see cref="Mf.EncodeAviProbe"/> calls this very method, so a machine that
    /// cannot finish the probe cannot finish a take either, and a machine that writes the probe but hangs on
    /// the take has a codec to blame rather than this side's sample plumbing.
    /// </summary>
    internal static int WriteSampleTo(Mf.IMFSinkWriter writer, int streamIndex, byte[] bytes, int count, long time, long duration,
        Action<string>? step = null)
    {
        // Every buffer and sample is a native allocation (a 1080p frame is about 3 MB). The runtime only sees the
        // tiny managed wrapper, so left to the collector they pile up for minutes and are then freed in one
        // stall; they are released here, as soon as the writer has taken its own reference.
        Mf.IMFMediaBuffer? buffer = null;
        Mf.IMFSample? sample = null;
        try
        {
            step?.Invoke($"NOTE MP4 encoder: a media buffer is being made for {count} bytes.");
            var hr = Mf.MFCreateMemoryBuffer(count, out buffer);
            if (hr < 0) return hr;
            step?.Invoke("NOTE MP4 encoder: the buffer is being locked.");
            hr = buffer.Lock(out var pointer, out _, out _);
            if (hr < 0) return hr;
            try
            {
                step?.Invoke("NOTE MP4 encoder: the buffer is locked; the bytes go in.");
                Marshal.Copy(bytes, 0, pointer, count);
            }
            finally { buffer.Unlock(); }
            step?.Invoke("NOTE MP4 encoder: the bytes are in; saying how much of the buffer is filled.");
            buffer.SetCurrentLength(count);
            step?.Invoke("NOTE MP4 encoder: a sample is being made to carry the buffer.");
            hr = Mf.MFCreateSample(out sample);
            if (hr < 0) return hr;
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            step?.Invoke("NOTE MP4 encoder: the sample is stamped; handing it to the writer.");
            return writer.WriteSample(streamIndex, sample);
        }
        finally
        {
            if (sample is not null) { try { Marshal.ReleaseComObject(sample); } catch { } }
            if (buffer is not null) { try { Marshal.ReleaseComObject(buffer); } catch { } }
        }
    }
}
