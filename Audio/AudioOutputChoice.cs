namespace PianoPath;

/// <summary>The audio backends the settings page can ask for, from "decide for me" to a named driver.</summary>
internal enum AudioBackendKind { Auto, WasapiShared, WasapiExclusive, Asio, WaveOut }

/// <summary>
/// The user's output choice as one stable id string. The Windows endpoint id is kept verbatim, so virtual
/// devices such as Voicemeeter VAIO/AUX/VAIO3 survive refreshes and language changes without relying on a
/// friendly name that Windows allows the user or driver to change.
/// </summary>
internal readonly record struct AudioOutputChoice(AudioBackendKind Kind, string AsioDriver, string EndpointId = "")
{
    internal const string AutoId = "auto";
    internal const string WasapiSharedId = "wasapi";
    internal const string WasapiExclusiveId = "wasapi-exclusive";
    internal const string WaveOutId = "waveout";
    internal const string WasapiEndpointPrefix = "wasapi-endpoint:";
    internal const string WasapiExclusiveEndpointPrefix = "wasapi-exclusive-endpoint:";
    private const string AsioPrefix = "asio:";

    internal static AudioOutputChoice Auto => new(AudioBackendKind.Auto, "");
    internal static AudioOutputChoice ForAsio(string driverName) => new(AudioBackendKind.Asio, driverName);
    internal static AudioOutputChoice ForWasapiEndpoint(string endpointId, bool exclusive = false) =>
        new(exclusive ? AudioBackendKind.WasapiExclusive : AudioBackendKind.WasapiShared, "", endpointId);

    internal string Id => Kind switch
    {
        AudioBackendKind.WasapiShared when EndpointId.Length > 0 => WasapiEndpointPrefix + EndpointId,
        AudioBackendKind.WasapiExclusive when EndpointId.Length > 0 => WasapiExclusiveEndpointPrefix + EndpointId,
        AudioBackendKind.WasapiShared => WasapiSharedId,
        AudioBackendKind.WasapiExclusive => WasapiExclusiveId,
        AudioBackendKind.Asio => AsioPrefix + AsioDriver,
        AudioBackendKind.WaveOut => WaveOutId,
        _ => AutoId,
    };

    /// <summary>Anything unknown parses as Auto, so an edited settings file can never leave the app silent.</summary>
    internal static AudioOutputChoice Parse(string? id)
    {
        if (id == WasapiSharedId) return new(AudioBackendKind.WasapiShared, "");
        if (id == WasapiExclusiveId) return new(AudioBackendKind.WasapiExclusive, "");
        if (id == WaveOutId) return new(AudioBackendKind.WaveOut, "");
        if (id is { } shared && shared.StartsWith(WasapiEndpointPrefix, StringComparison.Ordinal) && shared.Length > WasapiEndpointPrefix.Length)
            return ForWasapiEndpoint(shared[WasapiEndpointPrefix.Length..]);
        if (id is { } exclusive && exclusive.StartsWith(WasapiExclusiveEndpointPrefix, StringComparison.Ordinal) && exclusive.Length > WasapiExclusiveEndpointPrefix.Length)
            return ForWasapiEndpoint(exclusive[WasapiExclusiveEndpointPrefix.Length..], exclusive: true);
        if (id is { } asio && asio.StartsWith(AsioPrefix, StringComparison.Ordinal) && asio.Length > AsioPrefix.Length)
            return ForAsio(asio[AsioPrefix.Length..]);
        return Auto;
    }
}
