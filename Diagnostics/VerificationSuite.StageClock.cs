using System.Windows;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// W0's second half: the slow-motion setting has to slow the particles and nothing else.
    ///
    /// <para>The GPU computes <c>particleDt = frameDt × PhysicsTimeFactor</c> and keeps rings, flashes, live
    /// bars, key heat and the ambient clock on <c>frameDt</c>. The software stage used one scaled clock for
    /// everything, so at any factor but 100 its live bars fell more slowly than the engine's — same song,
    /// different animation. This builds the same keypress twice, once at 100 and once at 40, and reads the
    /// two clocks back: the bar must travel the same distance, the wisp motes must not.</para>
    ///
    /// <para>No pixels are compared here; the accepted previews were rendered at the default 100, where both
    /// clocks are the frame clock, so this change cannot move them.</para>
    /// </summary>
    private static void VerifyStageClock()
    {
        static PianoStage Stage(int physicsPercent)
        {
            var settings = new PianoVisualSettings
            {
                ShowBackground = false, ShowStars = false, ShowNotes = false, ShowKeys = false,
                ShowHalo = false, ShowImpactRings = false, ShowImpactFlash = false, ShowLightBeams = false,
                ShowEmbers = false, ShowFlame = false, ShowPetals = false, ShowWisps = true,
                WispAmount = 45, WispTurbulence = 55, WispWidth = 40, PhysicsTimeFactor = physicsPercent,
                ShowWatermark = false, ShowCounter = false, ShowFps = false,
            };
            settings.Clamp();
            var stage = new PianoStage { Width = 1280, Height = 720 };
            stage.SetVisualSettings(settings);
            stage.Measure(new Size(1280, 720)); stage.Arrange(new Rect(0, 0, 1280, 720)); stage.UpdateLayout();
            // A held live key: it makes the falling bar and it is what the wisp emitter reads.
            stage.AddLiveNote(60);
            stage.SetState([], 0, false, new HashSet<int> { 60 });
            return stage;
        }

        var fast = Stage(100);
        var slow = Stage(40);
        for (var frame = 0; frame < 48; frame++) { fast.Advance(1.0 / 60); slow.Advance(1.0 / 60); }

        Assert(fast.LiveTrailCount == 1 && slow.LiveTrailCount == 1,
            "W0: neither stage lost its held live bar while the clock check ran.");
        Assert(Math.Abs(fast.FirstLiveTrailY - slow.FirstLiveTrailY) < 1e-9,
            $"W0: a keypress must keep the frame clock — the bar travelled {fast.FirstLiveTrailY:0.###} px at 100% and "
            + $"{slow.FirstLiveTrailY:0.###} px at 40%, so slow motion is still dragging the player's own bar.");
        Assert(Math.Abs(slow.FirstLiveTrailY - 48 / 60.0 * 550) < .5,
            $"W0: the bar should sit where 48 frames of real time put it (550 px/s), not where a scaled clock would; "
            + $"it sat at {slow.FirstLiveTrailY:0.###} px.");

        var fastTravel = fast.FurthestSparkTravel;
        var slowTravel = slow.FurthestSparkTravel;
        Assert(fastTravel > 1 && slowTravel > 1,
            $"W0: both stages must actually emit wisps for this comparison (fast {fastTravel:0.##} px, slow {slowTravel:0.##} px).");
        // Not exactly 0.4: the motes also lose speed to drag, so 0.4 of the *time* is a bit more than 0.4 of
        // the distance. What must hold is the shape — visibly less travel, nowhere near the full amount.
        var ratio = slowTravel / fastTravel;
        Assert(ratio is > .3 and < .6,
            $"W0: at 40% the motes must climb visibly less than at 100% ({slowTravel:0.##} px against {fastTravel:0.##} px, "
            + $"ratio {ratio:0.###}), which is the whole point of the slider; only the particle clock may be scaled.");

        Results.Add($"PASS W0 stage clock: the held-bar clock stays real ({fast.FirstLiveTrailY:0.#} px at both 100% and 40%) "
            + $"while the particle clock scales ({slowTravel:0.#} px against {fastTravel:0.#} px after 48 frames), matching GpuStageSimulation's frameDt/particleDt split.");
    }
}
