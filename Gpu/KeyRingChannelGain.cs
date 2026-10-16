namespace PianoPath;

/// <summary>Shared draw-time knob ownership for pitch-tagged simulated rings.
/// Release rings already captured ReleaseIntensity in Strength at birth: do not apply it a second time.</summary>
internal static class KeyRingChannelGain
{
    internal static float Resolve(byte channel, float waveIntensity, float morphIntensity)
    {
        var gain = channel switch { 0 => waveIntensity, 1 => morphIntensity, 2 => 1f, _ => 0f };
        return float.IsFinite(gain) && gain > 0 ? gain : 0;
    }
}
