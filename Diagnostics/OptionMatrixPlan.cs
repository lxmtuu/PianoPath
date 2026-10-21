namespace PianoPath;

/// <summary>
/// Pure, renderer-free plan for the N12(b) option matrix: one family per settings string that branches in
/// both renderers, every value the settings class accepts, one fixed scene and one sample time per family.
///
/// <para>
/// The audit in <c>docs/EFFECT-COVERAGE-AUDIT.md</c> could only say "chưa test riêng option X" for most of
/// these values: the suite exercised a family with one hand-picked style, so nothing in the repository proved
/// that choosing the other five options changed a pixel. This plan makes that provable without inventing an
/// art direction: the same scene, the same seed and the same input, with exactly one option different.
/// </para>
///
/// <para>
/// The values are the settings arrays themselves (pinned to <see cref="PianoVisualSettings"/> by
/// <c>tools/check_sources.py</c>), and <see cref="CapturedValues"/> derives what to capture, so a new option
/// cannot be added to the product and silently skipped by the review.
/// </para>
/// </summary>
internal static class OptionMatrixPlan
{
    /// <summary>What the stage is doing while the option is looked at.</summary>
    internal enum Scene
    {
        /// <summary>A held song note travelling above the hit line (note material and falling trail).</summary>
        Falling,
        /// <summary>No song: a single live hit at <see cref="TriggerFrame"/> (wave, burst, morph, flash).</summary>
        Impact,
        /// <summary>No song: a live note pressed at frame zero and released at <see cref="ReleaseFrame"/>.</summary>
        Release,
        /// <summary>No song: a live note pressed at frame zero and still held at the capture (N12(e) bar width).</summary>
        Held,
        /// <summary>No song and no input: the ambient layers alone.</summary>
        Ambient
    }

    /// <summary>
    /// One settings axis: the property, the values the UI offers (the settings array, in order), and the value
    /// the baseline keeps. <see cref="Gate"/> is the visibility switch that turns the whole family off when no
    /// value means "off" (the burst and flash families always draw something).
    /// </summary>
    internal sealed record Family(string Id, string Setting, Scene Scene, IReadOnlyList<string> Values,
        string Neutral, string? Gate = null);

    /// <summary>
    /// 960×540, the same review size the rest of the harness uses for its 2×2-averaged captures: large enough
    /// that a shard, a spark or a ring is dozens of pixels, small enough that 128 PNGs stay a small artifact
    /// (the Phase 1 bundle is already 1+ GiB and this review must not ride inside it).
    /// </summary>
    internal const int Width = 960, Height = 540, Fps = 60, Seed = 20261021;
    /// <summary>Middle C: far enough from both ends that the hit is never clipped by the stage edge.</summary>
    internal const int Pitch = 60;
    /// <summary>The velocity of the sampled note/hit, as the renderers' own 0..1 strength.</summary>
    internal const double Strength = 96 / 127.0;
    /// <summary>Frame the single hit lands on; the capture is <see cref="Frames"/> - 1 - this many frames later.</summary>
    internal const int TriggerFrame = 1;
    /// <summary>Frame the live note is released on (release family only).</summary>
    internal const int ReleaseFrame = 8;

    internal static IReadOnlyList<Family> Families { get; } = Array.AsReadOnly(new[]
    {
        new Family("note-style", "NoteStyle", Scene.Falling,
            ["Solid", "Neon", "Glass", "Fire", "Gem", "CrystalOutline"], "Solid"),
        new Family("falling-trail", "FallingTrail", Scene.Falling,
            ["None", "Glow", "Sparkles", "Speed Lines", "Blur", "Ribbon", "Rainbow", "Stream"], "None"),
        new Family("impact-wave", "ImpactWave", Scene.Impact,
            ["None", "Ring", "Shockwave", "Ripple"], "None"),
        new Family("impact-burst", "ImpactBurst", Scene.Impact,
            ["Embers", "Splash", "Fireworks", "Confetti", "Dust"], "Embers", "ShowEmbers"),
        new Family("impact-morph", "ImpactMorph", Scene.Impact,
            ["None", "Shatter", "Melt", "Absorb", "Bounce", "Morph"], "None"),
        new Family("impact-flash", "ImpactFlashStyle", Scene.Impact,
            ["Flash", "Lightning", "Plasma"], "Flash", "ShowImpactFlash"),
        new Family("release", "ReleaseEffect", Scene.Release,
            ["Fade", "Float Up", "Dissolve", "Smoke", "Snap Back", "Echo Rings"], "Fade"),
        new Family("ambient-energy", "AmbientEnergy", Scene.Ambient,
            ["None", "Lightning Storm", "Laser Beams", "Confetti Rain", "Fireworks"], "None"),
        new Family("ambient-nature", "AmbientNature", Scene.Ambient,
            ["None", "Rain", "Snow", "Smoke", "Leaves", "Butterflies", "Dust", "Aurora"], "None"),
        new Family("ambient-light", "AmbientLight", Scene.Ambient,
            ["None", "Gradient Wave", "Prism", "Color Splash", "Spotlights"], "None"),
        new Family("ambient-cosmic", "AmbientCosmic", Scene.Ambient,
            ["None", "Galaxy", "Black Hole", "Matrix Rain", "Geometric", "Fractal"], "None")
    });

    /// <summary>
    /// N12(d) — the AR/pose slice. The flat matrix proves that each value draws in the stage renderer; it
    /// cannot prove the option survives the calibrated projection, where rings become quads, flashes become
    /// billboards and particles are re-aimed. These five axes are the ones whose art is drawn into the camera
    /// image (wave, burst, the two morph channels and the release ring), and each is shot at the three
    /// calibrated poses with its own off state. The values are product values: <see cref="PoseFamily"/>
    /// resolves them through <see cref="Families"/>, so a rename fails loudly instead of silently rendering
    /// the neutral twice.
    /// </summary>
    internal static IReadOnlyList<(string Family, string Value)> PoseSelections { get; } = Array.AsReadOnly(new[]
    {
        ("impact-wave", "Shockwave"),
        ("impact-burst", "Fireworks"),
        ("impact-morph", "Shatter"),   // particle channel
        ("impact-morph", "Morph"),     // flash channel
        ("release", "Echo Rings")       // ring channel
    });

    /// <summary>
    /// N12(d), part 2 — the rest of what the overlay draws and part 1 does not touch: the two song-note axes, whose
    /// art is built as projected patches rather than screen sprites, and the four ambient layers, which are emitted
    /// in screen space over the camera picture. One row per family, so a family cannot appear in both parts.
    ///
    /// <para>
    /// The note rows are shot at the family's own <see cref="Scene.Falling"/> scene, and the reason they are inside a
    /// picture calibrated around the keyboard is worth writing down, because the naive reading of the projection says
    /// otherwise. <see cref="Ar3DNoteProjection"/> clamps the leading edge at the key plane
    /// (<c>head = max(0, (start − position) × fall)</c>) and takes the tail as
    /// <c>max((start − position) × fall + duration × fall − gap, head)</c>: at the Falling capture the five-second-old
    /// note has already begun to sound, so its visible body is the part still above the keys — <b>0</b> to
    /// <b>0.735</b> key-depths behind the key plane, with its trail running from 0.735 to about <b>1.22</b>
    /// (<c>tail + body × FallingTrailLength + 0.08</c>, clipped by the style's reach of 4). All of that is above the
    /// keyboard's back edge, which is where the fixture's backdrop is.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<(string Family, string Value)> PoseSliceSelections { get; } = Array.AsReadOnly(new[]
    {
        ("note-style", "Gem"),              // projected note material
        ("falling-trail", "Ribbon"),        // projected note trail
        ("ambient-energy", "Laser Beams"),
        ("ambient-nature", "Aurora"),
        ("ambient-light", "Color Splash"),
        ("ambient-cosmic", "Galaxy")
    });

    /// <summary>
    /// Where the Falling scene's note body ends up at the capture, in key-depths behind the key plane. Its trail
    /// runs from <see cref="FallingSceneNoteTailDepth"/> to about 1.22 with the shipped trail length of 55 %
    /// (<c>tail + max(.08, body × .55 + .08)</c>, clipped by the style's reach). Pinned by
    /// <c>OptionMatrixPlanTests</c> against <see cref="ArNoteStyle.Default"/>, so a slower fall or a longer note
    /// fails on Linux instead of quietly sliding the note out of the calibrated picture on the runner.
    /// </summary>
    internal const double FallingSceneNoteHeadDepth = 0;
    internal const double FallingSceneNoteTailDepth = 0.735;

    internal static Family FamilyById(string id) => Families.SingleOrDefault(family => family.Id == id)
        ?? throw new ArgumentException($"The option plan names unknown family '{id}'.", nameof(id));

    internal static Family PoseFamily(string id) => FamilyById(id);

    /// <summary>
    /// Frames each scene runs before the capture. A family's sample time is the last frame's time, so one
    /// number per scene keeps the reviewed moment and the rendered frames from drifting apart.
    /// </summary>
    internal static int Frames(Family family) => Frames(family.Scene);

    /// <summary>
    /// Frames each scene runs before the capture, per scene. A family's sample time is the last frame's time,
    /// so one number keeps the reviewed moment and the rendered frames from drifting apart.
    /// </summary>
    internal static int Frames(Scene scene) => scene switch
    {
        // Long enough for a note to travel a third of the stage and for the ambient layers to populate.
        Scene.Falling or Scene.Ambient => 90,
        // The ring, burst, morph and flash all live 0.26-0.55 s; 0.117 s after the hit shows every one of them.
        Scene.Impact => 8,
        // 0.267 s in, 0.133 s after the release: inside every release lifetime (0.25-1.3 s).
        Scene.Release => 16,
        // Half a second of a held key: the bar is fully grown and, on the software stage, still being drawn by
        // the path that used to floor its width.
        Scene.Held => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(scene))
    };

    internal static double Seconds(Family family) => Seconds(family.Scene);
    internal static double Seconds(Scene scene) => Frames(scene) / (double)Fps;

    /// <summary>The song the scene plays; the release and held scenes use a live note instead, so they have none.</summary>
    internal static IReadOnlyList<NoteEvent> Notes(Family family) => Notes(family.Scene);

    internal static IReadOnlyList<NoteEvent> Notes(Scene scene) => scene == Scene.Falling
        ? Array.AsReadOnly(new[] { new NoteEvent { Pitch = Pitch, Start = 0, Duration = 2, Velocity = 96, Track = 0 } })
        : Array.Empty<NoteEvent>();

    internal static bool Playing(Family family) => Playing(family.Scene);
    internal static bool Playing(Scene scene) => scene == Scene.Falling;

    /// <summary>
    /// The values this family is captured with. A family whose neutral is a real value ("None", "Fade",
    /// "Solid") does not capture that value twice: the baseline <em>is</em> that value. A gated family has no
    /// "off" value, so its baseline turns the gate off and every value is captured with the gate on.
    /// </summary>
    internal static IReadOnlyList<string> CapturedValues(Family family) => family.Gate is null
        ? family.Values.Where(value => value != family.Neutral).ToArray()
        : family.Values;

    /// <summary>True when the value is the family's own off state.</summary>
    internal static bool IsNeutral(Family family, string value) => family.Gate is null && value == family.Neutral;

    internal static int SceneCount => Families.Sum(family => CapturedValues(family).Count + 1);

    /// <summary>A file-name-safe spelling of a settings value ("Speed Lines" → "speed-lines").</summary>
    internal static string Slug(string value)
    {
        var slug = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-');
    }

    /// <summary>
    /// Pixels that differ between two BGRA frames in a colour channel by more than <paramref name="tolerance"/>.
    /// Alpha is ignored: both renderers write an opaque stage, and a background that lands on 0 instead of 255
    /// in one renderer is not an option difference.
    /// </summary>
    internal static int ChangedPixels(byte[] left, byte[] right, int tolerance = 2)
    {
        if (left.Length != right.Length || left.Length % 4 != 0)
            throw new ArgumentException("ChangedPixels needs two equally sized BGRA frames.", nameof(right));
        var changed = 0;
        for (var i = 0; i < left.Length; i += 4)
        {
            if (Math.Abs(left[i] - right[i]) > tolerance
                || Math.Abs(left[i + 1] - right[i + 1]) > tolerance
                || Math.Abs(left[i + 2] - right[i + 2]) > tolerance) changed++;
        }
        return changed;
    }

    /// <summary>Pixels that are not the pure black of an undrawn stage; a blank baseline would make the diff meaningless.</summary>
    internal static int NonBlackPixels(byte[] pixels)
    {
        var lit = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] > 2 || pixels[i + 1] > 2 || pixels[i + 2] > 2) lit++;
        return lit;
    }
}
