namespace PianoPath;

/// <summary>
/// One contract for the short-lived effects both renderers draw.
///
/// <para>
/// The two renderers spend very different amounts of time per effect — a WPF spark is a drawing, a GPU
/// particle is one more point in a single instanced draw — so their ceilings stay different on purpose:
/// a ceiling is a frame-time budget, not a look. What <em>must</em> agree is the visible behaviour. A
/// flash lives exactly as long on either engine, and both drop the oldest effect when their own ceiling
/// is reached, so a preset cannot quietly flash 1.4–2.1× longer or keep every other ring on one engine.
/// </para>
///
/// <para>
/// Read the constants as two lists: <see cref="FlareLifeSeconds"/> and
/// <see cref="StyledFlashLifeSeconds"/> are the look; the <c>*Cap</c> values are each renderer's own
/// frame-time ceiling. The software stage keeps lower ring and flash ceilings because each one is a
/// WPF drawing; at the 16-note chord the effect audit measures, both engines are under their flash and
/// wave ceilings and show the same flashes and waves. Particles keep their own ceilings on purpose.
/// </para>
/// </summary>
internal static class EffectBudget
{
    // ---------------------------------------------------------------------------------------------
    // The look: identical on both engines
    // ---------------------------------------------------------------------------------------------

    /// <summary>The default anamorphic impact flare: the white-hot flare at the hit point.</summary>
    internal const double FlareLifeSeconds = .38;

    /// <summary>The Lightning and Plasma impact flashes, which are sharper and shorter than the flare.</summary>
    internal const double StyledFlashLifeSeconds = .26;

    // ---------------------------------------------------------------------------------------------
    // The ceilings: one per renderer, each a frame-time budget
    // ---------------------------------------------------------------------------------------------

    /// <summary>How many impact waves the software stage keeps; the GPU stage is instanced and has its own.</summary>
    internal const int WpfRingCap = 64;

    /// <summary>How many impact flashes the software stage keeps.</summary>
    internal const int WpfFlashCap = 32;

    /// <summary>
    /// The software stage's particle ceiling. Each spark is a WPF drawing (an ellipse or a short stroke),
    /// so this is a frame-time budget, not a design choice; the 9.2× gap to the GPU ceiling is intentional.
    /// </summary>
    internal const int WpfParticleCap = 2600;

    /// <summary>How many impact waves the GPU stage keeps.</summary>
    internal const int GpuRingCap = 96;

    /// <summary>How many impact flashes the GPU stage keeps.</summary>
    internal const int GpuFlashCap = 48;

    /// <summary>The GPU stage's ceiling: all sparks are one instanced draw, so the ceiling is memory-bound.</summary>
    internal const int GpuParticleCap = 24000;
}
