using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace PianoPath;

/// <summary>Additional-channel baseline; preserves the original family inventory. Not an art or latency certificate.</summary>
internal static class AuxParticleVisualReview
{
    private const int Width = 1280, Height = 720, Fps = 60;
    private sealed record Capture(string Route, string Channel, string Effect, int Pitch, string Mode, string Phase,
        double RequestedTime, double ActualTime, string File, string PixelSha256,
        int StageSprites, int ArParticles, int ArReleaseEffects, int? ChangedPixels);

    internal static int Render(string output)
    {
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            Directory.CreateDirectory(output);
            var calibration = ArCalibration.Create(KeyboardLayout.Standard88,
                [(70, 292), (1214, 268), (1236, 421), (52, 449)], Width, Height, out var problem, 70)
                ?? throw new InvalidDataException($"Particle capture calibration: {problem}");
            var camera = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(), "ar-keyboard-topdown.png"));
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp) throw new InvalidOperationException("P3a captures require WARP.");
            var captures = new List<Capture>();
            foreach (var fixture in AuxParticleTimeline.Routes)
            foreach (var pitch in new[] { 60, 61 })
            foreach (var overCamera in new[] { false, true })
            {
                byte[]? neutral = null;
                foreach (var phase in AuxParticleTimeline.Phases)
                {
                    var settings = AuxParticleFixtures.Settings(fixture, phase != "off");
                    var stage = new PianoStage { Width = Width, Height = Height };
                    stage.SetVisualSettings(settings);
                    stage.Measure(new Size(Width, Height)); stage.Arrange(new Rect(0, 0, Width, Height)); stage.UpdateLayout();
                    var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
                    feed.SetLook(GpuLook.From(settings, (pitch, track) => stage.AdjustColor(stage.NoteColor(pitch, track)),
                        stage.KeyboardHeight / stage.ActualHeight, stage.AdjustColor, overCamera));
                    feed.SetStageHeight(Height);
                    if (overCamera)
                    {
                        var ring = new CameraFrameRing();
                        camera.CopyTo(ring.BeginWrite(camera.Length, out var slot), 0);
                        ring.Publish(slot, Width, Height, Stopwatch.GetTimestamp());
                        feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
                        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, Quality: ArQualityTier.Ultra,
                            ProjectedNotes: true, ShowDamperStrip: false);
                    }
                    var requestedTime = AuxParticleTimeline.SampleTime(fixture, phase);
                    var frames = (int)Math.Ceiling(requestedTime * Fps);
                    void BeforeFrame(int frame) => AuxParticleFixtures.Publish(feed, fixture, phase, frame, pitch);
                    var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, 1.0 / Fps,
                        false, out _, BeforeFrame, KeyParticleFixtures.Seed);
                    if (phase == "active")
                    {
                        if (overCamera ? renderer.ArParticleFxCount <= 0 : renderer.StageSpriteCount <= 0)
                            throw new InvalidDataException($"Missing {fixture.Id}/{phase} particles in {(overCamera ? "AR" : "Stage")}.");
                    }
                    if (phase is "expired" or "off")
                        if (overCamera ? renderer.ArParticleFxCount != 0 : renderer.StageSpriteCount != 0)
                            throw new InvalidDataException($"Unexpected particles after release/off: {fixture.Id}/{phase}, AR={overCamera}.");
                    if (overCamera && (renderer.StageNoteCount != 0 || renderer.StageKeyCount != 0 || renderer.StageSpriteCount != 0))
                        throw new InvalidDataException("Stage-space geometry leaked onto the particle AR capture.");
                    int? changed = null;
                    if (phase == "off") neutral = pixels;
                    if (phase == "active")
                    {
                        if (neutral is null) throw new InvalidDataException("Active capture needs its same-time/same-input effect-off control.");
                        changed = 0;
                        for (var i = 0; i < pixels.Length; i += 4)
                            if (Math.Max(Math.Abs(pixels[i] - neutral[i]), Math.Max(Math.Abs(pixels[i + 1] - neutral[i + 1]),
                                Math.Abs(pixels[i + 2] - neutral[i + 2]))) > 4) changed++;
                        if (changed == 0) throw new InvalidDataException($"Invisible active channel: {fixture.Id}, AR={overCamera}.");
                    }
                    var mode = overCamera ? "ar" : "live-stage";
                    var file = $"{fixture.Id}/{pitch}/{mode}/{phase}.png";
                    PrismFlowVisualReview.WriteAndVerifyPng(Path.Combine(output, file), pixels);
                    captures.Add(new Capture(fixture.Id, fixture.Source.ToString(), fixture.Effect, pitch, mode, phase, requestedTime,
                        frames / (double)Fps, file, Convert.ToHexString(SHA256.HashData(pixels)), renderer.StageSpriteCount,
                        renderer.ArParticleFxCount, renderer.ArReleaseEffectCount, changed));
                }
            }
            if (captures.Count != 128) throw new InvalidDataException("Auxiliary inventory requires 8 channels × 2 pitches × 4 phases × 2 modes = 128 captures.");
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, status = "p3a-auxiliary-baselines-not-art-signoff",
                adapter = renderer.AdapterName, width = Width, height = Height, fps = Fps, seed = KeyParticleFixtures.Seed,
                input = "explicit single-key feed states; no OS MIDI callback/controller import",
                releaseAfterHeldFrames = AuxParticleTimeline.ReleaseFrame, ar = "Ultra / synthetic keyboard / Fit / no mirror / delay=0",
                emission = "off and active share input/time; held=physical state only; morph=one impact; release=live down then up at frame 21; pedal-tail/expired=physical release with pedal held",
                excludedNonParticleChannels = new[] { "Absorb", "Morph", "Snap Back", "Echo Rings" },
                settings = AuxParticleTimeline.Routes.Select(f => new { f.Id, Source = f.Source.ToString(), f.Effect, f.Shape, f.Wisp, Values = JsonSerializer.Deserialize<JsonElement>(AuxParticleFixtures.Settings(f).ToJson()) }),
                limitations = new[] { "not upgraded family art", "not nonzero camera delay", "not WPF/recording parity", "not other quality tiers", "not full reset/RNG rewind", "not hardware FPS or motion clip" },
                captureCount = captures.Count, captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>P3a auxiliary particle baseline</title><style>body{background:#111;color:#eee;font:16px sans-serif}figure{display:inline-block;max-width:640px;margin:12px}img{max-width:100%}figcaption{padding:8px}</style><h1>P3a — wisps / flames / particle morphs / releases</h1><p>Existing shapes/physics, fixed seed, Stage / AR Ultra, synthetic camera. Not a new flow look or a motion/latency certificate.</p>");
            foreach (var capture in captures)
                html.Append($"<figure><figcaption>{WebUtility.HtmlEncode(capture.Route + " / " + capture.Pitch + " / " + capture.Mode + " / " + capture.Phase)}</figcaption><img loading=\"lazy\" src=\"{capture.File}\"></figure>");
            File.WriteAllText(Path.Combine(output, "index.html"), html.ToString());
            return captures.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }
}
