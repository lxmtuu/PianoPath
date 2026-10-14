using System.Collections.Concurrent;
using System.Numerics;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>Exercise the actual feed and simulation across the Capture → Clear → Step interleaving.</summary>
    private static void VerifyTransientFrameEpoch()
    {
        foreach (var fixture in KeyParticleFixtures.All)
        foreach (var pitch in fixture.Zone ? new[] { fixture.Pitch } : new[] { 60, 61 })
        {
            var settings = KeyParticleFixtures.Settings(fixture); settings.ShowNotes = true;
            var look = ParticleLook(settings);
            var feed = new GpuStageFeed(); feed.SetLook(look); feed.SetStageHeight(720);
            var notes = new[] { new NoteEvent { Pitch = pitch, Start = 0, Duration = 4, Velocity = 100 } };
            feed.SetDeterministicState(notes, .5, true, new HashSet<int> { pitch });
            feed.LiveNote(pitch, true, .9); feed.Impact(pitch, .9);
            var input = new GpuFrameInput(); var older = new GpuFrameInput();
            var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
            for (var i = 0; i < 36; i++) { feed.Capture(input, GpuStageFeed.Now); sim.Step(1.0 / 60, input, feed, 1280); }
            Assert(sim.ParticleCount > 0 && sim.LiveTrailCount == 1 && input.Pressed[pitch],
                $"{fixture.Id}/{pitch}: epoch test must start with real burst/held particles and an old pressed snapshot.");
            feed.Capture(older, GpuStageFeed.Now);
            var oldGeneration = input.Generation; var time = sim.Time;
            var newLook = ParticleLook(settings);
            var history = new ArKeyHistory(); history.Record(1, sim.KeyColors, input.Generation);

            // Force the real two-thread race in a known order, without relying on sleeps or runner speed.
            var worker = Task.Run(() =>
            {
                feed.ClearTransient();
                feed.SetDeterministicState([], 10, false, new HashSet<int>());
                feed.SetSustainLevel(true, 1); // pedal may still be down after physical release/seek
                feed.SetDeterministicBeatPulse(0);
                feed.SetStageHeight(360); feed.SetLook(newLook); feed.SetPointer(.2, .8);
            });
            Assert(worker.Wait(TimeSpan.FromSeconds(5)), "Controlled feed clear must complete without waiting for GPU work.");
            worker.GetAwaiter().GetResult();
            sim.Step(.01, input, feed, 1280); // deliberately do NOT Capture again on the test side
            Assert(input.Generation == oldGeneration + 1 && input.Position == 10 && !input.Playing
                    && input.Notes.Count == 0 && input.Pressed.All(p => !p) && input.Sustain && input.SustainLevel == 1
                    && ReferenceEquals(input.Look, newLook) && input.PointerX == .2f && input.PointerY == .8f,
                $"{fixture.Id}/{pitch}: Step must refresh the whole stale snapshot, not just clear particle lists.");
            Assert(sim.ParticleCount == 0 && sim.EmitterParticlesBorn == 0 && sim.ArImpacts.Count == 0
                    && sim.LiveTrailCount == 0 && sim.EmissionCredits(pitch) == (0d, 0d, 0d)
                    && sim.KeyColors.All(c => c.W == 0) && Math.Abs(sim.Time - time - .01) < .00001,
                $"{fixture.Id}/{pitch}: a stale held/song state cannot revive emission, heat or trails; soft clear preserves time.");
            Assert(input.StageHeightDip == 360 && Math.Abs((float)Field(sim, "_simWidth") - 640) < .001f,
                "A refreshed height must preserve the output aspect instead of mixing old width with new height.");
            var instances = new GpuInstanceList<GpuNoteInstance>(16);
            sim.BuildNotes(input, new GpuSceneLayout(640, 360, look.KeyboardFraction), instances);
            Assert(instances.Count == 0, $"{fixture.Id}/{pitch}: render builders must also see the refreshed stopped song.");
            history.Record(1.1, sim.KeyColors, input.Generation);
            var delayed = new Vector4[128]; history.Sample(.95, delayed);
            Assert(history.Count == 1 && delayed.All(c => c == Vector4.Zero),
                $"{fixture.Id}/{pitch}: a delayed AR key history must not resurrect pre-clear glow.");

            feed.SetDeterministicState([], 10, false, new HashSet<int> { 64 });
            feed.LiveNote(64, true, .9); feed.Impact(64, .9);
            // A SECOND old frame arrives after the clear flag was consumed. It must still refresh,
            // but must not clear/lose valid post-boundary events or reset the RNG/session clock again.
            sim.Step(.001, older, feed, 1280);
            Assert(older.Generation == input.Generation && older.Pressed[64] && older.Pressed.Count(p => p) == 1
                    && sim.ArImpacts.Count == 1 && sim.ArImpacts[0].Pitch == 64
                    && sim.LivePresses.Count == 1 && sim.LivePresses[0].Pitch == 64
                    && sim.ParticleCount > 0 && ParticleSnapshot(sim).All(p => p.Pitch == 64),
                $"{fixture.Id}/{pitch}: post-clear Note On survives the stale snapshot and is emitted exactly once.");
            var born = sim.ParticlesBorn;
            sim.Step(.001, older, feed, 640);
            Assert(sim.ParticlesBorn == born && sim.ArImpacts.Count == 1 && sim.LivePresses.Count == 1,
                $"{fixture.Id}/{pitch}: repeated output frames cannot replay the new Note On.");
        }
        VerifyClearStatePublicationGap();
        VerifyHeatEmitterEpochClear();
        VerifyEpochQueueBoundary();
        Results.Add("PASS P3a frame epoch: Capture/clear/Step race, stale song/pressed snapshots, post-clear hits, repeated/coalesced clears, viewport refresh and generation-aware AR key history. Soft clear only; no particle-delay history, RNG rewind or art redesign claim.");
    }

    private static void VerifyClearStatePublicationGap()
    {
        var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0]);
        var feed = new GpuStageFeed(); feed.SetLook(ParticleLook(settings));
        var notes = new[] { new NoteEvent { Pitch = 60, Start = 0, Duration = 5, Velocity = 100 } };
        feed.SetDeterministicState(notes, .5, true, new HashSet<int> { 60 });
        var input = new GpuFrameInput(); feed.Capture(input, GpuStageFeed.Now);
        var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
        for (var i = 0; i < 30; i++) sim.Step(1.0 / 60, input, feed, 1280);
        Assert(sim.ParticleCount > 0, "State-publication race must begin with a song-active emitter.");
        feed.ClearTransient();
        // UI has not yet published its seek/stop state. Old song intervals must not re-arm hold.
        sim.Step(.05, input, feed, 1280);
        Assert(!input.Playing && input.Pressed.All(p => !p) && sim.ParticleCount == 0 && sim.EmitterParticlesBorn == 0,
            "A clear must fence song-active emission until a new owner SetState publication.");
        feed.Impact(64, .9); feed.LiveNote(64, true, .9);
        sim.Step(.001, input, feed, 1280);
        Assert(sim.ArImpacts.Count == 1 && sim.ArImpacts[0].Pitch == 64 && sim.LivePresses.Count == 1 && sim.ParticleCount > 0,
            "Waiting for a state snapshot must not suppress a valid post-clear live Note On.");
        feed.SetDeterministicState([], 2, false, new HashSet<int> { 64 });
        feed.Capture(input, GpuStageFeed.Now);
        var oldBorn = sim.EmitterParticlesBorn;
        for (var i = 0; i < 30; i++) sim.Step(1.0 / 60, input, feed, 1280);
        Assert(input.Pressed[64] && sim.EmitterParticlesBorn > oldBorn && sim.ArImpacts.Count == 1,
            "The first post-clear authoritative pressed snapshot resumes held emission without another burst.");
    }

    private static void VerifyHeatEmitterEpochClear()
    {
        foreach (var family in new[] { "wisps", "flames" })
        foreach (var pitch in new[] { 60, 61 })
        {
            var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0]);
            settings.ShowEmbers = false; settings.ShowWisps = family == "wisps"; settings.ShowFlame = family == "flames";
            settings.WispAmount = 32; settings.FlameIntensity = 70;
            var feed = new GpuStageFeed(); feed.SetLook(ParticleLook(settings));
            feed.SetDeterministicState([], 0, false, new HashSet<int> { pitch });
            var input = new GpuFrameInput(); var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
            for (var frame = 0; frame < 60; frame++) { feed.Capture(input, GpuStageFeed.Now); sim.Step(1.0 / 60, input, feed, 1280); }
            Assert(sim.ParticleCount > 0 && sim.EmitterParticlesBorn > 0,
                $"{family}/{pitch}: epoch test must start with production held/heat emission.");
            feed.ClearTransient(); feed.SetDeterministicState([], 0, false, new HashSet<int>()); feed.SetSustainLevel(true, 1);
            sim.Step(.05, input, feed, 1280);
            Assert(sim.ParticleCount == 0 && sim.EmitterParticlesBorn == 0 && sim.EmissionCredits(pitch) == (0d, 0d, 0d),
                $"{family}/{pitch}: stale held/heat state must not revive after clear, even under pedal.");
        }
    }

    private static void VerifyEpochQueueBoundary()
    {
        var feed = new GpuStageFeed(); var old = new GpuFrameInput();
        feed.Capture(old, GpuStageFeed.Now); feed.PrepareFrame(old);
        feed.ClearTransient();
        // Simulate a producer that read the old epoch, was descheduled at clear, then enqueued late.
        ((ConcurrentQueue<GpuHit>)Field(feed, "_hits")).Enqueue(new GpuHit(60, 1, GpuStageFeed.Now, old.Generation));
        ((ConcurrentQueue<GpuLiveEvent>)Field(feed, "_live")).Enqueue(new GpuLiveEvent(60, true, 1, GpuStageFeed.Now, old.Generation));
        feed.Impact(64, 1); feed.LiveNote(64, true, 1);
        Assert(!feed.TryDequeueHit(old.Generation, out _) && !feed.TryDequeueLive(old.Generation, out _),
            "A frame accepted before a later clear must not steal the later epoch's queued events.");
        var fresh = new GpuFrameInput(); feed.Capture(fresh, GpuStageFeed.Now);
        Assert(feed.PrepareFrame(fresh) && feed.TryDequeueHit(fresh.Generation, out var hit) && hit.Pitch == 64
                && feed.TryDequeueLive(fresh.Generation, out var live) && live.Pitch == 64,
            "The current frame drops late OLD producers and retains NEW producers for both queues.");
        Assert(!feed.TryDequeueHit(out _) && !feed.TryDequeueLive(out _) && !feed.PrepareFrame(fresh),
            "The clear flag and valid post-clear events must each be consumed once.");

        feed.Impact(65, 1); feed.ClearTransient(); feed.Impact(66, 1); feed.ClearTransient(); feed.Impact(67, 1);
        Assert(feed.TakeClearRequest(), "Two pending clears may coalesce into one reset.");
        Assert(!feed.PrepareFrame(fresh) && fresh.Generation == 3 && feed.TryDequeueHit(fresh.Generation, out var last) && last.Pitch == 67,
            "Consuming the flag must not revalidate an old frame; repeated boundaries keep only the newest events.");
    }
}
