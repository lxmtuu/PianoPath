namespace PianoPath;

/// <summary>Review-only dot/streak material tags. Does not own physics, colour, dimensions or opacity.</summary>
internal static class SparkParticleAppearance
{
    internal const float StreakKind = 18, DotKind = 19;
    internal static float StageKind(bool candidate, int shape, bool wisp, float legacyKind)
        => candidate && !wisp && shape is 0 or 1 ? (shape == 1 ? StreakKind : DotKind) : legacyKind;
    internal static float ArKind(bool candidate, int shape, bool wisp)
        => StageKind(candidate, shape, wisp, 2);
}
