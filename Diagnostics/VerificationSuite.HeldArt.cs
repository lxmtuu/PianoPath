using System.Numerics;

namespace PianoPath;

internal static partial class VerificationSuite
{
    private static void VerifyHeldParticleArt(GpuArLayer layer, GpuArInput ar, ArView view,
        GpuInstanceList<GpuSpriteInstance> sprites, GpuInstanceList<GpuArQuad> quads)
    {
        GpuKeyInstance[] lightControls = [
            new() { Box=new(1,2,3,4), Emit=new(.2f,.3f,.4f,.7f), Misc=new(0,.4f,.2f,.1f), Base=new(1,.8f,2,3) },
            new() { Box=new(2,3,4,5), Emit=new(.8f,.4f,.2f,.9f), Misc=new(1,.7f,.2f,.1f), Base=new(2,.5f,3,4) },
            new() { Box=new(3,4,5,6), Emit=new(.3f,.2f,.1f,.6f), Misc=new(2,0,0,0), Base=new(3,.2f,4,5) }
        ];
        var beforeMask = lightControls.ToArray();
        GpuStageRenderer.MaskPressedKeyLightForReview(lightControls);
        for (var k=0;k<lightControls.Length;k++)
        {
            var before=beforeMask[k]; var after=lightControls[k];
            Assert(after.Box==before.Box && after.Misc==before.Misc && after.Base.X==before.Base.X
                && after.Base.Z==before.Base.Z && after.Base.W==before.Base.W,
                "The review light-off mask must preserve key geometry, depression and shadows.");
            Assert(after.Emit.X==before.Emit.X && after.Emit.Y==before.Emit.Y && after.Emit.Z==before.Emit.Z,
                "The review light-off mask must not replace note colour.");
            Assert(k==2 ? after.Emit.W==before.Emit.W && after.Base.Y==before.Base.Y : after.Emit.W==0 && after.Base.Y==0,
                "Only real-key tint/emission/spill are muted; the felt is unchanged.");
        }
        foreach (var tint in new[] {false,true})
        foreach (var spill in new[] {false,true})
        {
            var split=beforeMask.ToArray(); GpuStageRenderer.MaskPressedKeyLightForReview(split,tint,spill);
            for (var k=0;k<split.Length;k++)
            {
                var before=beforeMask[k]; var after=split[k];
                Assert(after.Box==before.Box && after.Misc==before.Misc && after.Base.X==before.Base.X
                    && after.Base.Z==before.Base.Z && after.Base.W==before.Base.W,
                    "Split review mask preserves geometry/depression/shadows.");
                Assert(after.Emit.X==before.Emit.X && after.Emit.Y==before.Emit.Y && after.Emit.Z==before.Emit.Z
                    && after.Emit.W==(k<2 && tint ? 0 : before.Emit.W)
                    && after.Base.Y==(k<2 && spill ? 0 : before.Base.Y),
                    "Tint/emission and spill masks are independent; felt stays untouched.");
            }
        }
        Assert(!new PianoVisualSettings().HeldParticleArt && !CommunityPresets.Find("Prism Flow Preview")!.Settings.HeldParticleArt,
            "Legacy defaults and the particle-off palette control must not enable experimental art.");
        foreach (var route in AuxParticleTimeline.Routes.Where(r => r.Source == AuxParticleTimeline.Channel.Held))
        foreach (var pitch in new[] { 60, 61 })
        {
            var settings = AuxParticleFixtures.Settings(route); settings.HeldParticleArt = true;
            var clone = settings.Clone(); var applied = new PianoVisualSettings(); applied.CopyFrom(clone);
            Assert(clone.HeldParticleArt && applied.HeldParticleArt, "Held art must survive JSON/clone/apply.");
            var art = ParticleLook(settings); settings.HeldParticleArt = false; var legacy = ParticleLook(settings);
            var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var feed = new GpuStageFeed(); feed.SetLook(art); feed.SetStageHeight(720);
            var input = new GpuFrameInput();
            for (var f = 0; f < 36; f++)
            {
                AuxParticleFixtures.Publish(feed, route, "active", f, pitch); feed.Capture(input, GpuStageFeed.Now);
                sim.Step(1.0 / 60, input, feed, 1280);
            }
            var snapshot = ParticleSnapshot(sim); var time = sim.Time; var born = sim.ParticlesBorn;
            var layout = new GpuSceneLayout(1280,720,art.KeyboardFraction);
            sim.BuildSprites(art, layout, sprites);
            var stage = sprites.Span.ToArray().Where(s => s.PosSize.W == 17).ToArray();
            Assert(stage.Length > 0 && stage.All(s => s.Dir.X > 0 && s.Dir.Z == (route.Wisp ? 7 : 4)),
                $"{route.Id}/{pitch}: Stage must use the new shared material, not the classic dot.");
            input.Look = art; layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
            var projected = quads.Span.ToArray().Where(q => q.Misc.Z == 17).ToArray();
            Assert(projected.Length == stage.Length, $"{route.Id}/{pitch}: camera-facing held particles must survive this calibration.");
            foreach (var tint in new[] {false,true})
            foreach (var spill in new[] {false,true})
            {
                layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true,
                    suppressKeyTintForReview:!tint,suppressKeySpillForReview:!spill);
                Assert((tint ? layer.KeyQuadCount>0 : layer.KeyQuadCount==0)
                    && (spill ? layer.NoteLightCount>0 : layer.NoteLightCount==0)
                    && layer.ParticleFxCount==projected.Length,
                    "AR tint/spill review gates must independently preserve particle packing.");
            }
            layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);
            foreach (var p in snapshot)
            {
                Assert(HeldParticleAppearance.TryResolve(p.Wisp,p.Size,p.Age,p.Life,p.Phase,p.Glow,p.Color,art.WispWidth,art.FlameNoteColor,out var surface),
                    "Living production particles must resolve finite shared surfaces.");
                if (surface.Color.W <= .003f) continue;
                Assert(stage.Any(s => s.Color == surface.Color && s.Dir.Y == surface.Progress && s.Dir.W == surface.Phase)
                    && projected.Any(q => q.Color == surface.Color && q.Misc.X == surface.Progress && q.Misc.W == surface.Phase),
                    "Both builders must pack identical intrinsic colour, alpha, lifetime and phase.");
            }
            sim.BuildSprites(legacy,layout,sprites); input.Look=legacy;
            layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);
            Assert(sprites.Span.ToArray().All(s => s.PosSize.W != 17) && quads.Span.ToArray().All(q => q.Misc.Z != 17),
                "Disabling the prototype must use the retained classic path.");
            Assert(sim.Time == time && sim.ParticlesBorn == born && ParticleSnapshot(sim).SequenceEqual(snapshot),
                "Switching art/drawing another output cannot consume RNG, age or spawn particles.");
            feed.ClearTransient(); sim.Step(.01,input,feed,1280);
            Assert(sim.ParticleCount == 0 && sim.LiveTrailCount == 0, "Prototype surfaces must retain the frame-epoch clear boundary.");
        }
        Results.Add("PASS P3b held art: opt-in JSON/apply/snapshot; shared wisps/flames surface payloads, classic path, read-only dual-output builds and epoch clear. No all-family redesign, real-camera/motion quality or hardware claim.");
    }
}
