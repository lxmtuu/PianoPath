using System.Runtime.InteropServices;
using System.Text;

namespace PianoPath;

/// <summary>
/// The small part of Media Foundation this application needs: enumerate the cameras, open one (or a video
/// file) as a source reader, and read frames as top-down 32-bit BGRA.
///
/// <para>
/// The COM surface is declared by hand, in vtable order, exactly as the headers order it — the attribute
/// methods come first because <c>IMFMediaType</c>, <c>IMFSample</c> and <c>IMFActivate</c> all inherit
/// <c>IMFAttributes</c>, and a wrong order would corrupt the stack at the first call. Only the methods the
/// reader actually calls have real signatures; the ones before them exist to keep the order right.
/// </para>
///
/// <para>
/// Nothing here is called unless the overlay is switched on, and every entry point reports its failure as an
/// <c>HRESULT</c> the managed side turns into a sentence, so a machine with no camera (or no media stack)
/// simply says so instead of failing.
/// </para>
/// </summary>
internal static class Mf
{
    // ---- HRESULTs and reader flags -------------------------------------------------------------------
    internal const int S_OK = 0;
    internal const int MF_E_END_OF_STREAM = unchecked((int)0xC00D3E84);
    internal const int MF_SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
    internal const int MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = 0x00000001;
    /// <summary>The sample flag set at the end of a stream.</summary>
    internal const int MF_SOURCE_READERF_ENDOFSTREAM = 0x00000002;
    internal const int MF_SOURCE_READERF_STREAMTICK = 0x00000100;

    // ---- attribute keys -----------------------------------------------------------------------------
    internal static readonly Guid MajorType = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    internal static readonly Guid SubType = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    internal static readonly Guid FrameSize = new("1652C33D-D6B2-4012-B834-72030849A37D");
    internal static readonly Guid DefaultStride = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    /// <summary>Source-reader attribute that lets the reader insert the converter to RGB32.</summary>
    internal static readonly Guid ReaderAdvancedKey = new("A634A91C-822B-41B9-A494-4DE4643612B0");

    internal static readonly Guid VideoMajorType = new("73646976-0000-0010-8000-00AA00389B71"); // 'vids'
    internal static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00AA00389B71");            // BGRA in memory

    // ---- device attributes --------------------------------------------------------------------------
    internal static readonly Guid DeviceSourceType = new("C60AC5FE-252A-478F-A0EF-BC8FA5F7CAD3");
    internal static readonly Guid VideoCapture = new("8AC3587A-4AE7-42D8-99E0-0A6013EEF90F");
    internal static readonly Guid FriendlyName = new("60D0E559-52F8-4FA2-BBCE-ACDB8C3F34A3");
    internal static readonly Guid SymbolicLink = new("58F0AAD8-22BF-4F8A-BB3D-D2C4978C6E2F");

    // ---- platform -----------------------------------------------------------------------------------
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr activates, out int count);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateDeviceSource([MarshalAs(UnmanagedType.Interface)] IMFActivate activate, out IntPtr source);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    internal static extern int MFCreateSourceReaderFromMediaSource(IntPtr source, IMFAttributes? attributes, out IMFSourceReader reader);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes? attributes, out IMFSourceReader reader);

    /// <summary>Media Foundation version 2.0, the only one Windows 7 and later ship.</summary>
    internal const int MF_VERSION = 0x00020070;

    // The interface methods below take their attribute keys by <c>ref</c>, and a readonly field cannot be
    // passed by reference, so these five helpers copy the key into a local first: one place gets that right
    // instead of every call site repeating it.
    /// <summary>Sets a UINT32 attribute from a key constant.</summary>
    internal static int SetUINT32Key(this IMFAttributes attributes, Guid key, int value)
    {
        var local = key;
        return attributes.SetUINT32(ref local, value);
    }

    /// <summary>Sets a GUID attribute (a media type or sub-type) from a key constant.</summary>
    internal static int SetGUIDKey(this IMFAttributes attributes, Guid key, Guid value)
    {
        var local = key;
        return attributes.SetGUID(ref local, ref value);
    }

    /// <summary>Reads a UINT32 attribute from a key constant.</summary>
    internal static int GetUINT32Key(this IMFAttributes attributes, Guid key, out int value)
    {
        var local = key;
        return attributes.GetUINT32(ref local, out value);
    }

    /// <summary>Reads a UINT64 attribute from a key constant.</summary>
    internal static int GetUINT64Key(this IMFAttributes attributes, Guid key, out long value)
    {
        var local = key;
        return attributes.GetUINT64(ref local, out value);
    }

    /// <summary>Reads a string attribute from a key constant, as the allocated wide string MF hands out.</summary>
    internal static int GetAllocatedStringKey(this IMFAttributes attributes, Guid key, out IntPtr value, out int length)
    {
        var local = key;
        return attributes.GetAllocatedString(ref local, out value, out length);
    }

    /// <summary>Reads a UINT64 attribute as its two halves: MF stores sizes as (high, low).</summary>
    internal static (int High, int Low) Split(long packed) => ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));

    /// <summary>An HRESULT as the hex code every Media Foundation document uses, for the status line.</summary>
    internal static string Describe(int hr) => hr == S_OK ? "0x00000000" : $"0x{hr:X8}";

    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size, out int length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, int size, out int blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType : IMFAttributes
    {
        [PreserveSig] int GetMajorType(out Guid major);
        [PreserveSig] int IsCompressedFormat(out int compressed);
        [PreserveSig] int IsEqual(IMFMediaType theirs, out int flags);
        [PreserveSig] int GetRepresentation(Guid representation, out IntPtr value);
        [PreserveSig] int FreeRepresentation(Guid representation, IntPtr value);
    }

    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int maxLength);
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample : IMFAttributes
    {
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        [PreserveSig] int RemoveBufferByIndex(int index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int length);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFActivate : IMFAttributes
    {
        [PreserveSig] int ActivateObject(ref Guid riid, out IntPtr obj);
        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    [ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int index, out int selected);
        [PreserveSig] int SetStreamSelection(int index, int selected);
        [PreserveSig] int GetNativeMediaType(int index, int typeIndex, out IntPtr type);
        [PreserveSig] int GetCurrentMediaType(int index, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(int index, IntPtr reserved, IMFMediaType type);
        /// <summary>Declared to keep the vtable order; the reader loops a file by re-opening it instead of seeking.</summary>
        [PreserveSig] int SetCurrentPosition(ref Guid format, IntPtr position);
        [PreserveSig] int ReadSample(int index, int flags, out int actualIndex, out int streamFlags, out long timestamp, out IMFSample sample);
        [PreserveSig] int Flush(int index);
        [PreserveSig] int GetServiceForStream(int index, ref Guid service, ref Guid riid, out IntPtr result);
        [PreserveSig] int GetPresentationAttribute(int index, ref Guid attribute, IntPtr value);
    }
}
