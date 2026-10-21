using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// N12(d), first slice: the options whose art is drawn <em>into the camera image</em> — the impact wave, the
/// impact burst, the two morph channels and the release ring — re-shot through the three calibrated AR poses
/// with their own off state.
///
/// <para>
/// The flat matrix (<see cref="OptionMatrixReview"/>) proves each value reaches the stage renderer. It cannot
/// prove the same value survives the projection, where a ring becomes a quad, a flash becomes a billboard and
/// a shard is re-aimed along the note; a value that only the stage path knows about would still be green there.
/// This review holds the camera and the input fixed and changes one option, exactly like the flat matrix, so a
/// zero-pixel result means the AR layer never drew the option at all.
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
    private const int ExpectedCaptures = 27; // 3 poses × (4 off states + 5 values; the morph axis has two values)

    private sealed record Counters(int Sprites, int ArParticles);

    private sealed record Shot(string Pose, string Family, string Setting, string Value, bool Neutral,
        string File, string PixelSha256, int ChangedPixels, double SampleSeconds, Counters Counters)
    {
        /// <summary>Held only while the pair is compared; dropped before the manifest is written.</summary>
        internal byte[]? PixelBytes { get; init; }
    }

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
            foreach (var pose in poses)
            // One off state per family: the morph axis is shot twice at the same pose (its particle channel and
            // its flash), and both values are compared against the same off frame instead of re-rendering it.
            foreach (var group in OptionMatrixPlan.PoseSelections.GroupBy(selection => selection.Family))
            {
                var family = OptionMatrixPlan.PoseFamily(group.Key);
                var off = Capture(root, renderer, pose, cameras[pose.Id], family, null, "off", neutral: true);
                captures.Add(off with { ChangedPixels = 0, PixelBytes = null });
                foreach (var selection in group)
                {
                    if (!family.Values.Contains(selection.Value))
                        throw new InvalidDataException($"The {family.Id} pose slice asks for '{selection.Value}', which the family does not offer.");
                    var on = Capture(root, renderer, pose, cameras[pose.Id], family, selection.Value,
                        OptionMatrixPlan.Slug(selection.Value), neutral: false);
                    // The two shots differ in exactly one setting; anything else would make the comparison meaningless.
                    var changed = OptionMatrixPlan.ChangedPixels(off.PixelBytes!, on.PixelBytes!);
                    // The frames of this pair are no longer needed once they have been compared.
                    captures.Add(on with { ChangedPixels = changed, PixelBytes = null });
                    if (changed < MinimumChangedPixels)
                        throw new InvalidDataException(
                            $"{pose.Id}: {family.Id}/{selection.Value} moved {changed} pixels in AR (floor {MinimumChangedPixels}): " +
                            "the option draws on the stage but not through the calibrated projection.");
                }
            }

            if (captures.Count != ExpectedCaptures)
                throw new InvalidDataException($"The AR/pose slice wrote {captures.Count} captures; exactly {ExpectedCaptures} are required.");
            WriteManifest(root, renderer.AdapterName, poses, cameras, captures);
            WriteHtmlIndex(root, captures);
            VerifyWrittenArtifacts(root, captures);
            return captures.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static Shot Capture(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        OptionMatrixPlan.Family family, string? value, string label, bool neutral)
    {
        var settings = OptionMatrixReview.Settings(family, value);
        var seconds = OptionMatrixPlan.Seconds(family);
        var pixels = CaptureGpu(renderer, pose, camera, family, settings);
        var relative = Path.Combine("option-pose", pose.Id, family.Id, $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height,
            $"{family.Id}/{value ?? "off"} in {pose.Id}");
        return new Shot(pose.Id, family.Id, family.Setting, value ?? family.Neutral, neutral,
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

    private static void VerifyWrittenArtifacts(string root, IReadOnlyList<Shot> captures)
    {
        foreach (var capture in captures)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, capture.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != capture.PixelSha256)
                throw new InvalidDataException($"{capture.File} no longer decodes to the pixels the manifest names.");
        }
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<HeldParticleControlPlan.View> poses,
        IReadOnlyDictionary<string, byte[]> cameras, IReadOnlyList<Shot> captures)
    {
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            claim = "N12(d) part 1: five effect axes whose art is projected into the synthetic camera are drawn through the three calibrated AR poses, each against its own off state at the same pose, input and seed.",
            notClaimed = "No art, hardware, real-camera, occlusion, ambient/note option or default change.",
            renderer = "GPU-WARP", adapter,
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            pitch = OptionMatrixPlan.Pitch, strength = OptionMatrixPlan.Strength,
            sampleSeconds = OptionMatrixPlan.PoseSelections
                .Select(selection => OptionMatrixPlan.PoseFamily(selection.Family))
                .DistinctBy(family => family.Id)
                .ToDictionary(family => family.Id, family => OptionMatrixPlan.Seconds(family)),
            floorChangedPixels = MinimumChangedPixels,
            poses = poses.Select(pose => new
            {
                pose.Id,
                corners = pose.Corners!.Select(corner => new { corner.X, corner.Y }),
                cameraSha256 = Convert.ToHexString(SHA256.HashData(cameras[pose.Id]))
            }),
            captures = captures.Select(capture => new
            {
                capture.Pose, capture.Family, capture.Setting, capture.Value, capture.Neutral, capture.File,
                capture.PixelSha256, capture.ChangedPixels,
                SampleSeconds = Math.Round(capture.SampleSeconds, 6),
                capture.Counters
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, IReadOnlyList<Shot> captures)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new System.Text.StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N12(d) — option × AR pose</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1500px;margin:24px auto;padding:16px}")
            .Append("h2{margin:28px 0 8px}p{line-height:1.55;color:#a9b6c7}figure{margin:0}figcaption{padding:4px 0;color:#8fa3ba}")
            .Append("img{width:100%;height:auto;border:1px solid #263243}.row{display:grid;grid-template-columns:repeat(5,1fr);gap:10px}</style>")
            .Append("<h1>Ma trận option × pose AR (N12(d), phần 1)</h1><p>Năm trục hiệu ứng được chiếu vào ảnh camera ")
            .Append("tổng hợp, mỗi trục hai lần chụp (tắt / bật) ở cùng pose, cùng input, cùng seed, WARP. Ảnh native ")
            .Append("1280×720, không cắt, không ghép. Đây là bằng chứng renderer có nhánh cho từng option ở cả ba pose — ")
            .Append("không phải nghiệm thu mỹ thuật, hardware hay camera thật.</p>");
        foreach (var pose in captures.Select(capture => capture.Pose).Distinct())
        {
            body.Append($"<h2>{Encode(pose)}</h2><div class=\"row\">");
            foreach (var family in captures.Where(capture => capture.Pose == pose).GroupBy(capture => capture.Family))
            {
                var off = family.Single(capture => capture.Neutral);
                foreach (var on in family.Where(capture => !capture.Neutral))
                    body.Append($"<figure><figcaption>{Encode(family.Key)}: {Encode(on.Value)} — đổi {on.ChangedPixels:N0} px so với tắt</figcaption>")
                        .Append($"<img loading=\"lazy\" src=\"{Encode(on.File)}\" alt=\"{Encode(family.Key)} {Encode(on.Value)}\">")
                        .Append($"<figcaption><a href=\"{Encode(off.File)}\">ảnh tắt cùng pose</a></figcaption></figure>");
            }
            body.Append("</div>");
        }
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "index.html"), body.ToString());
    }
}
