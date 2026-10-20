using System.Windows;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// W2's first half: the held-key flame has to be the same effect on both renderers.
    ///
    /// <para>The engine turns a sounding key's heat into a stream of motes — seventy a second per key at full
    /// intensity, climbing at 50–200 px/s, living a third to two thirds of a second and cooling white to deep
    /// red. The software stage drew one ellipse plume pinned to the key top: no rise, no stream, no life. The
    /// same preset therefore looked like fire on a GPU machine and like a stuck decal without one.</para>
    ///
    /// <para>This builds real stages and reads the motes back. No pixels are compared — the accepted previews
    /// and the WARP evidence are all rendered by the engine, so the fallback's flame was never in them.</para>
    /// </summary>
    private static void VerifyWpfFlameParity()
    {
        static PianoStage Stage(int physicsPercent, bool flame)
        {
            var settings = new PianoVisualSettings
            {
                ShowBackground = false, ShowStars = false, ShowNotes = false, ShowKeys = false,
                ShowHalo = false, ShowImpactRings = false, ShowImpactFlash = false, ShowLightBeams = false,
                ShowEmbers = false, ShowPetals = false, ShowWisps = false,
                ShowFlame = flame, FlameIntensity = 70, FlameHeight = 60, FlameColorMode = "Warm",
                PhysicsTimeFactor = physicsPercent,
                ShowWatermark = false, ShowCounter = false, ShowFps = false,
            };
            settings.Clamp();
            var stage = new PianoStage { Width = 1280, Height = 720 };
            stage.SetVisualSettings(settings);
            stage.Measure(new Size(1280, 720)); stage.Arrange(new Rect(0, 0, 1280, 720)); stage.UpdateLayout();
            stage.AddLiveNote(60);
            stage.SetState([], 0, false, new HashSet<int> { 60 });
            return stage;
        }

        var fast = Stage(100, true);
        for (var frame = 0; frame < 60; frame++) fast.Advance(1.0 / 60);
        // Steady state: 49 motes born per second and a mean life near .46 s is about twenty-two alive at once.
        // The band only has to tell "a stream of motes" from "one plume" or "nothing at all".
        Assert(fast.FlameMoteCount is >= 8 and <= 80,
            $"W2: a sounding key at 70% intensity should hold a couple of dozen flame motes, not {fast.FlameMoteCount}; "
            + "one blurred ellipse is the shape this replaced.");
        Assert(fast.HighestFlameRise > 25,
            $"W2: the flame must climb away from the key line, but the highest mote only reached {fast.HighestFlameRise:0.#} px; "
            + "staying on the key is exactly the bug.");
        var steadyMotes = fast.FlameMoteCount; var steadyRise = fast.HighestFlameRise;

        // Before any mote dies, travel is pure |Vy| x age, so the slow take must be 0.4 of the fast one to the
        // pixel — same motes, same speeds, a quarter of the time.
        var earlyFast = Stage(100, true); var earlySlow = Stage(40, true);
        for (var frame = 0; frame < 12; frame++) { earlyFast.Advance(1.0 / 60); earlySlow.Advance(1.0 / 60); }
        Assert(earlyFast.HighestFlameRise > 5 && earlySlow.HighestFlameRise > 2,
            $"W2: the ratio needs live motes on both stages (fast {earlyFast.HighestFlameRise:0.#} px, slow {earlySlow.HighestFlameRise:0.#} px).");
        var ratio = earlySlow.HighestFlameRise / earlyFast.HighestFlameRise;
        Assert(ratio is > .35 and < .45,
            $"W2: at 40% physics the flame must rise 0.4 as far in the same real time ({ratio:0.###}), because it is a particle "
            + "and the live bar behind it is not.");

        var off = Stage(100, false);
        for (var frame = 0; frame < 30; frame++) off.Advance(1.0 / 60);
        Assert(off.FlameMoteCount == 0, "W2: with the flame switched off the stage must not birth a single mote.");

        // The engine keeps emitting from cooling heat after the key is up, and those last motes burn out on their
        // own lifetime. A fallback that hard-cuts instead would pop.
        fast.SetState([], 0, false, new HashSet<int>());
        for (var frame = 0; frame < 90; frame++) fast.Advance(1.0 / 60);
        Assert(fast.FlameMoteCount == 0,
            $"W2: a released key's flame must burn out within a second and a half, but {fast.FlameMoteCount} motes survived; "
            + "that is either a leak or a lifetime the engine's numbers do not explain.");

        Results.Add($"PASS W2 flame parity: the software flame is a stream of {steadyMotes} motes climbing {steadyRise:0.#} px "
            + "and cooling on the engine's schedule, rises 0.4 as far at 40% physics, and burns out after release "
            + "instead of being a plume glued to the key line.");
    }
}
