using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>P0 paired output captures using the shipping D3D renderer, forced to WARP.
/// Baseline song-state path with explicit fixture CC64 injection; not a live MIDI, latency or hardware test.</summary>
internal static class PrismFlowVisualReview
{
    private const int Width = 1280, Height = 720, Fps = 60;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    private sealed record Capture(string Preset, string Mode, string Checkpoint, double RequestedTime,
        double ActualTime, string Variant, string File, string PixelSha256,
        int StageNotes, int StageKeys, int ArQuads, int ArParticles, int? ChangedPixels);

    internal static string FindSamples()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var samples = Path.Combine(directory.FullName, "docs", "samples");
            if (File.Exists(Path.Combine(samples, "prism-flow-p0", "manifest.json"))) return samples;
        }
        throw new FileNotFoundException("Run the Prism Flow P0 diagnostics from the repository checkout.");
    }

    internal static int Render(string output)
    {
        var samples = FindSamples();
        var scene = PrismFlowBaseline.Load(Path.Combine(samples, "prism-flow-p0"));
        Directory.CreateDirectory(output);
        var captures = new List<Capture>();
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var calibration = ArCalibration.Create(KeyboardLayout.Standard88,
                [(70, 292), (1214, 268), (1236, 421), (52, 449)], Width, Height, out var problem, 70)
                ?? throw new InvalidDataException($"Prism Flow synthetic calibration failed: {problem}");
            if (calibration.CameraPose is null) throw new InvalidDataException("Prism Flow requires a calibrated 3D pose.");
            var ring = ReadSyntheticCamera(Path.Combine(samples, "ar-keyboard-topdown.png"));
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp) throw new InvalidOperationException("Prism Flow baseline requires WARP.");
            foreach (var presetName in new[] { "Concert Gold", "Ice Crystal", "Neon Violet" })
            foreach (var overCamera in new[] { false, true })
            {
                var settings = (VisualPresets.FindBuiltIn(presetName)
                    ?? throw new InvalidDataException($"Missing control preset: {presetName}")).Settings.Clone();
                settings.KeyboardSize = "88";
                settings.Clamp();
                foreach (var checkpoint in scene.Manifest.Checkpoints)
                {
                    var pixels = CaptureFrame(settings, presetName, overCamera, checkpoint, "baseline", null);
                    // One paired effect-off frame per preset/mode. This is presence evidence only,
                    // not an assertion of individual style/binding completeness or cross-mode parity.
                    if (checkpoint.Id != "dense-chord") continue;
                    var neutral = settings.Clone();
                    neutral.ShowEmbers = neutral.ShowWisps = neutral.ShowFlame = false;
                    neutral.ShowImpactRings = neutral.ShowImpactFlash = false;
                    neutral.ImpactWave = "None"; neutral.ImpactMorph = "None";
                    neutral.ReleaseEffect = "Fade"; neutral.HoldElectricArc = false;
                    CaptureFrame(neutral, presetName, overCamera, checkpoint, "phase-fx-off", pixels);
                }
            }
            if (captures.Count != 78) throw new InvalidDataException("Prism Flow must capture 72 baseline and six phase-FX-off frames.");
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, status = "warp-baseline-captures-not-parity-or-performance-certification",
                adapter = renderer.AdapterName, width = Width, height = Height, fps = Fps,
                seed = scene.Manifest.RenderSeedProposal, fixtureMidiSha256 = scene.Manifest.MidiSha256,
                timeline = "song notes from shipping MidiReader; CC64 explicitly injected from manifest",
                sampling = "ceil checkpoint to frame end; advance fixed dt from zero with a fresh cursor/simulation per capture",
                ar = new { quality = "Ultra", delaySeconds = 0, mirror = false, fit = "Fit", fovDegrees = 70,
                    camera = "repository synthetic keyboard image; no webcam",
                    corners = calibration.Corners.Select(p => new { x = p.X, y = p.Y }).ToArray() },
                limitations = new[] { "not live MIDI routing", "not camera delay/history validation", "not WPF parity",
                    "not every particle style", "not physical GPU timings", "not reset/seek verification" },
                captureCount = captures.Count, captures,
            }, new JsonSerializerOptions { WriteIndented = true }));
            var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Prism Flow P0 baseline</title><style>body{background:#111;color:#eee;font:16px sans-serif}img{max-width:100%;width:640px}figure{display:inline-block;margin:12px;max-width:640px}figcaption{padding:8px}</style><h1>Prism Flow P0 — WARP baseline</h1><p>Song-state replay, explicit pedal injection, synthetic camera, zero AR delay. Not live MIDI / parity / FPS certification. Ice Crystal is the existing Glass-material control preset.</p>");
            foreach (var capture in captures)
                html.Append($"<figure><figcaption>{WebUtility.HtmlEncode(capture.Preset + " / " + capture.Mode + " / " + capture.Checkpoint + " / " + capture.Variant)} · {capture.ActualTime:0.000}s</figcaption><img loading=\"lazy\" src=\"{capture.File}\"></figure>");
            File.WriteAllText(Path.Combine(output, "index.html"), html.ToString());
            return captures.Count;

            byte[] CaptureFrame(PianoVisualSettings visual, string presetName, bool overCamera, PrismFlowBaseline.Checkpoint checkpoint,
                string variant, byte[]? comparison)
            {
                var stage = new PianoStage { Width = Width, Height = Height };
                stage.SetVisualSettings(visual);
                stage.Measure(new Size(Width, Height));
                stage.Arrange(new Rect(0, 0, Width, Height));
                stage.UpdateLayout();
                var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
                feed.SetLook(GpuLook.From(visual,
                    (pitch, track) => stage.AdjustColor(stage.NoteColor(pitch, track)),
                    stage.KeyboardHeight / stage.ActualHeight, stage.AdjustColor, overCamera));
                feed.SetStageHeight(Height);
                if (overCamera)
                {
                    feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
                    feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1,
                        Quality: ArQualityTier.Ultra, ProjectedNotes: true);
                }
                var cursor = scene.CreateCursor();
                var frames = Math.Max(1, (int)Math.Ceiling(checkpoint.Time * Fps));
                void BeforeFrame(int frame)
                {
                    var time = (frame + 1) / (double)Fps;
                    feed.SetDeterministicState(scene.Notes, time, true, NoPressed);
                    feed.SetDeterministicBeatPulse(0);
                    cursor.AdvanceTo(time, n => feed.Impact(n.Pitch, n.Velocity / 127.0), feed.SetSustainLevel);
                }
                var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1,
                    1.0 / Fps, false, out _, BeforeFrame, scene.Manifest.RenderSeedProposal);
                if (pixels.Length != Width * Height * 4 || !pixels.Where((_, i) => i % 4 != 3).Any(b => b != 0))
                    throw new InvalidDataException("Prism Flow readback is empty.");
                if (overCamera && (renderer.StageNoteCount != 0 || renderer.StageKeyCount != 0))
                    throw new InvalidDataException("Unprojected stage geometry leaked onto the AR camera.");
                if (checkpoint.Id == "white-black-hold"
                    && (overCamera ? renderer.ArShapeCount == 0 : renderer.StageNoteCount == 0))
                    throw new InvalidDataException("Held-note baseline is missing its visible note geometry.");
                int? changed = null;
                if (comparison is not null)
                {
                    changed = 0;
                    for (var i = 0; i < pixels.Length; i += 4)
                        if (Math.Max(Math.Abs(pixels[i] - comparison[i]), Math.Max(Math.Abs(pixels[i + 1] - comparison[i + 1]),
                            Math.Abs(pixels[i + 2] - comparison[i + 2]))) > 8) changed++;
                    if (changed == 0) throw new InvalidDataException("Phase-FX-off control did not change the dense-chord capture.");
                }
                var mode = overCamera ? "ar" : "live-stage";
                var file = $"{presetName.ToLowerInvariant().Replace(' ', '-')}/{mode}/{checkpoint.Id}-{variant}.png";
                WriteAndVerifyPng(Path.Combine(output, file), pixels);
                captures.Add(new Capture(presetName, mode, checkpoint.Id, checkpoint.Time, frames / (double)Fps,
                    variant, file, Convert.ToHexString(SHA256.HashData(pixels)), renderer.StageNoteCount,
                    renderer.StageKeyCount, renderer.ArQuadCount, renderer.ArParticleFxCount, changed));
                return pixels;
            }
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static CameraFrameRing ReadSyntheticCamera(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth != Width || bitmap.PixelHeight != Height)
            throw new InvalidDataException("Prism Flow camera fixture must be 1280x720.");
        var ring = new CameraFrameRing();
        var pixels = ring.BeginWrite(Width * Height * 4, out var slot);
        bitmap.CopyPixels(pixels, Width * 4, 0);
        ring.Publish(slot, Width, Height, Stopwatch.GetTimestamp());
        return ring;
    }

    private static void WriteAndVerifyPng(string path, byte[] pixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        using var read = File.OpenRead(path);
        var decoded = BitmapDecoder.Create(read, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (decoded.PixelWidth != Width || decoded.PixelHeight != Height)
            throw new InvalidDataException("Prism Flow PNG dimensions differ from readback.");
        var roundTrip = new byte[pixels.Length];
        new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0).CopyPixels(roundTrip, Width * 4, 0);
        if (!pixels.AsSpan().SequenceEqual(roundTrip)) throw new InvalidDataException("Prism Flow PNG pixels failed round trip.");
    }
}
