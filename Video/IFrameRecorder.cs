namespace PianoPath;

/// <summary>
/// What the REC button writes. Two recorders implement this — <see cref="AviVideoRecorder"/> for a single
/// video file and <see cref="PngSequenceRecorder"/> for a folder of frames — so the recording session in
/// <c>Ui/MainWindow.xaml.cs</c> paces frames, shows the elapsed time and stops at a limit without caring
/// which one is running.
///
/// <para>
/// Both are fed the same way: one frame buffer, repeated <paramref name="repeat"/> times when the render
/// cannot keep up with the clock, so a slow machine produces a file that plays at the right speed instead
/// of a fast-forward. The pixel layout of that buffer belongs to the recorder —
/// <see cref="HasAlpha"/> and <see cref="FrameBytes"/> describe it, and the session asks the stage for the
/// matching buffer.
/// </para>
/// </summary>
internal interface IFrameRecorder : IDisposable
{
    /// <summary>Frame width in pixels; always even for the AVI recorder.</summary>
    int Width { get; }

    /// <summary>Frame height in pixels; always even for the AVI recorder.</summary>
    int Height { get; }

    /// <summary>Frames per second the file or the sequence plays back at.</summary>
    int FrameRate { get; }

    /// <summary>Frames written so far, counting a repeated frame once for every copy.</summary>
    int FrameCount { get; }

    /// <summary>Payload written so far: AVI stream bytes, or the size of the PNG files.</summary>
    long BytesWritten { get; }

    /// <summary>True when the recorder is close to a format limit and the session should stop.</summary>
    bool IsNearSizeLimit { get; }

    /// <summary>Where the recording went: the file, or the folder of frames.</summary>
    string OutputPath { get; }

    /// <summary>Bytes one frame occupies in the buffer handed to <see cref="WriteFrame"/>.</summary>
    int FrameBytes { get; }

    /// <summary>True when the recorder keeps the alpha channel of the frames it is handed.</summary>
    bool HasAlpha { get; }

    /// <summary>Writes one frame, or <paramref name="repeat"/> copies of it to catch up with the clock.</summary>
    void WriteFrame(byte[] frame, int repeat = 1);
}
