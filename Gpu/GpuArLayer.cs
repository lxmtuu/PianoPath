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
    /// <summary>x = fade-in 0..1, y = 1 while the note sounds.</summary>
    public Vector4 Misc;
}

/// <summary>What the UI thread asks of the AR layer; replaced whole (see <see cref="GpuStageFeed.Ar"/>), null means none.</summary>
/// <param name="Calibration">Where the keyboard is in the camera picture.</param>
/// <param name="Notes">How notes fall over it.</param>
/// <param name="Opacity">0..1, how strongly the layer is drawn.</param>
internal sealed record GpuArInput(ArCalibration Calibration, ArNoteStyle Notes, float Opacity);

/// <summary>Turns the song's notes at this moment into quads for the AR pass.</summary>
internal sealed class GpuArLayer
{
    private readonly List<ArNoteShape> _shapes = [];

    /// <summary>How many shapes the last <see cref="Build"/> produced (the notes before they were packed).</summary>
    internal int ShapeCount => _shapes.Count;

    /// <summary>
    /// Refills <paramref name="quads"/> with the notes to draw. Like the stage's own notes they are drawn while the song plays,
    /// in the colours of the look; a note that has been played or missed takes the stage's played and missed colours.
    /// </summary>
    internal void Build(GpuFrameInput input, GpuArInput ar, ArView view, GpuInstanceList<GpuArQuad> quads)
    {
        quads.Clear();
        _shapes.Clear();
        if (!input.Playing || input.Notes.Count == 0 || ar.Opacity <= 0) return;
        ArNotes.Build(ar.Calibration, view, input.Notes, input.Position, input.MaxNoteDuration, ar.Notes, _shapes);
        var look = input.Look;
        var opacity = Math.Clamp(ar.Opacity, 0f, 1f);
        foreach (var shape in _shapes)
        {
            var note = input.Notes[shape.NoteIndex];
            var color = note.Played ? new Vector3(82 / 255f, 237 / 255f, 208 / 255f)
                : note.Missed ? new Vector3(1f, 83 / 255f, 113 / 255f)
                : look.NoteColor(note.Pitch, note.Track);
            var patch = shape.Patch;
            quads.Add(new GpuArQuad
            {
                A = new Vector4((float)patch.BackLeft.X, (float)patch.BackLeft.Y, (float)patch.BackRight.X, (float)patch.BackRight.Y),
                B = new Vector4((float)patch.FrontRight.X, (float)patch.FrontRight.Y, (float)patch.FrontLeft.X, (float)patch.FrontLeft.Y),
                W = new Vector4((float)patch.BackLeft.W, (float)patch.BackRight.W, (float)patch.FrontRight.W, (float)patch.FrontLeft.W),
                Color = new Vector4(color, note.Played ? .42f * opacity : opacity),
                Misc = new Vector4((float)shape.Fade, shape.Sounding ? 1 : 0, 0, 0)
            });
        }
    }
}
