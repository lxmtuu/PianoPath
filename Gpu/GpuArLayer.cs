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
    /// <summary>For notes, x=sounding, y=seed and w=hold-bar strength; z selects note 0, key tint 1, particle 2, key light 3, pulse 4, ribbon 5, flash 6, beam 7, shimmer 8, note trail 9 or note label 10.</summary>
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
/// <param name="ProjectedNotes">Build song notes from the calibrated keyboard-local 3D pose. Key-anchored live effects use the calibrated pose independently; ambient sprites stay screen-space.</param>
/// <param name="MinAspect">The minimum note length as a fraction of its projected key width; 0 keeps each note's natural length.</param>
/// <param name="ShowLiveTrails">Draw bars for keys the player presses, in flat or projected mode; key tint is separate.</param>
/// <param name="NoteDirection">Independent keyboard-local 3D direction of the notes and live bars.</param>
/// <param name="ShowRisingMidiNotes">In Perform mode, draw each MIDI Note On as a separate calibrated 3D note that keeps travelling after Note Off.</param>
internal sealed record GpuArInput(ArCalibration Calibration, ArNoteStyle Notes, float Opacity, float KeyLight = .6f, bool ShowNotes = true, float DelaySeconds = 0, ArQualityTier Quality = ArQualityTier.Standard, bool ProjectedNotes = false, float MinAspect = 0, bool ShowLiveTrails = false, ArNoteDirection NoteDirection = default, bool ShowRisingMidiNotes = false)
{
    /// <summary>Live press bars keep the user's width/fade graphs but use a fixed travel reach, independent of Learn lookahead.</summary>
    internal ArNoteStyle LivePressStyle => ArOptions.LivePressNoteStyle(Notes);

    /// <summary>Perform notes keep the user's width/fade graphs but use a fixed travel reach, independent of Learn lookahead.</summary>
    internal ArNoteStyle RisingMidiNoteStyle => ArOptions.RisingMidiNoteStyle(Notes);
}

/// <summary>Builds calibrated quads for the camera layer: projected notes, real-key tint and pitch-tagged Live Stage effects.</summary>
internal sealed partial class GpuArLayer
{
    private readonly List<ArNoteShape> _shapes = [];
    private readonly List<ArLiveBar> _liveBars = [];
    private readonly List<Ar3DNoteShape> _projectedShapes = [];
    private readonly List<Ar3DLiveBar> _projectedLiveBars = [];
    private readonly List<Ar3DLiveMidiNote> _risingMidiNotes = [];
    private readonly List<ArPatch> _keyPatches = [];

    /// <summary>Key glow below this is not drawn.</summary>
    internal const float MinKeyGlow = .02f;

    /// <summary>The velocity band the style's graphs walk until the stage's own simulation feeds the layer: raw MIDI velocity mapped straight onto 0..1.</summary>
    private static readonly PianoSimulation VelocityBand = new();

    internal int KeyQuadCount { get; private set; }
    internal int ShapeCount => _shapes.Count + _projectedShapes.Count;
    internal int LiveBarCount { get; private set; }
    internal int RisingMidiNoteCount { get; private set; }
    internal int NoteTrailQuadCount { get; private set; }
    internal int GhostNoteQuadCount { get; private set; }
    internal int LandingGlowCount { get; private set; }
    internal int NoteLabelCount { get; private set; }
    internal int ImpactLightCount { get; private set; }
    internal int NoteLightCount { get; private set; }
    internal int PulseCount { get; private set; }
    internal int ParticleFxCount { get; private set; }
    internal int FlashCount { get; private set; }
    internal int HoldArcCount { get; private set; }
    internal int BeamCount { get; private set; }
    internal int ReleaseEffectCount { get; private set; }

    /// <summary>
    /// Refills the projected AR quads. Key tint remains independent of the optional live bars. Actual stage-simulation
    /// particles, rings and flashes retain their MIDI pitch and are projected through the saved 3D pose; stage-space
    /// sprites are never placed flat over the camera. Ambient sprites are owned by the separate screen-space pass.
    /// </summary>
    internal void Build(GpuFrameInput input, GpuArInput ar, ArView view, IReadOnlyList<Vector4> keyColors,
        GpuStageSimulation simulation, GpuInstanceList<GpuArQuad> quads, bool projectedNotes)
    {
        quads.Clear();
        _shapes.Clear(); _projectedShapes.Clear(); _liveBars.Clear(); _projectedLiveBars.Clear(); _risingMidiNotes.Clear();
        KeyQuadCount = LiveBarCount = RisingMidiNoteCount = 0;
        NoteTrailQuadCount = GhostNoteQuadCount = LandingGlowCount = NoteLabelCount = 0;
        ImpactLightCount = NoteLightCount = PulseCount = ParticleFxCount = FlashCount = HoldArcCount = BeamCount = ReleaseEffectCount = 0;
        if (ar.Opacity <= 0) return;

        var opacity = Math.Clamp(ar.Opacity, 0f, 1f);
        var keyLight = ar.Quality == ArQualityTier.Lite ? 0f : Math.Clamp(ar.KeyLight, 0f, 1f);
        var layout = ar.Calibration.Layout;
        var look = input.Look;
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

        if (ar.Calibration.CameraPose is { } keyPose && ar.Quality != ArQualityTier.Lite && keyLight > 0)
            BuildProjectedNoteLights(ar, view, keyColors, quads, opacity * keyLight, look, layout, keyPose);

        if (ar.ShowNotes && input.Playing && input.Notes.Count > 0)
        {
            var position = input.Position - Math.Clamp(ar.DelaySeconds, 0f, 1f);
            if (projectedNotes)
            {
                // The selected note direction is independent of the piano pose. Song-note bodies and their ghosts,
                // trails and landing cues all come from calibrated 3D patches; stage-space sprites remain suppressed.
                Ar3DNoteProjection.Build(ar.Calibration, view, input.Notes, position, input.MaxNoteDuration, ar.Notes,
                    ar.NoteDirection, ar.MinAspect, _projectedShapes, VelocityBand);
                foreach (var shape in _projectedShapes)
                {
                    if ((uint)shape.NoteIndex >= (uint)input.Notes.Count) continue;
                    var note = input.Notes[shape.NoteIndex];
                    BuildProjectedSongNoteEffects(ar, view, shape, note, position, opacity, look, simulation, quads);
                    var patch = shape.Patch;
                    if (shape.Sounding && look.HoldVibration)
                        patch = VibrateLiveBar(ar.Calibration, view, shape.Pitch, patch, look, simulation.Time, simulation.SimulationWidth);
                    AddSongNote(quads, patch, shape.Pitch, shape.NoteIndex, shape.Sounding, shape.Fade,
                        note, opacity, look, simulation.Time);
                    if (ar.Calibration.CameraPose is { } labelPose)
                        BuildProjectedSongNoteLabel(view, shape, note, opacity, look, simulation.Time, quads, labelPose);
                }
            }
            else
            {
                ArNotes.Build(ar.Calibration, view, input.Notes, position, input.MaxNoteDuration, ar.Notes, _shapes, VelocityBand);
                foreach (var shape in _shapes)
                {
                    if ((uint)shape.NoteIndex >= (uint)input.Notes.Count) continue;
                    var note = input.Notes[shape.NoteIndex];
                    AddSongNote(quads, shape.Patch, shape.Pitch, shape.NoteIndex, shape.Sounding, shape.Fade,
                        note, opacity, look, simulation.Time);
                }
            }
        }

        // S5 maps the simulation's hit/hold/release channels only when a full camera pose is available. Even if
        // song-note perspective is switched off, live effects stay attached to the calibrated piano in 3D.
        if (ar.Calibration.CameraPose is { } pose && ar.Quality != ArQualityTier.Lite)
            BuildStageEffects(ar, view, simulation, keyColors, quads, opacity, look, layout, pose);

        // MIDI onsets are note objects, not optional held-key bars. Keep them in the calibrated pose even in Lite;
        // they share the note material and direction controls and continue after Note Off until their reach expires.
        var risingMidiNotes = simulation.ArRisingMidiNotes;
        if (ar.ShowRisingMidiNotes && ar.Calibration.CameraPose is not null && risingMidiNotes.Count > 0)
        {
            Ar3DNoteProjection.BuildRisingMidiNotes(ar.Calibration, view, risingMidiNotes, ar.RisingMidiNoteStyle,
                ar.NoteDirection, _risingMidiNotes, VelocityBand);
            foreach (var note in _risingMidiNotes)
            {
                if (ar.Calibration.CameraPose is { } midiPose)
                    BuildProjectedRisingMidiEffects(ar, view, note, opacity, look, simulation.Time, quads, midiPose);
                var patch = note.Patch;
                if (note.Held && look.HoldVibration)
                    patch = VibrateLiveBar(ar.Calibration, view, note.Pitch, patch, look, simulation.Time, simulation.SimulationWidth);
                var color = look.NoteColor(note.Pitch, 0);
                AddLiveBar(quads, patch, note.Pitch, note.Fade, note.Held, note.Strength,
                    color, opacity, look, simulation.Time);
                if (ar.Calibration.CameraPose is { } labelPose)
                    BuildProjectedLiveNoteLabel(view, note.Pitch, note.Target, note.Travel, note.HeadDistance,
                        note.TailDistance, note.HalfWidthWorld, note.HalfWidthPixels, note.HalfLengthPixels,
                        note.Fade, note.Held, note.Strength, opacity, look, simulation.Time, quads, labelPose);
                RisingMidiNoteCount++;
            }
        }

        var live = simulation.LivePresses;
        if (!ar.ShowLiveTrails || live.Count == 0) return;
        var liveProjected = ar.Calibration.CameraPose is not null && ar.Quality != ArQualityTier.Lite;
        if (liveProjected)
        {
            Ar3DNoteProjection.BuildLive(ar.Calibration, view, live, ar.LivePressStyle, ar.NoteDirection, _projectedLiveBars, VelocityBand);
            foreach (var bar in _projectedLiveBars)
            {
                if (ar.Calibration.CameraPose is { } livePose)
                    BuildProjectedLiveBarEffects(ar, view, bar, opacity, look, simulation.Time, quads, livePose);
                var patch = bar.Patch;
                if (bar.Held && look.HoldVibration)
                    patch = VibrateLiveBar(ar.Calibration, view, bar.Pitch, patch, look, simulation.Time, simulation.SimulationWidth);
                var color = look.NoteColor(bar.Pitch, 0);
                AddLiveBar(quads, patch, bar.Pitch, bar.Fade, bar.Held, bar.Strength, color, opacity, look, simulation.Time);
                if (ar.Calibration.CameraPose is { } labelPose)
                    BuildProjectedLiveNoteLabel(view, bar.Pitch, bar.Target, bar.Travel, bar.HeadDistance,
                        bar.TailDistance, bar.HalfWidthWorld, bar.HalfWidthPixels, bar.HalfLengthPixels,
                        bar.Fade, bar.Held, bar.Strength, opacity, look, simulation.Time, quads, labelPose);
                LiveBarCount++;
            }
        }
        else
        {
            ArNotes.BuildLive(ar.Calibration, view, live, ar.LivePressStyle, _liveBars, VelocityBand);
            foreach (var bar in _liveBars)
            {
                var color = look.NoteColor(bar.Pitch, 0);
                AddLiveBar(quads, bar.Patch, bar.Pitch, bar.Fade, bar.Held, bar.Strength, color, opacity, look, simulation.Time);
                LiveBarCount++;
            }
        }
    }

    private static Vector3 SongNoteColor(int pitch, NoteEvent note, bool sounding, GpuLook look, double time)
    {
        var played = note.Played;
        var missed = note.Missed;
        var color = played ? new Vector3(82 / 255f, 237 / 255f, 208 / 255f)
            : missed ? new Vector3(1f, 83 / 255f, 113 / 255f)
            : look.NoteColor(pitch, note.Track);
        if (!played && !missed && look.VelocityColor && look.VelocityColorAmount > 0)
            color = Vector3.Lerp(color, GpuStageSimulation.VelocityTint(note.Velocity / 127f), look.VelocityColorAmount);
        if (sounding && look.HoldColorCycle)
        {
            var cycling = GpuStageSimulation.ColorFromHue(GpuStageSimulation.Hue188(pitch) + time * look.HoldColorCycleSpeed * 3);
            color = Vector3.Lerp(color, cycling, .8f);
        }
        if (look.RainbowTrail) color = GpuStageSimulation.ColorFromHue(time * look.RainbowHueSpeed + pitch * 9);
        return color;
    }

    private static void AddSongNote(GpuInstanceList<GpuArQuad> quads, ArPatch patch, int pitch, int noteIndex,
        bool sounding, double fade, NoteEvent note, float opacity, GpuLook look, double time, bool ghostPass = false)
    {
        var played = note.Played;
        var color = SongNoteColor(pitch, note, sounding, look, time);
        var noteOpacity = played ? .42f : 1f;
        var pulse = look.FallingPulse && !sounding
            ? .62f + .38f * MathF.Sin((float)time * (1.5f + look.FallingPulseRate * 9) + pitch * .7f) : 1f;
        var breath = sounding && look.HoldBreath
            ? .72f + .28f * MathF.Sin((float)time * (1 + look.HoldBreathRate * 5)) : 1f;
        var linear = GpuStageSimulation.ToLinear(color) * breath;
        var alpha = opacity * noteOpacity * (float)Math.Clamp(fade, 0, 1) * pulse;
        var holdBar = sounding && look.HoldBar ? look.HoldBarIntensity * opacity * noteOpacity * (float)Math.Clamp(fade, 0, 1) : 0;
        var seed = (noteIndex % 97) / 97f;
        quads.Add(Pack(patch, linear, alpha, new Vector4(sounding ? 1 : 0, seed, 0, holdBar)));
        if (!ghostPass) AddNoteShimmer(quads, patch, pitch, linear, alpha, look);
    }

    private static Vector3 LiveNoteColor(int pitch, bool held, float strength, Vector3 color, GpuLook look, double time)
    {
        if (held && look.HoldColorCycle)
        {
            var cycling = GpuStageSimulation.ColorFromHue(GpuStageSimulation.Hue188(pitch) + time * look.HoldColorCycleSpeed * 3);
            color = Vector3.Lerp(color, cycling, .8f);
        }
        if (look.VelocityColor && look.VelocityColorAmount > 0)
            color = Vector3.Lerp(color, GpuStageSimulation.VelocityTint(strength), look.VelocityColorAmount);
        return look.RainbowTrail ? GpuStageSimulation.ColorFromHue(time * look.RainbowHueSpeed + pitch * 9) : color;
    }

    private static void AddLiveBar(GpuInstanceList<GpuArQuad> quads, ArPatch patch, int pitch, double fade, bool held,
        float strength, Vector3 color, float opacity, GpuLook look, double time, bool ghostPass = false)
    {
        var breath = held && look.HoldBreath
            ? .72f + .28f * MathF.Sin((float)time * (1 + look.HoldBreathRate * 5)) : 1f;
        color = LiveNoteColor(pitch, held, strength, color, look, time);

        var pulse = look.FallingPulse && !held
            ? .62f + .38f * MathF.Sin((float)time * (1.5f + look.FallingPulseRate * 9) + pitch * .7f) : 1f;
        var linear = GpuStageSimulation.ToLinear(color) * breath;
        var alpha = opacity * (float)Math.Clamp(fade, 0, 1) * pulse;
        var holdBar = held && look.HoldBar ? look.HoldBarIntensity * opacity * (float)Math.Clamp(fade, 0, 1) : 0;
        var seed = (pitch % 97) / 97f;
        quads.Add(Pack(patch, linear, alpha, new Vector4(held ? 1 : 0, seed, 0, holdBar)));
        if (!ghostPass) AddNoteShimmer(quads, patch, pitch, linear, alpha, look);
    }

    private static double Distance(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>Projects the GPU stage's moving note sheen along an AR note's own calibrated quad.</summary>
    private static void AddNoteShimmer(GpuInstanceList<GpuArQuad> quads, ArPatch patch, int pitch,
        Vector3 linearColor, float alpha, GpuLook look)
    {
        if (!look.NoteShimmer || look.NoteShimmerAmount <= .01f || alpha <= .01f) return;
        var width = (Distance(patch.BackRight.X - patch.BackLeft.X, patch.BackRight.Y - patch.BackLeft.Y)
                     + Distance(patch.FrontRight.X - patch.FrontLeft.X, patch.FrontRight.Y - patch.FrontLeft.Y)) * .5;
        var length = (Distance(patch.FrontLeft.X - patch.BackLeft.X, patch.FrontLeft.Y - patch.BackLeft.Y)
                      + Distance(patch.FrontRight.X - patch.BackRight.X, patch.FrontRight.Y - patch.BackRight.Y)) * .5;
        if (width < 5 || length < 6) return;
        var phase = pitch * 1.6180339f + .37f;
        quads.Add(Pack(patch, linearColor, alpha,
            new Vector4(phase, look.NoteShimmerAmount, 8, 0)));
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
