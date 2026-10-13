namespace PianoPath;

/// <summary>
/// The alignment-check overlay (roadmap §14.7 "Thêm 1"): redraws the saved key area as thin guide lines right over
/// the live camera picture, so a bumped tripod or a moved keyboard shows up immediately — the lines no longer sit on
/// the real keys. The painter packs them after every note and effect so they stay readable on any look, on any
/// quality tier, and they appear only while a calibration exists: the lines <em>are</em> the calibration's own key
/// area, projected through the same <see cref="ArProjection"/> the keys use, so a match means the calibration still
/// matches the camera.
///
/// <para>
/// All geometry is keyboard-plane space: the four borders hug the saved quad (<c>u</c> 0..1 across the key row,
/// <c>v</c> 0..1 down the key depth) and one line stands at every octave boundary that has keys on both sides, so a
/// drift also reads as a wrong octave count. The set is pure arithmetic, checked by the xUnit project on every
/// machine; the GPU draws each line as a key-tint quad (<c>Misc.z = 1</c> in <c>PsArQuad</c>) so no new shader or
/// colour knob is needed, and the white lines read over both dark camera video and the bright key tint below.
/// </para>
/// </summary>
internal static class ArAlignmentGuides
{
    /// <summary>Back and front border thickness along the key depth, in v units (1 == the full key depth).</summary>
    internal const double BorderThicknessV = .018;

    /// <summary>Left, right and octave guide thickness across the key row, in u units (1 == the full key row).</summary>
    internal const double GuideThicknessU = .0038;

    /// <summary>The saved key area's own frame is the strong set: it must be visible at a glance.</summary>
    internal const float BorderAlpha = .5f;

    /// <summary>Octave guides only help counting and stay quiet so the picture remains usable while playing.</summary>
    internal const float OctaveAlpha = .22f;

    internal readonly record struct GuidePatch(ArPatch Patch, float Alpha);

    /// <summary>High-contrast guide colours the player may pick: each must read over both dark video and bright keys.</summary>
    internal static readonly string[] ColorIds = ["White", "Cyan", "Yellow", "Magenta", "Lime"];

    internal const string DefaultColorId = "White";

    /// <summary>The RGB of a guide colour id in off-white HDR space; anything unknown keeps the default white.</summary>
    internal static (float R, float G, float B) ColorFor(string? id) => id switch
    {
        "Cyan" => (.35f, .95f, 1f),
        "Yellow" => (1f, .92f, .35f),
        "Magenta" => (1f, .4f, .95f),
        "Lime" => (.55f, 1f, .45f),
        _ => (.92f, .98f, 1f),
    };

    /// <summary>
    /// Projects the fixed guide set — the key area's four borders first, then one line at every octave boundary with
    /// keys on both sides — into <paramref name="into"/>. Without a usable projection (uncalibrated camera) the list
    /// simply stays empty, so the toggle is safe in any camera state.
    /// </summary>
    internal static void Build(ArCalibration calibration, ArView view, List<GuidePatch> into)
    {
        into.Clear();
        Add(calibration, view, into, 0, 1, 0, BorderThicknessV, BorderAlpha);
        Add(calibration, view, into, 0, 1, 1 - BorderThicknessV, 1, BorderAlpha);
        Add(calibration, view, into, 0, GuideThicknessU, 0, 1, BorderAlpha);
        Add(calibration, view, into, 1 - GuideThicknessU, 1, 0, 1, BorderAlpha);

        // Octaves begin at every C: pitch % 12 == 0 names all C keys, so any keyboard size gets the same guides.
        var layout = calibration.Layout;
        for (var pitch = layout.FirstPitch + 1; pitch <= layout.LastPitch; pitch++)
        {
            if (pitch % 12 != 0 || !layout.Contains(pitch - 1)) continue;
            var (left, _) = layout.Span(pitch);
            var half = GuideThicknessU * .5;
            Add(calibration, view, into, left - half, left + half, 0, 1, OctaveAlpha);
        }
    }

    private static void Add(ArCalibration calibration, ArView view, List<GuidePatch> into,
        double u0, double u1, double v0, double v1, float alpha)
    {
        if (ArProjection.TryProject(calibration, view, u0, u1, v0, v1, out var patch, out _, out _))
            into.Add(new GuidePatch(patch, alpha));
    }
}
