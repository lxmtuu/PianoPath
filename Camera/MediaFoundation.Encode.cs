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

    /// <summary>MF_E_OUT_OF_MEMORY, the code a media type that could not be prepared reports as.</summary>
    internal const int MF_E_OUT_OF_MEMORY = unchecked((int)0x8007000E);

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

    /// <summary>One experiment on the objects a take is made of; see <see cref="MediaObjectCaseRun"/>.</summary>
    internal enum MediaObjectCase
    {
        /// <summary>The take's own order: buffer, sample, buffer into sample, then the time.</summary>
        TimeAfterBuffer,
        /// <summary>The stamps go on before the buffer is attached.</summary>
        TimeBeforeBuffer,
        /// <summary>A sample with no buffer at all.</summary>
        TimeAlone,
        /// <summary>An inherited attribute write — the kind media types already accept — on a sample.</summary>
        AttributeOnSample,
        /// <summary>Asking a fresh sample for its time, which writes nothing anywhere.</summary>
        ReadTime,
        /// <summary>The control: the same write on a media type, which the take performing at all proves works.</summary>
        AttributeOnMediaType,
    }

    /// <summary>
    /// Runs one of the small experiments a machine whose samples will not be stamped can be examined with. Every
    /// case touches a different set of calls, so the one that comes back tells which part of the object works: a
    /// stamp after the buffer, a stamp before it, a stamp with no buffer, an inherited attribute write, a plain
    /// read, and a media type write for comparison. The caller runs these under a watchdog — the point of them is
    /// that a call may not return — and prints what each said.
    /// </summary>
    internal static int MediaObjectCaseRun(MediaObjectCase kind, Action<string>? step = null)
    {
        var hr = MediaStartup();
        if (hr < 0) return hr;
        step?.Invoke("NOTE media objects: the media stack is up.");
        if (kind == MediaObjectCase.AttributeOnMediaType)
        {
            hr = MFCreateMediaType(out var type);
            if (hr < 0) return hr;
            step?.Invoke("NOTE media objects: a media type is made; a GUID attribute is going on.");
            hr = type.SetGUIDKey(SubType, H264);
            step?.Invoke($"NOTE media objects: the media type took the attribute ({Describe(hr)}).");
            return hr;
        }
        hr = MFCreateSample(out var sample);
        if (hr < 0) return hr;
        if (kind == MediaObjectCase.ReadTime)
        {
            step?.Invoke("NOTE media objects: a bare sample is made; its time is being read.");
            hr = sample.GetSampleTime(out _);
            step?.Invoke($"NOTE media objects: the bare sample answered the read ({Describe(hr)}).");
            return hr;
        }
        if (kind == MediaObjectCase.AttributeOnSample)
        {
            step?.Invoke("NOTE media objects: a bare sample is made; a GUID attribute is going on.");
            hr = sample.SetGUIDKey(MfSampleAttribute, H264);
            step?.Invoke($"NOTE media objects: the sample took the attribute ({Describe(hr)}).");
            return hr;
        }
        IMFMediaBuffer? attached = null;
        if (kind == MediaObjectCase.TimeBeforeBuffer)
        {
            step?.Invoke("NOTE media objects: a bare sample is made; the time goes on before any buffer does.");
            hr = sample.SetSampleTime(0);
            step?.Invoke($"NOTE media objects: the sample's time is set with no buffer on it ({Describe(hr)}).");
            if (hr < 0) return hr;
            hr = sample.SetSampleDuration(1);
            step?.Invoke($"NOTE media objects: its duration is set too ({Describe(hr)}).");
            if (hr < 0) return hr;
        }
        if (kind != MediaObjectCase.TimeAlone)
        {
            hr = MFCreateMemoryBuffer(4096, out var buffer);
            if (hr < 0) return hr;
            step?.Invoke("NOTE media objects: a buffer is made and locked.");
            hr = buffer.Lock(out var pointer, out _, out _);
            if (hr < 0) return hr;
            Marshal.Copy(new byte[4096], 0, pointer, 4096);
            buffer.Unlock();
            buffer.SetCurrentLength(4096);
            step?.Invoke("NOTE media objects: the buffer is filled; it is going into the sample.");
            hr = sample.AddBuffer(buffer);
            if (hr < 0) return hr;
            attached = buffer;
        }
        if (kind is MediaObjectCase.TimeAfterBuffer or MediaObjectCase.TimeAlone)
        {
            step?.Invoke("NOTE media objects: the sample's time is going on.");
            hr = sample.SetSampleTime(0);
            if (hr < 0) return hr;
            step?.Invoke("NOTE media objects: the sample's time is set; its duration is going on.");
            hr = sample.SetSampleDuration(1);
            if (hr < 0) return hr;
        }
        step?.Invoke($"NOTE media objects: case {kind} finished with the object stamped ({Describe(hr)}, buffer {(attached is null ? "absent" : "attached")}).");
        return S_OK;
    }

    /// <summary>A GUID attribute the sample's own attribute store can hold; the key is only ever used by the probe.</summary>
    private static readonly Guid MfSampleAttribute = new("7BD72FDD-4A9A-4E4B-9C1C-6A4B2E6E0D30");

    /// <summary>
    /// Builds one media buffer and one sample out of it — make, lock, fill, release, size, carry, stamp — and
    /// writes nothing anywhere. That is the smallest set of Media Foundation objects a take is made of, and on a
    /// machine whose media stack stalls in the middle of handing them over it names the call instead of leaving a
    /// take to time out: a run that stops here has stopped before any encoder, file or sink writer was involved.
    /// </summary>
    /// <returns>The first call that failed, or <see cref="S_OK"/> when the objects were built and stamped.</returns>
    internal static int MediaObjectProbe(int bytes, Action<string>? step = null)
    {
        var hr = MediaStartup();
        if (hr < 0) return hr;
        step?.Invoke("NOTE media objects: the media stack is up.");
        step?.Invoke($"NOTE media objects: a {bytes}-byte buffer is being made.");
        hr = MFCreateMemoryBuffer(bytes, out var buffer);
        if (hr < 0) return hr;
        step?.Invoke("NOTE media objects: the buffer is being locked.");
        hr = buffer.Lock(out var pointer, out _, out _);
        if (hr < 0) return hr;
        step?.Invoke("NOTE media objects: the buffer is locked; the bytes go in.");
        var pattern = new byte[Math.Min(bytes, 64)];
        for (var i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 7);
        for (var offset = 0; offset < bytes; offset += pattern.Length)
            Marshal.Copy(pattern, 0, pointer + offset, Math.Min(pattern.Length, bytes - offset));
        step?.Invoke("NOTE media objects: the bytes are in; the buffer is being released.");
        buffer.Unlock();
        step?.Invoke("NOTE media objects: the buffer is released; saying how much of it is filled.");
        buffer.SetCurrentLength(bytes);
        step?.Invoke("NOTE media objects: a sample is being made to carry the buffer.");
        hr = MFCreateSample(out var sample);
        if (hr < 0) return hr;
        step?.Invoke("NOTE media objects: the buffer is being put into the sample.");
        sample.AddBuffer(buffer);
        step?.Invoke("NOTE media objects: the sample's time is being set.");
        hr = sample.SetSampleTime(0);
        step?.Invoke($"NOTE media objects: the time is set ({Describe(hr)}); the duration is being set.");
        if (hr < 0) return hr;
        hr = sample.SetSampleDuration(1);
        if (hr < 0) return hr;
        step?.Invoke("NOTE media objects: the buffer and the sample came together and were stamped.");
        return S_OK;
    }

    /// <summary>
    /// Asks the media stack whether it can hold an H.264 stream of the given description, and writes nothing at
    /// all: before a sink writer will take a stream it has to find an encoder for the target type, so a machine
    /// that answers yes has an H.264 encoder a take can use, while a machine that answers no can be told about
    /// in one sentence instead of being left to hang inside a take that was never going to be written.
    /// </summary>
    /// <returns>The first step that failed, or <see cref="S_OK"/> when the stream was taken.</returns>
    internal static int EncodeSinkProbe(string path, IMFMediaType target, Action<string>? step = null)
    {
        var hr = MediaStartup();
        if (hr < 0) return hr;
        step?.Invoke("NOTE MP4 encoder: the media stack is up for the encoder check.");
        IMFSinkWriter? writer = null;
        try
        {
            hr = MFCreateSinkWriterFromURL(path, IntPtr.Zero, null, out writer);
            if (hr < 0) return hr;
            step?.Invoke("NOTE MP4 encoder: the MP4 sink writer is open.");
            hr = writer!.AddStream(target, out _);
            if (hr >= 0) step?.Invoke("NOTE MP4 encoder: the MP4 sink writer took the stream.");
            return hr;
        }
        catch { return MF_E_OUT_OF_MEMORY; }
        finally
        {
            if (writer is not null) { try { Marshal.ReleaseComObject(writer); } catch { } }
        }
    }

    /// <summary>
    /// Writes a handful of pictures into an AVI through the very path a take uses — one media buffer, one
    /// sample, one stamped call at a time — but with an uncompressed picture type, so no encoder is involved
    /// anywhere. It answers the question a machine whose encoders misbehave cannot answer any other way:
    /// whether this side's sample plumbing works and the codecs are at fault, or the plumbing itself is.
    /// </summary>
    /// <returns>The first step that failed, or <see cref="S_OK"/> when the file really was written.</returns>
    internal static int EncodeAviProbe(string path, byte[] pixels, int width, int height, int frameRate, int frames,
        Action<string>? step = null)
    {
        var hr = MediaStartup();
        if (hr < 0) return hr;
        IMFSinkWriter? writer = null;
        try
        {
            step?.Invoke("NOTE MP4 encoder: the media stack is up for the encoder-free probe.");
            hr = MFCreateSinkWriterFromURL(path, IntPtr.Zero, null, out writer);
            if (hr < 0) return hr;
            step?.Invoke("NOTE MP4 encoder: the AVI sink writer is open.");
            if (MFCreateMediaType(out var target) != S_OK) return MF_E_OUT_OF_MEMORY;
            target.SetGUIDKey(MajorType, VideoMajorType);
            target.SetGUIDKey(SubType, Rgb32); // the uncompressed 32-bit type the AVI container takes as it is
            target.SetUINT64Key(FrameSize, Pack(height, width));
            target.SetUINT64Key(FrameRateKey, Pack(frameRate, 1));
            target.SetUINT64Key(PixelAspectRatio, Pack(1, 1));
            target.SetUINT32Key(InterlaceMode, InterlaceProgressive);
            hr = writer!.AddStream(target, out var index);
            if (hr < 0) return hr;
            if (MFCreateMediaType(out var input) != S_OK) return MF_E_OUT_OF_MEMORY;
            input.SetGUIDKey(MajorType, VideoMajorType);
            input.SetGUIDKey(SubType, Rgb32);
            input.SetUINT64Key(FrameSize, Pack(height, width));
            input.SetUINT64Key(FrameRateKey, Pack(frameRate, 1));
            input.SetUINT64Key(PixelAspectRatio, Pack(1, 1));
            hr = writer.SetInputMediaType(index, input, null);
            if (hr < 0) return hr;
            step?.Invoke("NOTE MP4 encoder: the AVI sink writer took the uncompressed stream as it is.");
            hr = writer.BeginWriting();
            if (hr < 0) return hr;
            step?.Invoke("NOTE MP4 encoder: the AVI writer began writing.");
            for (var frame = 0; frame < frames; frame++)
            {
                var (time, duration) = Mp4Recorder.FrameTime(frame, frameRate);
                hr = Mp4Recorder.WriteSampleTo(writer, index, pixels, pixels.Length, time, duration, step);
                if (hr < 0) return hr;
            }
            step?.Invoke($"NOTE MP4 encoder: {frames} uncompressed pictures went in; closing the probe file.");
            return writer.FinalizeFile();
        }
        catch { return MF_E_OUT_OF_MEMORY; }
        finally
        {
            if (writer is not null) { try { Marshal.ReleaseComObject(writer); } catch { } }
        }
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
