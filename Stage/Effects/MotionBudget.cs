namespace PianoPath;

/// <summary>
/// The small set of motion rules both renderers share, and the numbers the accessibility audit quotes.
///
/// <para>
/// The accessibility toggles already stand the intrinsic animation down (PianoVisualSettings applies them to
/// every family before the stage draws); what remains here is the one pulse
/// that is part of the note itself. The falling pulse is a sine whose angular rate is
/// <c>1.5 + 9 * rate</c> rad/s, so even the fastest setting is 1.67 Hz - well under the three-flashes-
/// per-second line, and the preset audit can prove that instead of trusting it.
/// </para>
/// </summary>
internal static class MotionBudget
{
    /// <summary>The falling pulse's angular rate in rad/s for a 0..1 rate; clamped so a preset cannot outrun it.</summary>
    internal static double PulseAngularRate(double rate) => 1.5 + 9 * Math.Clamp(rate, 0, 1);

    /// <summary>The same rate in hertz. The fastest setting stays below three.</summary>
    internal static double PulseHertz(double rate) => PulseAngularRate(rate) / (2 * Math.PI);
}
