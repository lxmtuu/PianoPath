namespace PianoPath;

/// <summary>
/// The two clocks a frame runs on, kept apart on purpose.
///
/// <para><c>PhysicsTimeFactor</c> is the slow-motion slider. On the GPU it scales the particles and
/// nothing else: <c>GpuStageSimulation.Step</c> computes <c>particleDt = frameDt × factor</c> and feeds it
/// to the particle integrator, while rings, flashes, live bars, key heat and the beat envelope all advance
/// on <c>frameDt</c>. The software stage used one scaled clock for everything, so at any factor but 100 it
/// drew live bars, impact flashes and the beat slower than the engine did — a slow-motion take looked like a
/// different animation rather than the same animation filmed slowly.</para>
///
/// <para>The particle clock is <c>frame × factor</c>, with the same arithmetic the GPU uses
/// (<c>frameDt * PhysicsTimeFactor</c>, where the look holds the factor as a fraction). At the default of
/// 100 that is <c>frame × 1.0</c>, which is bit-for-bit the frame clock — the older
/// <c>frame × 100 / 100</c> was not, because it rounded twice. The previews and the WARP evidence advance in
/// 1/30 s and 1/60 s steps, where both spellings agree exactly, so those frames stay byte-identical; the
/// Flames v3 and Wisps v3 gates re-render and compare them hash by hash, which is what proves it.</para>
/// </summary>
internal static class StageClock
{
    /// <summary>
    /// Seconds to hand the particle integrator: the frame clock scaled by the slow-motion setting, exactly
    /// as <c>GpuStageSimulation</c> computes its <c>particleDt</c>. Multiplying by the factor as a fraction
    /// rather than by the percentage and dividing after keeps the default (100) exact, because
    /// <c>x × 1.0 == x</c> in binary floating point.
    /// </summary>
    internal static double Particle(double frameSeconds, double physicsPercent) => frameSeconds * (physicsPercent / 100);
}
