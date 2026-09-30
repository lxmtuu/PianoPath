using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>
/// The encoding half of the Media Foundation surface: what turns the stage's own frames and the engine's own
/// samples into a single MP4. It sits beside the camera's half in <c>Camera/MediaFoundation.cs</c> and follows
/// the same rule — the COM interfaces are declared by hand in vtable order, and only the methods actually
/// called have real signatures, since a wrong order would corrupt the stack at the first call.
///
/// <para>
/// The work is done by a sink writer: it owns the file, the H.264 encoder and the AAC encoder, and this side
/// hands it uncompressed samples — NV12 frames (see <see cref="Nv12Frame"/>) and 16-bit stereo PCM — each
/// stamped with its own time. Nothing here is called unless the recording format is MP4, and every failure is
/// reported as the <c>HRESULT</c> the managed side turns into a sentence.
/// </para>
/// </summary>
internal static partial class Mf
{
    // ---- attribute keys the writer side needs --------------------------------------------------------
    /// <summary>MF_MT_AVG_BITRATE: the bitrate the encoder is asked for, in bits a second.</summary>
    internal static readonly Guid AverageBitrate = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    /// <summary>MF_MT_FRAME_RATE: frames a second, packed as (numerator, denominator).</summary>
    internal static readonly Guid FrameRateKey = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    /// <summary>MF_MT_PIXEL_ASPECT_RATIO: square pixels, packed the same way.</summary>
    internal static readonly Guid PixelAspectRatio = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
    /// <summary>MF_MT_INTERLACE_MODE: see <see cref="InterlaceProgressive"/>.</summary>
    internal static readonly Guid InterlaceMode = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    /// <summary>MF_MT_ALL_SAMPLES_INDEPENDENT: every frame is a key frame on its own.</summary>
    internal static readonly Guid AllSamplesIndependent = new("C9173739-5E56-461C-B713-46FB995CB95F");
    /// <summary>MF_MT_AUDIO_NUM_CHANNELS.</summary>
    internal static readonly Guid AudioChannels = new("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
    /// <summary>MF_MT_AUDIO_SAMPLES_PER_SECOND.</summary>
    internal static readonly Guid AudioSamplesPerSecond = new("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
    /// <summary>MF_MT_AUDIO_BLOCK_ALIGNMENT: bytes one whole frame of audio takes.</summary>
    internal static readonly Guid AudioBlockAlignment = new("322DE230-9EEB-43BD-AB7A-FF412251541D");
    /// <summary>MF_MT_AUDIO_AVG_BYTES_PER_SECOND.</summary>
    internal static readonly Guid AudioAverageBytesPerSecond = new("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
    /// <summary>MF_MT_AUDIO_BITS_PER_SAMPLE.</summary>
    internal static readonly Guid AudioBitsPerSample = new("F2DEB57F-40FA-4764-AA33-EDEA233B1986");

    // ---- media type values ---------------------------------------------------------------------------
    /// <summary>The major type of an audio media type, 'auds'.</summary>
    internal static readonly Guid AudioMajorType = new("73647561-0000-0010-8000-00AA00389B71");
    /// <summary>The H.264 video encoder's output sub-type, 'H264'.</summary>
    internal static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
    /// <summary>The encoder's input sub-type this side produces, 'NV12'.</summary>
    internal static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    /// <summary>The AAC encoder's output sub-type, MFAudioFormat_AAC.</summary>
    internal static readonly Guid Aac = new("00001610-0000-0010-8000-00AA00389B71");
    /// <summary>Uncompressed 16-bit PCM, the audio the engine renders.</summary>
    internal static readonly Guid Pcm = new("00000001-0000-0010-8000-00AA00389B71");

    /// <summary>MFVideoInterlace_Progressive: the stage draws progressive frames and nothing else.</summary>
    internal const int InterlaceProgressive = 2;

    // ---- the writer ---------------------------------------------------------------------------------
    /// <summary>Creates a sink writer that writes into a file. The file is created (or truncated) at once.</summary>
    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IMFAttributes? attributes, out IMFSinkWriter writer);

    /// <summary>Allocates the buffer a sample carries.</summary>
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    /// <summary>Allocates one empty sample.</summary>
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateSample(out IMFSample sample);

    /// <summary>
    /// Packs the two halves a media type stores in one UINT64 — a frame size or a frame rate — in the order
    /// Media Foundation reads them: the first value in the high word, the second in the low one.
    /// </summary>
    internal static long Pack(int high, int low) => ((long)high << 32) | (uint)low;

    /// <summary>
    /// Sets a UINT64 attribute from a key constant. Like the five helpers beside the camera's half, this copies
    /// the key into a local first because a readonly field cannot be passed by reference.
    /// </summary>
    internal static int SetUINT64Key(this IMFAttributes attributes, Guid key, long value)
    {
        var local = key;
        return attributes.SetUINT64(ref local, value);
    }

    /// <summary>
    /// The sink writer: the object that owns the MP4 and the two encoders behind it. The methods are in the
    /// order <c>mfreadwrite.h</c> declares them, so the two at the end exist to keep the order right and are
    /// never called — <c>Finalize</c> is a C# keyword, hence the name of the one that is.
    /// </summary>
    [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSinkWriter
    {
        /// <summary>Adds the stream the writer will produce, and returns its index.</summary>
        [PreserveSig] int AddStream(IMFMediaType target, out int streamIndex);
        /// <summary>Declares the format this side hands that stream.</summary>
        [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType input, IMFAttributes? parameters);
        /// <summary>Starts the encoding; no sample may be written before it.</summary>
        [PreserveSig] int BeginWriting();
        /// <summary>Hands over one stamped sample.</summary>
        [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
        [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
        [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(int streamIndex);
        [PreserveSig] int Flush(int streamIndex);
        /// <summary>Closes the file: the index and the last frames are written here.</summary>
        [PreserveSig] int FinalizeFile();
        [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid service, ref Guid interfaceId, out IntPtr value);
        [PreserveSig] int GetStatistics(int streamIndex, IntPtr statistics);
    }
}
