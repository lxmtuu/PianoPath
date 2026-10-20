using System.Windows;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// The third and last emission gap between the two renderers: how much a held key lets off.
    ///
    /// <para>The engine births <c>ParticleAmount × 1.3</c> embers and <c>WispAmount × 4</c> motes a second for
    /// every sounding key. The software stage had no held-ember channel at all and birthed wisps at
    /// <c>WispAmount</c> — a quarter of the engine's — so the same preset showed a key streaming embers and two
    /// twisting strands of smoke on a GPU machine, and a bare key with a thin trickle without one.</para>
    ///
    /// <para>This counts births, not pixels: one second of a held key, read back from the stage. The counts are
    /// the rates the engine uses, and the two renderers now differ only in the ceiling that drops the excess —
    /// which is the line <c>EffectBudget</c> already draws for waves, flashes and particles.</para>
    /// </summary>
    private static void VerifyWpfEmissionParity()
    {
        static PianoStage Stage(Action<PianoVisualSettings> tweak)
        {
            var settings = new PianoVisualSettings
            {
                ShowBackground = false, ShowStars = false, ShowNotes = false, ShowKeys = false,
                ShowHalo = false, ShowImpactRings = false, ShowImpactFlash = false, ShowLightBeams = false,
                ShowPetals = false, ShowWisps = false, ShowFlame = false, ShowEmbers = false,
                ShowWatermark = false, ShowCounter = false, ShowFps = false,
            };
            tweak(settings);
            settings.Clamp();
            var stage = new PianoStage { Width = 1280, Height = 720 };
            stage.SetVisualSettings(settings);
            stage.Measure(new Size(1280, 720)); stage.Arrange(new Rect(0, 0, 1280, 720)); stage.UpdateLayout();
            stage.AddLiveNote(60);
            stage.SetState([], 0, false, new HashSet<int> { 60 });
            return stage;
        }

        static (long Wisps, long Sparks, long Flames) OneSecond(PianoStage stage)
        {
            for (var frame = 0; frame < 60; frame++) stage.Advance(1.0 / 60);
            return stage.BirthCounts;
        }

        var wisps = OneSecond(Stage(s => { s.ShowWisps = true; s.WispAmount = 45; }));
        Assert(wisps.Wisps == 180,
            $"W0: a held key must birth WispAmount x 4 motes a second, as the engine does — 45 on the slider is 180 a "
            + $"second, but the fallback birthed {wisps.Wisps}; a quarter of the smoke is what this replaced.");
        Assert(wisps.Sparks == 0 && wisps.Flames == 0,
            $"W0: the wisp run must not birth anything else (sparks {wisps.Sparks}, flames {wisps.Flames}).");

        var embers = OneSecond(Stage(s => { s.ShowEmbers = true; s.ParticleAmount = 34; }));
        Assert(embers.Sparks == 44,
            $"W0: a held key must birth ParticleAmount x 1.3 embers a second, as the engine does — 34 on the slider is "
            + $"44.2 a second, but the fallback birthed {embers.Sparks}; it had no sustained-ember channel at all before.");
        Assert(embers.Wisps == 0 && embers.Flames == 0, "W0: the ember run must not birth anything else.");

        var flames = OneSecond(Stage(s => { s.ShowFlame = true; s.FlameIntensity = 70; s.FlameHeight = 60; }));
        // 70 x .7 = 49 a second once the heat has risen, which takes .14 s; 45 is what one second holds.
        Assert(flames.Flames is >= 43 and <= 49,
            $"W2: the flame rate must stay the engine's 70 x intensity x heat, but one second birthed {flames.Flames} "
            + "instead of about 45.");

        var quiet = OneSecond(Stage(_ => { }));
        Assert(quiet.Wisps == 0 && quiet.Sparks == 0 && quiet.Flames == 0,
            $"W0: with every particle channel off a held key must birth nothing (wisp {quiet.Wisps}, spark {quiet.Sparks}, "
            + $"flame {quiet.Flames}).");

        var pace = HeldEmission.Wisps(45) / HeldEmission.Sparks(34);
        Results.Add($"PASS W0 emission parity: a held key births {wisps.Wisps} wisps, {embers.Sparks} embers and "
            + $"{flames.Flames} flame motes in one second at the defaults — the engine's rates (WispAmount x {HeldEmission.Wisps(1)}, "
            + $"ParticleAmount x {HeldEmission.Sparks(1):0.0}) rather than the fallback's old 1x and none, with each ceiling "
            + $"dropping the excess instead of the rate being quietly lower ({pace:0.00} wisps per ember).");
    }
}
