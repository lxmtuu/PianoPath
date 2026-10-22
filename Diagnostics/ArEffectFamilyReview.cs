using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N17, first batch: the five hold families shot through the AR overlay, the way N16 shot the note materials.
///
/// <para>
/// Every hold family is <c>Native</c> on the WPF stage, the Direct3D stage and recording, and <c>Unverified</c> on the
/// overlay. The overlay is not unmeasured: <c>VerifyArNoteProjection</c> already flips the bar, breath, vibration and
/// colour-cycle switches and moves each of their four percentages between two ends, asserting more than twenty
/// changed pixels for each. What that check does not do — and what this page adds — is keep the pictures. It asserts
/// inside a forty-minute job and leaves nothing a person can look at, and it never measures the arc at all: the arc is
/// only counted, once at the overhead view where it must be zero and once at Ultra where it must not.
/// </para>
///
/// <para>
/// So this is a page, not a new claim. It holds the pose, the camera, the pressed keys and the seed, changes one
/// setting, counts the pixels that moved and commits the frames — the same thing N16 did for the note materials, for
/// the families whose evidence has lived in an assertion. It does not flip a binding: the contract keeps these
/// families <c>Unverified</c> because one value per setting at one pose is not the value and lifecycle coverage an
/// <c>Applied</c> claim asks for, and this page does not change that arithmetic.
/// </para>
///
/// <para>
/// The scene is one held pair of keys, C4 and E4, for the whole run: two live bars, both under the finger, and — being
/// two active pitches — exactly one arc between them. Each column turns <em>one</em> family on and leaves the other
/// four off, which is stricter than N16 had to be: a difference in a column cannot come from a family the column is
/// not about. The first row of a column is that family at the product's own values; the second flips its switch; the
/// third pushes its percentage to the top of the range the contract declares.
/// </para>
///
/// <para>
/// The claim is deliberately small. The contract still says <c>Unverified</c> on the overlay for these families, so
/// this page records what the pixels did and claims nothing; turning a measurement into a binding is a second,
/// separate commit that reads this manifest. Not art approval, not every value of the range, not a real camera, not
/// hardware frames, and no default change.
/// </para>
/// </summary>
internal static class ArEffectFamilyReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    /// <summary>The same floor the other matrices use: more than a stray pixel is "the setting changed the picture".</summary>
    private const int MinimumChangedPixels = 12;
    /// <summary>
    /// How much of the camera picture the overlay has to repaint for a capture to mean anything. An AR frame is
    /// composited over the camera, so "lit pixels" is the whole picture and proves nothing; the painting is what the
    /// quads add. A capture where the held bars went missing would move zero pixels for every setting, and this page
    /// would then record five families as ignored for the wrong reason.
    /// </summary>
    private const int MinimumOverlayPixels = 500;
    /// <summary>One column per row of the page: the family at the product's values, its switch flipped, its
    /// percentage at the top of its declared range.</summary>
    private const int Columns = 3;
    private const int TileWidth = Width, TileHeight = Height;
    /// <summary>The pose every capture uses: the reference pose the other AR reviews are calibrated against.</summary>
    private const string PoseId = "ar-reference";
    /// <summary>Two keys held for the whole run: two live bars, and one arc between the two active pitches.</summary>
    private static readonly int[] HeldKeys = [60, 64];
    private const int Frames = 12;
    private const double FrameSeconds = 1.0 / 30;

    private sealed class Shot
    {
        internal required string File { get; init; }
        internal required string PixelSha256 { get; init; }
        internal required int Lit { get; init; }
        /// <summary>Pixels the overlay repainted over the camera picture — the only measure that the quads drew.</summary>
        internal required int Painted { get; init; }
        /// <summary>The family's own quad counter after this capture, the witness that the scene carried it.</summary>
        internal required int Quads { get; init; }
        /// <summary>Held only while the pair is compared; dropped before the manifest is written.</summary>
        internal byte[]? Pixels { get; set; }
    }

    private sealed record Cell(string Family, string Parameter, string Row, double Value, string File,
        string PixelSha256, int ArChanged, int ArLit, int ArPainted, int Quads, string Reading, string Note);

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The AR effect review output directory is empty.", nameof(output));
        var pilot = EffectContractCatalog.HoldFxPilot;
        if (pilot.Count != 5)
            throw new InvalidDataException($"The AR effect review pins the five hold families, but the contract lists {pilot.Count}.");
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            var samples = PrismFlowVisualReview.FindSamples();
            var original = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(samples, "ar-keyboard-topdown.png"));
            var pose = HeldParticleControlPlan.Views.Single(view => view.Id == PoseId);
            var camera = HeldParticleControlPlan.CameraFor(pose, original);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The AR effect review requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var shapes = pilot.Select(contract => Shape(contract)).ToArray();
            var captions = pilot.SelectMany((contract, column) => new[]
                {
                    $"{contract.Id} on", $"{shapes[column].Toggle} off", $"{shapes[column].Amount} {shapes[column].AmountValue:0}"
                }).ToArray();
            var sheet = new ReviewSheet.Canvas(pilot.Count * Columns, TileWidth, TileHeight, Columns,
                "N17 AR HOLD FAMILIES", ReviewSheet.CaptionScaleFor(captions, TileWidth));

            var shots = new List<Shot>(pilot.Count * Columns);
            var cells = new List<Cell>(pilot.Count * Columns);
            for (var column = 0; column < pilot.Count; column++)
            {
                var contract = pilot[column];
                var shape = shapes[column];
                var atRest = Settings(contract, null, 0);
                var rest = CaptureAr(root, renderer, pose, camera, atRest, contract.Id, "rest");
                if (rest.Painted < MinimumOverlayPixels)
                    throw new InvalidDataException(
                        $"{contract.Id}: the held scene repaints {rest.Painted} pixels of the camera picture in AR; a " +
                        "picture this empty cannot show that a setting moved anything.");
                if (rest.Quads <= 0)
                    throw new InvalidDataException(
                        $"{contract.Id} drew no quads of its own in the held scene, so its three captures would all " +
                        "measure a family that never ran. The scene has to carry it before the page can judge it.");
                shots.Add(rest);
                // Five families across a three-column grid: the cell index is family × row + row, and the captions
                // were flattened in that same order, so a family reads along its own line.
                sheet.Blit(column * Columns, captions[column * Columns], rest.Pixels!, Width, Height, premultiplied: false);
                cells.Add(new Cell(contract.Id, contract.Id, "rest", 0, rest.File, rest.PixelSha256, 0, rest.Lit,
                    rest.Painted, rest.Quads, "Reference", "The family at the product's own values."));

                // The switch, flipped: the family stops drawing altogether.
                var off = CaptureAr(root, renderer, pose, camera, Settings(contract, shape.Toggle, shape.ToggleValue),
                    contract.Id, "toggle");
                if (off.Painted < MinimumOverlayPixels)
                    throw new InvalidDataException(
                        $"{contract.Id}: switching {shape.Toggle} off leaves the overlay repainting {off.Painted} pixels " +
                        "of the camera picture; the comparison would be between two nearly empty frames.");
                shots.Add(off);
                sheet.Blit(column * Columns + 1, captions[column * Columns + 1], off.Pixels!, Width, Height, premultiplied: false);
                cells.Add(new Cell(contract.Id, shape.Toggle, "toggle", shape.ToggleValue, off.File, off.PixelSha256,
                    OptionMatrixPlan.ChangedPixels(off.Pixels!, rest.Pixels!), off.Lit, off.Painted, off.Quads,
                    Read(OptionMatrixPlan.ChangedPixels(off.Pixels!, rest.Pixels!)),
                    "The switch turned off; every other frame of this column has the family on."));

                // The percentage at the top of its declared range: the largest difference the setting can make
                // without switching the family off, which the row above already covers.
                var top = CaptureAr(root, renderer, pose, camera, Settings(contract, shape.Amount, shape.AmountValue),
                    contract.Id, "amount");
                if (top.Painted < MinimumOverlayPixels)
                    throw new InvalidDataException(
                        $"{contract.Id}: {shape.Amount} = {shape.AmountValue} leaves the overlay repainting {top.Painted} " +
                        "pixels of the camera picture; the comparison would be between two nearly empty frames.");
                shots.Add(top);
                sheet.Blit(column * Columns + 2, captions[column * Columns + 2], top.Pixels!, Width, Height,
                    premultiplied: false);
                var changed = OptionMatrixPlan.ChangedPixels(top.Pixels!, rest.Pixels!);
                cells.Add(new Cell(contract.Id, shape.Amount, "amount", shape.AmountValue, top.File, top.PixelSha256,
                    changed, top.Lit, top.Painted, top.Quads, Read(changed),
                    "The range's top end, against the product's own value."));

                off.Pixels = null;
                top.Pixels = null;
                rest.Pixels = null;
            }

            var finished = sheet.Finish();
            var layout = sheet.Sheet;
            var sheetFile = "ar-effect/sheet.png";
            var sheetSha = ReviewPng.Write(Path.Combine(root, sheetFile), finished, layout.Width, layout.Height,
                $"N17 AR hold families: {pilot.Count * Columns} captures in {layout.Columns}×{layout.Rows} cells",
                PixelFormats.Pbgra32);
            VerifyShots(root, shots);
            WriteManifest(root, renderer.AdapterName, pilot, shapes, cells, sheetFile, sheetSha,
                layout.Columns, layout.Rows);
            WriteHtmlIndex(root, renderer.AdapterName, cells, sheetFile);
            return cells.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    /// <summary>
    /// The two settings a hold family declares: one switch and one amount. Read from the contract, never typed here,
    /// so a family whose shape changes fails at the top of the run instead of being shot at the wrong value.
    /// </summary>
    private static (string Toggle, double ToggleValue, string Amount, double AmountValue) Shape(EffectContractCatalog.EffectFamilyContract contract)
    {
        var toggle = contract.Parameters.SingleOrDefault(parameter => parameter.Kind == EffectParameterKind.Boolean)
            ?? throw new InvalidDataException($"{contract.Id} declares no boolean switch for the AR effect review to flip.");
        var amount = contract.Parameters.SingleOrDefault(parameter => parameter.Kind == EffectParameterKind.Percentage)
            ?? throw new InvalidDataException($"{contract.Id} declares no percentage for the AR effect review to push.");
        if (contract.Parameters.Count != 2)
            throw new InvalidDataException(
                $"{contract.Id} declares {contract.Parameters.Count} settings; the AR effect review drives one switch " +
                "and one percentage, so a third setting would fall off the page without anyone noticing.");
        // Every frame of a column has the family on, so the switch row is what turns it off. The product keeps all
        // five switches off, which is why reading the flip out of the settings class would be wrong here: it would
        // flip the family *on* and the row would be a second copy of the rest frame.
        var top = amount.Maximum ?? throw new InvalidDataException($"{contract.Id}: {amount.SettingName} has no maximum to shoot.");
        return (toggle.SettingName, 0d, amount.SettingName, top);
    }

    /// <summary>
    /// The scene, with one family on and the other four off. Everything else — no background, no stars, no beats, no
    /// ambient layers, no impact art — is the neutral look the option matrix starts from, so a difference between two
    /// captures of a column is the setting and nothing else.
    /// </summary>
    private static PianoVisualSettings Settings(EffectContractCatalog.EffectFamilyContract contract, string? parameter, double value)
    {
        var settings = OptionMatrixReview.BaseSettings();
        var shape = Shape(contract);
        // The family under test is on, at the product's own values: that is the frame its three rows compare with.
        Apply(settings, shape.Toggle, 1);
        if (parameter is not null) Apply(settings, parameter, value);
        settings.Clamp();
        // The rest frame and the amount row have to carry the family; only the switch row is allowed to take it away.
        if (parameter != shape.Toggle && Read(settings, shape.Toggle) < .5)
            throw new InvalidDataException($"The settings class turned {shape.Toggle} off; the scene would have no family in it.");
        if (parameter is not null && Math.Abs(Read(settings, parameter) - value) > 0.001)
            throw new InvalidDataException(
                $"The settings class turned {parameter} = {value} into {Read(settings, parameter)}; the capture would " +
                "not be the value its caption names.");
        return settings;
    }

    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        PianoVisualSettings settings, string family, string label)
    {
        var pixels = RenderAr(renderer, pose, camera, settings);
        if (pixels.Length != Width * Height * 4)
            throw new InvalidDataException(
                $"AR returned {pixels.Length} bytes for {family}/{label}; expected {Width}×{Height} BGRA pixels.");
        var quads = Probe(renderer, family);
        var relative = Path.Combine("ar-effect", OptionMatrixPlan.Slug(family), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{family}/{label} in AR");
        return new Shot
        {
            File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels),
            Painted = OptionMatrixPlan.ChangedPixels(pixels, camera), Quads = quads, Pixels = pixels
        };
    }

    /// <summary>One frame of the AR overlay: the calibrated pose, two held keys and the family's look.</summary>
    private static byte[] RenderAr(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        PianoVisualSettings settings)
    {
        // A stage that is never drawn: only its colour pipeline and keyboard fraction feed the look, exactly as the
        // flat matrices do, so both pages resolve the same palette from the same settings.
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
            HeldParticleControlPlan.Fov) ?? throw new InvalidDataException($"AR effect calibration: {problem}");
        var ring = new CameraFrameRing();
        camera.CopyTo(ring.BeginWrite(camera.Length, out var slot), 0);
        ring.Publish(slot, Width, Height, System.Diagnostics.Stopwatch.GetTimestamp());
        feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, KeyLight: .6f, Quality: ArQualityTier.Ultra,
            ProjectedNotes: true, ShowLiveTrails: true, ShowDamperStrip: false, ShowRisingMidiNotes: false);
        var pressed = new HashSet<int>(HeldKeys);
        var dt = FrameSeconds;
        void BeforeFrame(int frame)
        {
            // Playing stays false: there is no song in this scene, only the player's own two fingers. The pressed set
            // is what makes the pitches active (the arc links active pitches) and what lights the keys; the live events
            // are what creates the held bars, and they are sent once because they stay held for the whole run.
            feed.SetDeterministicState([], (frame + 1) * dt, false, pressed);
            feed.SetDeterministicBeatPulse(0);
            if (frame != 0) return;
            foreach (var pitch in HeldKeys) feed.LiveNote(pitch, true, .9);
        }
        return GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, Frames - 1, dt, false, out _,
            BeforeFrame, OptionMatrixPlan.Seed);
    }

    /// <summary>
    /// Which of the layer's own counters witnesses that this family drew something in the held scene. Four of the five
    /// ride on the held bar's own quad; the arc is a ribbon pass of its own, so it has a count to itself. A family the
    /// page does not know is a family nobody shot, so it throws instead of reporting zero.
    /// </summary>
    private static int Probe(GpuStageRenderer renderer, string family) => family switch
    {
        "hold.bar" => renderer.ArLiveBarCount,
        "hold.breath" => renderer.ArLiveBarCount,
        "hold.vibration" => renderer.ArLiveBarCount,
        "hold.color-cycle" => renderer.ArLiveBarCount,
        "hold.arc" => renderer.ArHoldArcCount,
        _ => throw new InvalidDataException(
            $"The AR effect review has no witness counter for '{family}'; teach Probe() about it before the page " +
            "claims to have shot it.")
    };

    /// <summary>What a number means on the page: above the floor the setting reaches the picture, below it does not.</summary>
    private static string Read(int changed) => changed >= MinimumChangedPixels ? "moves" : "still";

    private static void Apply(PianoVisualSettings settings, string parameter, double value)
    {
        switch (parameter)
        {
            case "HoldBar": settings.HoldBar = value >= 0.5; break;
            case "HoldBarIntensity": settings.HoldBarIntensity = value; break;
            case "HoldBreath": settings.HoldBreath = value >= 0.5; break;
            case "HoldBreathRate": settings.HoldBreathRate = value; break;
            case "HoldVibration": settings.HoldVibration = value >= 0.5; break;
            case "HoldVibrationAmount": settings.HoldVibrationAmount = value; break;
            case "HoldColorCycle": settings.HoldColorCycle = value >= 0.5; break;
            case "HoldColorCycleSpeed": settings.HoldColorCycleSpeed = value; break;
            case "HoldElectricArc": settings.HoldElectricArc = value >= 0.5; break;
            case "HoldArcIntensity": settings.HoldArcIntensity = value; break;
            default:
                throw new InvalidDataException(
                    $"The AR effect review cannot drive '{parameter}'; teach Apply() and Read() about it first.");
        }
    }

    private static double Read(PianoVisualSettings settings, string parameter) => parameter switch
    {
        "HoldBar" => settings.HoldBar ? 1 : 0,
        "HoldBarIntensity" => settings.HoldBarIntensity,
        "HoldBreath" => settings.HoldBreath ? 1 : 0,
        "HoldBreathRate" => settings.HoldBreathRate,
        "HoldVibration" => settings.HoldVibration ? 1 : 0,
        "HoldVibrationAmount" => settings.HoldVibrationAmount,
        "HoldColorCycle" => settings.HoldColorCycle ? 1 : 0,
        "HoldColorCycleSpeed" => settings.HoldColorCycleSpeed,
        "HoldElectricArc" => settings.HoldElectricArc ? 1 : 0,
        "HoldArcIntensity" => settings.HoldArcIntensity,
        _ => throw new InvalidDataException($"The AR effect review cannot read '{parameter}'.")
    };

    private static void VerifyShots(string root, IReadOnlyList<Shot> shots)
    {
        foreach (var shot in shots)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, shot.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != shot.PixelSha256)
                throw new InvalidDataException($"{shot.File} no longer decodes to the pixels the manifest names.");
        }
        if (shots.Select(shot => shot.File).Distinct(StringComparer.Ordinal).Count() != shots.Count)
            throw new InvalidDataException(
                $"The AR effect review wrote {shots.Count} captures into fewer files; two settings overwrote one picture.");
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<EffectContractCatalog.EffectFamilyContract> pilot,
        IReadOnlyList<(string Toggle, double ToggleValue, string Amount, double AmountValue)> shapes,
        IReadOnlyList<Cell> cells, string sheetFile, string sheetSha, int columns, int rows)
    {
        File.WriteAllText(Path.Combine(root, "ar-effect", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            status = "n17-ar-hold-families-measured-not-claimed",
            claim = "every setting of the five hold families is shot through the calibrated AR overlay with two keys held, one setting at a time against the same family at the product's own values",
            notClaimed = "art approval, every value of every range, a real camera, hand occlusion, hardware frames, other quality tiers, a binding change or a default change",
            renderer = "GPU-WARP",
            adapter,
            width = Width, height = Height, fps = (int)Math.Round(1 / FrameSeconds), frames = Frames,
            seed = OptionMatrixPlan.Seed,
            pose = PoseId,
            scene = "two keys held for the whole run (C4 and E4): two held bars under the fingers and one arc between the two active pitches; no song, no background, no other effect family",
            heldKeys = HeldKeys,
            families = pilot.Select(contract => contract.Id),
            parameters = pilot.SelectMany((contract, column) => new[]
            {
                new { family = contract.Id, setting = shapes[column].Toggle, value = shapes[column].ToggleValue },
                new { family = contract.Id, setting = shapes[column].Amount, value = shapes[column].AmountValue }
            }),
            floorChangedPixels = MinimumChangedPixels,
            floorOverlayPixels = MinimumOverlayPixels,
            sheet = new { File = sheetFile, PixelSha256 = sheetSha, Columns = columns, Rows = rows },
            cells = cells.Select(cell => new
            {
                cell.Family, cell.Parameter, cell.Row, Value = Math.Round(cell.Value, 3), cell.File,
                cell.PixelSha256, cell.ArChanged, cell.ArLit, cell.ArPainted, cell.Quads, cell.Reading, cell.Note
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<Cell> cells, string sheetFile)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N17 — AR hold families</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 6px}p{line-height:1.55;color:#a9b6c7}table{border-collapse:collapse;margin:12px 0}")
            .Append("th,td{border:1px solid #263243;padding:5px 9px;text-align:left;font-variant-numeric:tabular-nums}")
            .Append("th{color:#c9d6e6;font-weight:600}td.y{color:#7df0b1}td.n{color:#ffd479}")
            .Append("img{max-width:100%;height:auto;border:1px solid #263243;margin-top:8px}</style>")
            .Append("<h1>Hold families trong lớp AR (N17, đợt 1)</h1>")
            .Append($"<p>Cùng pose <code>{Encode(PoseId)}</code>, cùng camera, hai phím giữ nguyên C4 và E4, cùng seed; mỗi cột bật ")
            .Append("đúng một family và mỗi khung chỉ đổi một thiết lập so với khung \"on\" của chính cột đó. Số là pixel đổi ")
            .Append($"giữa hai khung. Máy đo: {Encode(adapter)}.</p>")
            .Append("<table><tr><th>Family</th><th>Thiết lập</th><th>Hàng</th><th>Giá trị</th><th>AR</th><th>Quad</th><th>Đọc</th></tr>");
        foreach (var cell in cells)
            body.Append($"<tr><td>{Encode(cell.Family)}</td><td>{Encode(cell.Parameter)}</td><td>{Encode(cell.Row)}</td>")
                .Append($"<td>{cell.Value:0.###}</td>")
                .Append($"<td class=\"{(cell.ArChanged >= MinimumChangedPixels ? "y" : "n")}\">{cell.ArChanged:N0} px</td>")
                .Append($"<td>{cell.Quads:N0}</td><td>{Encode(cell.Reading)}</td></tr>");
        body.Append("</table>");
        body.Append("<h2>Tờ ảnh</h2><p>Mỗi hàng một family; ba cột là: family đang bật ở giá trị của sản phẩm, công tắc bị lật, ")
            .Append("và phần trăm bị đẩy tới đầu trên của khoảng contract khai báo.</p>")
            .Append($"<img src=\"{Encode(Path.GetFileName(sheetFile))}\" alt=\"N17 AR hold families\">")
            .Append("</html>");
        File.WriteAllText(Path.Combine(root, "ar-effect", "index.html"), body.ToString());
    }
}
