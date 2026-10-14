namespace PianoPath;

/// <summary>
/// Fills <paramref name="interleaved"/> with <paramref name="frames"/> stereo frames of float samples in
/// [-1, 1]. Called on the sink's audio thread (or the ASIO driver's thread), so it must not allocate, block
/// on the UI or throw.
/// </summary>
internal delegate void AudioRenderCallback(float[] interleaved, int frames);

/// <summary>
/// One way out of the machine for the synthesiser's stereo float stream. The engine renders at
/// <see cref="PianoAudioEngine.SampleRate"/> on every backend — WASAPI shared lets Windows resample to the
/// device's mix rate, the exclusive and ASIO paths open the device at that rate directly — so the recording
/// pipeline (<see cref="WavWriter"/>, <see cref="Mp4Recorder"/>) never has to care which backend is live.
///
/// <para>
/// A sink either comes out of <see cref="AudioSinkFactory.TryCreate"/> running, or it does not exist: there
/// is no half-open state to poll. Construction failures are returned as a localized message, because "why is
/// the piano silent" must be answerable from the settings page without a debugger.
/// </para>
/// </summary>
internal interface IAudioSink : IDisposable
{
    /// <summary>Human-readable description of the live path, e.g. <c>WASAPI shared · 10.7 ms</c>.</summary>
    string Description { get; }

    /// <summary>Total device-side buffering in milliseconds — the floor of what the player can feel.</summary>
    double BufferMilliseconds { get; }

    /// <summary>
    /// Why the stream stopped after it had already started (device unplugged, driver reset), in words the
    /// settings page can print; null while the stream is running. Opening failures are not reported here —
    /// a sink that could not open does not exist at all (see <see cref="AudioSinkFactory"/>).
    /// </summary>
    string? StreamFailure { get; }
}

/// <summary>
/// Opens the backend the user asked for, falling back along <c>WASAPI shared → waveOut</c> when the request
/// was <see cref="AudioBackendKind.Auto"/> or when the explicit request failed. The factory never throws:
/// a machine with no audio device at all returns <c>null</c> plus the first failure's message, and the
/// engine keeps synthesizing silently exactly as it always has.
/// </summary>
internal static class AudioSinkFactory
{
    /// <summary>
    /// <paramref name="latencyMilliseconds"/> is the target buffer period for WASAPI and ASIO (clamped to
    /// sizes the driver/device supports); waveOut keeps its fixed compatibility queue. A WASAPI choice may
    /// also name one Windows render endpoint, including a Voicemeeter virtual input.
    /// <paramref name="failure"/> carries the explicit request's error even when a fallback then succeeded,
    /// so the settings page can say "ASIO failed, using WASAPI" instead of silently ignoring the choice.
    /// </summary>
    internal static IAudioSink? TryCreate(AudioOutputChoice choice, double latencyMilliseconds, AudioRenderCallback render, IntPtr asioSystemHandle, out string? failure)
    {
        failure = null;
        switch (choice.Kind)
        {
            case AudioBackendKind.WasapiShared:
                return WasapiAudioSink.TryCreate(exclusive: false, latencyMilliseconds, render, out failure, endpointId: choice.EndpointId)
                    ?? Fallback(latencyMilliseconds, render, ref failure);
            case AudioBackendKind.WasapiExclusive:
            {
                var exclusive = WasapiAudioSink.TryCreate(exclusive: true, latencyMilliseconds, render, out failure, endpointId: choice.EndpointId);
                if (exclusive is not null) return exclusive;
                // An endpoint can allow shared mode while refusing exclusive mode. Preserve the selected
                // Voicemeeter/physical output before falling back to Windows' default device.
                if (choice.EndpointId.Length > 0)
                {
                    var shared = WasapiAudioSink.TryCreate(exclusive: false, latencyMilliseconds, render, out _, endpointId: choice.EndpointId);
                    if (shared is not null) return shared;
                }
                return Fallback(latencyMilliseconds, render, ref failure);
            }
            case AudioBackendKind.Asio:
                return AsioAudioSink.TryCreate(choice.AsioDriver, latencyMilliseconds, asioSystemHandle, render, out failure)
                    ?? Fallback(latencyMilliseconds, render, ref failure);
            case AudioBackendKind.WaveOut:
                return (IAudioSink?)WaveOutAudioSink.TryCreate(render, out failure) ?? Fallback(latencyMilliseconds, render, ref failure);
            default:
                // Auto promises no particular backend, so reaching the speakers at all clears the failure;
                // only a machine where every path refused keeps a message (the WASAPI one, which names the
                // real condition — "no device" — rather than waveOut's error code for the same situation).
                var sink = WasapiAudioSink.TryCreate(exclusive: false, latencyMilliseconds, render, out failure);
                if (sink is not null) { failure = null; return sink; }
                if (WaveOutAudioSink.TryCreate(render, out _) is { } waveOut) { failure = null; return waveOut; }
                return null;
        }
    }

    /// <summary>The chain behind an explicit request that failed: the message of the request is kept.</summary>
    private static IAudioSink? Fallback(double latencyMilliseconds, AudioRenderCallback render, ref string? failure)
    {
        // The cast picks the common interface for ??: the two sinks share IAudioSink and nothing else.
        var sink = (IAudioSink?)WasapiAudioSink.TryCreate(exclusive: false, latencyMilliseconds, render, out var wasapiFailure)
            ?? WaveOutAudioSink.TryCreate(render, out _);
        failure ??= wasapiFailure;
        return sink;
    }
}
