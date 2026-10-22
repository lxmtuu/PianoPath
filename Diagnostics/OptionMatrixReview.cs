using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// N12(b): one capture per option the eleven settings families accept, through the shipping WPF stage and the
/// Direct3D renderer forced to WARP, at a fixed scene, seed and sample time.
///
/// <para>
/// Until this review existed the suite proved that <em>a</em> style of each family drew something (one chosen
/// burst, one chosen morph, one chosen trail). The audit could not claim more, so every other value was
/// "chưa test riêng option X". Here the input is held constant and only the option changes: the same scene
/// renders once with the family off, once per value, and the review fails unless every value moves pixels and
/// no two values paint the same picture. That is a claim about the renderer branching, not about taste — the
/// output is a contact sheet for a human, not an art sign-off.
/// </para>
///
/// <para>
/// Nothing here touches the product's defaults, presets, RNG streams or drawing code: it reads the settings,
/// drives the public stage API the way the app does, and writes PNGs outside the repository. The first run
/// found a real defect anyway — three of the five morphs and both ring releases were drawn by a layer whose
/// own switch was off (see <see cref="EffectChannel"/>) — and the fix lives in the stage, with its own static
/// guard in <c>tools/check_sources.py</c>, not in this file.
/// </para>
/// </summary>
internal static class OptionMatrixReview
{
    private const int Width = OptionMatrixPlan.Width, Height = OptionMatrixPlan.Height;
    /// <summary>Renderer ids, in manifest order.</summary>
    private static readonly string[] RendererIds = ["WPF", "GPU-WARP"];
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    /// <summary>
    /// The floor one option must move to count as drawn. A single 3×3 particle is nine pixels, and the
    /// smallest branches here (one snap-back ring, one dust puff) cover far more, so this floor only rejects
    /// "the option did nothing" instead of grading the art.
    /// </summary>
    private const int MinimumChangedPixels = 12;
    /// <summary>Frames and captures the plan asks for: 62 values plus 11 scenes that keep their off baseline.</summary>
    private const int ExpectedScenes = 64, ExpectedCaptures = ExpectedScenes * 2;
    /// <summary>One sheet per renderer, eight pictures to a row, a sixth of the capture size each.</summary>
    private const int SheetTileWidth = Width / 6, SheetTileHeight = Height / 6, SheetColumns = 8;

    internal sealed record GpuCounters(int Sprites, int Ambient, int NoteTrails, int Notes, int Keys);

    /// <summary>One renderer's contact sheet, held until it has been written and its file read back.</summary>
    private sealed record Sheet(string Renderer, string File, ReviewSheet.Canvas Canvas);

    /// <summary>A sheet as the manifest names it: where it is, how big it is, and the pixels it decodes to.</summary>
    private sealed record WrittenSheet(string Renderer, string File, int Width, int Height, int Columns, int Rows,
        int Tiles, string PixelSha256);

    private sealed class Pending
    {
        internal required string Family { get; init; }
        internal required string Setting { get; init; }
        internal required string Value { get; init; }
        internal required bool Neutral { get; init; }
        internal required string Renderer { get; init; }
        internal required string File { get; init; }
        internal required string PixelSha256 { get; init; }
        internal required double SampleSeconds { get; init; }
        /// <summary>Held only while the family is being compared; dropped before the manifest is written.</summary>
        internal byte[]? Pixels { get; set; }
        internal GpuCounters? Counters { get; init; }
        internal int ChangedPixels { get; set; }
    }

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The option matrix output directory is empty.", nameof(output));
        if (OptionMatrixPlan.SceneCount != ExpectedScenes)
            throw new InvalidDataException(
                $"The option plan now captures {OptionMatrixPlan.SceneCount} scenes; this review pins {ExpectedScenes}. " +
                "Update the plan, this number, docs/OPTION-MATRIX-N12B.md and the suite assert together.");
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The option matrix requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var captures = new List<Pending>(ExpectedCaptures);
            var comparisons = new List<(string Family, string Renderer, string Left, string Right, int Changed)>();
            var baselines = new Dictionary<(OptionMatrixPlan.Scene Scene, string Renderer, string Family), (string Hash, int Lit)>();
            // Two pages for the whole review, one per renderer: a reviewer can see at a glance whether a value
            // draws at all, without the 128 native captures that only live in the artifact.
            var sheets = RendererIds.ToDictionary(id => id,
                id => new ReviewSheet.Canvas(ExpectedScenes, SheetTileWidth, SheetTileHeight, SheetColumns,
                    $"N12(B) {id}"));
            var cells = RendererIds.ToDictionary(id => id, _ => 0);

            foreach (var family in OptionMatrixPlan.Families)
            {
                var baseline = Capture(root, renderer, family, null, "neutral", neutral: true);
                // One entry per value, each holding its WPF and its WARP capture; the flat list is what the
                // manifest records, the pairs are what the per-value comparisons index.
                var scenes = OptionMatrixPlan.CapturedValues(family)
                    .Select(value => Capture(root, renderer, family, value, OptionMatrixPlan.Slug(value), neutral: false))
                    .ToArray();
                var values = scenes.SelectMany(scene => scene).ToArray();
                if (scenes.Length == 0)
                    throw new InvalidDataException($"Option matrix family '{family.Id}' captures no values; the review would never run.");

                foreach (var rendererId in RendererIds)
                {
                    var neutral = baseline.Single(capture => capture.Renderer == rendererId);
                    var lit = OptionMatrixPlan.NonBlackPixels(neutral.Pixels!);
                    if (lit < Width * Height / 50)
                        throw new InvalidDataException(
                            $"The {family.Id} baseline is nearly blank on {rendererId} ({lit} lit pixels); a difference against it would not mean anything.");
                    baselines[(family.Scene, rendererId, family.Id)] = (neutral.PixelSha256, lit);

                    foreach (var capture in values.Where(capture => capture.Renderer == rendererId))
                    {
                        var changed = OptionMatrixPlan.ChangedPixels(capture.Pixels!, neutral.Pixels!);
                        capture.ChangedPixels = changed;
                        comparisons.Add((family.Id, rendererId, "neutral", OptionMatrixPlan.Slug(capture.Value), changed));
                        if (changed < MinimumChangedPixels)
                            throw new InvalidDataException(
                                $"{family.Id}/{capture.Value} moved {changed} pixels on {rendererId} (floor {MinimumChangedPixels}): " +
                                "the option is accepted by the settings class but does not draw.");
                    }

                    for (var left = 0; left < scenes.Length; left++)
                    for (var right = left + 1; right < scenes.Length; right++)
                    {
                        var a = scenes[left].Single(capture => capture.Renderer == rendererId);
                        var b = scenes[right].Single(capture => capture.Renderer == rendererId);
                        var changed = OptionMatrixPlan.ChangedPixels(a.Pixels!, b.Pixels!);
                        comparisons.Add((family.Id, rendererId, OptionMatrixPlan.Slug(a.Value), OptionMatrixPlan.Slug(b.Value), changed));
                        if (changed == 0)
                            throw new InvalidDataException(
                                $"{family.Id}: '{a.Value}' and '{b.Value}' paint the same pixels on {rendererId} at " +
                                $"{a.SampleSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s; two options on one axis must not be one picture.");
                    }
                }

                captures.AddRange(baseline);
                captures.AddRange(values);
                // Every capture of this family goes on its renderer's page before the buffers are dropped.
                foreach (var pending in baseline.Concat(values))
                {
                    var pixels = pending.Pixels;
                    if (pixels is null) continue;
                    var cell = cells[pending.Renderer]++;
                    sheets[pending.Renderer].Blit(cell, $"{family.Id} {pending.Value}", pixels, Width, Height,
                        pending.Renderer == "WPF");
                }
                // The comparison is done; the 2.1 MB frames of this family are not needed by the manifest.
                foreach (var pending in baseline.Concat(values)) pending.Pixels = null;
            }

            // Every family's baseline is the same scene, the same seed and the same input — only the family
            // under test differs, and it is off in all of them. So the baselines of one scene must be the same
            // bytes on one renderer. This is the "option off changes nothing" half of the matrix, and it also
            // catches a renderer that is not deterministic frame to frame.
            foreach (var group in baselines.GroupBy(entry => (entry.Key.Scene, entry.Key.Renderer)))
            {
                var hashes = group.Select(entry => entry.Value.Hash).Distinct().ToArray();
                if (hashes.Length != 1)
                    throw new InvalidDataException(
                        $"{group.Key.Renderer}: the {group.Key.Scene} baselines of {string.Join(", ", group.Select(entry => entry.Key.Family))} " +
                        "are not the same picture; turning every family off must render one scene, bit for bit.");
            }

            if (captures.Count != ExpectedCaptures)
                throw new InvalidDataException($"The option matrix wrote {captures.Count} captures; exactly {ExpectedCaptures} are required.");

            var sheetsWritten = WriteSheets(root, RendererIds
                .Select(id => new Sheet(id, $"option-matrix/sheet-{(id == "WPF" ? "wpf" : "gpu-warp")}.png", sheets[id]))
                .ToArray());
            WriteManifest(root, renderer.AdapterName, captures, comparisons, baselines, sheetsWritten);
            WriteHtmlIndex(root, captures, sheetsWritten);
            VerifyWrittenArtifacts(root, captures, sheetsWritten);
            return captures.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    /// <summary>Renders one scene twice (WPF and WARP), writes both PNGs and returns them for the manifest.</summary>
    private static Pending[] Capture(string root, GpuStageRenderer renderer, OptionMatrixPlan.Family family,
        string? value, string label, bool neutral)
    {
        var settings = Settings(family, value);
        var seconds = OptionMatrixPlan.Seconds(family);
        // A gated family's baseline is the channel switched off, not its first value: label it "off" so a
        // reviewer never reads the Embers baseline as the Embers capture.
        var shown = value ?? (family.Gate is null ? family.Neutral : "off");
        var wpf = CaptureWpf(family, settings);
        var (gpu, counters) = CaptureGpu(renderer, family, settings);
        return
        [
            // The software stage hands out premultiplied BGRA (it draws brushes with alpha); the Direct3D
            // readback is straight BGRA. Writing each in its own format keeps the PNG's colours exact.
            Store(root, family, shown, neutral, "WPF", label, seconds, wpf, PixelFormats.Pbgra32, null),
            Store(root, family, shown, neutral, "GPU-WARP", label, seconds, gpu, PixelFormats.Bgra32, counters)
        ];
    }

    private static Pending Store(string root, OptionMatrixPlan.Family family, string value, bool neutral,
        string rendererId, string label, double seconds, byte[] pixels, PixelFormat format, GpuCounters? counters)
    {
        if (pixels.Length != Width * Height * 4)
            throw new InvalidDataException($"{rendererId} returned {pixels.Length} bytes for {family.Id}/{value}; expected {Width}×{Height} BGRA pixels.");
        var relative = Path.Combine("option-matrix", family.Id, $"{label}-{(rendererId == "WPF" ? "wpf" : "gpu-warp")}.png");
        var sha = WritePng(Path.Combine(root, relative), pixels, format, $"{family.Id}/{value} on {rendererId}");
        return new Pending
        {
            Family = family.Id, Setting = family.Setting, Value = value, Neutral = neutral, Renderer = rendererId,
            File = relative.Replace('\\', '/'), PixelSha256 = sha,
            SampleSeconds = seconds, Pixels = pixels, Counters = counters
        };
    }

    /// <summary>
    /// Every family is off in the baseline and exactly one is on per value; the layers that would add a second
    /// channel are pinned rather than inherited from a preset, so a preset edit cannot change the matrix.
    /// </summary>
    internal static PianoVisualSettings Settings(OptionMatrixPlan.Family family, string? value)
    {
        var settings = new PianoVisualSettings
        {
            ShowBackground = true, ShowNotes = true, ShowSheet = false,
            ShowHalo = false, ShowKeys = true, ShowKeyFelt = false, KeyLabels = "None",
            ShowEmbers = false, ShowFlame = false, ShowWisps = false, ShowLightBeams = false,
            ShowPetals = false, ShowStars = false, NoteLandingGlow = false, NoteShimmer = false, ShootingStars = false,
            ShowImpactRings = false, ShowImpactFlash = false,
            BackgroundMotion = "None", ShowWatermark = false, ShowCounter = false, ShowFps = false,
            NoteStyle = "Solid", FallingTrail = "None", ImpactWave = "None", ImpactMorph = "None",
            ReleaseEffect = "Fade", ReleaseIntensity = 70,
            AmbientEnergy = "None", AmbientNature = "None", AmbientLight = "None", AmbientCosmic = "None",
            FallingTrailIntensity = 100, ImpactMorphIntensity = 70, ImpactFlashIntensity = 80,
            ParticleAmount = 34, ParticleResponse = 55, NoteWidth = 80, NoteFallSpeed = 550
        };
        if (value is not null) Apply(settings, family, value);
        settings.Clamp();
        // The morph and the release effect are the two families with no visibility switch of their own, so
        // their captures are also the proof that they draw while Embers, Impact rings and Impact flash are
        // off. If a future edit switches a layer on for them, this fails instead of quietly weakening the
        // comparison.
        if ((family.Setting is "ImpactMorph" or "ReleaseEffect")
            && (settings.ShowEmbers || settings.ShowWisps || settings.ShowImpactRings || settings.ShowImpactFlash))
            throw new InvalidDataException(
                $"The {family.Id} family must be captured with every layer switch off; its value has to draw on its own.");
        return settings;
    }

    private static void Apply(PianoVisualSettings settings, OptionMatrixPlan.Family family, string value)
    {
        switch (family.Setting)
        {
            case "NoteStyle": settings.NoteStyle = value; break;
            case "FallingTrail": settings.FallingTrail = value; break;
            // The three channels that are an effect on every value: their baseline is the channel switched off.
            case "ImpactWave": settings.ImpactWave = value; settings.ShowImpactRings = true; break;
            case "ImpactBurst": settings.ImpactBurst = value; settings.ShowEmbers = true; break;
            case "ImpactMorph": settings.ImpactMorph = value; break;
            case "ImpactFlashStyle": settings.ImpactFlashStyle = value; settings.ShowImpactFlash = true; break;
            case "ReleaseEffect": settings.ReleaseEffect = value; break;
            case "AmbientEnergy": settings.AmbientEnergy = value; break;
            case "AmbientNature": settings.AmbientNature = value; break;
            case "AmbientLight": settings.AmbientLight = value; break;
            case "AmbientCosmic": settings.AmbientCosmic = value; break;
            default:
                throw new InvalidDataException($"Option matrix family '{family.Id}' names an unknown setting '{family.Setting}'.");
        }
    }

    internal static byte[] CaptureWpf(OptionMatrixPlan.Family family, PianoVisualSettings settings)
        => CaptureWpf(family.Scene, settings);

    /// <summary>
    /// Renders one scene on the shipping software stage. Shared with the N12(e) numeric review: a second copy
    /// of this loop is how the two reviews would end up describing different frames.
    /// </summary>
    internal static byte[] CaptureWpf(OptionMatrixPlan.Scene scene, PianoVisualSettings settings)
    {
        // Its own stage, its own seed, its own clock: nothing a previous capture left behind can reach it. The
        // key tiles are baked synchronously so the picture does not depend on a background render pass.
        var stage = new PianoStage { Width = Width, Height = Height, SynchronousReviewKeyTileBake = true };
        stage.SetVisualSettings(settings);
        stage.Measure(new Size(Width, Height));
        stage.Arrange(new Rect(0, 0, Width, Height));
        stage.UpdateLayout();
        stage.ResetAnimation(OptionMatrixPlan.Seed);
        var notes = OptionMatrixPlan.Notes(scene);
        var playing = OptionMatrixPlan.Playing(scene);
        var dt = 1.0 / OptionMatrixPlan.Fps;
        for (var frame = 0; frame < OptionMatrixPlan.Frames(scene); frame++)
        {
            // The clock only ever moves forward, so the stage can never read a rewind as a seek and drop the
            // release backlog; the hit and the release are driven at the same frame numbers on both renderers.
            stage.SetState(notes, (frame + 1) * dt, playing, NoPressed);
            Drive(scene, frame,
                pitch => stage.Impact(pitch, OptionMatrixPlan.Strength),
                pitch => stage.AddLiveNote(pitch, OptionMatrixPlan.Strength),
                pitch => stage.ReleaseLiveNote(pitch));
            stage.Advance(dt);
        }
        Phase1VisualReview.FlushWpfRenderPass();
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        bitmap.Freeze();
        var pixels = new byte[Width * Height * 4];
        bitmap.CopyPixels(pixels, Width * 4, 0);
        return pixels;
    }

    private static (byte[] Pixels, GpuCounters Counters) CaptureGpu(GpuStageRenderer renderer,
        OptionMatrixPlan.Family family, PianoVisualSettings settings)
        => CaptureGpu(renderer, family.Scene, settings);

    /// <summary>Renders one scene through the Direct3D stage on WARP; likewise shared with N12(e).</summary>
    internal static (byte[] Pixels, GpuCounters Counters) CaptureGpu(GpuStageRenderer renderer,
        OptionMatrixPlan.Scene scene, PianoVisualSettings settings)
    {
        // A stage that is never drawn: its colour pipeline and keyboard fraction are what the look is built
        // from, exactly as the app builds it, so the two renderers resolve the same palette.
        var stage = new PianoStage { Width = Width, Height = Height };
        stage.SetVisualSettings(settings);
        stage.Measure(new Size(Width, Height));
        stage.Arrange(new Rect(0, 0, Width, Height));
        stage.UpdateLayout();
        var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
        feed.SetLook(GpuLook.From(settings, (pitch, track) => stage.AdjustColor(stage.NoteColor(pitch, track)),
            stage.KeyboardHeight / stage.ActualHeight, stage.AdjustColor));
        feed.SetStageHeight(Height);
        var notes = OptionMatrixPlan.Notes(scene);
        var playing = OptionMatrixPlan.Playing(scene);
        var frames = OptionMatrixPlan.Frames(scene);
        var dt = 1.0 / OptionMatrixPlan.Fps;
        void BeforeFrame(int frame)
        {
            feed.SetDeterministicState(notes, (frame + 1) * dt, playing, NoPressed);
            feed.SetDeterministicBeatPulse(0);
            Drive(scene, frame,
                pitch => feed.Impact(pitch, OptionMatrixPlan.Strength),
                pitch => feed.LiveNote(pitch, true, OptionMatrixPlan.Strength),
                pitch => feed.LiveNote(pitch, false, OptionMatrixPlan.Strength));
        }
        var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _,
            BeforeFrame, OptionMatrixPlan.Seed);
        return (pixels, new GpuCounters(renderer.StageSpriteCount, renderer.AmbientCount, renderer.StageNoteTrailCount,
            renderer.StageNoteCount, renderer.StageKeyCount));
    }

    internal static void Drive(OptionMatrixPlan.Family family, int frame, Action<int> impact, Action<int> down, Action<int> up)
        => Drive(family.Scene, frame, impact, down, up);

    /// <summary>The one place a review turns a frame number into the input that scene asks for.</summary>
    internal static void Drive(OptionMatrixPlan.Scene scene, int frame, Action<int> impact, Action<int> down, Action<int> up)
    {
        switch (scene)
        {
            case OptionMatrixPlan.Scene.Impact when frame == OptionMatrixPlan.TriggerFrame:
                impact(OptionMatrixPlan.Pitch);
                break;
            case OptionMatrixPlan.Scene.Release or OptionMatrixPlan.Scene.Held when frame == 0:
                down(OptionMatrixPlan.Pitch);
                break;
            // The held scene never lets go: the bar it draws is the one the numeric review measures.
            case OptionMatrixPlan.Scene.Release when frame == OptionMatrixPlan.ReleaseFrame:
                up(OptionMatrixPlan.Pitch);
                break;
        }
    }

    /// <summary>
    /// Writes one PNG and returns the SHA-256 of the pixels <em>as they decode back from the file</em>, so the
    /// manifest names the picture a reviewer opens rather than the buffer the renderer held. The two are not
    /// byte-identical by construction: PNG stores straight alpha while the software stage hands out
    /// premultiplied BGRA, and a codec is free to round the conversion. The checksum is therefore taken after
    /// the round trip, and <see cref="VerifyWrittenArtifacts"/> re-derives it from the files on disk.
    /// </summary>
    private static string WritePng(string path, byte[] pixels, PixelFormat format, string what)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, format, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return Convert.ToHexString(SHA256.HashData(ReadPng(path, format, what)));
    }

    /// <summary>The pixels a written PNG decodes to, in the renderer's own format; also the one size check.</summary>
    private static byte[] ReadPng(string path, PixelFormat format, string what)
    {
        using var read = File.OpenRead(path);
        var decoded = BitmapDecoder.Create(read, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (decoded.PixelWidth != Width || decoded.PixelHeight != Height)
            throw new InvalidDataException($"The option matrix PNG of {what} is {decoded.PixelWidth}×{decoded.PixelHeight}, not {Width}×{Height}.");
        var pixels = new byte[Width * Height * 4];
        new FormatConvertedBitmap(decoded, format, null, 0).CopyPixels(pixels, Width * 4, 0);
        return pixels;
    }

    private static void WriteManifest(string root, string adapter, List<Pending> captures,
        List<(string Family, string Renderer, string Left, string Right, int Changed)> comparisons,
        Dictionary<(OptionMatrixPlan.Scene Scene, string Renderer, string Family), (string Hash, int Lit)> baselines,
        List<WrittenSheet> sheets)
    {
        var manifest = new
        {
            schemaVersion = 1,
            status = "n12b-option-matrix-not-art-signoff",
            claim = "every value of eleven settings families is accepted, drawn and distinguishable from its own off state and from its siblings on the WPF stage and the Direct3D stage on WARP at one fixed scene",
            notClaimed = "art approval, live MIDI/CC64 routing, AR projection, hardware frames, other quality tiers or a default change",
            ownedChannels = "the morph and the release families have no visibility switch of their own and are captured with Embers, Impact rings, Impact flash and wisps off, so each of those captures proves the value draws by itself",
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            pitch = OptionMatrixPlan.Pitch, strength = Math.Round(OptionMatrixPlan.Strength, 6),
            adapter,
            input = "one held song note (falling), one live hit (impact), one live press/release (release) or no input (ambient); no OS MIDI callback",
            gate = "the burst and flash families have no off value: their baseline switches ShowEmbers / ShowImpactFlash off and every value is captured with it on",
            minimumChangedPixels = MinimumChangedPixels,
            families = OptionMatrixPlan.Families.Select(family => new
            {
                family.Id, family.Setting, Scene = family.Scene.ToString(), family.Values, family.Neutral,
                family.Gate, Frames = OptionMatrixPlan.Frames(family),
                SampleSeconds = Math.Round(OptionMatrixPlan.Seconds(family), 4),
                captured = OptionMatrixPlan.CapturedValues(family)
            }),
            baselines = baselines.OrderBy(entry => entry.Key.Scene).ThenBy(entry => entry.Key.Renderer)
                .Select(entry => new { Scene = entry.Key.Scene.ToString(), entry.Key.Renderer, entry.Key.Family, entry.Value.Hash, entry.Value.Lit }),
            comparisons = comparisons.Select(entry => new { entry.Family, entry.Renderer, entry.Left, entry.Right, entry.Changed }),
            // The two files of this review that are committed: every scene on one page per renderer.
            sheets = sheets.Select(sheet => new
            {
                sheet.Renderer, sheet.File, sheet.Width, sheet.Height, sheet.Columns, sheet.Rows, sheet.Tiles,
                sheet.PixelSha256
            }),
            captureCount = captures.Count,
            captures = captures.Select(capture => new
            {
                capture.Family, capture.Setting, capture.Value, capture.Neutral, capture.Renderer, capture.File,
                capture.PixelSha256, capture.ChangedPixels, capture.SampleSeconds,
                Sprites = capture.Counters?.Sprites, Ambient = capture.Counters?.Ambient,
                NoteTrails = capture.Counters?.NoteTrails, Notes = capture.Counters?.Notes, Keys = capture.Counters?.Keys
            })
        };
        File.WriteAllText(Path.Combine(root, "option-matrix", "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, List<Pending> captures, List<WrittenSheet> sheets)
    {
        var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Keyflow option matrix (N12(b))</title>");
        html.Append("<style>body{background:#111;color:#eee;font:15px/1.45 system-ui,sans-serif;margin:24px}");
        html.Append("h1{font-size:22px}h2{font-size:17px;margin:28px 0 8px;border-bottom:1px solid #333;padding-bottom:4px}");
        html.Append(".row{display:flex;flex-wrap:wrap;gap:10px}.card{margin:0;width:308px}img{width:100%;display:block;border:1px solid #2a2a2a}");
        html.Append("figcaption{color:#aaa;font-size:12px;padding-top:4px}b{color:#fff}</style>");
        html.Append("<h1>Keyflow — ma trận option N12(b)</h1>");
        html.Append("<p>Mỗi khung là cùng một cảnh, cùng seed, cùng thời điểm; chỉ option đang xét thay đổi. ");
        html.Append("WPF và Direct3D/WARP, 960×540. Morph và release không có công tắc riêng nên được chụp khi ");
        html.Append("Embers / Impact rings / Impact flash / wisps đều tắt: mỗi khung đó chứng minh option tự vẽ. ");
        html.Append("Đây là bằng chứng renderer rẽ nhánh, không phải duyệt mỹ thuật.</p>");
        foreach (var sheet in sheets)
            html.Append($"<p><b>{WebUtility.HtmlEncode(sheet.Renderer)}</b>: toàn bộ {sheet.Tiles} khung trên một tờ ")
                .Append($"<a href=\"{WebUtility.HtmlEncode(sheet.File)}\">{sheet.Columns}×{sheet.Rows}</a> ")
                .Append($"({sheet.Width}×{sheet.Height} px) — bản được commit vào repository.</p>")
                .Append($"<figure class=\"card\"><img src=\"{WebUtility.HtmlEncode(sheet.File)}\" alt=\"{WebUtility.HtmlEncode(sheet.Renderer)} contact sheet\"></figure>");
        foreach (var family in OptionMatrixPlan.Families)
        {
            html.Append($"<h2>{WebUtility.HtmlEncode(family.Id)} · {WebUtility.HtmlEncode(family.Setting)}</h2><div class=\"row\">");
            foreach (var capture in captures.Where(capture => capture.Family == family.Id))
            {
                html.Append("<figure class=\"card\">");
                html.Append($"<img loading=\"lazy\" src=\"{WebUtility.HtmlEncode(capture.File)}\" alt=\"{WebUtility.HtmlEncode(capture.Value)}\">");
                var changed = capture.Neutral ? "baseline" : $"+{capture.ChangedPixels} px vs baseline";
                html.Append($"<figcaption><b>{WebUtility.HtmlEncode(capture.Value)}</b> · {capture.Renderer} · {changed}</figcaption></figure>");
            }
            html.Append("</div>");
        }
        File.WriteAllText(Path.Combine(root, "option-matrix", "index.html"), html.ToString());
    }

    /// <summary>
    /// Writes one contact sheet per renderer: every scene of the matrix, in plan order, on one page.
    /// </summary>
    private static List<WrittenSheet> WriteSheets(string root, IReadOnlyList<Sheet> sheets)
    {
        var written = new List<WrittenSheet>(sheets.Count);
        foreach (var sheet in sheets)
        {
            var layout = sheet.Canvas.Sheet;
            var sha = ReviewPng.Write(Path.Combine(root, sheet.File), sheet.Canvas.Finish(), layout.Width,
                layout.Height, $"{sheet.Renderer}: {sheet.Canvas.Count} scenes in {layout.Columns}×{layout.Rows} cells",
                PixelFormats.Pbgra32);
            written.Add(new WrittenSheet(sheet.Renderer, sheet.File, layout.Width, layout.Height, layout.Columns,
                layout.Rows, sheet.Canvas.Count, sha));
        }
        return written;
    }

    private static void VerifyWrittenArtifacts(string root, List<Pending> captures, List<WrittenSheet> sheets)
    {
        // The sheets are the part of this review that leaves the artifact: each one has to carry every scene the
        // manifest counts on its renderer, and its file has to decode to the pixels the manifest names.
        foreach (var sheet in sheets)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, sheet.File), sheet.Width, sheet.Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != sheet.PixelSha256)
                throw new InvalidDataException($"{sheet.File} no longer decodes to the pixels the manifest names.");
            var scenes = captures.Count(capture => capture.Renderer == sheet.Renderer);
            if (sheet.Tiles != scenes)
                throw new InvalidDataException(
                    $"{sheet.File} carries {sheet.Tiles} pictures but {sheet.Renderer} wrote {scenes} captures; " +
                    "a sheet has to show the whole renderer, because it is the copy that outlives the artifact.");
        }
        foreach (var capture in captures)
        {
            var path = Path.Combine(root, capture.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new InvalidDataException($"Option matrix capture is missing: {capture.File}");
            // The manifest's checksum is the file's own: re-derive it, so a truncated or re-encoded PNG cannot
            // sit under a hash that describes a picture nobody can open.
            var format = capture.Renderer == "WPF" ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
            var sha = Convert.ToHexString(SHA256.HashData(ReadPng(path, format, $"{capture.Family}/{capture.Value}")));
            if (!string.Equals(sha, capture.PixelSha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"The option matrix PNG of {capture.Family}/{capture.Value} on {capture.Renderer} no longer decodes to the pixels its manifest names.");
        }
        var index = Path.Combine(root, "option-matrix", "index.html");
        if (!File.Exists(index)) throw new InvalidDataException("The option matrix index.html is missing.");
        var page = File.ReadAllText(index);
        foreach (var family in OptionMatrixPlan.Families)
            if (!page.Contains($"<h2>{family.Id}", StringComparison.Ordinal))
                throw new InvalidDataException($"The option matrix index.html does not show the '{family.Id}' family.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "option-matrix", "manifest.json")));
        var entries = manifest.RootElement.GetProperty("captures").EnumerateArray().ToArray();
        if (manifest.RootElement.GetProperty("captureCount").GetInt32() != captures.Count || entries.Length != captures.Count)
            throw new InvalidDataException("The option matrix manifest and the captures on disk disagree.");
        if (entries.Any(entry => !File.Exists(Path.Combine(root, entry.GetProperty("File").GetString()!.Replace('/', Path.DirectorySeparatorChar)))))
            throw new InvalidDataException("The option matrix manifest names a file that was not written.");
    }
}
