using System.Numerics;
using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>One quad of the AR layer as the shader reads it; the field order is <c>ArQuadInstance</c> in Gpu/StageShaders.hlsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuArQuad
{
    /// <summary>x0, y0, x1, y1: back-left and back-right corners, output pixels.</summary>
    public Vector4 A;
    /// <summary>x2, y2, x3, y3: front-right and front-left corners, output pixels.</summary>
    public Vector4 B;
    /// <summary>The four corner weights, in the same order.</summary>
    public Vector4 W;
    /// <summary>Linear rgb and opacity.</summary>
    public Vector4 Color;
    /// <summary>x = fade-in 0..1, y = 1 while the note sounds, z = 1 for a lit key (0 for a note).</summary>
    public Vector4 Misc;
}

/// <summary>What the UI thread asks of the AR layer; replaced whole (see <see cref="GpuStageFeed.Ar"/>), null means none.</summary>
/// <param name="Calibration">Where the keyboard is in the camera picture.</param>
/// <param name="Notes">How notes fall over it.</param>
/// <param name="Opacity">0..1, how strongly the layer is drawn.</param>
/// <param name="KeyLight">0..1, how strongly a held key is tinted: the share of the camera picture that the key's colour replaces at full glow.</param>
/// <param name="ShowNotes">Draw the notes to come (Learn), not only the lit keys (Perform).</param>
/// <param name="DelaySeconds">How late the camera's picture is. The notes are drawn for the song's position this long ago; the render loop lights the keys as they were lit this long ago.</param>
/// <param name="Quality">Which elements of the AR catalogue this frame may draw (Lite/Standard/Ultra); Lite draws the flat quads the overlay has always drawn.</param>
/// <param name="ProjectedNotes">AR-S4b/AR-S4: the stage's own note pass draws the notes (and the sprite pass the particles) through the scene→image map (Gpu/StageShaders.hlsl <c>VsNote</c>, <c>VsSprite</c>) instead of this layer keeping flat note and live-bar quads of its own. Off by default: a frame that does not ask for the map draws exactly what it drew before AR-S4b.</param>
/// <param name="MinScalePx">The smallest magnification the projected note pass draws with, output pixels per scene DIP (the shader's <c>ArMatrixFlags.z</c>): a note that would collapse under a pixel is drawn at this floor instead. 0 draws every note at the map's own magnification.</param>
internal sealed record GpuArInput(ArCalibration Calibration, ArNoteStyle Notes, float Opacity, float KeyLight = .6f, bool ShowNotes = true, float DelaySeconds = 0, ArQualityTier Quality = ArQualityTier.Standard, bool ProjectedNotes = false, float MinScalePx = 0);

/// <summary>Turns the song's notes at this moment into quads for the AR pass.</summary>
internal sealed class GpuArLayer
{
    private readonly List<ArNoteShape> _shapes = [];
    private readonly List<ArLiveBar> _liveBars = [];
    private readonly List<ArPatch> _keyPatches = [];

    /// <summary>Key glow below this is not drawn.</summary>
    internal const float MinKeyGlow = .02f;

    /// <summary>The velocity band the style's graphs walk until the stage's own simulation feeds the layer: raw MIDI velocity mapped straight onto 0..1.</summary>
    private static readonly PianoSimulation VelocityBand = new();

    /// <summary>How many key quads the last <see cref="Build"/> produced.</summary>
    internal int KeyQuadCount { get; private set; }

    /// <summary>How many shapes the last <see cref="Build"/> produced (the notes before they were packed).</summary>
    internal int ShapeCount => _shapes.Count;

    /// <summary>
    /// Refills <paramref name="quads"/>: first the lit keys, then the notes. A key is lit by the colour and glow the stage
    /// simulation holds for it (<paramref name="keyColors"/>, indexed by pitch: linear rgb and glow 0..1), so the AR keys
    /// rise and fade exactly as the stage's own, for a held key of the song or of the player alike. Notes are drawn while the
    /// song plays, in the colours of the look; a note that has been played or missed takes the stage's played and missed
    /// colours. When <paramref name="projectedNotes"/> is set (AR-S4b) the note pass is already drawing the notes and the
    /// bars through the scene→image map, so this layer keeps only the lit keys and holds no flat copy of its own.
    /// </summary>
    /// <summary>How many bars the last <see cref="Build"/> produced, over and above the keys and the notes of the song.</summary>
    internal int LiveBarCount { get; private set; }

    internal void Build(GpuFrameInput input, GpuArInput ar, ArView view, IReadOnlyList<Vector4> keyColors, IReadOnlyList<ArLivePress> live, GpuInstanceList<GpuArQuad> quads, bool projectedNotes)
    {
        quads.Clear();
        _shapes.Clear();
        KeyQuadCount = 0;
        LiveBarCount = 0;
        if (ar.Opacity <= 0) return;
        var opacity = Math.Clamp(ar.Opacity, 0f, 1f);
        var keyLight = ar.Quality == ArQualityTier.Lite ? 0f : Math.Clamp(ar.KeyLight, 0f, 1f); // Lite leaves the camera's keys alone
        var layout = ar.Calibration.Layout;
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch && pitch < keyColors.Count; pitch++)
        {
            var key = keyColors[pitch];
            if (!(key.W > MinKeyGlow) || keyLight <= 0) continue;
            _keyPatches.Clear();
            ArKeys.Patches(ar.Calibration, view, pitch, _keyPatches);
            foreach (var patch in _keyPatches)
            {
                quads.Add(Pack(patch, new Vector3(key.X, key.Y, key.Z), Math.Min(1f, key.W) * keyLight * opacity, new Vector4(1, 0, 1, 0)));
                KeyQuadCount++;
            }
        }
        var look = input.Look;
        if (ar.ShowNotes && input.Playing && input.Notes.Count > 0 && !projectedNotes)
        {
            ArNotes.Build(ar.Calibration, view, input.Notes, input.Position - Math.Clamp(ar.DelaySeconds, 0f, 1f), input.MaxNoteDuration, ar.Notes, _shapes, VelocityBand);
            // The AR layer draws flat emissive quads while the note pass does not draw through the map. An unported
            // note style (Gem, Crystal Outline, named by ArLookGuard) keeps its track colour and the flat fallback
            // material until the styles are ported onto the projected note pass (AR-S4 remainder); today the colours
            // are the same either way, so the guard is an assertion target.
            foreach (var shape in _shapes)
            {
                var note = input.Notes[shape.NoteIndex];
                var color = note.Played ? new Vector3(82 / 255f, 237 / 255f, 208 / 255f)
                    : note.Missed ? new Vector3(1f, 83 / 255f, 113 / 255f)
                    : look.NoteColor(note.Pitch, note.Track);
                quads.Add(Pack(shape.Patch, color, note.Played ? .42f * opacity : opacity, new Vector4((float)shape.Fade, shape.Sounding ? 1 : 0, 0, 0)));
            }
        }
        // the keys under the player's own fingers, drawn over the notes of the song as the stage draws its live trails.
        // Lite keeps the bar's colour flat (no velocity tint), which is what the overlay has always done.
        if (projectedNotes) return;                 // the bars are the note pass's job now, like the notes
        var velocityTint = ar.Quality != ArQualityTier.Lite && look.VelocityColor && look.VelocityColorAmount > 0;
        var velocityAmount = velocityTint ? look.VelocityColorAmount : 0;
        if (live.Count == 0) return;
        ArNotes.BuildLive(ar.Calibration, view, live, ar.Notes, _liveBars, VelocityBand);
        foreach (var bar in _liveBars)
        {
            var color = look.NoteColor(bar.Pitch, 0);
            if (velocityTint) color = Vector3.Lerp(color, GpuStageSimulation.VelocityTint(bar.Strength), velocityAmount);
            quads.Add(Pack(bar.Patch, color, opacity, new Vector4((float)bar.Fade, bar.Held ? 1 : 0, 0, 0)));
            LiveBarCount++;
        }
    }

    private static GpuArQuad Pack(ArPatch patch, Vector3 color, float alpha, Vector4 misc) => new()
    {
        A = new Vector4((float)patch.BackLeft.X, (float)patch.BackLeft.Y, (float)patch.BackRight.X, (float)patch.BackRight.Y),
        B = new Vector4((float)patch.FrontRight.X, (float)patch.FrontRight.Y, (float)patch.FrontLeft.X, (float)patch.FrontLeft.Y),
        W = new Vector4((float)patch.BackLeft.W, (float)patch.BackRight.W, (float)patch.FrontRight.W, (float)patch.FrontLeft.W),
        Color = new Vector4(color, alpha),
        Misc = misc
    };
}
