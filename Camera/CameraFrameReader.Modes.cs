using System.Runtime.InteropServices;

namespace PianoPath;

// The half of the reader that asks a camera which modes it has and switches it to the one the overlay wants.
internal sealed partial class CameraFrameReader
{
    /// <summary>The device sends no more types than this many; a driver that never stops listing them is cut off.</summary>
    private const int MaxNativeModes = 512;

    /// <summary>A camera mode together with the Media Foundation type that selects it.</summary>
    private sealed record NativeMode(CameraMode Mode, Mf.IMFMediaType Type);

    /// <summary>
    /// Switches the camera to the mode <see cref="CameraModes.Choose"/> prefers among the ones it reports, and returns
    /// that mode. Returns null — leaving the camera in its own default mode, with <see cref="ModeNote"/> saying why —
    /// when the camera lists no modes or will not switch. Nothing here may fail the open: a camera that works in its
    /// default mode is still a camera.
    /// </summary>
    private CameraMode? ChooseCameraMode()
    {
        var native = new List<NativeMode>();
        try
        {
            ReadNativeModes(native);
            if (native.Count == 0)
            {
                ModeNote = Loc.T("The camera reported no video modes, so its default mode is used.");
                return null;
            }
            var index = CameraModes.Choose(native.Select(entry => entry.Mode).ToList());
            if (index < 0)
            {
                ModeNote = Loc.T("The camera reported no video modes, so its default mode is used.");
                return null;
            }
            var wanted = native[index];
            var hr = _reader!.SetCurrentMediaType(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, wanted.Type);
            if (hr != Mf.S_OK)
            {
                ModeNote = Loc.F("The camera would not switch to {0} ({1}), so its default mode is used.", wanted.Mode.Label, Mf.Describe(hr));
                return null;
            }
            return wanted.Mode;
        }
        catch (Exception ex)
        {
            ModeNote = Loc.F("The camera would not switch to the mode that was chosen ({0}), so its default mode is used.", ex.Message);
            return null;
        }
        finally { foreach (var entry in native) Release(entry.Type); }
    }

    /// <summary>Switches the camera to the first mode it lists — the one it would use by itself. False when it will not.</summary>
    private bool SelectFirstNativeMode()
    {
        try
        {
            if (_reader!.GetNativeMediaType(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out var pointer) != Mf.S_OK || pointer == IntPtr.Zero) return false;
            Mf.IMFMediaType? type = null;
            try
            {
                type = Marshal.GetObjectForIUnknown(pointer) as Mf.IMFMediaType;
                return type is not null && _reader.SetCurrentMediaType(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, type) == Mf.S_OK;
            }
            finally { Marshal.Release(pointer); Release(type); }
        }
        catch { return false; }
    }

    /// <summary>Lists the video modes the camera reports, in the order it reports them, skipping any type that is not video.</summary>
    private void ReadNativeModes(List<NativeMode> into)
    {
        for (var typeIndex = 0; typeIndex < MaxNativeModes; typeIndex++)
        {
            var hr = _reader!.GetNativeMediaType(Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, typeIndex, out var pointer);
            if (hr != Mf.S_OK || pointer == IntPtr.Zero) return;   // MF_E_NO_MORE_TYPES ends the list
            Mf.IMFMediaType? type = null;
            try { type = Marshal.GetObjectForIUnknown(pointer) as Mf.IMFMediaType; }
            catch { type = null; }
            finally { Marshal.Release(pointer); }
            if (type is null) continue;

            var keep = false;
            try
            {
                if (type.GetGUIDKey(Mf.MajorType, out var major) != Mf.S_OK || major != Mf.VideoMajorType) continue;
                if (type.GetUINT64Key(Mf.FrameSize, out var packedSize) != Mf.S_OK) continue;
                var (width, height) = Mf.Split(packedSize);
                var (numerator, denominator) = type.GetUINT64Key(Mf.FrameRateKey, out var packedRate) == Mf.S_OK ? Mf.Split(packedRate) : (0, 0);
                var format = type.GetGUIDKey(Mf.SubType, out var subtype) == Mf.S_OK ? CameraModes.FormatName(subtype) : "";
                into.Add(new NativeMode(new CameraMode(width, height, numerator, denominator, format), type));
                keep = true;
            }
            finally { if (!keep) Release(type); }
        }
    }
}

internal static partial class Mf
{
    /// <summary>Reads a GUID attribute from a key constant.</summary>
    internal static int GetGUIDKey(this object attributes, Guid key, out Guid value)
    {
        var local = key;
        return ((IMFAttributes)attributes).GetGUID(ref local, out value);
    }
}
