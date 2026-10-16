using System.Diagnostics;
using System.IO;

namespace PianoPath;

/// <summary>
/// The REC format for the open web: one WebM holding the stage's frames as VP9 and the engine's own
/// samples as Opus. Windows ships no VP9 encoder and no WebM muxer, so the encoding itself is done by the
/// ffmpeg the machine already has (see <see cref="WebmEncoderPlan"/> for where it is looked for and why it
/// is not bundled); this class is the recording session around it, shaped exactly like the MP4 take.
///
/// <para>
/// While the take runs, a worker thread feeds the frames it is handed into ffmpeg's stdin — the queue and
/// the folding of repeats into the newest frame are the MP4 recorder's rules, so a slow encoder never
/// stalls the window and the file still keeps time. The audio side is an <see cref="IAudioTrack"/> like the
/// MP4's, but the samples land in a temporary WAV while the picture encodes; stopping the take closes both
/// and runs the second, fast pass that copies the picture and encodes the track as Opus into the file the
/// user named. A take that fails anywhere (no ffmpeg, an encoder that dies mid-take) says so and keeps the
/// temporary picture out of the user's way.
/// </para>
/// </summary>
internal sealed class WebmRecorder : IFrameRecorder, IAudioTrack
{
    /// <summary>Frames that may wait for the encoder before further ones are folded into the newest as repeats.</summary>
    private const int MaxQueuedFrames = 4;

    /// <summary>How long ffmpeg gets to finish a take before the file is abandoned rather than the window.</summary>
    private static readonly TimeSpan FinishLimit = TimeSpan.FromSeconds(30);

    /// <summary>A frame on its way to the encoder: its pixels and how many timeline slots it fills.</summary>
    private sealed class PendingFrame(byte[] bgra, int repeat)
    {
        internal readonly byte[] Bgra = bgra;
        internal int Repeat = repeat;
    }

    private readonly object _queueGate = new();
    private readonly Queue<PendingFrame> _queue = new();
    private readonly Stack<byte[]> _spare = new();
    private PendingFrame? _tail;
    private readonly Thread _worker;
    private readonly Process _ffmpeg;
    private readonly string _tempVideo;
    private readonly string? _tempAudio;
    private readonly WavWriter? _audio;
    private readonly Lock _audioLock = new();
    private bool _closing;
    private bool _disposed;
    private Exception? _workerError;

    /// <summary>
    /// Starts the video pass and, when <paramref name="withAudio"/>, the temporary track. Throws when the
    /// machine has no ffmpeg to encode with — the sentence is the one the recording dialog shows — or when
    /// ffmpeg refuses the take in its first moments.
    /// </summary>
    /// <param name="alpha">Keep the frames' alpha channel (the stage draws without its opaque fills): the
    /// picture is encoded as yuva420p, the WebM-with-alpha the PNG sequence's manifest used to promise.</param>
    internal WebmRecorder(string path, int width, int height, int frameRate, bool withAudio, bool alpha)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(Loc.T("A WebM recording needs a file to go into."), nameof(path));
        if (width < 2 || (width & 1) != 0) throw new ArgumentOutOfRangeException(nameof(width), Loc.T("A WebM frame needs an even width of at least 2."));
        if (height < 2 || (height & 1) != 0) throw new ArgumentOutOfRangeException(nameof(height), Loc.T("A WebM frame needs an even height of at least 2."));
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        Width = width; Height = height; FrameRate = frameRate; OutputPath = path; Alpha = alpha;

        var ffmpeg = WebmEncoderPlan.FindFfmpeg(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"))
            ?? throw new InvalidOperationException(Loc.T("WebM recording needs ffmpeg, and none was found beside the app or on the PATH. Install ffmpeg (or put ffmpeg.exe next to Keyflow) and try again — AVI, MP4 and the PNG sequence need nothing extra."));
        _tempVideo = path + ".video.webm";
        _ffmpeg = StartPass(ffmpeg, WebmEncoderPlan.VideoPassArguments(width, height, frameRate, _tempVideo, alpha));
        if (withAudio)
        {
            _tempAudio = path + ".audio.wav";
            _audio = WavWriter.TryCreate(_tempAudio);
        }
        _worker = new Thread(PumpFrames) { IsBackground = true, Name = "WebM encoder" };
        _worker.Start();
    }

    public int Width { get; }
    public int Height { get; }
    public int FrameRate { get; }
    public int FrameCount { get; private set; }

    /// <summary>The size of the file being written (the temporary picture until the take is muxed).</summary>
    public long BytesWritten
    {
        get
        {
            try { return new FileInfo(File.Exists(OutputPath) ? OutputPath : _tempVideo).Length; }
            catch { return 0; }
        }
    }

    /// <summary>WebM has no 2 GB ceiling to run into; part of <see cref="IFrameRecorder"/>.</summary>
    public bool IsNearSizeLimit => false;

    public string OutputPath { get; }

    /// <summary>
    /// Bytes one frame takes: the tightly packed BGRA buffer the stage hands the PNG sequence, exactly as
    /// the MP4 take asks for it — the alpha byte travels along and is dropped by the yuv420p conversion.
    /// </summary>
    public int FrameBytes => Width * Height * 4;

    /// <summary>
    /// True because the stage hands over its own pixels, alpha byte and all; whether the WebM keeps the
    /// alpha (<see cref="Alpha"/>) or drops it in the yuv420p conversion is the take's own choice.
    /// </summary>
    public bool HasAlpha => true;

    /// <summary>True when the take carries a piano track that will be muxed in as Opus on stop.</summary>
    internal bool HasAudio => _audio is not null;

    /// <summary>True when the picture keeps the stage's alpha channel (yuva420p) instead of flattening it.</summary>
    internal bool Alpha { get; }

    /// <summary>Seconds of audio handed to the track so far; part of <see cref="IAudioTrack"/>.</summary>
    public double Seconds { get { lock (_audioLock) return _audio?.Seconds ?? 0; } }

    /// <summary>
    /// Hands one frame to the encoder thread, or <paramref name="repeat"/> copies when the render fell
    /// behind the clock. Beyond a short queue, frames fold into the newest one as repeats so the window
    /// never waits for the codec and the file still keeps time. A failure on the encoder thread is raised
    /// from the next call here.
    /// </summary>
    public void WriteFrame(byte[] frame, int repeat = 1) => EnqueueFrame(frame, repeat, offline: false);

    public void WriteOfflineFrame(byte[] frame) => EnqueueFrame(frame, 1, offline: true);

    private void EnqueueFrame(byte[] frame, int repeat, bool offline)
    {
        if (frame.Length != FrameBytes) throw new ArgumentException(Loc.F("Expected a {0}×{1} BGRA frame ({2} bytes).", Width, Height, FrameBytes), nameof(frame));
        if (repeat < 1) return;
        lock (_queueGate)
        {
            if (offline)
                OfflineFrameQueue.WaitForSpace(_queueGate, () => _queue.Count, MaxQueuedFrames, () =>
                {
                    if (_workerError is { } offlineError) throw new InvalidOperationException(offlineError.Message, offlineError);
                    if (_disposed || _closing) throw new InvalidOperationException("The offline video recorder is closing.");
                });
            if (_workerError is { } error) throw new InvalidOperationException(error.Message, error);
            if (_disposed || _closing) return;
            if (!offline && _queue.Count >= MaxQueuedFrames && _tail is not null)
            {
                _tail.Repeat += repeat;
            }
            else
            {
                var buffer = _spare.Count > 0 ? _spare.Pop() : new byte[FrameBytes];
                Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);
                _tail = new PendingFrame(buffer, repeat);
                _queue.Enqueue(_tail);
                Monitor.Pulse(_queueGate);
            }
            FrameCount += repeat;
        }
    }

    /// <summary>The encoder thread: writes queued frames into ffmpeg's stdin until the take closes.</summary>
    private void PumpFrames()
    {
        var stdin = _ffmpeg.StandardInput.BaseStream;
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
                Monitor.PulseAll(_queueGate);
            }
            try
            {
                for (var copy = 0; copy < item.Repeat; copy++) stdin.Write(item.Bgra, 0, item.Bgra.Length);
                stdin.Flush();
            }
            catch (Exception ex)
            {
                lock (_queueGate) { _workerError = ex; _queue.Clear(); _tail = null; Monitor.PulseAll(_queueGate); }
                return;
            }
            lock (_queueGate) _spare.Push(item.Bgra);
        }
    }

    /// <summary>Appends the engine's own samples to the take's track; part of <see cref="IAudioTrack"/>.</summary>
    public void Append(short[] samples, int count)
    {
        lock (_audioLock) _audio?.Append(samples, count);
    }

    /// <summary>Appends silence so the track keeps step with the recording clock; part of <see cref="IAudioTrack"/>.</summary>
    public void AppendSilence(long frames)
    {
        lock (_audioLock) _audio?.AppendSilence(frames);
    }

    /// <summary>
    /// Closes the take: the queued frames finish encoding, the track is finalized, and the mux pass writes
    /// the file the user named. Any failure leaves the temporaries deleted and the reason thrown, so the
    /// dialog can say what happened instead of pointing at a half-written file.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_queueGate) { _closing = true; Monitor.PulseAll(_queueGate); }
        _worker.Join(FinishLimit);
        try { _ffmpeg.StandardInput.Close(); } catch { }
        if (!_ffmpeg.WaitForExit((int)FinishLimit.TotalMilliseconds))
        {
            try { _ffmpeg.Kill(entireProcessTree: true); } catch { }
            throw new IOException(Loc.T("ffmpeg stopped answering while finishing the WebM take, so the recording was abandoned."));
        }
        lock (_audioLock) _audio?.Dispose();
        try
        {
            if (_workerError is { } error) throw new InvalidOperationException(error.Message, error);
            if (_ffmpeg.ExitCode != 0) throw new IOException(Loc.F("ffmpeg could not encode the WebM take (exit code {0}).", _ffmpeg.ExitCode));
            if (FrameCount == 0) return; // nothing was captured: no file, and the session says so itself
            RunMuxPass();
        }
        finally
        {
            try { File.Delete(_tempVideo); } catch { }
            if (_tempAudio is not null) { try { File.Delete(_tempAudio); } catch { } }
        }
    }

    /// <summary>Runs the mux pass to completion and turns a non-zero exit into the sentence the dialog shows.</summary>
    private void RunMuxPass()
    {
        var ffmpeg = WebmEncoderPlan.FindFfmpeg(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"))
            ?? throw new InvalidOperationException(Loc.T("WebM recording needs ffmpeg, and none was found beside the app or on the PATH. Install ffmpeg (or put ffmpeg.exe next to Keyflow) and try again — AVI, MP4 and the PNG sequence need nothing extra."));
        var audio = _audio is { Frames: > 0 } ? _tempAudio : null;
        using var pass = StartPass(ffmpeg, WebmEncoderPlan.MuxPassArguments(_tempVideo, audio, OutputPath));
        pass.StandardInput.Close();
        if (!pass.WaitForExit((int)FinishLimit.TotalMilliseconds))
        {
            try { pass.Kill(entireProcessTree: true); } catch { }
            throw new IOException(Loc.T("ffmpeg stopped answering while finishing the WebM take, so the recording was abandoned."));
        }
        if (pass.ExitCode != 0) throw new IOException(Loc.F("ffmpeg could not finish the WebM file (exit code {0}).", pass.ExitCode));
    }

    /// <summary>
    /// Starts one ffmpeg pass with its stdin as the frame pipe (closed unused by the mux pass) and its
    /// console output swallowed: a codec's chatter belongs in no window, and an unread redirected stream
    /// would fill its pipe and wedge the pass.
    /// </summary>
    private static Process StartPass(string ffmpeg, string[] arguments)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException(Loc.T("ffmpeg would not start."));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
