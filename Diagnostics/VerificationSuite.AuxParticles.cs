using System.Numerics;

namespace PianoPath;

internal static partial class VerificationSuite
{
    private static void VerifyAuxParticleLifecycle(GpuArLayer layer, GpuArInput ar, ArView view,
        GpuInstanceList<GpuSpriteInstance> sprites, GpuInstanceList<GpuArQuad> quads)
    {
        Assert(PianoVisualSettings.ImpactMorphs.ToHashSet().SetEquals(
            AuxParticleTimeline.Routes.Where(r => r.Source == AuxParticleTimeline.Channel.Impact).Select(r => r.Effect)
                .Concat(new[] { "None", "Absorb", "Morph" })), "Every impact morph must be classified as particle or non-particle.");
        Assert(PianoVisualSettings.ReleaseEffects.ToHashSet().SetEquals(
            AuxParticleTimeline.Routes.Where(r => r.Source == AuxParticleTimeline.Channel.Release).Select(r => r.Effect)
                .Concat(new[] { "Fade", "Snap Back", "Echo Rings" })), "Every release effect must be classified as particle or non-particle.");
        foreach (var route in AuxParticleTimeline.Routes)
        foreach (var pitch in new[] { 60, 61 })
        {
            var settings = AuxParticleFixtures.Settings(route); var look = ParticleLook(settings);
            var feed = new GpuStageFeed(); feed.SetLook(look); feed.SetStageHeight(720);
            var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var input = new GpuFrameInput();
            var frames = (int)Math.Round(AuxParticleTimeline.SampleTime(route, "active") * 60);
            for (var f = 0; f < frames; f++)
            {
                AuxParticleFixtures.Publish(feed, route, "active", f, pitch);
                feed.Capture(input, GpuStageFeed.Now); sim.Step(1.0 / 60, input, feed, 1280);
                if (route.Source == AuxParticleTimeline.Channel.Release && f < 21)
                    Assert(sim.ParticlesBorn == 0, $"{route.Id}/{pitch}: Note On/held state must not emit a release burst.");
            }
            var snapshot = ParticleSnapshot(sim); var born = sim.ParticlesBorn; var time = sim.Time;
            var origin = route.Source switch { AuxParticleTimeline.Channel.Held => 2, AuxParticleTimeline.Channel.Release => 1, _ => 0 };
            Assert(snapshot.Length > 0 && snapshot.All(p => p.Pitch == pitch && p.Shape == route.Shape && p.Wisp == route.Wisp && p.Origin == origin),
                $"{route.Id}/{pitch}: production births retain channel, source pitch and material shape tags.");
            Assert(snapshot.All(p=>p.MaterialRoute==(route.Id switch {"morph-melt"=>1,"morph-bounce"=>2,"release-float-up"=>3,"release-dissolve"=>4,"release-smoke"=>5,_=>0})),
                $"{route.Id}/{pitch}: Melt/Bounce/release births carry distinct material provenance; not a shape/origin heuristic.");
            Assert(snapshot.All(p => p.Age >= 0 && p.Age < p.Life && float.IsFinite(p.X) && float.IsFinite(p.Y)),
                $"{route.Id}/{pitch}: active baseline must contain finite living particles.");
            Assert(route.Source == AuxParticleTimeline.Channel.Held ? sim.EmitterParticlesBorn == born : sim.EmitterParticlesBorn == 0,
                $"{route.Id}/{pitch}: release/morph births are not continuous emitter births.");
            var layout = new GpuSceneLayout(1280, 720, look.KeyboardFraction);
            sim.BuildSprites(look, layout, sprites);
            Assert(sprites.Count > 0, $"{route.Id}/{pitch}: Stage must build the selected channel.");
            layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
            Assert(layer.ParticleFxCount > 0, $"{route.Id}/{pitch}: AR must project the selected channel.");
            if (route.Source == AuxParticleTimeline.Channel.Release)
                Assert(layer.ReleaseEffectCount > 0, $"{route.Id}/{pitch}: AR must retain release provenance.");
            layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
            sim.BuildSprites(look, layout, sprites);
            Assert(sim.Time == time && sim.ParticlesBorn == born && ParticleSnapshot(sim).SequenceEqual(snapshot),
                $"{route.Id}/{pitch}: repeated Stage/AR builds must not spawn or advance the timeline.");
            if (route.Source == AuxParticleTimeline.Channel.Held)
            {
                feed.SetDeterministicState([], 1, false, new HashSet<int>()); feed.SetSustainLevel(true, 1);
                feed.Capture(input, GpuStageFeed.Now); sim.Step(.05, input, feed, 1280);
                Assert(route.Wisp ? sim.ParticlesBorn == born : sim.ParticlesBorn > born,
                    $"{route.Id}/{pitch}: wisps stop births on release; flames intentionally keep cooling-heat births, not pedal ownership.");
            }
            else if (route.Source == AuxParticleTimeline.Channel.Release)
            {
                feed.LiveNote(pitch, false, .9); sim.Step(0, input, feed, 1280);
                Assert(sim.ParticlesBorn == born, $"{route.Id}/{pitch}: duplicate Note Off cannot spawn a second release burst.");
                VerifyAuxSongRelease(route, pitch);
            }
            for (var f = 0; f < 180; f++) sim.Step(1.0 / 60, input, feed, 1280);
            // Held input was explicitly released above; event-only channels never have an active emitter.
            Assert(sim.ParticleCount == 0, $"{route.Id}/{pitch}: tails must expire even under pedal.");
            sim.Clear();
            Assert(sim.ParticleCount == 0 && sim.ParticlesBorn == 0 && sim.EmitterParticlesBorn == 0,
                $"{route.Id}/{pitch}: soft clear removes auxiliary channel births.");
            var disabled = new GpuStageFeed(); disabled.SetLook(ParticleLook(AuxParticleFixtures.Settings(route, false)));
            var dark = new GpuStageSimulation(KeyParticleFixtures.Seed); var control = new GpuFrameInput();
            for (var f = 0; f < frames; f++)
            {
                AuxParticleFixtures.Publish(disabled, route, "off", f, pitch);
                disabled.Capture(control, GpuStageFeed.Now); dark.Step(1.0 / 60, control, disabled, 1280);
            }
            Assert(dark.ParticlesBorn == 0 && dark.ParticleCount == 0,
                $"{route.Id}/{pitch}: same-input effect-off control cannot borrow another particle family.");
        }
        VerifyMeltMixedRoutes(layer,ar,view,sprites,quads);
        VerifyBounceMixedRoutes(layer,ar,view,sprites,quads);
        VerifyReleaseMixedRoutes(layer,ar,view,sprites,quads);
        Results.Add("PASS P3a auxiliary lifecycle: wisps/flames, Shatter/Melt/Bounce and Float Up/Dissolve/Smoke; white/black keys, tagged births, read-only Stage/AR builds, physical-release/heat/pedal policy, duplicate Note Off, song release cursor, disabled controls and expired tails. Baseline only, not new art or nonzero camera delay.");
    }

    private static void VerifyMeltMixedRoutes(GpuArLayer layer,GpuArInput ar,ArView view,
        GpuInstanceList<GpuSpriteInstance> sprites,GpuInstanceList<GpuArQuad> quads)
    {
        var settings=AuxParticleFixtures.Settings(AuxParticleTimeline.Routes.Single(r=>r.Id=="morph-melt"));
        settings.ShowEmbers=true;settings.ImpactBurst="Splash";settings.ParticleAmount=18;
        var classic=ParticleLook(settings);var art=classic.WithMeltArtForReview(true).WithMeltFrontLayerForReview(true);
        Assert(!classic.MeltArtForReview && !classic.MeltFrontLayerForReview,"Melt material must remain default-off.");
        var feed=new GpuStageFeed();feed.SetLook(classic);feed.SetStageHeight(720);
        feed.SetDeterministicState([],1.0/60,false,new HashSet<int>());feed.Impact(60,.9);
        var input=new GpuFrameInput();feed.Capture(input,GpuStageFeed.Now);
        var sim=new GpuStageSimulation(KeyParticleFixtures.Seed);sim.Step(1.0/60,input,feed,1280);
        var raw=ParticleSnapshot(sim);var born=sim.ParticlesBorn;var time=sim.Time;
        var melt=raw.Count(p=>p.MaterialRoute==MeltParticleAppearance.Route);
        Assert(melt>0 && raw.Length>melt && raw.All(p=>p.Shape==5 && !p.Wisp && p.Origin==0),
            "Mixed Melt+Splash has colliding shape/origin tags but distinct source metadata.");
        var layout=new GpuSceneLayout(1280,720,classic.KeyboardFraction);
        sim.BuildSprites(classic,layout,sprites);var before=sprites.Span.ToArray();
        sim.BuildSprites(art,layout,sprites);var after=sprites.Span.ToArray();
        Assert(before.Length==raw.Length*3 && after.Length==before.Length,"Mixed Stage keeps exactly three components per droplet.");
        for(var i=0;i<raw.Length;i++)for(var c=0;c<3;c++)
        {
            var a=before[i*3+c];var b=after[i*3+c];
            if(raw[i].MaterialRoute==0)Assert(a.Equals(b),"Melt candidate cannot recolour primary Splash.");
            else
            {
                Assert(b.PosSize.W==(c==0?37:c==1?36:38),"Melt keeps halo/body/glint component order.");
                b.PosSize.W=a.PosSize.W;b.Color=a.Color;
                Assert(a.Equals(b),"Melt candidate cannot alter actual Stage position, axes, extents or softness.");
            }
        }
        layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var arBefore=quads.Span.ToArray();
        feed.SetLook(art);feed.Capture(input,GpuStageFeed.Now);
        layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var arAfter=quads.Span.ToArray();
        Assert(arBefore.Length==arAfter.Length && arAfter.Count(q=>q.Misc.Z==33)==melt,
            "Mixed AR selects only Melt source bodies, preserving count and order.");
        for(var i=0;i<arBefore.Length;i++)
        {
            var a=arBefore[i];var b=arAfter[i];
            if(b.Misc.Z==33){b.Misc.Z=a.Misc.Z;b.Color=a.Color;}
            Assert(a.Equals(b),"Melt cannot change AR geometry or a non-Melt packet.");
        }
        Assert(sim.Time==time && sim.ParticlesBorn==born && raw.SequenceEqual(ParticleSnapshot(sim)),
            "Mixed repeated builds are read-only including material provenance.");
        Results.Add("PASS Melt mixed-route isolation: same-shape/origin primary Splash unchanged, actual Stage/AR geometry/order/count unchanged; only tagged Melt material changes; read-only builds and default-off.");
    }

    private static void VerifyBounceMixedRoutes(GpuArLayer layer,GpuArInput ar,ArView view,
        GpuInstanceList<GpuSpriteInstance> sprites,GpuInstanceList<GpuArQuad> quads)
    {
        foreach(var family in new[]{"Embers","Fireworks"})
        {
            var settings=AuxParticleFixtures.Settings(AuxParticleTimeline.Routes.Single(r=>r.Id=="morph-bounce"));
            settings.ShowEmbers=true;settings.ImpactBurst=family;settings.ParticleAmount=18;
            var classic=ParticleLook(settings);var art=classic.WithBounceArtForReview(true);
            Assert(!classic.BounceArtForReview,"Bounce must remain default-off.");
            var feed=new GpuStageFeed();feed.SetLook(classic);feed.SetStageHeight(720);
            feed.SetDeterministicState([],1.0/60,false,new HashSet<int>());feed.Impact(60,.9);
            var input=new GpuFrameInput();feed.Capture(input,GpuStageFeed.Now);
            var sim=new GpuStageSimulation(KeyParticleFixtures.Seed);sim.Step(1.0/60,input,feed,1280);
            var raw=ParticleSnapshot(sim);var born=sim.ParticlesBorn;var time=sim.Time;
            var bounce=raw.Count(p=>p.MaterialRoute==BounceParticleAppearance.Route);
            Assert(bounce>0 && raw.Any(p=>p.MaterialRoute==0 && p.Shape==1 && p.Origin==0),
                $"Mixed Bounce+{family} must actually include colliding streak shape/origin with distinct provenance.");
            var layout=new GpuSceneLayout(1280,720,classic.KeyboardFraction);
            sim.BuildSprites(classic,layout,sprites);var before=sprites.Span.ToArray();
            sim.BuildSprites(art,layout,sprites);var after=sprites.Span.ToArray();
            Assert(before.Length==after.Length && after.Count(q=>q.PosSize.W==39)==bounce,
                "Mixed Stage retains the companion ring and one body per selected Bounce particle.");
            for(var i=0;i<before.Length;i++)
            {
                var a=before[i];var b=after[i];
                if(b.PosSize.W==39){Assert(a.PosSize.W==1,"Bounce body was a streak.");b.PosSize.W=a.PosSize.W;b.Color=a.Color;}
                Assert(a.Equals(b),"Bounce cannot change non-body/ring/primary packets or Stage geometry/order.");
            }
            layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var arBefore=quads.Span.ToArray();
            feed.SetLook(art);feed.Capture(input,GpuStageFeed.Now);
            layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var arAfter=quads.Span.ToArray();
            Assert(arBefore.Length==arAfter.Length && arAfter.Count(q=>q.Misc.Z==39)==bounce,
                "Mixed AR changes only Bounce source bodies, preserving count/order.");
            for(var i=0;i<arBefore.Length;i++)
            {
                var a=arBefore[i];var b=arAfter[i];
                if(b.Misc.Z==39){Assert(a.Misc.Z==2,"Bounce body was a particle quad.");b.Misc.Z=a.Misc.Z;b.Color=a.Color;}
                Assert(a.Equals(b),"Bounce cannot alter primary/ring AR packets or body corners/weights.");
            }
            var billboard=art.WithBounceBillboardForReview(true);
            feed.SetLook(billboard);feed.Capture(input,GpuStageFeed.Now);
            layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var aligned=quads.Span.ToArray();
            Assert(aligned.Length==arAfter.Length,"Bounce billboard preserves actual mixed count.");
            for(var i=0;i<aligned.Length;i++)
            {
                var a=arAfter[i];var b=aligned[i];
                if(b.Misc.Z==39){b.A=a.A;b.B=a.B;b.W=a.W;}
                Assert(a.Equals(b),"Bounce billboard cannot change primary/ring packets, body RGBA or ordering.");
            }
            sim.BuildSprites(billboard,layout,sprites);
            Assert(after.SequenceEqual(sprites.Span.ToArray()),"AR-only Bounce geometry flag cannot change Stage packets.");
            Assert(sim.Time==time && sim.ParticlesBorn==born && raw.SequenceEqual(ParticleSnapshot(sim)),
                "Mixed Bounce builds are read-only including material provenance.");
        }
        Results.Add("PASS Bounce mixed-route isolation: same-shape/origin Ember/Firework streaks unchanged, companion ring unchanged, Stage/AR geometry/order/counts unchanged; only tagged Bounce body material changes, read-only and default-off.");
    }

    private static void VerifyReleaseMixedRoutes(GpuArLayer layer,GpuArInput ar,ArView view,
        GpuInstanceList<GpuSpriteInstance> sprites,GpuInstanceList<GpuArQuad> quads)
    {
        var settings=AuxParticleFixtures.Settings(AuxParticleTimeline.Routes.Single(r=>r.Id=="release-float-up"));
        settings.ShowEmbers=true;settings.ImpactBurst="Splash";settings.ParticleAmount=18;
        settings.ImpactMorph="Melt";settings.ImpactMorphIntensity=70;
        var feed=new GpuStageFeed();feed.SetStageHeight(720);var sim=new GpuStageSimulation(KeyParticleFixtures.Seed);var input=new GpuFrameInput();
        var tick=0;
        foreach(var effect in new[]{"Float Up","Dissolve","Smoke"})
        {
            settings.ReleaseEffect=effect;var current=ParticleLook(settings);feed.SetLook(current);
            feed.SetDeterministicState([],++tick/60.0,false,new HashSet<int>());feed.SetSustainLevel(true,1);
            if(tick==1)feed.Impact(60,.9);
            feed.LiveNote(61,true,.9);feed.LiveNote(61,false,.9);
            feed.Capture(input,GpuStageFeed.Now);sim.Step(1.0/60,input,feed,1280);
        }
        var classic=ParticleLook(settings);var art=classic.WithReleaseArtForReview(true);
        Assert(!classic.ReleaseArtForReview && !classic.ReleaseFrontLayerForReview,"Release material/depth flags must default off.");
        var raw=ParticleSnapshot(sim);var time=sim.Time;var born=sim.ParticlesBorn;
        Assert(new[]{0,1,3,4,5}.All(route=>raw.Any(p=>p.MaterialRoute==route)),
            "Mixed fixture must retain primary Splash, Melt and all three simultaneous release tails across setting changes.");
        var layout=new GpuSceneLayout(1280,720,classic.KeyboardFraction);
        sim.BuildSprites(classic,layout,sprites);var before=sprites.Span.ToArray();
        sim.BuildSprites(art,layout,sprites);var after=sprites.Span.ToArray();
        Assert(before.Length==after.Length,"Release material cannot change component count/order.");
        Assert(after.Count(q=>q.PosSize.W==40)==raw.Count(p=>p.MaterialRoute==3)
            && after.Count(q=>q.PosSize.W==43)==raw.Count(p=>p.MaterialRoute==4)
            && after.Count(q=>q.PosSize.W==46)==raw.Count(p=>p.MaterialRoute==5),"All release source tags must retain their own pigment even after changing current release setting.");
        for(var i=0;i<before.Length;i++)
        {
            var a=before[i];var b=after[i];
            if(b.PosSize.W>=40 && b.PosSize.W<=46){b.PosSize.W=a.PosSize.W;b.Color=a.Color;}
            Assert(a.Equals(b),"Release material cannot alter primary/morph packets, geometry or ordered non-body data.");
        }
        feed.SetLook(classic);feed.Capture(input,GpuStageFeed.Now);
        layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var arBefore=quads.Span.ToArray();
        feed.SetLook(art);feed.Capture(input,GpuStageFeed.Now);
        layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);var arAfter=quads.Span.ToArray();
        Assert(arBefore.Length==arAfter.Length,"Release AR keeps ordered counts.");
        for(var i=0;i<arBefore.Length;i++)
        {
            var a=arBefore[i];var b=arAfter[i];
            if(b.Misc.Z is 40 or 43 or 46){b.Misc.Z=a.Misc.Z;b.Color=a.Color;}
            Assert(a.Equals(b),"Release cannot alter AR corners/weights, primary Splash/Melt packets or non-body data.");
        }
        var front=art.WithReleaseFrontLayerForReview(true);
        sim.BuildSprites(front,layout,sprites);var foreground=sprites.Span.ToArray();
        Assert(foreground.Length==after.Length,"Release front-layer keeps all accepted components.");
        for(var i=0;i<after.Length;i++)
        {
            var a=after[i];var b=foreground[i];
            if(a.PosSize.W>=40 && a.PosSize.W<=46){Assert(b.PosSize.W==a.PosSize.W+7,"Front-layer changes only tagged release kind.");b.PosSize.W=a.PosSize.W;}
            Assert(a.Equals(b),"Release depth option cannot change XY geometry/RGBA/non-release data.");
        }
        feed.SetLook(front);feed.Capture(input,GpuStageFeed.Now);layer.Build(input,ar,view,sim.KeyColors,sim,quads,projectedNotes:true);
        Assert(arAfter.SequenceEqual(quads.Span.ToArray()),"Stage-only release depth flag cannot change AR.");
        Assert(time==sim.Time && born==sim.ParticlesBorn && raw.SequenceEqual(ParticleSnapshot(sim)),"Mixed release builds are read-only, including provenance and RNG-dependent future state.");
        Results.Add("PASS release mixed provenance: Float Up/Dissolve/Smoke tails coexist after setting changes, primary Splash/Melt unchanged; actual Stage/AR geometry/count/order and state unchanged, separate Stage-only depth tag; default-off.");
    }

    private static void VerifyAuxSongRelease(AuxParticleTimeline.Route route, int pitch)
    {
        var feed = new GpuStageFeed(); feed.SetLook(ParticleLook(AuxParticleFixtures.Settings(route)));
        var notes = new[] { new NoteEvent { Pitch = pitch, Start = 0, Duration = .35, Velocity = 100 } };
        var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var input = new GpuFrameInput();
        feed.SetDeterministicState(notes, 0, true, new HashSet<int>());
        feed.Capture(input, GpuStageFeed.Now); sim.Step(0, input, feed, 1280); // establish cursor before the first interval
        for (var f = 0; f < 30; f++)
        {
            feed.SetDeterministicState(notes, (f + 1) / 60.0, true, new HashSet<int>());
            feed.SetSustainLevel(true, 1); feed.Capture(input, GpuStageFeed.Now); sim.Step(1.0 / 60, input, feed, 1280);
        }
        Assert(sim.ParticlesBorn > 0 && ParticleSnapshot(sim).All(p => p.Origin == 1 && p.Pitch == pitch),
            $"{route.Id}/{pitch}: crossing a song note end must also create tagged release particles.");
        var born = sim.ParticlesBorn;
        for (var f = 0; f < 30; f++) sim.Step(1.0 / 60, input, feed, 1280);
        Assert(sim.ParticlesBorn == born, $"{route.Id}/{pitch}: the same song position cannot replay release births.");
    }
}
