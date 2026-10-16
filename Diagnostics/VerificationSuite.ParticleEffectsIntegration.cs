namespace PianoPath;

internal static partial class VerificationSuite
{
    // Contract for future verification runs. No native captures or additional profiling are requested.
    private static void VerifyUpgradedParticleEffectsSettings()
    {
        var old=PianoVisualSettings.FromJson("{}");
        Assert(!old.UpgradedParticleEffects && !ParticleLook(old).UseFairArParticleBudget,
            "Old settings and fresh profiles retain classic particle rendering and AR selection.");
        foreach(var legacyHeld in new[]{false,true})
        {
            var settings=new PianoVisualSettings{UpgradedParticleEffects=true,HeldParticleArt=legacyHeld};
            var clone=settings.Clone();var applied=new PianoVisualSettings();applied.CopyFrom(clone);
            var shared=VisualPresetShare.TryDecode(VisualPresetShare.Encode(settings),out var decoded,out _);
            Assert(clone.UpgradedParticleEffects && applied.UpgradedParticleEffects && shared && decoded.UpgradedParticleEffects,
                "The approved effect opt-in survives JSON, clone, preset apply and share codes.");
            foreach(var ar in new[]{false,true})
            {
                var look=GpuLook.From(applied,(_,_)=>System.Windows.Media.Colors.Cyan,.205,overCamera:ar);
                Assert(look.HeldParticleArt && look.UseWispArtV3 && look.UseFlameArtV3 && look.UseReleaseArt && look.UseFairArParticleBudget,
                    "Both output snapshots use the same accepted held/release materials and AR allocation policy.");
                Assert(!look.WispArtV3ForReview && !look.FlameArtV3ForReview && !look.ReleaseArtForReview && !look.ArFairParticleBudgetForReview,
                    "Normal app integration must not depend on setting private diagnostic overrides.");
                Assert(!look.ReleaseFrontLayerForReview && !look.MeltFrontLayerForReview && !look.ShatterBillboardForReview && !look.BounceBillboardForReview
                    && !look.SparkArtForReview && !look.SplashArtForReview && !look.FireworkArtForReview && !look.ConfettiArtForReview && !look.DustArtForReview
                    && !look.ShatterArtForReview && !look.MeltArtForReview && !look.BounceArtForReview,
                    "Integration must not silently promote unselected burst/morph, depth or billboard experiments.");
                applied.UpgradedParticleEffects=false;
                var off=GpuLook.From(applied,(_,_)=>System.Windows.Media.Colors.Cyan,.205,overCamera:ar);
                Assert(!off.UseWispArtV3 && !off.UseFlameArtV3 && !off.UseReleaseArt && !off.UseFairArParticleBudget && off.HeldParticleArt==legacyHeld
                    && look.UseWispArtV3 && look.UseFairArParticleBudget,
                    "Turning the option off restores legacy behavior without mutating the prior render-thread snapshot or legacy held setting.");
                applied.UpgradedParticleEffects=true;
            }
        }
        foreach(var name in new[]{"Prism Flow - Wisps","Prism Flow - Flames"})
            Assert(CommunityPresets.Find(name)?.Settings.UpgradedParticleEffects==true,
                "Accepted effect recipes must be accessible from the embedded community shelf.");
        Assert(!CommunityPresets.Find("Prism Flow Preview")!.Settings.UpgradedParticleEffects,
            "The frozen palette-only control and existing presets must not inherit upgraded particles.");
    }
}
