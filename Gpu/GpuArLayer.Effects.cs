using System.Numerics;

namespace PianoPath;

internal sealed partial class GpuArLayer
{
    private const int MaxProjectedParticles = 192;
    // Null in production. Observes only particles that really packed; never participates in selection.
    internal Action<GpuArParticle>? ObservePackedParticleForReview { get; set; }
    private const byte ParticleStreak = 1, ParticleConfetti = 2, ParticleDust = 3,
        ParticleFlame = 4, ParticleDroplet = 5, ParticleShard = 6, ParticleWisp = 7;
    private static readonly ArVec3 Across = new(1, 0, 0);
    private static readonly ArVec3 Depth = new(0, 1, 0);
    private static readonly ArVec3 Normal = new(0, 0, 1);

    /// <summary>Pose-projects note ghosts, trails and approach glows which the stage keeps in its screen-space sprite pass.</summary>
    private void BuildProjectedSongNoteEffects(GpuArInput ar, ArView view, Ar3DNoteShape shape, NoteEvent note,
        double position, float opacity, GpuLook look, GpuStageSimulation simulation, GpuInstanceList<GpuArQuad> quads)
    {
        if (ar.Calibration.CameraPose is not { } pose) return;
        var color = SongNoteColor(shape.Pitch, note, shape.Sounding, look, simulation.Time);
        var noteOpacity = note.Played ? .42f : 1f;

        AddProjectedNoteTrail(ar.Notes.Reach, shape, color, opacity * noteOpacity * (float)shape.Fade,
            look, simulation.Time, quads, pose, view);

        if (look.FallingGhost && !shape.Sounding && look.FallingGhostAmount > .01f)
        {
            var strength = look.FallingGhostAmount * noteOpacity;
            var copies = strength > .66f ? 3 : strength > .33f ? 2 : 1;
            var bodyLength = Math.Max(0, shape.TailDistance - shape.HeadDistance);
            var step = Math.Max(.05, .06 + bodyLength * .12);
            for (var i = 1; i <= copies; i++)
            {
                // Song notes advance toward the key plane as the playhead moves, so their leading echoes use
                // smaller keyboard-local distances. Each copy remains its own camera-facing quad at the pose.
                var offset = step * i;
                var head = Math.Max(0, shape.HeadDistance - offset);
                var tail = Math.Max(0, shape.TailDistance - offset);
                if (!(tail > head + 1e-7)
                    || !Ar3DNoteProjection.TryPatch(pose, view, shape.Target, shape.Travel, head, tail,
                        shape.HalfWidthWorld, out var ghostPatch, out _, out _, out _)) continue;
                AddSongNote(quads, ghostPatch, shape.Pitch, shape.NoteIndex, false, shape.Fade,
                    note, opacity * (float)(strength * .3 / i), look, simulation.Time, ghostPass: true);
                GhostNoteQuadCount++;
            }
        }

        if (!look.NoteLandingGlow || look.Chroma) return;
        var until = note.Start - position;
        if (!(until > 0) || until > .55) return;
        var rise = 1 - Math.Clamp(until / .55, 0, 1);
        var glowStrength = (float)(look.NoteLandingGlowAmount * rise * rise * (.7 + .3 * simulation.Activity));
        if (glowStrength <= .01f) return;
        var center = shape.Target + Depth * .27;
        var halfWidth = shape.HalfWidthWorld * 1.4;
        var halfDepth = .035 + .05 * rise;
        var linear = GpuStageSimulation.ToLinear(color);
        if (Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth,
                halfWidth, halfDepth, out var broadGlow))
        {
            quads.Add(Pack(broadGlow, linear * 1.7f, opacity * glowStrength * .5f, new Vector4(0, 0, 3, 0)));
            LandingGlowCount++;
        }
        if (Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth,
                halfWidth * .48, halfDepth * .52, out var hotGlow))
        {
            quads.Add(Pack(hotGlow, Vector3.One * 1.9f, opacity * glowStrength * .8f, new Vector4(0, 0, 3, 0)));
            LandingGlowCount++;
        }
    }

    private void BuildProjectedRisingMidiEffects(GpuArInput ar, ArView view, Ar3DLiveMidiNote note, float opacity,
        GpuLook look, double time, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose)
    {
        var color = LiveNoteColor(note.Pitch, note.Held, note.Strength, look.NoteColor(note.Pitch, 0), look, time);
        var reach = ar.RisingMidiNoteStyle.Reach;
        AddProjectedTrail(reach, note.Pitch, note.Target, note.Travel, note.HeadDistance, note.TailDistance,
            note.HalfWidthWorld, true, color, opacity * (float)note.Fade, look, time, quads, pose, view);
        if (!look.FallingGhost || note.Held || look.FallingGhostAmount <= .01f) return;
        AddProjectedMovingGhosts(note.Target, note.Travel, note.HeadDistance, note.TailDistance,
            note.HalfWidthWorld, reach, note.Fade, opacity, look, quads, pose, view,
            (patch, fade, ghostOpacity) => AddLiveBar(quads, patch, note.Pitch, fade, false, note.Strength,
                look.NoteColor(note.Pitch, 0), ghostOpacity, look, time, ghostPass: true));
    }

    private void BuildProjectedLiveBarEffects(GpuArInput ar, ArView view, Ar3DLiveBar bar, float opacity,
        GpuLook look, double time, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose)
    {
        var color = LiveNoteColor(bar.Pitch, bar.Held, bar.Strength, look.NoteColor(bar.Pitch, 0), look, time);
        AddProjectedTrail(ar.LivePressStyle.Reach, bar.Pitch, bar.Target, bar.Travel, bar.HeadDistance, bar.TailDistance,
            bar.HalfWidthWorld, true, color, opacity * (float)bar.Fade, look, time, quads, pose, view);
        if (!look.FallingGhost || bar.Held || look.FallingGhostAmount <= .01f) return;
        AddProjectedMovingGhosts(bar.Target, bar.Travel, bar.HeadDistance, bar.TailDistance,
            bar.HalfWidthWorld, ar.LivePressStyle.Reach, bar.Fade, opacity, look, quads, pose, view,
            (patch, fade, ghostOpacity) => AddLiveBar(quads, patch, bar.Pitch, fade, false, bar.Strength,
                look.NoteColor(bar.Pitch, 0), ghostOpacity, look, time, ghostPass: true));
    }

    private void AddProjectedMovingGhosts(ArVec3 target, ArVec3 travel, double headDistance,
        double tailDistance, double halfWidthWorld, double reach, double fade, float opacity,
        GpuLook look, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose, ArView view,
        Action<ArPatch, double, float> drawGhost)
    {
        var strength = look.FallingGhostAmount;
        if (strength <= .01f) return;
        var copies = strength > .66f ? 3 : strength > .33f ? 2 : 1;
        var bodyLength = Math.Max(0, tailDistance - headDistance);
        var step = Math.Max(.05, .06 + bodyLength * .12);
        for (var i = 1; i <= copies; i++)
        {
            // Rising live objects advance toward increasing depth distances; copies lead farther along that same path.
            var head = Math.Min(reach, headDistance + step * i);
            var tail = Math.Min(reach, tailDistance + step * i);
            if (!(tail > head + 1e-7)
                || !Ar3DNoteProjection.TryPatch(pose, view, target, travel, head, tail, halfWidthWorld,
                    out var ghostPatch, out _, out _, out _)) continue;
            var ghostOpacity = opacity * (float)(strength * .3 / i);
            drawGhost(ghostPatch, fade, ghostOpacity);
            GhostNoteQuadCount++;
        }
    }

    private void BuildProjectedSongNoteLabel(ArView view, Ar3DNoteShape shape, NoteEvent note, float opacity,
        GpuLook look, double time, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose)
    {
        var color = SongNoteColor(shape.Pitch, note, shape.Sounding, look, time);
        var noteOpacity = note.Played ? .42f : 1f;
        AddProjectedNoteLabel(view, shape.Pitch, shape.Target, shape.Travel, shape.HeadDistance, shape.TailDistance,
            shape.HalfWidthWorld, shape.HalfWidthPixels, shape.HalfLengthPixels, shape.Fade,
            color, opacity * noteOpacity, look, quads, pose);
    }

    private void BuildProjectedLiveNoteLabel(ArView view, int pitch, ArVec3 target, ArVec3 travel,
        double headDistance, double tailDistance, double halfWidthWorld, double halfWidthPixels,
        double halfLengthPixels, double fade, bool held, float strength, float opacity, GpuLook look,
        double time, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose)
    {
        var color = LiveNoteColor(pitch, held, strength, look.NoteColor(pitch, 0), look, time);
        AddProjectedNoteLabel(view, pitch, target, travel, headDistance, tailDistance, halfWidthWorld,
            halfWidthPixels, halfLengthPixels, fade, color, opacity, look, quads, pose);
    }

    private void AddProjectedNoteLabel(ArView view, int pitch, ArVec3 target, ArVec3 travel,
        double headDistance, double tailDistance, double halfWidthWorld, double halfWidthPixels,
        double halfLengthPixels, double fade, Vector3 color, float opacity, GpuLook look,
        GpuInstanceList<GpuArQuad> quads, ArCameraPose pose)
    {
        if (!look.ShowNoteLabels || halfWidthPixels * 2 < 11 || halfLengthPixels * 2 < 15) return;
        var length = tailDistance - headDistance;
        if (!(length > 1e-7)) return;
        var fontPixels = Math.Min(11, halfWidthPixels * 2 * .62);
        var worldPerPixel = length / (2 * halfLengthPixels);
        var glyphLength = Math.Min(length, fontPixels * worldPerPixel);
        var glyphHalfWidth = Math.Min(halfWidthWorld, glyphLength);
        if (!(glyphLength > 1e-7) || !(glyphHalfWidth > 0)) return;
        // The note-name atlas cell is anchored near the leading/key-side edge, like the Stage label's lower edge.
        var labelStart = headDistance;
        var labelEnd = Math.Min(tailDistance, labelStart + glyphLength);
        if (!Ar3DNoteProjection.TryPatch(pose, view, target, travel, labelStart, labelEnd,
                glyphHalfWidth, out var patch, out _, out _, out _)) return;
        var luminance = (.2126f * color.X + .7152f * color.Y + .0722f * color.Z) * 255;
        var textColor = look.NoteStyle == 1 || luminance <= 150
            ? Vector3.One : GpuStageSimulation.ToLinear(new Vector3(12 / 255f, 8 / 255f, 20 / 255f));
        var cell = (halfWidthPixels * 2 >= 20 ? GpuStageSimulation.AtlasNoteCell : GpuStageSimulation.AtlasShortCell) + pitch;
        quads.Add(Pack(patch, textColor, opacity * (float)fade * (230 / 255f), new Vector4(cell, 0, 10, 0)));
        NoteLabelCount++;
    }

    private void AddProjectedNoteTrail(double reach, Ar3DNoteShape shape, Vector3 color, float opacity,
        GpuLook look, double time, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose, ArView view) =>
        AddProjectedTrail(reach, shape.Pitch, shape.Target, shape.Travel, shape.HeadDistance, shape.TailDistance,
            shape.HalfWidthWorld, false, color, opacity, look, time, quads, pose, view);

    private void AddProjectedTrail(double reach, int pitch, ArVec3 target, ArVec3 travel, double headDistance,
        double tailDistance, double halfWidthWorld, bool trailsBehindMotion, Vector3 color, float opacity,
        GpuLook look, double time, GpuInstanceList<GpuArQuad> quads, ArCameraPose pose, ArView view)
    {
        if (look.FallingTrail == "None" || look.FallingTrailIntensity <= .01f || opacity <= .001f) return;
        var kind = look.FallingTrail switch
        {
            "Glow" => 0, "Sparkles" => 1, "Speed Lines" => 2, "Blur" => 3,
            "Ribbon" => 4, "Rainbow" => 5, "Stream" => 6, _ => -1
        };
        if (kind < 0) return;
        var bodyLength = Math.Max(0, tailDistance - headDistance);
        var trailLength = Math.Max(.08, bodyLength * look.FallingTrailLength + .08);
        var start = trailsBehindMotion ? Math.Max(0, headDistance - trailLength) : tailDistance;
        var end = trailsBehindMotion ? headDistance : Math.Min(reach, tailDistance + trailLength);
        if (!(end > start + 1e-7)
            || !Ar3DNoteProjection.TryPatch(pose, view, target, travel, start, end,
                halfWidthWorld, out var patch, out _, out _, out _)) return;
        quads.Add(Pack(patch, GpuStageSimulation.ToLinear(color) * 1.35f,
            opacity * look.FallingTrailIntensity,
            new Vector4(kind, look.FallingTrailIntensity, 9, pitch * 1.6180339f + (float)time * .13f)));
        NoteTrailQuadCount++;
    }

    /// <summary>Soft note-light spill lands on neighbouring real keys, using the same glow history as the key tint.</summary>
    private void BuildProjectedNoteLights(GpuArInput ar, ArView view, IReadOnlyList<Vector4> keyColors,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose)
    {
        if (!ArElementCatalog.CanDraw(ar.Quality, ArElement.NoteLights) || opacity <= 0) return;
        var ratio = pose.KeyboardWidthDepthRatio;
        var radius = 1 + look.KeyGlowRadius * 5;
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch && pitch < keyColors.Count; pitch++)
        {
            var spill = 0f;
            var tint = Vector3.Zero;
            var tintWeight = 0f;
            for (var other = Math.Max(layout.FirstPitch, pitch - 8); other <= Math.Min(layout.LastPitch, pitch + 8) && other < keyColors.Count; other++)
            {
                if (other == pitch) continue;
                var glow = Math.Clamp(keyColors[other].W, 0, 1);
                if (glow <= 0) continue;
                var weight = glow * MathF.Exp(-Math.Abs(other - pitch) / radius);
                spill += weight;
                tint += new Vector3(keyColors[other].X, keyColors[other].Y, keyColors[other].Z) * weight;
                tintWeight += weight;
            }
            spill = Math.Min(1, spill);
            if (spill < .035f || tintWeight <= 1e-5f) continue;
            var center = new ArVec3(layout.CenterX(pitch) * ratio, .27, 0);
            var (left, right) = layout.Span(pitch);
            var halfKey = (right - left) * ratio * .5;
            if (!Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth,
                    halfKey * 1.3, .09, out var patch)) continue;
            var color = tint / tintWeight;
            var alpha = opacity * spill * .14f;
            quads.Add(Pack(patch, color * 1.25f, alpha, new Vector4(0, 0, 3, 0)));
            NoteLightCount++;
        }
    }

    private void BuildStageEffects(GpuArInput ar, ArView view, GpuStageSimulation simulation, IReadOnlyList<Vector4> keyColors,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose,
        GpuAdaptiveQualityTier adaptiveTier)
    {
        // The view-tuned table (roadmap §14.7 "Thêm 5"): same stage, same look, but the lift of the flat-born
        // effects comes from the camera's angle, and the stand-up quads stay home on the straight-down view.
        var preset = ArViewPresets.ForProfile(ar.ViewProfileId);
        // Each gate below passes through roadmap §14.8's adaptive budget (ArQualityBudget), in its exact sentence
        // order: particles first, then pulses, note lights (in Build), light bars, note style, the impact glow last.
        if (ArQualityBudget.CanDraw(ar.Quality, adaptiveTier, ArElement.ImpactLights))
            BuildImpactLights(view, simulation, quads, opacity, layout, pose, preset);
        if (ArQualityBudget.CanDraw(ar.Quality, adaptiveTier, ArElement.Pulses))
            BuildStageRings(view, simulation, quads, opacity, look, layout, pose, preset);
        if (!ArQualityBudget.CanDraw(ar.Quality, adaptiveTier, ArElement.Particles)) return;

        BuildStageParticles(view, simulation, quads, opacity, look, layout, pose);
        BuildStageFlashes(view, simulation, quads, opacity, look, layout, pose);
        if (!preset.TallEffectsVisible) return;
        BuildHoldArcs(view, simulation, quads, opacity, look, layout, pose);
        if (!ArQualityBudget.IsCut(adaptiveTier, ArBudgetCut.LightBars))
            BuildLightBeams(view, keyColors, quads, opacity, look, layout, pose);
    }

    private void BuildImpactLights(ArView view, GpuStageSimulation simulation,
        GpuInstanceList<GpuArQuad> quads, float opacity, KeyboardLayout layout, ArCameraPose pose, ArViewPreset preset)
    {
        var ratio = pose.KeyboardWidthDepthRatio;
        foreach (var impact in simulation.ArImpacts)
        {
            if (!layout.Contains(impact.Pitch) || !double.IsFinite(impact.Age) || impact.Age < 0 || impact.Age >= .62) continue;
            var strength = Math.Clamp(impact.Strength, 0, 1);
            if (strength <= .01f) continue;
            var (left, right) = layout.Span(impact.Pitch);
            var halfKey = (right - left) * ratio * .5;
            // Retain the strike's own note/velocity colour; current modulation must not recolour a transient in flight.
            var linear = GpuStageSimulation.ToLinear(impact.Color);
            // The view-tuned paper-thin lift (ArViewPresets): leaned cameras keep the catalogue's 0.2 cm so the
            // flare never z-hides behind the key fronts; overhead it shrinks to a film the plane can be read through.
            var center = new ArVec3(layout.CenterX(impact.Pitch) * ratio, .12, -preset.ImpactGlow);
            var fade = (float)Math.Pow(Math.Clamp(1 - impact.Age / .62, 0, 1), 1.25);
            var alpha = Math.Clamp(opacity * strength * fade * simulation.GlowBoost, 0, 1);
            if (alpha <= .004f || !Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth,
                    halfKey * 1.05, .105, out var patch)) continue;
            quads.Add(Pack(patch, linear * 1.8f, alpha, new Vector4(0, 0, 3, 0)));
            ImpactLightCount++;
        }
    }

    private void BuildStageRings(ArView view, GpuStageSimulation simulation,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose, ArViewPreset preset)
    {
        for (var i = 0; i < simulation.ArRingCount; i++)
        {
            var ring = simulation.ArRingAt(i);
            if (!layout.Contains(ring.Pitch) || !(ring.Life > 0) || ring.Age < 0 || ring.Age >= ring.Life) continue;
            var channelIntensity = KeyRingChannelGain.Resolve(ring.Channel, look.ImpactWaveIntensity, look.ImpactMorphIntensity);
            if (channelIntensity <= 0) continue;
            var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
            var strength = Math.Clamp(.5f + ring.Strength * .5f, 0, 1.2f);
            var color = GpuStageSimulation.ToLinear(ring.Color) * 1.6f;
            var baseAlpha = opacity * channelIntensity * strength;
            var pixelUnit = pose.KeyboardWidthDepthRatio / Math.Max(1, simulation.SimulationWidth);
            var center = StageSurfacePoint(simulation, layout, pose, ring.Pitch, ring.X, ring.Y);
            // The view-tuned pulse floor (ArViewPresets): rings never come nearer the key plane than their view's
            // minimum, while rings the stage has already lifted keep their rise untouched.
            center = ArElementHeights.Lifted(center, preset.PulseMinimum);
            var rawSize = look.RingSizeRaw;

            switch (ring.Kind)
            {
                case 1: // Shockwave: a hot, quickly expanding core with a bright edge.
                {
                    var progress = 1 - MathF.Exp(-4.6f * (float)t);
                    var radius = (6 + progress * (26 + rawSize * 1.6f)) * (.6f + .4f * ring.Strength);
                    var fade = MathF.Pow(1 - (float)t, 1.4f);
                    AddRingBand(view, pose, center, radius, Math.Max(.6f, 2.4f * (1 - (float)t)), color * 1.08f,
                        baseAlpha * .92f * fade, 1, pixelUnit, quads);
                    if (t < .28)
                        AddSoftKeyGlow(view, pose, center, radius * .23 * pixelUnit, radius * .1 * pixelUnit,
                            Vector3.One * 1.4f, baseAlpha * .55f * (float)(1 - t / .28), quads);
                    break;
                }
                case 2: // Ripple: three concentric, separately decaying wave fronts.
                {
                    var progress = 1 - MathF.Exp(-3.4f * (float)t);
                    var radius = (8 + progress * (30 + rawSize * 1.4f)) * (.7f + .3f * ring.Strength);
                    for (var wave = 0; wave < 3; wave++)
                    {
                        var age = t - wave * .12;
                        if (age < 0) continue;
                        var waveRadius = radius * (1 - wave * .22);
                        var fade = Math.Pow(1 - age, 1.8) * (1 - wave * .25);
                        var rippleColor = wave == 1 ? Vector3.Lerp(color, new Vector3(.58f, .72f, 1.1f), .32f) : color;
                        AddRingBand(view, pose, center, waveRadius, Math.Max(.6f, 2 * (1 - (float)age) + .4f), rippleColor,
                            baseAlpha * (float)fade, 2, pixelUnit, quads);
                    }
                    break;
                }
                case 3: // Implosion: contract towards the struck key (Absorb / Snap Back).
                {
                    var radius = (34 + rawSize * 1.1f) * (1 - (float)t) + 3;
                    var fade = (1 - (float)t) * (1 - (float)t);
                    AddRingBand(view, pose, center, radius, Math.Max(.6f, 2.6f * (1 - (float)t) + .6f), color,
                        baseAlpha * fade, 3, pixelUnit, quads);
                    AddRingBand(view, pose, center, radius * .55, 1.2, Vector3.One * 1.25f,
                        baseAlpha * fade * .56f, 3, pixelUnit, quads);
                    break;
                }
                default: // Acoustic ring with a short harmonic echo.
                {
                    var progress = 1 - MathF.Exp(-4.2f * (float)t);
                    var radius = (5 + progress * (18 + rawSize * 1.25f)) * (.7f + .3f * ring.Strength);
                    var fade = Math.Pow(1 - t, 1.6);
                    AddRingBand(view, pose, center, radius, Math.Max(.5f, 2.2f * (1 - (float)t)), color,
                        baseAlpha * .9f * (float)fade, 0, pixelUnit, quads);
                    if (t > .08)
                        AddRingBand(view, pose, center, radius * .68, Math.Max(.5f, 1.5f * (1 - (float)t)), color,
                            baseAlpha * .4f * (float)fade, 0, pixelUnit, quads);
                    if (t < .3)
                        AddSoftKeyGlow(view, pose, center, radius * .2 * pixelUnit, radius * .09 * pixelUnit,
                            Vector3.One * 1.6f, baseAlpha * .4f * (float)(1 - t / .3), quads);
                    break;
                }
            }
            if (ring.Origin == 1) ReleaseEffectCount++;
        }
    }

    private void AddRingBand(ArView view, ArCameraPose pose, ArVec3 center, double radiusPixels, double penPixels,
        Vector3 color, float alpha, byte style, double pixelUnit, GpuInstanceList<GpuArQuad> quads)
    {
        if (alpha <= .004f || !(radiusPixels > .5)) return;
        var radius = radiusPixels * pixelUnit;
        var pen = Math.Max(.45 * pixelUnit, penPixels * pixelUnit);
        var halfX = radius + pen * 1.6;
        var halfY = radius * .34 + pen * 1.6;
        if (!Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth, halfX, halfY, out var patch)) return;
        var radiusRatio = (float)Math.Clamp(radius / halfX, .01, 1);
        var widthRatio = (float)Math.Clamp(pen / halfX, .003, .35);
        quads.Add(Pack(patch, color, Math.Clamp(alpha, 0, 1), new Vector4(radiusRatio, widthRatio, 4, style)));
        PulseCount++;
    }

    private void AddSoftKeyGlow(ArView view, ArCameraPose pose, ArVec3 center, double halfX, double halfY,
        Vector3 color, float alpha, GpuInstanceList<GpuArQuad> quads)
    {
        if (alpha <= .004f || !(halfX > 0) || !(halfY > 0)) return;
        if (!Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth, halfX, halfY, out var patch)) return;
        quads.Add(Pack(patch, color, Math.Clamp(alpha, 0, 1), new Vector4(0, 0, 3, 0)));
    }

    private void BuildStageParticles(ArView view, GpuStageSimulation simulation,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose)
    {
        var emitted = 0;
        // Draw impact and release bursts before continuous emitters when the AR tier's per-frame fill cap is reached.
        for (var originPass = 0; originPass < 2 && emitted < MaxProjectedParticles; originPass++)
        {
            for (var i = simulation.ArParticleCount - 1; i >= 0 && emitted < MaxProjectedParticles; i--)
            {
                var particle = simulation.ArParticleAt(i);
                if ((originPass == 0 && particle.Origin == 2) || (originPass == 1 && particle.Origin != 2)) continue;
                if (!layout.Contains(particle.Pitch) || !(particle.Life > 0) || particle.Age < 0 || particle.Age >= particle.Life) continue;
                if (!TryAddProjectedParticle(view, simulation, particle, quads, opacity, look, layout, pose)) continue;
                emitted++;
                ParticleFxCount++;
                ObservePackedParticleForReview?.Invoke(particle);
                if (particle.Origin == 1) ReleaseEffectCount++;
            }
        }
    }

    private bool TryAddProjectedParticle(ArView view, GpuStageSimulation simulation, GpuArParticle particle,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose)
    {
        var unit = pose.KeyboardWidthDepthRatio / Math.Max(1, simulation.SimulationWidth);
        var xPixels = particle.X;
        var u = xPixels / Math.Max(1, simulation.SimulationWidth);
        var sourceKeyCentre = PianoStage.KeyCenterOf(particle.Pitch);
        var x = (layout.CenterX(particle.Pitch) + u - sourceKeyCentre) * pose.KeyboardWidthDepthRatio;
        var y = Math.Clamp((simulation.SimulationHitY - particle.Y) * unit, -.2, 2.4);
        if (particle.Wisp)
        {
            var amp = (5 + look.WispTurbulence * 26) * (.45f + look.WispWidth) * Math.Min(1, particle.Age * 1.6f);
            x += Math.Sin(particle.Age * (3 + look.WispTurbulence * 5) + particle.Phase) * amp * unit;
        }
        var center = new ArVec3(x, .12, -y);
        if (look.HeldParticleArt && (particle.Wisp || particle.Shape == ParticleFlame))
        {
            if (!HeldParticleAppearance.TryResolve(particle.Wisp, particle.Size, particle.Age, particle.Life,
                particle.Phase, particle.Glow, particle.Color, look.WispWidth, look.FlameNoteColor, out var surface, look.WispArtV3ForReview, look.FlameArtV3ForReview)
                || !ArParticleBillboard.TryFacing(pose, center, out var faceX, out var faceY)) return false;
            var halfWidth = surface.HalfWidth * unit; var halfHeight = surface.HalfHeight * unit;
            var surfaceCenter = center + faceY * (surface.BaseAnchored ? halfHeight : 0);
            if (surface.Color.W * opacity <= .003f
                || !Ar3DNoteProjection.TryProjectBillboard(pose, view, surfaceCenter, faceX, faceY,
                    halfWidth, halfHeight, out var facePatch)) return false;
            quads.Add(Pack(facePatch, new Vector3(surface.Color.X, surface.Color.Y, surface.Color.Z),
                Math.Clamp(surface.Color.W * opacity, 0, 1),
                new Vector4(surface.Progress, surface.Shape, 17, surface.Phase)));
            return true;
        }
        var progress = Math.Clamp(particle.Age / particle.Life, 0, 1);
        var fade = 1 - progress;
        var stageShape = particle.Wisp ? ParticleWisp : particle.Shape;
        var size = Math.Clamp(particle.Size * unit, .0015, .16);
        var alpha = opacity * look.ParticleGlow;
        var color = GpuStageSimulation.ToLinear(particle.Color);
        ArVec3 axisX = Across, axisY = Normal;
        double halfX = size, halfY = size;

        switch (stageShape)
        {
            case ParticleStreak:
            {
                var velocity = new ArVec3(particle.Vx * unit, 0, particle.Vy * unit);
                var speed = Math.Sqrt(particle.Vx * particle.Vx + particle.Vy * particle.Vy);
                axisX = velocity.Length > 1e-8 ? velocity.Normalized : Normal;
                axisY = ArVec3.Cross(axisX, Depth).Normalized;
                if (axisY.Length < .9) axisY = Across;
                var stretch = Math.Clamp(speed * .018, .5, 7);
                halfX = size * 1.4 * (1 + stretch);
                halfY = size * .75;
                color *= .9f + look.ParticleGlow * 1.6f;
                alpha *= fade;
                break;
            }
            case ParticleConfetti:
            {
                // The shader applies the stage's age/phase tumble in billboard-local coordinates; keep
                // the projected 3D axes fixed so the rotation is not accidentally applied twice.
                halfX = size * 1.6;
                halfY = size;
                color *= 1.1f;
                alpha = opacity * Math.Min(1, fade * 1.5f);
                break;
            }
            case ParticleDust:
                halfX = halfY = size * 2.4;
                color *= .6f;
                alpha *= fade * .35f;
                break;
            case ParticleFlame:
            {
                // UV y increases away from the key so the flame's broad base attaches to the surface and its tip rises.
                axisY = Normal * -1;
                var t = progress;
                var fire = t < .25 ? Vector3.Lerp(new Vector3(1f, .95f, .8f), new Vector3(1f, .78f, .3f), (float)(t / .25))
                    : t < .6 ? Vector3.Lerp(new Vector3(1f, .78f, .3f), new Vector3(1f, .38f, .08f), (float)((t - .25) / .35))
                    : Vector3.Lerp(new Vector3(1f, .38f, .08f), new Vector3(.45f, .03f, .01f), (float)((t - .6) / .4));
                if (look.FlameNoteColor) fire = Vector3.Lerp(fire, particle.Color, .6f);
                color = GpuStageSimulation.ToLinear(fire) * (1.3f * particle.Glow);
                var swelling = .6 + Math.Sin(Math.Min(1, progress * 1.4) * Math.PI) * .7;
                halfX = size * swelling * .7;
                halfY = size * swelling * 1.8;
                alpha = opacity * fade;
                break;
            }
            case ParticleDroplet:
            {
                var stretch = 1 + Math.Clamp(Math.Abs(particle.Vy) / 700, 0, 1.2);
                halfX = size * (.7 + fade * .5);
                halfY = halfX * stretch;
                color *= 1.2f;
                alpha *= (float)Math.Pow(fade, .8);
                break;
            }
            case ParticleShard:
            {
                var angle = particle.Phase + particle.Age * 7.3;
                var baseAxis = new ArVec3(Math.Cos(angle), 0, Math.Sin(angle));
                var velocity = new ArVec3(particle.Vx * unit, 0, particle.Vy * unit);
                axisX = velocity.Length > 1e-8 ? velocity.Normalized : baseAxis;
                axisY = ArVec3.Cross(axisX, Depth).Normalized;
                if (axisY.Length < .9) axisY = new ArVec3(-baseAxis.Z, 0, baseAxis.X);
                halfX = size * 1.2;
                halfY = size * .42;
                color *= 1.4f;
                alpha *= fade;
                break;
            }
            case ParticleWisp:
                halfX = size * (1.1 + look.WispWidth * .6);
                halfY = size * 2.2;
                color *= .6f + look.WispGlow * 1.6f;
                alpha = opacity * Math.Min(1, progress * 6) * fade;
                break;
            default: // compact ember/dot
                halfX = halfY = size * 1.25;
                color *= .9f + look.ParticleGlow * 1.6f;
                alpha *= fade;
                break;
        }

        if (alpha <= .003f || !Ar3DNoteProjection.TryProjectBillboard(pose, view, center, axisX, axisY, halfX, halfY, out var patch))
            return false;
        quads.Add(Pack(patch, color, Math.Clamp((float)alpha, 0, 1),
            new Vector4((float)progress, stageShape, 2, particle.Phase)));
        return true;
    }

    private void BuildStageFlashes(ArView view, GpuStageSimulation simulation,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose)
    {
        for (var i = 0; i < simulation.ArFlashCount; i++)
        {
            var flash = simulation.ArFlashAt(i);
            if (!layout.Contains(flash.Pitch) || !(flash.Life > 0) || flash.Age < 0 || flash.Age >= flash.Life) continue;
            // Like the stage pass, use the intensity captured by the simulation at the strike. A changed toggle
            // suppresses future flashes but does not erase one that is already in flight.
            if (flash.Intensity <= 0) continue;
            var t = Math.Clamp(flash.Age / flash.Life, 0, 1);
            var strength = Math.Clamp(.5f + flash.Strength * .5f, 0, 1.2f);
            var flashGain = flash.Style == 0 ? flash.Intensity : flash.Intensity * strength;
            var color = GpuStageSimulation.ToLinear(flash.Color) * Math.Max(1, flashGain);
            var center = StageSurfacePoint(simulation, layout, pose, flash.Pitch, flash.X, flash.Y);
            var fade = flash.Style is 0 or 2 ? (1 - t) * (1 - t) : 1 - t;
            var alpha = opacity * (float)fade * Math.Min(1, flashGain);
            if (alpha <= .003f) continue;

            switch (flash.Style)
            {
                case 1: // jagged 3D lightning from a key into the air above it
                {
                    var before = quads.Count;
                    AddLightningFlash(view, pose, simulation, center, flash.Pitch, look, quads, alpha);
                    FlashCount += quads.Count - before;
                    break;
                }
                case 2: // plasma: a coloured core and time-varying concentric caustics
                {
                    var pixelUnit = pose.KeyboardWidthDepthRatio / Math.Max(1, simulation.SimulationWidth);
                    var radius = (12 + t * (30 + look.RingSizeRaw)) * pixelUnit;
                    var plasma = new Vector3(.58f, .36f, 1f);
                    var plasmaColor = Vector3.Lerp(color, GpuStageSimulation.ToLinear(plasma), .72f) * 2.0f;
                    if (Ar3DNoteProjection.TryProjectBillboard(pose, view, center + new ArVec3(0, 0, -.05), Across, Normal,
                            radius * 1.3, radius * .95, out var plasmaPatch))
                    {
                        quads.Add(Pack(plasmaPatch, plasmaColor, Math.Clamp(alpha, 0, 1), new Vector4((float)t, 2, 6, flash.Pitch * .17f)));
                        FlashCount++;
                    }
                    break;
                }
                case 3: // star morph
                {
                    var radius = (14 + look.RingSizeRaw * .9) * (.5 + .5 * t) * pose.KeyboardWidthDepthRatio / Math.Max(1, simulation.SimulationWidth);
                    if (Ar3DNoteProjection.TryProjectBillboard(pose, view, center + new ArVec3(0, 0, -.035), Across, Normal,
                            radius, radius, out var starPatch))
                    {
                        quads.Add(Pack(starPatch, color * 1.8f, Math.Clamp(alpha, 0, 1), new Vector4((float)t, 3, 6, 0)));
                        FlashCount++;
                    }
                    break;
                }
                default: // the standard soft strike flare
                {
                    var pixelUnit = pose.KeyboardWidthDepthRatio / Math.Max(1, simulation.SimulationWidth);
                    var radius = Math.Max(2, flash.Size * (.8 + t * .6)) * pixelUnit;
                    if (Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Depth, radius * 2.6, radius * 1.2, out var flarePatch))
                    {
                        quads.Add(Pack(flarePatch, color * 2.0f, Math.Clamp(alpha, 0, 1), new Vector4((float)t, 0, 6, 0)));
                        FlashCount++;
                    }
                    break;
                }
            }
            if (flash.Origin == 1) ReleaseEffectCount++;
        }
    }

    private void AddLightningFlash(ArView view, ArCameraPose pose, GpuStageSimulation simulation, ArVec3 origin,
        int pitch, GpuLook look, GpuInstanceList<GpuArQuad> quads, float alpha)
    {
        var pixelUnit = pose.KeyboardWidthDepthRatio / Math.Max(1, simulation.SimulationWidth);
        var height = Math.Clamp((260 + look.RingSizeRaw * 2) * pixelUnit, .18, 2.2);
        var seed = pitch * 73 + (int)(simulation.Time * 24);
        var previous = origin + new ArVec3(0, 0, -height);
        var glow = GpuStageSimulation.ToLinear(new Vector3(140 / 255f, 180 / 255f, 1f)) * 1.8f;
        for (var i = 1; i <= 8; i++)
        {
            var f = i / 8.0;
            var jitter = i == 8 ? 0 : (Hash01(seed + i * 17) - .5) * 44 * pixelUnit;
            var point = origin + new ArVec3(jitter, 0, -height * (1 - f));
            _ = AddRibbon(view, pose, previous, point, Math.Max(.65 * pixelUnit, .0035), glow,
                Math.Clamp(alpha * .9f, 0, 1), quads, 5, (float)f, .45f);
            previous = point;
        }
    }

    private void BuildHoldArcs(ArView view, GpuStageSimulation simulation,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose)
    {
        if (!look.HoldElectricArc || look.HoldArcIntensity <= .01f) return;
        var ratio = pose.KeyboardWidthDepthRatio;
        var pixelUnit = ratio / Math.Max(1, simulation.SimulationWidth);
        var color = GpuStageSimulation.ToLinear(new Vector3(130 / 255f, 180 / 255f, 1f)) * 1.8f;
        var alpha = Math.Clamp(opacity * look.HoldArcIntensity * simulation.GlowBoost * .76f, 0, 1);
        var previousPitch = -1;
        var links = 0;
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch && links < 6; pitch++)
        {
            if (!simulation.IsActive(pitch)) continue;
            if (previousPitch >= 0)
            {
                var x0 = layout.CenterX(previousPitch) * ratio;
                var x1 = layout.CenterX(pitch) * ratio;
                var screenDistance = Math.Abs(layout.CenterX(pitch) - layout.CenterX(previousPitch)) * simulation.SimulationWidth;
                var lift = (26 + screenDistance * .12) * pixelUnit;
                var oldPoint = new ArVec3(x0, .12, -.012);
                for (var segment = 1; segment <= 10; segment++)
                {
                    var f = segment / 10.0;
                    var jitter = (Hash01((int)(simulation.Time * 24) + links * 131 + segment * 17) - .5) * 16 * pixelUnit;
                    var point = new ArVec3(x0 + (x1 - x0) * f + jitter,
                        .12 + Math.Sin(f * Math.PI * 2 + simulation.Time * 5) * .006,
                        -.012 - Math.Sin(f * Math.PI) * lift);
                    if (AddRibbon(view, pose, oldPoint, point, Math.Max(.45 * pixelUnit, .0025), color, alpha,
                            quads, 5, (float)f, .38f)) HoldArcCount++;
                    oldPoint = point;
                }
                links++;
            }
            previousPitch = pitch;
        }
    }

    private void BuildLightBeams(ArView view, IReadOnlyList<Vector4> keyColors,
        GpuInstanceList<GpuArQuad> quads, float opacity, GpuLook look, KeyboardLayout layout, ArCameraPose pose)
    {
        if (!look.ShowLightBeams || look.BeamIntensity <= .01f) return;
        var ratio = pose.KeyboardWidthDepthRatio;
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch && pitch < keyColors.Count; pitch++)
        {
            var key = keyColors[pitch];
            // Match the stage beam's glow tail and the AR camera-delay key history; do not require the current active bit.
            if (key.W <= MinKeyGlow) continue;
            var (left, right) = layout.Span(pitch);
            var keyHalf = (right - left) * ratio * .5;
            var height = .32 + .62 * look.BeamIntensity;
            var center = new ArVec3(layout.CenterX(pitch) * ratio, .12, -height * .5);
            if (!Ar3DNoteProjection.TryProjectBillboard(pose, view, center, Across, Normal,
                    keyHalf * .54, height * .5, out var beamPatch)) continue;
            var color = new Vector3(key.X, key.Y, key.Z) * (.8f + look.BeamIntensity * 1.4f);
            quads.Add(Pack(beamPatch, color, Math.Clamp(opacity * look.BeamIntensity * Math.Min(1, key.W), 0, 1),
                new Vector4(look.BeamIntensity, 0, 7, 0)));
            BeamCount++;
        }
    }

    private static ArVec3 StageSurfacePoint(GpuStageSimulation simulation, KeyboardLayout layout, ArCameraPose pose,
        int pitch, float x, float y)
    {
        var width = Math.Max(1, simulation.SimulationWidth);
        var u = x / width;
        var sourceCentre = PianoStage.KeyCenterOf(pitch);
        var targetCentre = layout.CenterX(pitch);
        var worldX = (targetCentre + u - sourceCentre) * pose.KeyboardWidthDepthRatio;
        var rise = (simulation.SimulationHitY - y) * pose.KeyboardWidthDepthRatio / width;
        return new ArVec3(worldX, .12, -Math.Clamp(rise, -.2, 2.4));
    }

    private static bool AddRibbon(ArView view, ArCameraPose pose, ArVec3 start, ArVec3 end, double halfWidth,
        Vector3 color, float alpha, GpuInstanceList<GpuArQuad> quads, float kind, float progress, float softness)
    {
        var axis = end - start;
        var length = axis.Length;
        if (!(length > 1e-7) || alpha <= .003f) return false;
        axis /= length;
        var across = ArVec3.Cross(axis, Depth).Normalized;
        if (across.Length < .9) across = ArVec3.Cross(axis, Across).Normalized;
        if (across.Length < .9) across = Normal;
        if (!Ar3DNoteProjection.TryProjectBillboard(pose, view, (start + end) * .5, axis, across,
                length * .56, halfWidth, out var patch)) return false;
        quads.Add(Pack(patch, color, Math.Clamp(alpha, 0, 1), new Vector4(progress, softness, kind, 0)));
        return true;
    }

    private static ArPatch VibrateLiveBar(ArCalibration calibration, ArView view, int pitch, ArPatch patch,
        GpuLook look, double time, float simulationWidth)
    {
        if (calibration.CameraPose is not { } pose || !view.Valid) return patch;
        var offset = Math.Sin(time * 40 + pitch * 2.2) * look.HoldVibrationAmount * 4
            * pose.KeyboardWidthDepthRatio / Math.Max(1, simulationWidth);
        if (Math.Abs(offset) < 1e-9) return patch;
        var center = new ArVec3(calibration.Layout.CenterX(pitch) * pose.KeyboardWidthDepthRatio, 0, 0);
        if (!pose.TryProject(center, view, out var original) || !pose.TryProject(center + new ArVec3(offset, 0, 0), view, out var moved)) return patch;
        var dx = moved.X - original.X; var dy = moved.Y - original.Y;
        ArCorner Shift(ArCorner corner) => new(corner.X + dx, corner.Y + dy, corner.W);
        return new ArPatch(Shift(patch.BackLeft), Shift(patch.BackRight), Shift(patch.FrontRight), Shift(patch.FrontLeft));
    }

    private static float Hash01(int seed)
    {
        var value = Math.Sin(seed * 127.1 + 311.7) * 43758.5453;
        return (float)(value - Math.Floor(value));
    }
}
