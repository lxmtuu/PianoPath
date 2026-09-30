using System.IO;
using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>One camera the machine reports: the name a person sees and the link used to open it.</summary>
/// <param name="Name">Friendly name from the driver, e.g. "HD Webcam".</param>
/// <param name="Link">Symbolic link; the identity stored in the settings, empty when the driver gave none.</param>
internal sealed record CameraInfo(string Name, string Link);

/// <summary>
/// Reads video frames out of a live camera or a video file, always as top-down 32-bit BGRA.
///
/// <para>
/// A camera is opened from its symbolic link and a file from its path, and both go through the same source
/// reader configured for RGB32, so one piece of code — and one check — covers the two sources. Media
/// Foundation is started the first time anything here is used and shut down by <see cref="Shutdown"/> once the
/// window is closed and no reader is left.
/// </para>
///
/// <para>
/// Every failure is a sentence rather than an exception: a machine with no camera, a file whose codec is
/// missing and a media stack that refuses to start all come back through the <c>error</c> of the open method
/// or through <see cref="Error"/>, and the dock prints them.
/// </para>
/// </summary>
internal sealed class CameraFrameReader : IDisposable
{
    /// <summary>Frames are paced at this rate; the overlay is a picture-in-picture, not a film viewer.</summary>
    internal const int FrameRate = 30;

    /// <summary>Seconds one frame is shown, for the pump that reads ahead.</summary>
    internal static double FrameSeconds => 1.0 / FrameRate;

    private static readonly Lock StartupLock = new();
    private static bool _started;
    private static string? _startupError;

    private Mf.IMFSourceReader? _reader;
    private byte[] _sample = [];
    private bool _disposed;

    /// <summary>Friendly name of the camera or name of the file; what the dock shows.</summary>
    internal string Label { get; private set; } = "";

    /// <summary>The symbolic link or path this reader was opened from.</summary>
    internal string Source { get; private set; } = "";

    /// <summary>True when this reader follows a live camera instead of a file.</summary>
    internal bool IsLive { get; private set; }

    /// <summary>Why the reader cannot deliver frames, or <c>null</c> while it works.</summary>
    internal string? Error { get; private set; }

    internal int Width { get; private set; }

    internal int Height { get; private set; }

    /// <summary>The stride Media Foundation reports for the frame; <c>Width × 4</c> when it does not say.</summary>
    internal int Stride { get; private set; }

    /// <summary>True once the source reported the end of the stream; a file is rewound to loop it.</summary>
    internal bool AtEnd { get; private set; }

    /// <summary>Frames delivered so far, which is what the checks count instead of guessing.</summary>
    internal int Frames { get; private set; }

    /// <summary>
    /// True when Media Foundation started, which is what tells a caller an overlay can work at all. The reason
    /// it did not start is in <see cref="StartupError"/>.
    /// </summary>
    internal static bool Available { get { EnsureStarted(); return _started; } }

    /// <summary>The sentence for a media stack that would not start, or <c>null</c> when it did.</summary>
    internal static string? StartupError { get { EnsureStarted(); return _startupError; } }

    /// <summary>
    /// Every camera the machine reports, in driver order. A machine with none (a server, a VM, this project's
    /// build runner) yields an empty list, and a failure to enumerate is reported through
    /// <paramref name="error"/> so the dock can print it instead of claiming there is no camera.
    /// </summary>
    internal static IReadOnlyList<CameraInfo> Devices(out string? error)
    {
        var found = Enumerate(out error);
        var devices = found.Select(entry => entry.Info).ToList();
        foreach (var entry in found) Release(entry.Activate);
        return devices;
    }

    /// <summary>Opens the camera with this symbolic link, or the first one when the link is empty or gone.</summary>
    internal static CameraFrameReader? OpenDevice(string link, out string? error)
    {
        error = null;
        var found = Enumerate(out var listError);
        try
        {
            if (found.Count == 0)
            {
                error = listError ?? Loc.T("No camera was found on this machine.");
                return null;
            }
            var chosen = found.FirstOrDefault(entry => entry.Info.Link.Length > 0 && string.Equals(entry.Info.Link, link, StringComparison.OrdinalIgnoreCase));
            if (chosen.Activate is null)
            {
                // The stored link is gone (a camera was unplugged): fall back to the first one and say so.
                chosen = found.First();
                if (!string.IsNullOrEmpty(link)) error = Loc.T("That camera is gone, so the first camera on the machine is used instead.");
            }
            var reader = Attach(readerAttributes => DeviceReader(chosen.Activate!, readerAttributes), out var openError);
            error = openError ?? error;
            if (reader is not null)
            {
                reader.Label = chosen.Info.Name.Length > 0 ? chosen.Info.Name : Loc.T("Camera");
                reader.Source = chosen.Info.Link;
                reader.IsLive = true;
            }
            return reader;
        }
        finally { foreach (var entry in found) Release(entry.Activate); }
    }

    /// <summary>Opens a video file as the overlay's source; its frames are read exactly like a camera's.</summary>
    internal static CameraFrameReader? OpenFile(string path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = Loc.F("The video file {0} does not exist.", path ?? "");
            return null;
        }
        var reader = Attach(attributes => Mf.MFCreateSourceReaderFromURL(Path.GetFullPath(path), attributes, out var created) == Mf.S_OK ? created : null, out error);
        if (reader is not null) { reader.Label = Path.GetFileName(path); reader.Source = Path.GetFullPath(path); }
        return reader;
    }

    /// <summary>
    /// Reads the next frame into <paramref name="destination"/> (at least <c>Width × Height × 4</c> bytes),
    /// turning it into top-down BGRA and mirroring it when asked.
    ///
    /// <para>
    /// False means "no picture this time": the stream ended (<see cref="AtEnd"/>), the sample carried no buffer
    /// (a stream tick), or something failed and <see cref="Error"/> says what. The caller decides whether to
    /// retry, rewind or give up.
    /// </para>
    /// </summary>
    internal bool TryReadFrame(byte[] destination, bool mirror, out int width, out int height)
    {
        width = Width; height = Height;
        if (_disposed || _reader is null || Error is not null) return false;
        Mf.IMFSample? sample = null;
        try
        {
            var flags = 0;
            var hr = _reader.ReadSample(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out _, out flags, out _, out sample);
            if (hr == Mf.MF_E_END_OF_STREAM || (flags & Mf.MF_SOURCE_READERF_ENDOFSTREAM) != 0) { AtEnd = true; return false; }
            if (hr != Mf.S_OK) { Error = Loc.F("Reading a frame failed ({0}).", Mf.Describe(hr)); return false; }
            if (sample is null) return false;      // a stream tick: time passed, no picture
            if (sample.ConvertToContiguousBuffer(out var buffer) != Mf.S_OK || buffer is null)
            {
                Error = Loc.T("The frame carried no picture buffer.");
                return false;
            }
            try
            {
                if (buffer.Lock(out var pointer, out _, out var length) != Mf.S_OK)
                {
                    Error = Loc.T("The frame's picture buffer could not be locked.");
                    return false;
                }
                try
                {
                    var needed = Math.Abs(Stride) * Height;
                    if (Width <= 0 || Height <= 0 || length < needed)
                    {
                        Error = Loc.F("The frame was {0} bytes, too small for {1} × {2}.", length, Width, Height);
                        return false;
                    }
                    if (_sample.Length < length) _sample = new byte[length];
                    Marshal.Copy(pointer, _sample, 0, length);
                }
                finally { buffer.Unlock(); }
            }
            finally { Release(buffer); }
        }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally { Release(sample); }

        CameraOverlay.CopyFrame(_sample, Stride, Width, Height, destination, mirror);
        Frames++;
        return true;
    }

    /// <summary>
    /// Goes back to the first frame, which is how a file loops; a live camera has nothing to rewind.
    ///
    /// <para>
    /// The file is opened again rather than seeked: seeking means handing Media Foundation a hand-declared
    /// <c>PROPVARIANT</c> by value, and a mis-sized one of those corrupts the stack. A fresh reader also resets
    /// the decoder, so the loop starts clean.
    /// </para>
    /// </summary>
    internal void Rewind()
    {
        if (_reader is null || IsLive || Source.Length == 0) return;
        var replacement = Attach(attributes => Mf.MFCreateSourceReaderFromURL(Source, attributes, out var created) == Mf.S_OK ? created : null, out var error);
        if (replacement is null) { Error = error ?? Loc.T("The video file could not be opened again."); return; }
        var previous = _reader;
        _reader = replacement._reader; replacement._reader = null;
        Width = replacement.Width; Height = replacement.Height; Stride = replacement.Stride;
        // Releasing the replacement gives back its reference; the reader itself now belongs to this one.
        replacement.Dispose();
        AtEnd = false;
        if (previous is null) return;
        try { previous.Flush(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM); } catch { }
        Release(previous);
    }

    /// <summary>Closes the reader; a frame read that is in flight is broken out of first, so closing never waits.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var reader = _reader;
        _reader = null;
        if (reader is not null)
        {
            try { reader.Flush(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM); } catch { }
            Release(reader);
        }
    }

    // =================================================================================================
    // Opening
    // =================================================================================================

    /// <summary>
    /// Creates a source reader with the options the overlay needs and configures it for RGB32. The attach
    /// function builds the reader for one particular source: a camera or a file.
    /// </summary>
    private static CameraFrameReader? Attach(Func<Mf.IMFAttributes?, Mf.IMFSourceReader?> attach, out string? error)
    {
        EnsureStarted();
        if (!_started) { error = _startupError; return null; }
        Mf.IMFAttributes? attributes = null;
        Mf.IMFSourceReader? reader = null;
        try
        {
            if (Mf.MFCreateAttributes(out attributes, 1) != Mf.S_OK || attributes is null)
            {
                error = Loc.T("The reader options could not be prepared.");
                return null;
            }
            // Ask the reader to insert its own converter, so a device that produces YUY2, NV12 or MJPEG still
            // ends up as the 32-bit frames the overlay draws.
            attributes.SetUINT32Key(Mf.ReaderAdvancedKey, Mf.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING);
            reader = attach(attributes);
            if (reader is null)
            {
                error = Loc.T("The source could not be opened.");
                return null;
            }
            var result = new CameraFrameReader { _reader = reader };
            reader = null;                       // the reader object owns it from here
            if (result.Configure() is { } configureError) { error = configureError; result.Dispose(); return null; }
            error = null;
            return result;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Release(reader);
            return null;
        }
        finally { Release(attributes); }
    }

    /// <summary>Creates a reader from a camera's activator and hands the source to the caller.</summary>
    private static Mf.IMFSourceReader? DeviceReader(Mf.IMFActivate activate, Mf.IMFAttributes? attributes)
    {
        if (Mf.MFCreateDeviceSource(activate, out var source) != Mf.S_OK || source == IntPtr.Zero) return null;
        try { return Mf.MFCreateSourceReaderFromMediaSource(source, attributes, out var reader) == Mf.S_OK ? reader : null; }
        finally { Marshal.Release(source); }
    }

    /// <summary>
    /// Selects the video stream, asks for RGB32 and reads the frame size and stride the reader settled on.
    /// </summary>
    private string? Configure()
    {
        var reader = _reader!;
        var hr = reader.SetStreamSelection(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 1);
        if (hr != Mf.S_OK) return Loc.F("This source has no video stream ({0}).", Mf.Describe(hr));
        Mf.IMFMediaType? type = null;
        try
        {
            if (Mf.MFCreateMediaType(out type) != Mf.S_OK || type is null) return Loc.T("A media type could not be prepared.");
            type.SetGUIDKey(Mf.MajorType, Mf.VideoMajorType);
            type.SetGUIDKey(Mf.SubType, Mf.Rgb32);
            hr = reader.SetCurrentMediaType(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, type);
            if (hr != Mf.S_OK) return Loc.F("This source cannot produce the 32-bit frames the overlay draws ({0}).", Mf.Describe(hr));
            if (reader.GetCurrentMediaType(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var current) != Mf.S_OK || current is null)
                return Loc.T("The reader did not report the format of its video stream.");
            try
            {
                if (current.GetUINT64Key(Mf.FrameSize, out var size) == Mf.S_OK)
                {
                    var (high, low) = Mf.Split(size);
                    Width = high; Height = low;
                }
                Stride = current.GetUINT32Key(Mf.DefaultStride, out var stride) == Mf.S_OK && stride != 0 ? stride : Width * 4;
            }
            finally { Release(current); }
            if (Width <= 0 || Height <= 0) return Loc.T("The reader reported a video stream with no frame size.");
            return null;
        }
        finally { Release(type); }
    }

    // =================================================================================================
    // Enumeration and lifetime
    // =================================================================================================

    private static List<(CameraInfo Info, Mf.IMFActivate? Activate)> Enumerate(out string? error)
    {
        error = null;
        EnsureStarted();
        var devices = new List<(CameraInfo, Mf.IMFActivate?)>();
        if (!_started)
        {
            error = _startupError ?? Loc.T("The media stack did not start, so no camera can be read.");
            return devices;
        }
        Mf.IMFAttributes? attributes = null;
        var activates = IntPtr.Zero;
        try
        {
            if (Mf.MFCreateAttributes(out attributes, 1) != Mf.S_OK || attributes is null)
            {
                error = Loc.T("The camera list could not be prepared.");
                return devices;
            }
            if (attributes.SetGUIDKey(Mf.DeviceSourceType, Mf.VideoCapture) != Mf.S_OK)
            {
                error = Loc.T("The camera list could not be prepared.");
                return devices;
            }
            var hr = Mf.MFEnumDeviceSources(attributes, out activates, out var count);
            if (hr != Mf.S_OK)
            {
                error = Loc.F("The camera list could not be read ({0}).", Mf.Describe(hr));
                return devices;
            }
            for (var index = 0; index < count && index < 64; index++)
            {
                var pointer = Marshal.ReadIntPtr(activates, index * IntPtr.Size);
                if (pointer == IntPtr.Zero) continue;
                try
                {
                    if (Marshal.GetObjectForIUnknown(pointer) is not Mf.IMFActivate activate) continue;
                    devices.Add((new CameraInfo(Attribute(activate, Mf.FriendlyName) ?? Loc.F("Camera {0}", index + 1),
                        Attribute(activate, Mf.SymbolicLink) ?? ""), activate));
                }
                catch
                {
                    // A driver that will not answer is skipped; the other cameras are still listed.
                }
            }
        }
        catch (Exception ex) { error = ex.Message; }
        finally
        {
            if (activates != IntPtr.Zero) Marshal.FreeCoTaskMem(activates);
            Release(attributes);
        }
        return devices;
    }

    /// <summary>Reads a string attribute the way Media Foundation hands it out: an allocated wide string.</summary>
    private static string? Attribute(Mf.IMFAttributes attributes, Guid key)
    {
        if (attributes.GetAllocatedStringKey(key, out var pointer, out _) != Mf.S_OK || pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static void EnsureStarted()
    {
        lock (StartupLock)
        {
            if (_started || _startupError is not null) return;
            try
            {
                var hr = Mf.MediaStartup();
                if (hr == Mf.S_OK) _started = true;
                else _startupError = Loc.F("The media stack would not start ({0}).", Mf.Describe(hr));
            }
            catch (DllNotFoundException) { _startupError = Loc.T("This copy of Windows has no Media Foundation, so a camera overlay cannot run."); }
            catch (Exception ex) { _startupError = ex.Message; }
        }
    }

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            try { Marshal.ReleaseComObject(instance); } catch { }
        }
    }
}
