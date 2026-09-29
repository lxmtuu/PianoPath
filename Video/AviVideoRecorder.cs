using System.IO;
using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>Small Windows AVI writer; prefers the installed MJPEG VFW codec and falls back to raw RGB frames.</summary>
internal sealed class AviVideoRecorder : IDisposable
{
    private const uint OfWrite = 1, OfCreate = 0x1000, AviIfKeyFrame = 0x10;
    private IntPtr _file, _rawStream, _compressedStream, _writeStream;
    private readonly int _width, _height, _stride;
    private int _frameIndex;
    private bool _initialized, _disposed;
    /// <summary>Classic AVI (RIFF with 32-bit offsets) cannot grow past 2 GiB; stop a little before that so the index still fits.</summary>
    public const long SizeLimitBytes = 1_900_000_000;
    public bool UsesMjpeg { get; private set; }
    public int Width => _width;
    public int Height => _height;
    public int FrameRate { get; }
    /// <summary>Frames written so far, including repeated frames used to keep the file in sync with wall-clock time.</summary>
    public int FrameCount => _frameIndex;
    /// <summary>Approximate payload written so far (uncompressed size of every frame handed to the stream).</summary>
    public long BytesWritten { get; private set; }
    public bool IsNearSizeLimit => BytesWritten >= SizeLimitBytes;

    public AviVideoRecorder(string path, int width, int height, int frameRate = 20)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(Loc.T("Video recording currently requires Windows."));
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        _width = width & ~1; _height = height & ~1; FrameRate = frameRate; _stride = ((_width * 3 + 3) / 4) * 4;
        AVIFileInit(); _initialized = true;
        try { Open(path); }
        catch { Dispose(); throw; }
    }

    private void Open(string path)
    {
        var result = AVIFileOpenW(out _file, path, OfWrite | OfCreate, IntPtr.Zero);
        Check(result, "Could not create the AVI file");
        var info = new AviStreamInfo
        {
            Type = FourCc("vids"), Handler = 0, Scale = 1, Rate = (uint)FrameRate,
            SuggestedBufferSize = (uint)(_stride * _height), Quality = 8000,
            Frame = new AviRect { Right = _width, Bottom = _height }, Name = "Keyflow live piano"
        };
        Check(AVIFileCreateStream(_file, out _rawStream, ref info), "Could not create the AVI video stream");
        var format = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = _width, Height = _height,
            Planes = 1, BitCount = 24, Compression = 0, ImageSize = (uint)(_stride * _height)
        };
        var formatSize = Marshal.SizeOf<BitmapInfoHeader>();
        Check(AVIStreamSetFormat(_rawStream, 0, ref format, formatSize), "Could not set the video frame format");
        var options = new AviCompressOptions { Type = FourCc("vids"), Handler = FourCc("MJPG"), KeyFrameEvery = (uint)FrameRate, Quality = 8000 };
        if (AVIMakeCompressedStream(out _compressedStream, _rawStream, ref options, IntPtr.Zero) == 0)
        {
            if (AVIStreamSetFormat(_compressedStream, 0, ref format, formatSize) == 0)
            {
                _writeStream = _compressedStream; UsesMjpeg = true;
            }
            else { AVIStreamRelease(_compressedStream); _compressedStream = IntPtr.Zero; }
        }
        if (_writeStream == IntPtr.Zero) _writeStream = _rawStream;
    }

    /// <summary>Write one bottom-up, padded 24-bit BGR frame, optionally repeated so the stream keeps real-time pacing.</summary>
    public void WriteBgrFrame(byte[] pixels, int repeat = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var expected = _stride * _height;
        if (pixels.Length != expected) throw new ArgumentException(Loc.F("Expected a {0}×{1} BGR frame ({2} bytes).", _width, _height, expected), nameof(pixels));
        if (repeat < 1) return;
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            for (var i = 0; i < repeat; i++)
            {
                // Every raw frame stands alone; an MJPEG stream only has a real key frame every
                // FrameRate samples (see AviCompressOptions.KeyFrameEvery), so claim exactly those.
                var flags = UsesMjpeg && _frameIndex % FrameRate != 0 ? 0u : AviIfKeyFrame;
                var result = AVIStreamWrite(_writeStream, _frameIndex, 1, pinned.AddrOfPinnedObject(), pixels.Length, flags, out var written, out var bytes);
                Check(result, "Could not write a video frame");
                if (written != 1) throw new IOException(Loc.T("The AVI writer did not accept the video frame."));
                _frameIndex++; BytesWritten += bytes > 0 ? bytes : pixels.Length;
            }
        }
        finally { pinned.Free(); }
    }

    public static int BgrStride(int width) => ((width * 3 + 3) / 4) * 4;
    private static uint FourCc(string value) => (uint)value[0] | ((uint)value[1] << 8) | ((uint)value[2] << 16) | ((uint)value[3] << 24);
    private static void Check(int result, string message) { if (result != 0) throw new IOException(Loc.F("{0} (AVI error 0x{1:X8}).", Loc.T(message), result)); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_compressedStream != IntPtr.Zero) { AVIStreamRelease(_compressedStream); _compressedStream = IntPtr.Zero; }
        if (_rawStream != IntPtr.Zero) { AVIStreamRelease(_rawStream); _rawStream = IntPtr.Zero; }
        _writeStream = IntPtr.Zero;
        if (_file != IntPtr.Zero) { AVIFileRelease(_file); _file = IntPtr.Zero; }
        if (_initialized) { AVIFileExit(); _initialized = false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct AviRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] private struct AviStreamInfo
    {
        public uint Type, Handler, Flags, Caps;
        public ushort Priority, Language;
        public uint Scale, Rate, Start, Length, InitialFrames, SuggestedBufferSize, Quality, SampleSize;
        public AviRect Frame;
        public uint EditCount, FormatChangeCount;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter; public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct AviCompressOptions
    {
        public uint Type, Handler, KeyFrameEvery, Quality, BytesPerSecond, Flags;
        public IntPtr Format; public uint FormatSize; public IntPtr Parameters; public uint ParametersSize, InterleaveEvery;
    }

    [DllImport("avifil32.dll", EntryPoint = "AVIFileInit", CallingConvention = CallingConvention.StdCall)] private static extern void AVIFileInit();
    [DllImport("avifil32.dll", EntryPoint = "AVIFileExit", CallingConvention = CallingConvention.StdCall)] private static extern void AVIFileExit();
    [DllImport("avifil32.dll", EntryPoint = "AVIFileOpenW", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)] private static extern int AVIFileOpenW(out IntPtr file, string path, uint mode, IntPtr handler);
    [DllImport("avifil32.dll", CallingConvention = CallingConvention.StdCall)] private static extern int AVIFileCreateStream(IntPtr file, out IntPtr stream, ref AviStreamInfo info);
    [DllImport("avifil32.dll", CallingConvention = CallingConvention.StdCall)] private static extern int AVIStreamSetFormat(IntPtr stream, int position, ref BitmapInfoHeader format, int formatSize);
    [DllImport("avifil32.dll", CallingConvention = CallingConvention.StdCall)] private static extern int AVIMakeCompressedStream(out IntPtr compressed, IntPtr source, ref AviCompressOptions options, IntPtr handler);
    [DllImport("avifil32.dll", CallingConvention = CallingConvention.StdCall)] private static extern int AVIStreamWrite(IntPtr stream, int start, int samples, IntPtr buffer, int bufferSize, uint flags, out int samplesWritten, out int bytesWritten);
    [DllImport("avifil32.dll", CallingConvention = CallingConvention.StdCall)] private static extern int AVIStreamRelease(IntPtr stream);
    [DllImport("avifil32.dll", CallingConvention = CallingConvention.StdCall)] private static extern int AVIFileRelease(IntPtr file);
}
