using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// N12(d): the option surface the AR overlay draws, re-shot through the three calibrated AR poses with each
/// option's own off state.
///
/// <para>
/// Part 1 is the art drawn <em>into the camera image</em> — the impact wave, the impact burst, the two morph
/// channels and the release ring, where a ring becomes a quad, a flash becomes a billboard and a shard is
/// re-aimed along the note. Part 2 is the rest the overlay draws: the note material and the falling trail, built as
/// projected patches (the sampled note is still sounding, so its visible body is 0–0.735 key-depths behind the key
/// plane and its trail reaches about 1.22 — above the keyboard's back edge, inside the picture), and the four
/// ambient layers, emitted screen space over it. Both parts hold the camera and the input fixed and change one
/// option, exactly like the flat matrix, so a zero-pixel result means the AR layer never drew the option at all.
/// </para>
///
/// <para>
/// The poses, the synthetic camera and the calibration corners are the ones the held-particle review already
/// uses (<see cref="HeldParticleControlPlan.Views"/>), so no new fixture, pose or camera claim is introduced.
/// The output is WARP evidence for renderer branching, not an art, hardware or real-camera sign-off.
/// </para>
/// </summary>
internal static class OptionPoseReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    /// <summary>Matches the flat matrix's floor: a "drawn" claim needs more than a stray pixel.</summary>
    private const int MinimumChangedPixels = 12;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    /// <summary>3 poses × (4 off states + 5 values; the morph axis has two values).</summary>
    private const int ExpectedPart1 = 27;
    /// <summary>3 poses × 6 families × (one off state + one value): the note material, the trail, four ambient layers.</summary>
    private const int ExpectedPart2 = 36;
    private const int ExpectedCaptures = ExpectedPart1 + ExpectedPart2;
    /// <summary>One pose fills one sheet: four pictures to a row, a quarter of the capture size each.</summary>
    private const int SheetTileWidth = Width / 4, SheetTileHeight = Height / 4, SheetColumns = 4;

    /// <summary>Every caption one pose's sheet writes, off states first within their family.</summary>
    private static IEnumerable<string> Captions() =>
        new[] { OptionMatrixPlan.PoseSelections, OptionMatrixPlan.PoseSliceSelections }
            .SelectMany(rows => rows.GroupBy(row => row.Family)
                .SelectMany(group => new[] { $"{group.Key} off" }
                    .Concat(group.Select(row => $"{group.Key} {row.Value}"))));

    private sealed record Counters(int Sprites, int ArParticles);

    private sealed record Shot(int Part, string Pose, string Family, string Setting, string Value, bool Neutral,
        string File, string PixelSha256, int ChangedPixels, double SampleSeconds, Counters Counters)
    {
        /// <summary>Held only while the pair is compared; dropped before the manifest is written.</summary>
        internal byte[]? PixelBytes { get; init; }
    }

    /// <summary>One pose's contact sheet, held until it has been written and its file read back.</summary>
    private sealed record Sheet(string Pose, string File, ReviewSheet.Canvas Canvas);

    /// <summary>A sheet as the manifest names it: where it is, how big it is, and the pixels it decodes to.</summary>
    private sealed record WrittenSheet(string Pose, string File, int Width, int Height, int Columns, int Rows,
        int Tiles, string PixelSha256);

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The AR/pose output directory is empty.", nameof(output));
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            var samples = PrismFlowVisualReview.FindSamples();
            var original = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(samples, "ar-keyboard-topdown.png"));
            var poses = HeldParticleControlPlan.Views.Where(view => view.OverCamera).ToArray();
            if (poses.Length != 3) throw new InvalidDataException($"The pose slice needs three calibrated poses, not {poses.Length}.");
            var cameras = poses.ToDictionary(pose => pose.Id, pose => HeldParticleControlPlan.CameraFor(pose, original));
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp) throw new InvalidOperationException($"The AR/pose slice requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var captures = new List<Shot>(ExpectedCaptures);
            var sheets = new List<Sheet>(poses.Length);
            // The two parts walk the same loop: one off state per family, then one capture per value. Part 1 has a
            // family with two channels sharing that off state; part 2 has one row per family by construction.
            (int Part, IReadOnlyList<(string Family, string Value)> Selections)[] parts =
            [
                (1, OptionMatrixPlan.PoseSelections),
                (2, OptionMatrixPlan.PoseSliceSelections)
            ];
            foreach (var pose in poses)
            {
                // One pose fills one sheet: every picture it writes, off and on, in plan order.
                var sheet = new ReviewSheet.Canvas(ExpectedCaptures / poses.Length, SheetTileWidth, SheetTileHeight,
                    SheetColumns, $"N12(D) {pose.Id}", ReviewSheet.CaptionScaleFor(Captions(), SheetTileWidth));
                var cell = 0;
                foreach (var (part, selections) in parts)
                // One off state per family: the morph axis is shot twice at the same pose (its particle channel and
                // its flash), and both values are compared against the same off frame instead of re-rendering it.
                foreach (var group in selections.GroupBy(selection => selection.Family))
                {
                    var family = OptionMatrixPlan.PoseFamily(group.Key);
                    var off = Capture(root, renderer, pose, cameras[pose.Id], part, family, null, "off", neutral: true);
                    captures.Add(off with { ChangedPixels = 0, PixelBytes = null });
                    sheet.Blit(cell++, $"{family.Id} off", off.PixelBytes!, Width, Height, premultiplied: false);
                    foreach (var selection in group)
                    {
                        if (!family.Values.Contains(selection.Value))
                            throw new InvalidDataException($"The {family.Id} pose slice asks for '{selection.Value}', which the family does not offer.");
                        var on = Capture(root, renderer, pose, cameras[pose.Id], part, family, selection.Value,
                            OptionMatrixPlan.Slug(selection.Value), neutral: false);
                        // The two shots differ in exactly one setting; anything else would make the comparison meaningless.
                        var changed = OptionMatrixPlan.ChangedPixels(off.PixelBytes!, on.PixelBytes!);
                        // The frames of this pair are no longer needed once they have been compared.
                        captures.Add(on with { ChangedPixels = changed, PixelBytes = null });
                        sheet.Blit(cell++, $"{family.Id} {selection.Value}", on.PixelBytes!, Width, Height,
                            premultiplied: false);
                        if (changed < MinimumChangedPixels)
                            throw new InvalidDataException(
                                $"{pose.Id}: {family.Id}/{selection.Value} moved {changed} pixels in AR (floor {MinimumChangedPixels}): " +
                                "the option draws on the stage but not through the calibrated projection.");
                    }
                }
                sheets.Add(new Sheet(pose.Id, $"option-pose/sheet-{pose.Id}.png", sheet));
            }

            // Each part is counted on its own: a missing pose, a family that lost its value or a part that
            // quietly stopped running has to name itself instead of being hidden by the other part's captures.
            var part1 = captures.Count(capture => capture.Part == 1);
            var part2 = captures.Count(capture => capture.Part == 2);
            if (part1 != ExpectedPart1 || part2 != ExpectedPart2 || captures.Count != ExpectedCaptures)
                throw new InvalidDataException(
                    $"The AR/pose slice wrote {part1} part-1 and {part2} part-2 captures; {ExpectedPart1} and " +
                    $"{ExpectedPart2} are required.");
            var sheetsWritten = WriteSheets(root, sheets);
            WriteManifest(root, renderer.AdapterName, poses, cameras, captures, sheetsWritten);
            WriteHtmlIndex(root, captures, sheetsWritten);
            VerifyWrittenArtifacts(root, captures, sheetsWritten);
            return captures.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static Shot Capture(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        int part, OptionMatrixPlan.Family family, string? value, string label, bool neutral)
    {
        var settings = OptionMatrixReview.Settings(family, value);
        var seconds = OptionMatrixPlan.Seconds(family);
        var pixels = CaptureGpu(renderer, pose, camera, family, settings);
        var relative = Path.Combine("option-pose", pose.Id, family.Id, $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height,
            $"{family.Id}/{value ?? "off"} in {pose.Id}");
        return new Shot(part, pose.Id, family.Id, family.Setting, value ?? family.Neutral, neutral,
            relative.Replace('\\', '/'), sha, 0, seconds,
            new Counters(renderer.StageSpriteCount, renderer.ArParticleFxCount)) { PixelBytes = pixels };
    }

    private static byte[] CaptureGpu(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        OptionMatrixPlan.Family family, PianoVisualSettings settings)
    {
        // A stage that is never drawn: only its colour pipeline and keyboard fraction feed the look, exactly as
        // in the flat matrix, so both reviews resolve the same palette from the same settings.
        var stage = new PianoStage { Width = Width, Height = Height };
        stage.SetVisualSettings(settings);
        stage.Measure(new Size(Width, Height));
        stage.Arrange(new Rect(0, 0, Width, Height));
        stage.UpdateLayout();
        var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
        feed.SetLook(GpuLook.From(settings, (pitch, track) => stage.AdjustColor(stage.NoteColor(pitch, track)),
            stage.KeyboardHeight / stage.ActualHeight, stage.AdjustColor, true));
        feed.SetStageHeight(Height);
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, pose.Corners!, Width, Height, out var problem,
            HeldParticleControlPlan.Fov) ?? throw new InvalidDataException($"AR/pose calibration: {problem}");
        var ring = new CameraFrameRing();
        camera.CopyTo(ring.BeginWrite(camera.Length, out var slot), 0);
        ring.Publish(slot, Width, Height, Stopwatch.GetTimestamp());
        feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, KeyLight: .6f, Quality: ArQualityTier.Ultra,
            ProjectedNotes: true, ShowDamperStrip: false);
        var notes = OptionMatrixPlan.Notes(family);
        var playing = OptionMatrixPlan.Playing(family);
        var frames = OptionMatrixPlan.Frames(family);
        var dt = 1.0 / OptionMatrixPlan.Fps;
        void BeforeFrame(int frame)
        {
            feed.SetDeterministicState(notes, (frame + 1) * dt, playing, NoPressed);
            feed.SetDeterministicBeatPulse(0);
            OptionMatrixReview.Drive(family, frame,
                pitch => feed.Impact(pitch, OptionMatrixPlan.Strength),
                pitch => feed.LiveNote(pitch, true, OptionMatrixPlan.Strength),
                pitch => feed.LiveNote(pitch, false, OptionMatrixPlan.Strength));
        }
        return GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _, BeforeFrame,
            OptionMatrixPlan.Seed);
    }

    /// <summary>
    /// Writes one contact sheet per pose and names it in the manifest, so the pictures a reviewer needs are three
    /// files in the repository rather than 63 inside an artifact that expires in two days.
    /// </summary>
    private static List<WrittenSheet> WriteSheets(string root, IReadOnlyList<Sheet> sheets)
    {
        var written = new List<WrittenSheet>(sheets.Count);
        foreach (var sheet in sheets)
        {
            var layout = sheet.Canvas.Sheet;
            var sha = ReviewPng.Write(Path.Combine(root, sheet.File), sheet.Canvas.Finish(), layout.Width,
                layout.Height, $"{sheet.Pose}: {sheet.Canvas.Count} captures in {layout.Columns}×{layout.Rows} cells",
                PixelFormats.Pbgra32);
            written.Add(new WrittenSheet(sheet.Pose, sheet.File, layout.Width, layout.Height, layout.Columns,
                layout.Rows, sheet.Canvas.Count, sha));
        }
        return written;
    }

    private static void VerifyWrittenArtifacts(string root, IReadOnlyList<Shot> captures, IReadOnlyList<WrittenSheet> sheets)
    {
        foreach (var capture in captures)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, capture.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != capture.PixelSha256)
                throw new InvalidDataException($"{capture.File} no longer decodes to the pixels the manifest names.");
        }
        // The sheets are the part of the review that leaves the artifact, so each one has to be a whole pose:
        // every capture of that pose, on one page, and nothing else.
        foreach (var sheet in sheets)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, sheet.File), sheet.Width, sheet.Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != sheet.PixelSha256)
                throw new InvalidDataException($"{sheet.File} no longer decodes to the pixels the manifest names.");
            var shots = captures.Count(capture => capture.Pose == sheet.Pose);
            if (sheet.Tiles != shots)
                throw new InvalidDataException(
                    $"{sheet.File} carries {sheet.Tiles} pictures but the {sheet.Pose} pose wrote {shots} captures; " +
                    "a sheet has to show the whole pose, because it is the copy that outlives the artifact.");
        }
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<HeldParticleControlPlan.View> poses,
        IReadOnlyDictionary<string, byte[]> cameras, IReadOnlyList<Shot> captures, IReadOnlyList<WrittenSheet> sheets)
    {
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            claim = "N12(d): every option the overlay draws is re-shot through the three calibrated AR poses against its own off state at the same pose, input and seed — part 1 the five effects drawn into the camera image, part 2 the note material and the falling trail as projected patches plus the four ambient layers emitted screen-space over the picture.",
            notClaimed = "No art, hardware, real-camera, occlusion, hand-occlusion or default change; each axis is shot at one value on both sides, so this proves the option reaches the projection, not that every value does. The note axes are shot at one moment of the note's flight, so this does not claim every distance from the key plane.",
            renderer = "GPU-WARP", adapter,
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            pitch = OptionMatrixPlan.Pitch, strength = OptionMatrixPlan.Strength,
            parts = new[]
            {
                new { Part = 1, Captures = ExpectedPart1, Selections = OptionMatrixPlan.PoseSelections.Select(selection => $"{selection.Family}={selection.Value}") },
                new { Part = 2, Captures = ExpectedPart2, Selections = OptionMatrixPlan.PoseSliceSelections.Select(selection => $"{selection.Family}={selection.Value}") }
            },
            sampleSeconds = OptionMatrixPlan.PoseSelections.Concat(OptionMatrixPlan.PoseSliceSelections)
                .Select(selection => OptionMatrixPlan.PoseFamily(selection.Family))
                .DistinctBy(family => family.Id)
                .ToDictionary(family => family.Id, family => OptionMatrixPlan.Seconds(family)),
            floorChangedPixels = MinimumChangedPixels,
            // The contact sheets are the subset of this directory that leaves the artifact: one page per pose,
            // every capture of that pose on it, captioned, so the evidence can be committed and reviewed later.
            sheets = sheets.Select(sheet => new
            {
                sheet.Pose, sheet.File, sheet.Width, sheet.Height, sheet.Columns, sheet.Rows, sheet.Tiles,
                sheet.PixelSha256
            }),
            poses = poses.Select(pose => new
            {
                pose.Id,
                corners = pose.Corners!.Select(corner => new { corner.X, corner.Y }),
                cameraSha256 = Convert.ToHexString(SHA256.HashData(cameras[pose.Id]))
            }),
            captures = captures.Select(capture => new
            {
                capture.Part, capture.Pose, capture.Family, capture.Setting, capture.Value, capture.Neutral, capture.File,
                capture.PixelSha256, capture.ChangedPixels,
                SampleSeconds = Math.Round(capture.SampleSeconds, 6),
                capture.Counters
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, IReadOnlyList<Shot> captures, IReadOnlyList<WrittenSheet> sheets)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new System.Text.StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N12(d) — option × AR pose</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1500px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 4px}p{line-height:1.55;color:#a9b6c7}h3{margin:18px 0 8px;font-size:15px;color:#c9d6e6}")
            .Append("figure{margin:0}figcaption{padding:4px 0;color:#8fa3ba;font-size:13px}")
            .Append("img{width:100%;height:auto;border:1px solid #263243}")
            .Append(".row{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:10px}</style>")
            .Append("<h1>Ma trận option × pose AR (N12(d))</h1><p>Mỗi option được chụp hai lần — <b>tắt</b> và <b>bật</b> — ")
            .Append("ở cùng pose, cùng input, cùng seed trên WARP, ảnh native 1280×720, không cắt, không ghép. Phần 1 là năm ")
            .Append("trục hiệu ứng được vẽ <em>vào</em> ảnh camera; phần 2 là chất liệu nốt và trail dựng thành mảng chiếu ")
            .Append("cùng bốn lớp ambient mà lớp phủ phát ở không gian màn hình rồi đè lên ảnh. Đây là bằng chứng renderer có ")
            .Append("nhánh cho từng option ở cả ba pose — không phải nghiệm thu mỹ thuật, hardware hay camera thật.</p>");
        var parts = new (int Part, string Title, string Note)[]
        {
            (1, "Phần 1 — hiệu ứng vẽ vào ảnh camera",
                "wave, burst, hai kênh morph, vòng release: mỗi giá trị so với ảnh tắt ở cùng pose."),
            (2, "Phần 2 — nốt chiếu và bốn lớp ambient",
                "chất liệu nốt và trail đi qua đường dựng mảng chiếu 3D của một nốt còn ngân trên phím; bốn lớp ambient đè lên ảnh ở cùng khung hình.")
        };
        foreach (var pose in captures.Select(capture => capture.Pose).Distinct())
        {
            body.Append($"<h2>{Encode(pose)}</h2>");
            if (sheets.FirstOrDefault(candidate => candidate.Pose == pose) is { } sheet)
                body.Append($"<p>Tờ ảnh tiếp xúc của pose này: <a href=\"{Encode(sheet.File)}\">{sheet.Tiles} khung</a> trong lưới ")
                    .Append($"{sheet.Columns}×{sheet.Rows}, {sheet.Width}×{sheet.Height} px. Đây là bản được commit vào repository; ")
                    .Append("các ảnh native bên dưới chỉ nằm trong artifact của lượt chạy.</p>")
                    .Append($"<figure><img src=\"{Encode(sheet.File)}\" alt=\"tờ ảnh tiếp xúc {Encode(pose)}\"></figure>");
            foreach (var part in parts)
            {
                var shots = captures.Where(capture => capture.Pose == pose && capture.Part == part.Part).ToArray();
                if (shots.Length == 0) continue;
                body.Append($"<h3>{Encode(part.Title)}</h3><p>{Encode(part.Note)}</p><div class=\"row\">");
                // One off state per family: group by family so each value links back to the same off picture.
                foreach (var family in shots.GroupBy(capture => capture.Family))
                {
                    var off = family.Single(capture => capture.Neutral);
                    foreach (var on in family.Where(capture => !capture.Neutral))
                        body.Append($"<figure><figcaption>{Encode(family.Key)}: {Encode(on.Value)} — đổi {on.ChangedPixels:N0} px so với tắt</figcaption>")
                            .Append($"<img loading=\"lazy\" src=\"{Encode(on.File)}\" alt=\"{Encode(family.Key)} {Encode(on.Value)}\">")
                            .Append($"<figcaption><a href=\"{Encode(off.File)}\">ảnh tắt cùng pose</a></figcaption></figure>");
                }
                body.Append("</div>");
            }
        }
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "index.html"), body.ToString());
    }
}
