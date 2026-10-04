using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>One active Windows render endpoint, with a stable MMDevice id and its user-facing name.</summary>
internal sealed record AudioOutputEndpoint(string Id, string FriendlyName, bool IsDefault);

/// <summary>
/// Enumerates the active Windows playback endpoints. Unlike the old default-device-only path, this exposes
/// every virtual render target Windows knows about, including Voicemeeter VAIO, AUX and VAIO3 inputs.
/// </summary>
internal static class WasapiDeviceCatalog
{
    private const int DataFlowRender = 0;
    private const int RoleConsole = 0;
    private const int DeviceStateActive = 0x00000001;
    private const int StgmRead = 0;
    private const ushort VariantLpwstr = 31;
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    internal static IReadOnlyList<AudioOutputEndpoint> GetActiveRenderEndpoints()
    {
        object? enumeratorObject = null;
        IMMDeviceCollection? collection = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)(enumeratorObject = new MMDeviceEnumeratorComObject());
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleConsole, out var defaultDevice) == 0 && defaultDevice is not null)
            {
                defaultId = ReadId(defaultDevice);
                Release(defaultDevice);
            }

            if (enumerator.EnumAudioEndpoints(DataFlowRender, DeviceStateActive, out collection) != 0 || collection is null)
                return [];
            if (collection.GetCount(out var count) != 0) return [];

            var endpoints = new List<AudioOutputEndpoint>((int)Math.Min(count, 256));
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(index, out device) != 0 || device is null) continue;
                    var id = ReadId(device);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var name = ReadFriendlyName(device);
                    if (string.IsNullOrWhiteSpace(name)) name = id;
                    endpoints.Add(new AudioOutputEndpoint(id, name, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                catch { /* a stale endpoint should not hide the rest of the list */ }
                finally { if (device is not null) Release(device); }
            }

            return endpoints
                .DistinctBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(endpoint => endpoint.IsDefault ? 0 : 1)
                .ThenBy(endpoint => endpoint.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch { return []; } // a headless machine or a broken audio service is an empty picker, not a startup failure
        finally
        {
            if (collection is not null) Release(collection);
            if (enumeratorObject is not null) Release(enumeratorObject);
        }
    }

    private static string? ReadId(IMMDevice device)
    {
        if (device.GetId(out var pointer) != 0 || pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var properties) != 0 || properties is null) return null;
        try
        {
            var key = FriendlyNameKey;
            if (properties.GetValue(ref key, out var value) != 0) return null;
            try { return value.Type == VariantLpwstr && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(properties); }
    }

    private static void Release(object value)
    {
        try { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        catch { }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection? devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-C0A3B018B7E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, [MarshalAs(UnmanagedType.Interface)] out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
        [PreserveSig] int OpenPropertyStore(int access, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    // PROPVARIANT's union starts at byte 8 on both Windows x86 and x64; reserve its full 24-byte x64 size.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}
