using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Renders the agreed Phase 1 scene through the shipping WPF stage and the Direct3D WARP renderer.</summary>
internal static class Phase1VisualReview
{
    private const string FixtureRelativePath = "docs/samples/visual-effects-phase1-scene.json";
    private const string FixtureStatus = "review-fixture-consumed-by-verification";
    private static readonly IReadOnlySet<int> NoPressedKeys = new HashSet<int>();

    private sealed record ReviewImage(
        string LookId, string Look, string Preset, string CheckpointId, double TimeSeconds,
        string Renderer, string Adapter, int ScalePercent, int Width, int Height,
        string File, string? DerivedFrom, string? Downsample);

    internal static int Render(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("The review output directory is empty.", nameof(outputDirectory));
        VerifyDownsample2x2();
        var fixturePath = FindFixturePath();
        var scene = ReviewScene.Load(fixturePath);
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var images = new List<ReviewImage>(48);

        using var gpuRenderer = GpuStageRenderer.Create(forceWarp: true);
        if (!gpuRenderer.IsWarp)
            throw new InvalidOperationException($"The visual review requires WARP, but Direct3D created '{gpuRenderer.AdapterName}'.");

        foreach (var look in scene.Looks)
        {
            var preset = VisualPresets.FindBuiltIn(look.ReferencePreset)
                ?? throw new InvalidDataException($"The fixture references missing built-in preset '{look.ReferencePreset}'.");
            var settings = CreateReviewSettings(preset, scene);
            var stage = CreateStage(settings, scene);
            var gpuLook = GpuLook.From(settings,
                (pitch, track) => stage.AdjustColor(stage.NoteColor(pitch, track)),
                stage.KeyboardHeight / stage.ActualHeight,
                stage.AdjustColor);

            RenderSoftwareLook(stage, settings, look, scene, output, images);
            RenderGpuLook(gpuRenderer, gpuLook, settings, look, scene, output, images);
        }

        var expected = scene.Looks.Count * scene.Checkpoints.Count * 2 * 2;
        if (images.Count != expected || expected != 48)
            throw new InvalidOperationException($"The Phase 1 review wrote {images.Count} images; exactly 48 are required.");

        WriteManifest(output, fixturePath, scene, images);
        WriteHtmlIndex(output, scene, images);
        VerifyWrittenArtifact(output, images);
        return images.Count;
    }

    private static string FindFixturePath()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not find the Phase 1 review fixture '{FixtureRelativePath}'. Run --verify from the repository root.");
    }

    private static PianoVisualSettings CreateReviewSettings(VisualPreset preset, ReviewScene scene)
    {
        var settings = preset.Settings.Clone();
        settings.PresetName = preset.Name;
        settings.PresetModified = false;
        settings.KeyboardSize = scene.KeyCount.ToString(CultureInfo.InvariantCulture);
        settings.NoteDirection = scene.NoteDirection;

        // The fixture's hit line and look-ahead define geometry shared by both renderers; all art-direction
        // settings still come from an unmodified clone of the shipped preset.
        var baseKeyboardHeight = Math.Max(80, Math.Min(300, Math.Max(110, Math.Min(228, scene.Height * .205))));
        var targetKeyboardHeight = scene.Height * (1 - scene.HitLineNormalizedY);
        settings.KeyboardScale = targetKeyboardHeight / baseKeyboardHeight * 100;
        var hitLineY = scene.Height * scene.HitLineNormalizedY;
        settings.NoteFallSpeed = hitLineY * 550 / (PianoStage.FallSpeed * scene.LookAheadSeconds);
        settings.Clamp();
        return settings;
    }

    private static PianoStage CreateStage(PianoVisualSettings settings, ReviewScene scene)
    {
        var stage = new PianoStage { Width = scene.Width, Height = scene.Height, SynchronousReviewKeyTileBake = true };
        stage.SetVisualSettings(settings);
        stage.Measure(new Size(scene.Width, scene.Height));
        stage.Arrange(new Rect(0, 0, scene.Width, scene.Height));
        stage.UpdateLayout();
        stage.SetSheet(scene.BeatTimes, scene.BeatsPerBar, scene.DownbeatIndices);
        stage.ResetAnimation(scene.RandomSeed);
        if (Math.Abs(stage.ActualWidth - scene.Width) > .01 || Math.Abs(stage.ActualHeight - scene.Height) > .01)
            throw new InvalidOperationException($"WPF stage layout is {stage.ActualWidth:0.##}×{stage.ActualHeight:0.##}, not the fixture's {scene.Width}×{scene.Height}.");
        if (PianoStage.KeyCount != scene.KeyCount || PianoStage.FirstPitch != scene.FirstMidiPitch)
            throw new InvalidOperationException("The stage keyboard does not match the fixture's 88-key MIDI 21–108 layout.");
        return stage;
    }

    private static void RenderSoftwareLook(PianoStage stage, PianoVisualSettings settings, ReviewLook look,
        ReviewScene scene, string output, List<ReviewImage> images)
    {
        var byFrame = scene.Checkpoints.ToDictionary(checkpoint => checkpoint.Frame);
        var cursor = new ReviewTimelineCursor(scene);
        var dt = 1.0 / scene.FramesPerSecond;
        var lastFrame = scene.Checkpoints[^1].Frame;
        for (var frame = 1; frame <= lastFrame; frame++)
        {
            var time = frame * dt;
            stage.SetState(scene.Notes, time, playing: true, pressed: NoPressedKeys);
            cursor.AdvanceTo(time,
                note => stage.Impact(note.Pitch, note.Velocity / 127.0),
                down => stage.SetSustainPedal(down),
                strength => stage.PulseBeat(strength));
            stage.Advance(dt);

            if (!byFrame.TryGetValue(frame, out var checkpoint)) continue;
            var bitmap = new RenderTargetBitmap(scene.Width, scene.Height, scene.Dpi, scene.Dpi, PixelFormats.Pbgra32);
            bitmap.Render(stage);
            if (!bitmap.CanFreeze) throw new InvalidOperationException("The WPF review capture could not be frozen.");
            bitmap.Freeze();
            var pixels = new byte[scene.Width * scene.Height * 4];
            bitmap.CopyPixels(pixels, scene.Width * 4, 0);
            AddCapture(output, images, look, checkpoint, "WPF", "WPF software stage", pixels,
                scene.Width, scene.Height, PixelFormats.Pbgra32);
        }
    }

    private static void RenderGpuLook(GpuStageRenderer renderer, GpuLook lookData, PianoVisualSettings settings,
        ReviewLook look, ReviewScene scene, string output, List<ReviewImage> images)
    {
        foreach (var checkpoint in scene.Checkpoints)
        {
            var feed = new GpuStageFeed();
            feed.SetLook(lookData);
            feed.SetStageHeight(scene.Height);
            feed.LabelAtlas = PianoStage.GpuGlyphAtlas;
            var cursor = new ReviewTimelineCursor(scene, settings.PhysicsTimeFactor);
            var dt = 1.0 / scene.FramesPerSecond;
            void BeforeFrame(int frame)
            {
                var time = (frame + 1) * dt;
                feed.SetDeterministicState(scene.Notes, time, playing: true, pressed: NoPressedKeys);
                var pulse = cursor.AdvanceTo(time,
                    note => feed.Impact(note.Pitch, note.Velocity / 127.0),
                    feed.SetSustain,
                    _ => { });
                feed.SetDeterministicBeatPulse(pulse);
            }

            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, scene.Width, scene.Height,
                checkpoint.Frame - 1, dt, false, out _, BeforeFrame, scene.RandomSeed);
            AddCapture(output, images, look, checkpoint, "GPU-WARP", renderer.AdapterName, pixels,
                scene.Width, scene.Height, PixelFormats.Bgra32);
        }
    }

    private static void AddCapture(string output, List<ReviewImage> images, ReviewLook look,
        ReviewCheckpoint checkpoint, string renderer, string adapter, byte[] pixels,
        int width, int height, PixelFormat pixelFormat)
    {
        ValidateCapture(pixels, width, height, renderer, checkpoint);
        var presetSlug = Slug(look.ReferencePreset);
        var folder = Path.Combine(presetSlug, checkpoint.Id);
        var relativeFull = Path.Combine(folder, renderer == "WPF" ? "wpf-100.png" : "gpu-warp-100.png");
        var fullPath = Path.Combine(output, relativeFull);
        WritePng(fullPath, width, height, pixelFormat, pixels);
        images.Add(new ReviewImage(look.Id, look.Name, look.ReferencePreset, checkpoint.Id, checkpoint.TimeSeconds,
            renderer, adapter, 100, width, height, ToManifestPath(relativeFull), null, null));

        var halfWidth = width / 2;
        var halfHeight = height / 2;
        var halfPixels = DownsampleAverage2x2(pixels, width, height);
        var relativeHalf = Path.Combine(folder, renderer == "WPF" ? "wpf-50.png" : "gpu-warp-50.png");
        WritePng(Path.Combine(output, relativeHalf), halfWidth, halfHeight, pixelFormat, halfPixels);
        images.Add(new ReviewImage(look.Id, look.Name, look.ReferencePreset, checkpoint.Id, checkpoint.TimeSeconds,
            renderer, adapter, 50, halfWidth, halfHeight, ToManifestPath(relativeHalf),
            ToManifestPath(relativeFull), "2×2 arithmetic mean from the 1920×1080 capture; no smaller re-render."));
    }

    private static void ValidateCapture(byte[] pixels, int width, int height, string renderer, ReviewCheckpoint checkpoint)
    {
        if (pixels.Length != checked(width * height * 4))
            throw new InvalidOperationException($"{renderer} returned {pixels.Length} bytes at {checkpoint.TimeSeconds:0.##} s; expected {width}×{height} BGRA pixels.");
        var visible = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 0 && pixels[i] + pixels[i + 1] + pixels[i + 2] >= 48) visible++;
        if (visible < width * height / 100)
            throw new InvalidOperationException($"{renderer} capture at {checkpoint.TimeSeconds:0.##} s is unexpectedly blank ({visible} visible pixels).");
    }

    private static byte[] DownsampleAverage2x2(byte[] source, int width, int height)
    {
        if ((width & 1) != 0 || (height & 1) != 0) throw new ArgumentException("Averaging 2×2 pixels requires even dimensions.");
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        var target = new byte[halfWidth * halfHeight * 4];
        for (var y = 0; y < halfHeight; y++)
        for (var x = 0; x < halfWidth; x++)
        for (var channel = 0; channel < 4; channel++)
        {
            var sourceIndex = ((y * 2) * width + x * 2) * 4 + channel;
            var sum = source[sourceIndex] + source[sourceIndex + 4] + source[sourceIndex + width * 4] + source[sourceIndex + width * 4 + 4];
            target[(y * halfWidth + x) * 4 + channel] = (byte)((sum + 2) / 4);
        }
        return target;
    }

    private static void VerifyDownsample2x2()
    {
        byte[] sample = [0, 4, 8, 255, 4, 8, 12, 255, 8, 12, 16, 255, 12, 16, 20, 255];
        byte[] expected = [6, 10, 14, 255];
        if (!DownsampleAverage2x2(sample, 2, 2).AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("The review's 2×2 box-average downsampler failed its pixel check.");
    }

    private static void VerifyWrittenArtifact(string output, IReadOnlyList<ReviewImage> images)
    {
        foreach (var image in images)
        {
            var path = Path.Combine(output, image.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new InvalidDataException($"Review image is missing: {image.File}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth != image.Width || decoder.Frames[0].PixelHeight != image.Height)
                throw new InvalidDataException($"Review PNG has the wrong dimensions: {image.File}");
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
        var entries = manifest.RootElement.GetProperty("images").EnumerateArray().ToArray();
        if (manifest.RootElement.GetProperty("captureCount").GetInt32() != 48 || entries.Length != 48 ||
            entries.Count(entry => entry.GetProperty("scalePercent").GetInt32() == 100) != 24 ||
            entries.Count(entry => entry.GetProperty("scalePercent").GetInt32() == 50) != 24 ||
            !File.Exists(Path.Combine(output, "index.html")))
            throw new InvalidDataException("The Phase 1 review artifact is missing its 48-image manifest or HTML index.");
    }

    private static void WritePng(string path, int width, int height, PixelFormat format, byte[] pixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = BitmapSource.Create(width, height, 96, 96, format, null, pixels, checked(width * 4));
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteManifest(string output, string fixturePath, ReviewScene scene, IReadOnlyList<ReviewImage> images)
    {
        var manifest = new
        {
            schemaVersion = 1,
            status = "review-capture",
            generatedAtUtc = DateTime.UtcNow,
            fixture = FixtureRelativePath,
            fixtureSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixturePath))).ToLowerInvariant(),
            render = new
            {
                width = scene.Width, height = scene.Height, framesPerSecond = scene.FramesPerSecond,
                durationSeconds = scene.DurationSeconds, randomSeed = scene.RandomSeed,
                colorSpace = scene.ColorSpace, keyCount = scene.KeyCount,
                firstMidiPitch = scene.FirstMidiPitch, lastMidiPitch = scene.LastMidiPitch
            },
            looks = scene.Looks,
            checkpoints = scene.Checkpoints,
            captureCount = images.Count,
            derivedSize = "50% images are arithmetic 2×2 averages of the matching 100% capture; the scene is never re-laid out.",
            images
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(output, "manifest.json"), json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void WriteHtmlIndex(string output, ReviewScene scene, IReadOnlyList<ReviewImage> images)
    {
        var html = new StringBuilder(24_000);
        html.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine("<title>PianoPath · Phase 1 visual review</title><style>");
        html.AppendLine("body{margin:0;background:#090d16;color:#e9eef7;font:15px/1.5 system-ui,sans-serif}main{max-width:1500px;margin:auto;padding:28px}h1,h2,h3{line-height:1.2}h1{margin-bottom:.25em}header{color:#b9c5d8;margin-bottom:28px}.look{margin:32px 0;padding-top:18px;border-top:1px solid #344052}.checkpoint{margin:22px 0}.checks{color:#aab7ca}.pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.renderer{background:#111827;padding:12px;border-radius:10px}.samples{display:grid;grid-template-columns:1fr 1fr;gap:10px}.sample img{display:block;width:100%;height:auto;background:#000;border:1px solid #384558}.sample a{color:#a8d8ff}.caption{display:flex;justify-content:space-between;gap:8px;margin:5px 0}.small{font-size:12px;color:#9eacc0}@media(max-width:850px){.pair{grid-template-columns:1fr}main{padding:16px}}</style></head><body><main>");
        html.AppendLine("<h1>Phase 1 visual review</h1>");
        html.AppendLine($"<header>48 captures · {scene.Width}×{scene.Height} at 100%, plus 2×2-averaged 50% images · WPF and Direct3D WARP. The 50% files are derived from their matching full-size capture; scene geometry is unchanged.</header>");
        foreach (var look in scene.Looks)
        {
            html.AppendLine($"<section class=\"look\"><h2>{Html(look.Name)} <span class=\"small\">· {Html(look.ReferencePreset)}</span></h2>");
            foreach (var checkpoint in scene.Checkpoints)
            {
                html.AppendLine($"<article class=\"checkpoint\"><h3>{Html(checkpoint.Id)} · {checkpoint.TimeSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s</h3>");
                if (checkpoint.Checks.Length > 0)
                    html.AppendLine("<ul class=\"checks\">" + string.Join("", checkpoint.Checks.Select(check => $"<li>{Html(check)}</li>")) + "</ul>");
                html.AppendLine("<div class=\"pair\">");
                foreach (var renderer in new[] { "WPF", "GPU-WARP" })
                {
                    var full = images.Single(image => image.LookId == look.Id && image.CheckpointId == checkpoint.Id && image.Renderer == renderer && image.ScalePercent == 100);
                    var half = images.Single(image => image.LookId == look.Id && image.CheckpointId == checkpoint.Id && image.Renderer == renderer && image.ScalePercent == 50);
                    var rendererName = renderer == "WPF" ? "WPF stage" : $"GPU · {Html(full.Adapter)}";
                    html.AppendLine($"<div class=\"renderer\"><strong>{rendererName}</strong><div class=\"samples\">");
                    AppendFigure(html, full);
                    AppendFigure(html, half);
                    html.AppendLine("</div></div>");
                }
                html.AppendLine("</div></article>");
            }
            html.AppendLine("</section>");
        }
        html.AppendLine("</main></body></html>");
        File.WriteAllText(Path.Combine(output, "index.html"), html.ToString(), new UTF8Encoding(false));
    }

    private static void AppendFigure(StringBuilder html, ReviewImage image)
    {
        var path = Html(ToManifestPath(image.File));
        html.AppendLine($"<figure class=\"sample\"><div class=\"caption\"><a href=\"{path}\">{image.ScalePercent}% · {image.Width}×{image.Height}</a><span class=\"small\">{Html(image.Renderer)}</span></div><a href=\"{path}\"><img loading=\"lazy\" src=\"{path}\" alt=\"{Html(image.Look)} / {Html(image.CheckpointId)} / {Html(image.Renderer)} / {image.ScalePercent}%\"></a></figure>");
    }

    private static string Slug(string value) => string.Join("-", value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    private static string ToManifestPath(string value) => value.Replace('\\', '/');
    private static string Html(string value) => WebUtility.HtmlEncode(value) ?? "";

    private sealed class ReviewTimelineCursor(ReviewScene scene)
    {
        private int _noteIndex, _pedalIndex, _beatIndex;
        private double _lastTime, _beatPulse;

        internal double AdvanceTo(double time, Action<NoteEvent> noteOnset, Action<bool> pedalChange, Action<double> beatChange)
        {
            var dt = Math.Max(0, time - _lastTime);
            while (_noteIndex < scene.Notes.Count && scene.Notes[_noteIndex].Start <= time + 1e-9)
                noteOnset(scene.Notes[_noteIndex++]);
            while (_pedalIndex < scene.PedalEvents.Count && scene.PedalEvents[_pedalIndex].TimeSeconds <= time + 1e-9)
                pedalChange(scene.PedalEvents[_pedalIndex++].Down);
            while (_beatIndex < scene.BeatTimes.Count && scene.BeatTimes[_beatIndex] <= time + 1e-9)
            {
                _beatPulse = Math.Max(_beatPulse, 1);
                beatChange(1);
                _beatIndex++;
            }
            _beatPulse *= Math.Exp(-6 * dt);
            _lastTime = time;
            return _beatPulse;
        }
    }

    private sealed record ReviewLook(string Id, string Name, string ReferencePreset);
    private sealed record ReviewCheckpoint(string Id, double TimeSeconds, int Frame, string[] Checks);
    private sealed record TempoPoint(double Beat, double Bpm);
    private sealed record PedalEvent(double TimeSeconds, bool Down);

    private sealed class ReviewScene
    {
        internal required int Width { get; init; }
        internal required int Height { get; init; }
        internal required int Dpi { get; init; }
        internal required int FramesPerSecond { get; init; }
        internal required int FrameCount { get; init; }
        internal required double DurationSeconds { get; init; }
        internal required int RandomSeed { get; init; }
        internal required string ColorSpace { get; init; }
        internal required int KeyCount { get; init; }
        internal required int FirstMidiPitch { get; init; }
        internal required int LastMidiPitch { get; init; }
        internal required string NoteDirection { get; init; }
        internal required double HitLineNormalizedY { get; init; }
        internal required double LookAheadSeconds { get; init; }
        internal required int BeatsPerBar { get; init; }
        internal required List<TempoPoint> TempoMap { get; init; }
        internal required List<NoteEvent> Notes { get; set; }
        internal required List<double> BeatTimes { get; init; }
        internal required List<int> DownbeatIndices { get; init; }
        internal required List<PedalEvent> PedalEvents { get; set; }
        internal required List<ReviewCheckpoint> Checkpoints { get; set; }
        internal required List<ReviewLook> Looks { get; set; }

        internal double SecondsAtBeat(double beat)
        {
            if (beat < TempoMap[0].Beat) throw new InvalidDataException($"Beat {beat} precedes the fixture tempo map.");
            var elapsed = 0.0;
            for (var i = 0; i < TempoMap.Count; i++)
            {
                var point = TempoMap[i];
                if (i + 1 < TempoMap.Count)
                {
                    var next = TempoMap[i + 1];
                    if (beat <= next.Beat) return elapsed + (beat - point.Beat) * 60 / point.Bpm;
                    elapsed += (next.Beat - point.Beat) * 60 / point.Bpm;
                }
                else return elapsed + (beat - point.Beat) * 60 / point.Bpm;
            }
            throw new InvalidDataException($"Beat {beat} is outside the fixture tempo map.");
        }

        internal static ReviewScene Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("status").GetString() != FixtureStatus)
                throw new InvalidDataException("The Phase 1 fixture must use schema version 1 and the verification-consumed status.");
            var render = root.GetProperty("render");
            var keyboard = render.GetProperty("keyboard");
            var music = root.GetProperty("music");
            var tempo = music.GetProperty("tempoMap").EnumerateArray()
                .Select(point => new TempoPoint(point.GetProperty("beat").GetDouble(), point.GetProperty("bpm").GetDouble())).ToList();
            if (tempo.Count == 0 || tempo[0].Bpm <= 0 || tempo.Zip(tempo.Skip(1), (a, b) => a.Beat < b.Beat && b.Bpm > 0).Any(valid => !valid))
                throw new InvalidDataException("The fixture tempo map must be ordered and have positive BPM values.");

            var meter = music.GetProperty("meter").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var loop = music.GetProperty("loopBeats").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (meter.Length != 2 || loop.Length != 2 || loop[1] <= loop[0]) throw new InvalidDataException("The fixture meter or beat loop is invalid.");
            var scene = new ReviewScene
            {
                Width = render.GetProperty("width").GetInt32(),
                Height = render.GetProperty("height").GetInt32(),
                Dpi = render.GetProperty("dpi").GetInt32(),
                FramesPerSecond = render.GetProperty("framesPerSecond").GetInt32(),
                FrameCount = render.GetProperty("frameCount").GetInt32(),
                DurationSeconds = render.GetProperty("durationSeconds").GetDouble(),
                RandomSeed = render.GetProperty("randomSeed").GetInt32(),
                ColorSpace = render.GetProperty("colorSpace").GetString() ?? "",
                KeyCount = keyboard.GetProperty("keyCount").GetInt32(),
                FirstMidiPitch = keyboard.GetProperty("firstMidiPitch").GetInt32(),
                LastMidiPitch = keyboard.GetProperty("lastMidiPitch").GetInt32(),
                NoteDirection = keyboard.GetProperty("noteDirection").GetString() ?? "",
                HitLineNormalizedY = keyboard.GetProperty("hitLineNormalizedY").GetDouble(),
                LookAheadSeconds = keyboard.GetProperty("lookaheadSeconds").GetDouble(),
                BeatsPerBar = meter[0],
                TempoMap = tempo,
                Notes = [],
                BeatTimes = [],
                DownbeatIndices = [],
                PedalEvents = [],
                Checkpoints = [],
                Looks = []
            };
            if (scene.Width != 1920 || scene.Height != 1080 || scene.Dpi != 96 || scene.FramesPerSecond != 60 ||
                scene.FrameCount != 720 || Math.Abs(scene.DurationSeconds * scene.FramesPerSecond - scene.FrameCount) > 1e-9 ||
                scene.KeyCount != 88 || scene.FirstMidiPitch != 21 || scene.LastMidiPitch != 108 ||
                scene.NoteDirection != "Down" || scene.LookAheadSeconds <= 0 || scene.HitLineNormalizedY is <= 0 or >= 1)
                throw new InvalidDataException("The Phase 1 fixture no longer matches its agreed 1920×1080, 60 fps, 88-key capture geometry.");

            scene.Notes = music.GetProperty("notes").EnumerateArray().Select(note =>
            {
                var hand = note.GetProperty("hand").GetString();
                if (hand is not ("left" or "right")) throw new InvalidDataException("A fixture note has an unknown hand.");
                var startBeat = note.GetProperty("startBeat").GetDouble();
                var endBeat = startBeat + note.GetProperty("durationBeats").GetDouble();
                var start = scene.SecondsAtBeat(startBeat);
                return new NoteEvent
                {
                    Pitch = note.GetProperty("pitch").GetInt32(), Track = hand == "left" ? 0 : 1,
                    Start = start, Duration = scene.SecondsAtBeat(endBeat) - start,
                    Velocity = note.GetProperty("velocity").GetInt32()
                };
            }).OrderBy(note => note.Start).ThenBy(note => note.Pitch).ToList();
            if (scene.Notes.Count == 0 || scene.Notes.Any(note => note.Pitch < scene.FirstMidiPitch || note.Pitch > scene.LastMidiPitch || note.Duration <= 0 || note.Velocity is < 1 or > 127))
                throw new InvalidDataException("The fixture has no notes or contains notes outside the 88-key stage.");

            for (var beat = loop[0]; beat <= loop[1] + 1e-9; beat += 1)
            {
                scene.BeatTimes.Add(scene.SecondsAtBeat(beat));
                if (Math.Abs((beat - loop[0]) % scene.BeatsPerBar) < 1e-9) scene.DownbeatIndices.Add(scene.BeatTimes.Count - 1);
            }
            scene.PedalEvents = music.GetProperty("pedalEvents").EnumerateArray()
                .Select(item => new PedalEvent(scene.SecondsAtBeat(item.GetProperty("beat").GetDouble()), item.GetProperty("down").GetBoolean())).ToList();
            scene.Checkpoints = root.GetProperty("reviewFrames").EnumerateArray().Select(item =>
            {
                var time = item.GetProperty("timeSeconds").GetDouble();
                var frameValue = time * scene.FramesPerSecond;
                var frame = (int)Math.Round(frameValue, MidpointRounding.AwayFromZero);
                if (Math.Abs(frameValue - frame) > 1e-9 || frame <= 0 || frame > scene.FrameCount)
                    throw new InvalidDataException($"Review checkpoint at {time} s does not land on a fixture frame.");
                var checks = item.GetProperty("checks").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
                return new ReviewCheckpoint(item.GetProperty("id").GetString() ?? "", time, frame, checks);
            }).ToList();
            scene.Looks = root.GetProperty("looks").EnumerateArray().Select(item => new ReviewLook(
                item.GetProperty("id").GetString() ?? "", item.GetProperty("name").GetString() ?? "",
                item.GetProperty("referencePreset").GetString() ?? "")).ToList();
            if (scene.Checkpoints.Count != 4 || scene.Looks.Count != 3 || scene.Checkpoints.Select(c => c.Frame).Distinct().Count() != 4)
                throw new InvalidDataException("The Phase 1 fixture must define exactly three looks and four distinct review checkpoints.");
            if (scene.Looks.Any(look => string.IsNullOrWhiteSpace(look.Id) || string.IsNullOrWhiteSpace(look.Name) || string.IsNullOrWhiteSpace(look.ReferencePreset)))
                throw new InvalidDataException("The Phase 1 fixture contains an incomplete look.");
            return scene;
        }
    }
}
