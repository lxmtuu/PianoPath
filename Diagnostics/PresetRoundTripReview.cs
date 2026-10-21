using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N12(f): every preset that ships — fourteen built-ins and the six community shelf files — is applied,
/// saved to a preset file, read back, applied again, and must draw the same picture.
///
/// <para>
/// This is the half the other reviews cannot see. N12(b) proves each option draws and N12(e) proves each slider
/// is read, but both build their settings in memory. A look that a player saves and opens tomorrow takes a
/// different road: <see cref="PianoVisualSettings.CopyFrom"/> into the live settings,
/// <see cref="VisualPresets.WritePresetFile"/> to disk, <see cref="VisualPresets.ReadPresetFile"/> back,
/// <c>ApplyMigrations</c> and <c>Clamp</c> on the way in. A property that the file does not carry — or that a
/// migration rewrites — comes back as its default and the look changes after a reload, quietly.
/// </para>
///
/// <para>
/// The review therefore checks three things per preset and fails on any of them. First, <b>every writable
/// property</b> of the settings that come back from the file is compared with the settings that were written,
/// by reflection rather than through a hand-written list, and the failure names the property: a pixel count
/// alone would say "something moved" without saying what. Second, the same comparison runs against the settings
/// after the dock's own apply path, so a step that clamps or migrates on the way into the live settings is
/// caught too. Third, both renderers are asked for the same frame before and after the file, and the two must be
/// <b>identical byte for byte</b>; that half proves the settings the comparison checks are the settings the
/// renderer actually reads.
/// </para>
///
/// <para>
/// One scene is enough here, unlike the flat matrix: a field that the file drops is caught by the property
/// comparison whether or not the sampled scene happens to draw it. The frame is only the witness that the look
/// reaches a renderer at all. WARP evidence, not art approval, hardware, or a migration-version claim.
/// </para>
/// </summary>
internal static class PresetRoundTripReview
{
    private const int Width = OptionMatrixPlan.Width, Height = OptionMatrixPlan.Height;
    /// <summary>Fourteen built-in looks plus the six shelf files the repository ships. Adding a preset is a
    /// deliberate act, so this number being wrong turns the suite red instead of silently widening the review.</summary>
    private const int ExpectedPresets = 20;
    /// <summary>The note-and-ambient scene: the one moment nearly every look differs at (material, trail, keys,
    /// background, ambient), and the slowest is 90 frames — acceptable for 20 presets on two renderers.</summary>
    private const OptionMatrixPlan.Scene Scene = OptionMatrixPlan.Scene.Falling;

    internal sealed record Result(int Presets, int Frames, int Files, int Properties);

    private sealed record Case(string Name, string Source, PianoVisualSettings Settings, string Description);

    private sealed record Shot(string Preset, string Source, string Renderer, string File, string FrameSha256,
        string AfterSha256, string FileSha256, int ChangedPixels, string SettingsSha256, string SettingsAfterSha256,
        int Properties);

    internal static Result Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The preset round-trip output directory is empty.", nameof(output));
        var cases = Cases();
        if (cases.Count != ExpectedPresets)
            throw new InvalidDataException(
                $"The build ships {cases.Count} presets; this review pins {ExpectedPresets}. Update the number, the docs and the " +
                "suite assert together, so a new look is reviewed rather than silently skipped.");
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The preset round trip requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var shots = new List<Shot>(cases.Count * 2);
            var properties = 0;
            foreach (var item in cases)
            {
                var before = Apply(item, new PianoVisualSettings());
                var file = VisualPresets.WritePresetFile(before, null, item.Description);
                var stored = VisualPresets.ReadPresetFile(file);
                if (stored.Description != item.Description)
                    throw new InvalidDataException(
                        $"{item.Name}: the preset file came back with description '{stored.Description}' instead of " +
                        $"'{item.Description}'; the envelope does not survive its own writer.");

                // The file's own half: the settings that were written and the settings that came back.
                properties = Compare(item.Name, "after the preset file", before, stored.Settings);
                var after = Apply(new Case(item.Name, item.Source, stored.Settings, stored.Description), new PianoVisualSettings());
                // The dock's half: what the player sees, applied from the file rather than from memory.
                var applied = Compare(item.Name, "through the dock's apply path", before, after);
                if (applied != properties)
                    throw new InvalidDataException(
                        $"{item.Name}: the file comparison walked {properties} properties but the apply comparison walked {applied}.");

                var beforeJson = before.ToJson();
                var afterJson = after.ToJson();
                if (beforeJson != afterJson)
                    throw new InvalidDataException(
                        $"{item.Name}: the settings text changed after the round trip although every property compared " +
                        "equal; the writer and the comparison are not looking at the same object.");

                var wpfBefore = OptionMatrixReview.CaptureWpf(Scene, before);
                var wpfAfter = OptionMatrixReview.CaptureWpf(Scene, after);
                var (gpuBefore, _) = OptionMatrixReview.CaptureGpu(renderer, Scene, before);
                var (gpuAfter, _) = OptionMatrixReview.CaptureGpu(renderer, Scene, after);
                foreach (var (rendererId, a, b) in new[] { ("WPF", wpfBefore, wpfAfter), ("GPU-WARP", gpuBefore, gpuAfter) })
                {
                    // Not ChangedPixels' tolerance: "the same picture" here means the same bytes, because the
                    // settings are supposed to rebuild an identical stage, not a nearly identical one.
                    var changed = OptionMatrixPlan.ChangedPixels(a, b, tolerance: 0);
                    if (changed != 0 || !a.AsSpan().SequenceEqual(b))
                    {
                        var differing = 0;
                        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) differing++;
                        throw new InvalidDataException(
                            $"{item.Name} on {rendererId}: {differing} of {a.Length} bytes differ ({changed} colour pixels at " +
                            "zero tolerance) after a save/load round trip. The preset file does not carry the whole look, " +
                            "so the player's saved preset is not the look they saved.");
                    }
                    var relative = Path.Combine("preset-roundtrip", OptionMatrixPlan.Slug(item.Name),
                        rendererId == "WPF" ? "wpf.png" : "gpu-warp.png");
                    // Each renderer's buffer is stored in its own format: the software stage hands out
                    // premultiplied BGRA, the Direct3D readback straight BGRA (see OptionMatrixReview.Store).
                    var fileSha = ReviewPng.Write(Path.Combine(root, relative), a, Width, Height, $"{item.Name} on {rendererId}",
                        rendererId == "WPF" ? PixelFormats.Pbgra32 : PixelFormats.Bgra32);
                    shots.Add(new Shot(item.Name, item.Source, rendererId, relative.Replace('\\', '/'),
                        Convert.ToHexString(SHA256.HashData(a)), Convert.ToHexString(SHA256.HashData(b)), fileSha,
                        changed, SettingsSha(beforeJson), SettingsSha(afterJson), properties));
                }
            }

            WriteManifest(root, renderer.AdapterName, shots, properties);
            WriteHtmlIndex(root, shots, properties);
            VerifyWrittenArtifacts(root, shots);
            return new Result(cases.Count, shots.Count * 2, shots.Count, properties);
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    /// <summary>
    /// The presets the build ships, in product order. Built-ins first, then the shelf as
    /// <see cref="CommunityPresets"/> loaded it from the embedded files, so the review walks the product's own
    /// list instead of a copy that can go stale.
    /// </summary>
    private static List<Case> Cases()
    {
        var cases = new List<Case>();
        foreach (var preset in VisualPresets.BuiltIn)
            cases.Add(new Case(preset.Name, "built-in", preset.Settings, preset.Description));
        foreach (var preset in CommunityPresets.All)
            cases.Add(new Case(preset.Name, "shelf", preset.Settings, preset.Description));
        return cases;
    }

    /// <summary>
    /// Exactly what the dock does when a preset is clicked (<c>MainWindow.ApplyPreset</c>): copy onto the live
    /// settings, name it, mark it unmodified, and refuse an image background whose path is not there. Kept in
    /// step with that method by <c>tools/check_sources.py</c>.
    /// </summary>
    private static PianoVisualSettings Apply(Case item, PianoVisualSettings target)
    {
        target.CopyFrom(item.Settings, keepBackgroundImage: true);
        target.PresetName = item.Name;
        target.PresetModified = false;
        if (target.BackgroundMode == "Image" && string.IsNullOrWhiteSpace(target.BackgroundImagePath))
            target.BackgroundMode = "Solid";
        return target;
    }

    /// <summary>
    /// Compares every writable property, by reflection, and returns how many were compared. A hand-written list
    /// would be the one place a new property could be forgotten, so there is none: the loop walks the type.
    ///
    /// <para>
    /// A property that <see cref="PianoVisualSettings.CopyFrom"/> deliberately leaves alone (the language, the
    /// graphics engine, this machine's camera, audio device and key calibration) is equal on both sides because
    /// neither side ever set it — that is the product's rule, not a hole: a look is not allowed to carry it.
    /// </para>
    /// </summary>
    private static int Compare(string name, string where, PianoVisualSettings before, PianoVisualSettings after)
    {
        var compared = 0;
        foreach (var property in typeof(PianoVisualSettings).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite) continue;
            compared++;
            var a = property.GetValue(before);
            var b = property.GetValue(after);
            var same = (a, b) switch
            {
                (null, null) => true,
                (null, _) or (_, null) => false,
                (List<string> left, List<string> right) => left.SequenceEqual(right, StringComparer.Ordinal),
                _ => a!.Equals(b)
            };
            if (!same)
                throw new InvalidDataException(
                    $"{name}: {property.Name} came back as '{b}' instead of '{a}' {where}. A property the file or a " +
                    "migration drops is a look that changes when it is reopened.");
        }
        if (compared < 200)
            throw new InvalidDataException(
                $"{name}: only {compared} writable properties were compared; the settings class lost most of its surface " +
                "or the comparison stopped walking the type.");
        return compared;
    }

    private static void VerifyWrittenArtifacts(string root, IReadOnlyList<Shot> shots)
    {
        foreach (var shot in shots)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, shot.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != shot.FileSha256)
                throw new InvalidDataException($"{shot.File} no longer decodes to the pixels the manifest names.");
        }
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<Shot> shots, int properties)
    {
        File.WriteAllText(Path.Combine(root, "preset-roundtrip", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            status = "n12f-preset-roundtrip-not-art-signoff",
            claim = "every preset the build ships survives the product's own save/load path: every writable settings property is identical after the preset file and again after the dock's apply path, and both renderers produce a byte-identical frame before and after it",
            notClaimed = "art approval, migration-version coverage beyond the current pair, hardware frames, other scenes or a default change",
            whyOneScene = "a field the file drops is caught by the property comparison whether or not the sampled scene draws it; the frame is only the witness that the look reaches a renderer",
            applyPath = "MainWindow.ApplyPreset: CopyFrom(keepBackgroundImage), PresetName, PresetModified=false, Image without a path falls back to Solid",
            savePath = "VisualPresetStore.Save writes a clone after ClearMachineLocal; CopyFrom never copies a machine-local field, so the settings written here are already at this machine's defaults and the review writes them directly",
            filePath = "VisualPresets.WritePresetFile -> ReadPresetFile (envelope v1 with thumbnail and description), then FromJson's ApplyMigrations + Clamp",
            frameComparison = "exact byte equality of the BGRA buffers (ChangedPixels with tolerance 0 plus a span compare), not the 2/255 tolerance the option matrix uses",
            renderer = "GPU-WARP", adapter,
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            scene = Scene.ToString(), frames = OptionMatrixPlan.Frames(Scene),
            sampleSeconds = Math.Round(OptionMatrixPlan.Seconds(Scene), 4),
            propertiesCompared = properties,
            presets = shots.Select(shot => shot.Preset).Distinct().ToArray(),
            captures = shots.Select(shot => new
            {
                shot.Preset, shot.Source, shot.Renderer, shot.File, shot.FrameSha256, shot.AfterSha256, shot.FileSha256,
                shot.ChangedPixels, shot.SettingsSha256, shot.SettingsAfterSha256, shot.Properties
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>A stable digest of the settings JSON, so the manifest carries the text the file actually held.</summary>
    private static string SettingsSha(string json) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));

    private static void WriteHtmlIndex(string root, IReadOnlyList<Shot> shots, int properties)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new System.Text.StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N12(f) — round-trip preset</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px auto;padding:16px}")
            .Append("p{line-height:1.55;color:#a9b6c7}h2{margin:26px 0 6px;font-size:17px}table{border-collapse:collapse}")
            .Append("td,th{border:1px solid #263243;padding:3px 8px}img{width:100%;height:auto;border:1px solid #263243}")
            .Append(".pair{display:grid;grid-template-columns:1fr 1fr;gap:10px}.ok{color:#7fd2a0}</style>")
            .Append("<h1>Round-trip preset (N12(f))</h1><p>Mỗi preset đang ship được áp đúng đường của dock, ghi ra tệp preset, ")
            .Append("đọc lại, áp lần nữa — rồi so <b>")
            .Append(properties).Append("</b> thuộc tính ghi được (cả bản đọc từ tệp lẫn bản áp vào settings sống) và so khung hình trên <b>cả hai</b> renderer. Khung hình ")
            .Append("trước và sau phải <b>trùng từng byte</b>; ảnh dưới là khung trước khi lưu. Đây là bằng chứng tệp preset ")
            .Append("không làm mất look, không phải nghiệm thu mỹ thuật.</p>");
        foreach (var group in shots.GroupBy(shot => (shot.Preset, shot.Source)))
        {
            body.Append($"<h2>{Encode(group.Key.Preset)} <span class=\"ok\">· {Encode(group.Key.Source)} · ")
                .Append(string.Join(" · ", group.Select(shot => $"{Encode(shot.Renderer)} {shot.ChangedPixels} px")))
                .Append("</span></h2><div class=\"pair\">");
            foreach (var shot in group)
                body.Append($"<figure><figcaption>{Encode(shot.Renderer)}</figcaption>")
                    .Append($"<img loading=\"lazy\" src=\"{Encode(OptionMatrixPlan.Slug(group.Key.Preset) + "/" + Path.GetFileName(shot.File))}\" ")
                    .Append($"alt=\"{Encode(group.Key.Preset)} {Encode(shot.Renderer)}\"></figure>");
            body.Append("</div>");
        }
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "preset-roundtrip", "index.html"), body.ToString());
    }
}
