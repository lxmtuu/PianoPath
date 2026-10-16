using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace PianoPath;

/// <summary>P3a baseline inventory, not a particle redesign or latency/FPS certificate.</summary>
internal static class KeyParticleVisualReview
{
    private const int Width = 1280, Height = 720, Fps = 60;
    private sealed record Capture(string Family, string Style, int Pitch, string Mode, string Phase,
        double RequestedTime, double ActualTime, string File, string PixelSha256,
        int StageSprites, int ArParticles, int? ChangedPixels);
    private static readonly IReadOnlySet<int> Empty = new HashSet<int>();

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
            foreach (var fixture in KeyParticleFixtures.All)
            foreach (var overCamera in new[] { false, true })
            {
                byte[]? neutral = null;
                foreach (var phase in KeyParticleFixtures.Phases)
                {
                    var settings = KeyParticleFixtures.Settings(fixture, phase != "held-off");
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
                    var pressed = new HashSet<int> { fixture.Pitch };
                    var requestedTime = KeyParticleFixtures.SampleTime(phase);
                    var frames = (int)Math.Ceiling(requestedTime * Fps);
                    void BeforeFrame(int frame)
                    {
                        var time = (frame + 1) / (double)Fps;
                        feed.SetDeterministicState([], time, false, KeyParticleFixtures.PressedAt(phase, time) ? pressed : Empty);
                        feed.SetDeterministicBeatPulse(0);
                        // An explicit pedal level verifies that "audio sustain" is not a pressed-key emitter trigger.
                        feed.SetSustainLevel(phase is "released" or "tail", phase is "released" or "tail" ? 1 : 0);
                        if (phase == "burst" && frame == 0) feed.Impact(fixture.Pitch, .9);
                    }
                    var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, 1.0 / Fps,
                        false, out _, BeforeFrame, KeyParticleFixtures.Seed);
                    if (phase is "burst" or "held" or "released")
                    {
                        if (overCamera ? renderer.ArParticleFxCount <= 0 : renderer.StageSpriteCount <= 0)
                            throw new InvalidDataException($"Missing {fixture.Id}/{phase} particles in {(overCamera ? "AR" : "Stage")}.");
                    }
                    if (phase is "tail" or "held-off")
                        if (overCamera ? renderer.ArParticleFxCount != 0 : renderer.StageSpriteCount != 0)
                            throw new InvalidDataException($"Unexpected particles after release/off: {fixture.Id}/{phase}, AR={overCamera}.");
                    if (overCamera && (renderer.StageNoteCount != 0 || renderer.StageKeyCount != 0 || renderer.StageSpriteCount != 0))
                        throw new InvalidDataException("Stage-space geometry leaked onto the particle AR capture.");
                    int? changed = null;
                    if (phase == "held-off") neutral = pixels;
                    if (phase == "held")
                    {
                        if (neutral is null) throw new InvalidDataException("Held capture needs its emitter-off control.");
                        changed = 0;
                        for (var i = 0; i < pixels.Length; i += 4)
                            if (Math.Max(Math.Abs(pixels[i] - neutral[i]), Math.Max(Math.Abs(pixels[i + 1] - neutral[i + 1]),
                                Math.Abs(pixels[i + 2] - neutral[i + 2]))) > 4) changed++;
                        if (changed == 0) throw new InvalidDataException($"Invisible held emitter: {fixture.Id}, AR={overCamera}.");
                    }
                    var mode = overCamera ? "ar" : "live-stage";
                    var file = $"{fixture.Id}/{mode}/{phase}.png";
                    PrismFlowVisualReview.WriteAndVerifyPng(Path.Combine(output, file), pixels);
                    captures.Add(new Capture(fixture.Id, fixture.Style, fixture.Pitch, mode, phase, requestedTime,
                        frames / (double)Fps, file, Convert.ToHexString(SHA256.HashData(pixels)), renderer.StageSpriteCount,
                        renderer.ArParticleFxCount, changed));
                }
            }
            if (captures.Count != 70) throw new InvalidDataException("P3a inventory requires 7 routes × 5 phases × 2 modes = 70 captures.");
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, status = "p3a-current-family-baselines-not-art-signoff",
                adapter = renderer.AdapterName, width = Width, height = Height, fps = Fps, seed = KeyParticleFixtures.Seed,
                input = "explicit single-key feed states; no OS MIDI callback/controller import",
                releaseSeconds = .35, ar = "Ultra / synthetic keyboard / Fit / no mirror / delay=0",
                emission = "burst=one queued impact without held key; held=no queued impact; released/tail=pedal down but physical key up",
                settings = KeyParticleFixtures.All.Select(f => new { f.Id, Values = JsonSerializer.Deserialize<JsonElement>(KeyParticleFixtures.Settings(f).ToJson()) }),
                limitations = new[] { "not upgraded family art", "not nonzero camera delay", "not WPF/recording parity", "not other quality tiers", "not full reset/RNG rewind", "not hardware FPS or motion clip" },
                captureCount = captures.Count, captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>P3a particle family baseline</title><style>body{background:#111;color:#eee;font:16px sans-serif}figure{display:inline-block;max-width:640px;margin:12px}img{max-width:100%}figcaption{padding:8px}</style><h1>P3a — current burst / held family inventory</h1><p>Existing shapes/physics, fixed seed, Stage / AR Ultra, synthetic camera. Not a new flow look or a motion/latency certificate.</p>");
            foreach (var capture in captures)
                html.Append($"<figure><figcaption>{WebUtility.HtmlEncode(capture.Family + " / " + capture.Mode + " / " + capture.Phase)}</figcaption><img loading=\"lazy\" src=\"{capture.File}\"></figure>");
            File.WriteAllText(Path.Combine(output, "index.html"), html.ToString());
            return captures.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }
}
