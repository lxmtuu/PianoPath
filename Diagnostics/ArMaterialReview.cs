using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N16: the note-material settings, shot through the AR overlay on the three materials the typed contract pilots —
/// the matrix the contract itself asks for before any AR parameter binding may be called Applied or Ignored.
///
/// <para>
/// An AR note is a camera-facing quad shaded by the <em>same</em> <c>ShadeNoteMaterial</c> as the stage, so the
/// shader already says what should happen: tint, edge, edge width, refraction on Solid and Gem and the 3D switch
/// all reach the body, while Crystal Outline's branch never reads the refraction channel and no branch reads the
/// 3D switch for that material. That reading lived in <c>EffectContracts</c> as prose. What the contract could not
/// say is whether the quads really receive the uniforms, because no review ever compared two AR frames of the same
/// material. This one does exactly that: hold the pose, the camera, the scene and the seed, change one setting, and
/// count the pixels that moved.
/// </para>
///
/// <para>
/// Every pair is also drawn on the stage in the same two settings, so an AR number can be read next to the flat
/// one. That contrast is the point of the page: a setting that moves the stage picture and not the AR picture is a
/// finding about the overlay, not a rounding error, and the manifest says so instead of hiding it in one number.
/// The bindings stay separate on purpose — WPF and the GPU are allowed to differ (N8 is the record of exactly
/// that) — so this review never requires the two stage renderers to agree with each other.
/// </para>
///
/// <para>
/// The claim is narrow: renderer branching and uniform delivery, on WARP, at one calibrated pose, with one value
/// per setting. Not art approval, not every value of every range, not a real camera, not hardware frames, and no
/// default changes.
/// </para>
/// </summary>
internal static class ArMaterialReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    /// <summary>The same floor the other matrices use: more than a stray pixel is "the material changed".</summary>
    private const int MinimumChangedPixels = 12;
    /// <summary>
    /// How much of the camera picture the overlay has to repaint for a capture to mean anything. An AR frame is
    /// composited over the camera, so "lit pixels" is the whole picture and proves nothing; the painting is what
    /// the quad adds. If the quad went missing, every setting would move zero pixels and every Ignored expectation
    /// would pass for the wrong reason, so each capture is checked before its number is believed.
    /// </summary>
    private const int MinimumOverlayPixels = 500;
    /// <summary>One column per piloted material, one row per setting; the first row is each material at rest.</summary>
    private const int Columns = 3;
    /// <summary>A cell is the capture's own size: a downscaled tile averages away the twelve-pixel differences this
    /// page is made of, which is exactly what N8 had to undo when its first sheet could not show its own numbers.</summary>
    private const int TileWidth = Width, TileHeight = Height;
    /// <summary>The pose every capture uses: the reference pose the other AR reviews are calibrated against.</summary>
    private const string PoseId = "ar-reference";

    /// <summary>
    /// The settings this review can drive, in the order the page draws them. The list is checked against the
    /// contract's own parameters, and <see cref="Apply"/> throws on anything it does not know, so a parameter added
    /// to the pilot cannot quietly fall off the page.
    /// </summary>
    private static readonly string[] Known = ["NoteTint", "NoteGlow", "NoteEdge", "NoteEdgeWidth", "NoteRefraction", "Notes3D"];

    private sealed class Shot
    {
        internal required string File { get; init; }
        internal required string PixelSha256 { get; init; }
        internal required int Lit { get; init; }
        /// <summary>Pixels the overlay repainted over the camera picture — the only measure that the quad drew.</summary>
        internal required int Painted { get; init; }
        /// <summary>Held only while the pair is compared; dropped before the manifest is written.</summary>
        internal byte[]? Pixels { get; set; }
    }

    private sealed record Cell(string Material, string Parameter, bool Listed, double Value, string File,
        string PixelSha256, int ArChanged, int WpfChanged, int GpuChanged, int ArLit, int ArPainted,
        string Claim, string Note);

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The AR material review output directory is empty.", nameof(output));
        var pilot = EffectContractCatalog.NoteMaterialPilot;
        if (pilot.Count != 3)
            throw new InvalidDataException($"The AR material review pins the three piloted note materials, but the contract lists {pilot.Count}.");
        var parameters = ParameterOrder(pilot);
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
                throw new InvalidOperationException($"The AR material review requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var styles = pilot.Select(contract => contract.LegacyAliases[0]).ToArray();
            var rows = parameters.Length + 1;
            var captions = styles.SelectMany(style => new[] { $"{style} default" }
                    .Concat(parameters.Select(parameter => $"{style} {parameter} {Variants(pilot, style, parameter).Label}")))
                .ToArray();
            var sheet = new ReviewSheet.Canvas(styles.Length * rows, TileWidth, TileHeight, Columns,
                "N16 AR MATERIAL", ReviewSheet.CaptionScaleFor(captions, TileWidth));

            var arMeasurements = new List<(string Style, string Parameter, double Value, string File, string Sha, int Changed, int Lit, int Painted, PianoVisualSettings Settings, PianoVisualSettings Resting)>();
            var shots = new List<Shot>(styles.Length * rows);
            for (var column = 0; column < styles.Length; column++)
            {
                var style = styles[column];
                var atRest = Settings(style, null, 0);
                var rest = CaptureAr(root, renderer, pose, camera, atRest, style, "default");
                if (rest.Painted < MinimumOverlayPixels)
                    throw new InvalidDataException(
                        $"{style} at rest repaints {rest.Painted} pixels of the camera picture in AR; a picture this " +
                        "empty cannot show that a setting moved anything.");
                shots.Add(rest);
                sheet.Blit(column, $"{style} default", rest.Pixels!, Width, Height, premultiplied: false);

                for (var row = 0; row < parameters.Length; row++)
                {
                    var parameter = parameters[row];
                    var (value, label) = Variants(pilot, style, parameter);
                    var settings = Settings(style, parameter, value);
                    var variant = CaptureAr(root, renderer, pose, camera, settings, style, $"{parameter}-{label}");
                    if (variant.Painted < MinimumOverlayPixels)
                        throw new InvalidDataException(
                            $"{style}: {parameter} = {value} leaves the overlay repainting {variant.Painted} pixels of " +
                            "the camera picture; with the quad this faint its zero-pixel expectations would pass for " +
                            "the wrong reason.");
                    shots.Add(variant);
                    sheet.Blit((row + 1) * Columns + column, $"{style} {parameter} {label}", variant.Pixels!, Width, Height,
                        premultiplied: false);
                    arMeasurements.Add((style, parameter, value, variant.File, variant.PixelSha256,
                        OptionMatrixPlan.ChangedPixels(variant.Pixels!, rest.Pixels!), variant.Lit, variant.Painted,
                        settings, atRest));
                    variant.Pixels = null;
                }
                rest.Pixels = null;
            }

            // The stage takes its own turn once the overlay is done: the same two settings on the flat renderers,
            // so the page can put the AR number beside them without the two passes sharing a device mid-frame. Each
            // material's "at rest" stage frame is rendered once and reused by its six rows.
            var restWpf = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var restGpu = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var cells = new List<Cell>(arMeasurements.Count);
            foreach (var (style, parameter, value, file, sha, changed, lit, painted, settings, atRest) in arMeasurements)
            {
                if (!restWpf.ContainsKey(style))
                {
                    restWpf[style] = OptionMatrixReview.CaptureWpf(OptionMatrixPlan.Scene.Falling, atRest);
                    restGpu[style] = OptionMatrixReview.CaptureGpu(renderer, OptionMatrixPlan.Scene.Falling, atRest).Pixels;
                }
                var wpf = OptionMatrixPlan.ChangedPixels(
                    OptionMatrixReview.CaptureWpf(OptionMatrixPlan.Scene.Falling, settings), restWpf[style]);
                var gpu = OptionMatrixPlan.ChangedPixels(
                    OptionMatrixReview.CaptureGpu(renderer, OptionMatrixPlan.Scene.Falling, settings).Pixels, restGpu[style]);
                var contract = pilot.Single(candidate => candidate.LegacyAliases[0] == style);
                var (claim, note, listed) = Judge(contract, parameter, changed, wpf, gpu);
                cells.Add(new Cell(style, parameter, listed, value, file, sha, changed, wpf, gpu, lit, painted, claim, note));
            }

            var finished = sheet.Finish();
            var layout = sheet.Sheet;
            var sheetFile = "ar-material/sheet.png";
            var sheetSha = ReviewPng.Write(Path.Combine(root, sheetFile), finished, layout.Width, layout.Height,
                $"N16 AR material: {styles.Length * rows} captures in {layout.Columns}×{layout.Rows} cells",
                PixelFormats.Pbgra32);
            VerifyShots(root, shots);
            WriteManifest(root, renderer.AdapterName, styles, parameters, cells, sheetFile, sheetSha,
                layout.Columns, layout.Rows);
            WriteHtmlIndex(root, renderer.AdapterName, cells, sheetFile);
            return cells.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        PianoVisualSettings settings, string style, string label)
    {
        var pixels = RenderAr(renderer, pose, camera, settings);
        if (pixels.Length != Width * Height * 4)
            throw new InvalidDataException(
                $"AR returned {pixels.Length} bytes for {style}/{label}; expected {Width}×{Height} BGRA pixels.");
        var relative = Path.Combine("ar-material", OptionMatrixPlan.Slug(style), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{style}/{label} in AR");
        return new Shot
        {
            File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels),
            Painted = OptionMatrixPlan.ChangedPixels(pixels, camera), Pixels = pixels
        };
    }

    /// <summary>One frame of the AR overlay: the calibrated pose, the shipped note scene and the material's look.</summary>
    private static byte[] RenderAr(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        PianoVisualSettings settings)
    {
        // A stage that is never drawn: only its colour pipeline and keyboard fraction feed the look, exactly as
        // the flat matrix does, so the reviews resolve the same palette from the same settings.
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
            HeldParticleControlPlan.Fov) ?? throw new InvalidDataException($"AR material calibration: {problem}");
        var ring = new CameraFrameRing();
        camera.CopyTo(ring.BeginWrite(camera.Length, out var slot), 0);
        ring.Publish(slot, Width, Height, System.Diagnostics.Stopwatch.GetTimestamp());
        feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, KeyLight: .6f, Quality: ArQualityTier.Ultra,
            ProjectedNotes: true, ShowDamperStrip: false);
        var notes = OptionMatrixPlan.Notes(OptionMatrixPlan.Scene.Falling);
        var playing = OptionMatrixPlan.Playing(OptionMatrixPlan.Scene.Falling);
        var frames = OptionMatrixPlan.Frames(OptionMatrixPlan.Scene.Falling);
        var dt = 1.0 / OptionMatrixPlan.Fps;
        void BeforeFrame(int frame)
        {
            feed.SetDeterministicState(notes, (frame + 1) * dt, playing, NoPressed);
            feed.SetDeterministicBeatPulse(0);
        }
        return GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _,
            BeforeFrame, OptionMatrixPlan.Seed);
    }

    /// <summary>
    /// The settings the page draws: the pinned scene with one material, and either the product's own note palette
    /// (the "at rest" frame) or one setting moved to the end of its range that is furthest from where the product
    /// keeps it. Everything else stays put, so two captures of a row differ in one setting only.
    /// </summary>
    private static PianoVisualSettings Settings(string style, string? parameter, double value)
    {
        var settings = OptionMatrixReview.BaseSettings();
        settings.NoteStyle = style;
        if (parameter is not null) Apply(settings, parameter, value);
        settings.Clamp();
        if (settings.NoteStyle != style)
            throw new InvalidDataException($"The settings class turned '{style}' into '{settings.NoteStyle}'.");
        if (parameter is not null && Math.Abs(Read(settings, parameter) - value) > 0.001)
            throw new InvalidDataException(
                $"The settings class turned {parameter} = {value} into {Read(settings, parameter)}; the capture would " +
                "not be the value its caption names.");
        return settings;
    }

    private static void Apply(PianoVisualSettings settings, string parameter, double value)
    {
        switch (parameter)
        {
            case "NoteTint": settings.NoteTint = value; break;
            case "NoteGlow": settings.NoteGlow = value; break;
            case "NoteEdge": settings.NoteEdge = value; break;
            case "NoteEdgeWidth": settings.NoteEdgeWidth = value; break;
            case "NoteRefraction": settings.NoteRefraction = value; break;
            case "Notes3D": settings.Notes3D = value >= 0.5; break;
            default:
                throw new InvalidDataException(
                    $"The AR material review cannot drive '{parameter}'; teach Apply() and Known about it first.");
        }
    }

    private static double Read(PianoVisualSettings settings, string parameter) => parameter switch
    {
        "NoteTint" => settings.NoteTint,
        "NoteGlow" => settings.NoteGlow,
        "NoteEdge" => settings.NoteEdge,
        "NoteEdgeWidth" => settings.NoteEdgeWidth,
        "NoteRefraction" => settings.NoteRefraction,
        "Notes3D" => settings.Notes3D ? 1 : 0,
        _ => throw new InvalidDataException($"The AR material review cannot read '{parameter}'.")
    };

    /// <summary>The one value this page shoots for a setting, as a number and as the caption spells it.</summary>
    private static (double Value, string Label) Variants(IReadOnlyList<EffectContract> pilot, string style, string parameter)
    {
        var contract = pilot.Single(candidate => candidate.LegacyAliases[0] == style);
        // Crystal Outline does not list Notes3D: the setting still exists in the product, and shooting it is how
        // the page shows that the material ignores it rather than merely omitting the row.
        var declared = contract.Parameters.FirstOrDefault(candidate => candidate.SettingName == parameter)
            ?? pilot.SelectMany(candidate => candidate.Parameters).First(candidate => candidate.SettingName == parameter);
        var current = Read(OptionMatrixReview.BaseSettings(), parameter);
        if (declared.Kind == EffectParameterKind.Boolean)
        {
            var flipped = current >= 0.5 ? 0d : 1d;
            return (flipped, ((int)flipped).ToString());
        }
        var minimum = declared.Minimum ?? throw new InvalidDataException($"{parameter} has no minimum to shoot.");
        var maximum = declared.Maximum ?? throw new InvalidDataException($"{parameter} has no maximum to shoot.");
        var value = Math.Abs(maximum - current) >= Math.Abs(current - minimum) ? maximum : minimum;
        return (value, ((int)value).ToString());
    }

    /// <summary>The settings the page draws, in contract order, refused if the review cannot drive one of them.</summary>
    private static string[] ParameterOrder(IReadOnlyList<EffectContract> pilot)
    {
        var names = pilot.SelectMany(contract => contract.Parameters.Select(parameter => parameter.SettingName))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length != Known.Length || names.Any(name => !Known.Contains(name)) || Known.Any(name => !names.Contains(name)))
            throw new InvalidDataException(
                $"The contract now lists the note-material parameters [{string.Join(", ", names)}] but this review drives " +
                $"[{string.Join(", ", Known)}]; a parameter the page cannot shoot must not be claimed.");
        return names;
    }

    /// <summary>
    /// What the pixels say against what the contract says. The expectation is read out of the contract, never out of
    /// a material's name: a parameter the contract does not list for a material, or one it marks Ignored on the GPU,
    /// is drawn by the same shader the AR quad uses, so its AR frame has to stay under the floor against the material
    /// at rest. Everything else has to move. The glow is measured without a claim: its halo lives outside the note's
    /// own rectangle, so on a quad cut to the body it can only reach the rounded corners, and a small number there
    /// says nothing about what a reviewer sees. The stage columns are recorded beside the AR one and never compared
    /// with it as a requirement — the two renderers are allowed to differ, which is N8's subject, not this one's.
    /// </summary>
    private static (string Claim, string Note, bool Listed) Judge(EffectContract contract, string parameter, int ar, int wpf, int gpu)
    {
        var style = contract.LegacyAliases[0];
        var declared = contract.Parameters.FirstOrDefault(candidate => candidate.SettingName == parameter);
        var listed = declared is not null;
        var ignoredByMaterial = !listed || declared!.Bindings.GpuStage == EffectParameterBindingState.Ignored;
        var expected = parameter == "NoteGlow" ? "Recorded" : ignoredByMaterial ? "Ignored" : "Applied";
        var note = new StringBuilder();
        if (!listed)
            note.Append("The contract does not list this parameter for this material; the frame shows what it does anyway. ");
        switch (expected)
        {
            // The floor, not an exact zero: an AR frame is a composite over a camera picture, and the point is that
            // the setting does not reach the material, not that two renders of the same scene agree byte for byte.
            case "Ignored" when ar >= MinimumChangedPixels:
                throw new InvalidDataException(
                    $"{style}: {parameter} moved {ar} pixels in AR (floor {MinimumChangedPixels}), but the contract " +
                    "says the material's shader branch never reads it — a second pass started consuming the setting, " +
                    "or the AR quad stopped using the material's own branch.");
            case "Applied" when ar < MinimumChangedPixels:
                throw new InvalidDataException(
                    $"{style}: {parameter} moved {ar} pixels in AR (floor {MinimumChangedPixels}) although the contract " +
                    $"applies it on the GPU; the overlay would be dropping a setting the stage applies ({wpf} px on WPF, " +
                    $"{gpu} px on GPU-WARP).");
        }
        if (expected == "Recorded")
            note.Append("Measured only: the glow's halo lies outside the note's own rectangle, so this review does not claim the binding. ");
        if (wpf >= MinimumChangedPixels && gpu == 0)
            note.Append("The stage's WPF path moves and its GPU path does not; AR follows the GPU branch. ");
        return (expected, note.ToString().Trim(), listed);
    }

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
                $"The AR material review wrote {shots.Count} captures into fewer files; two settings overwrote one picture.");
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<string> styles,
        IReadOnlyList<string> parameters, IReadOnlyList<Cell> cells, string sheetFile, string sheetSha, int columns, int rows)
    {
        File.WriteAllText(Path.Combine(root, "ar-material", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            status = "n16-ar-material-not-art-signoff",
            claim = "every note-material setting is shot through the calibrated AR overlay on the three piloted materials, one setting at a time against each material at rest, and drawn in the same two settings on the WPF stage and the Direct3D stage for comparison",
            notClaimed = "art approval, every value of every range, a real camera, hand occlusion, hardware frames, other quality tiers or a default change",
            renderer = "GPU-WARP for AR and for the Direct3D stage; WPF for the software stage",
            sizes = "the AR column is the 1280x720 camera fixture the pose is calibrated against; the WPF and GPU columns are the 960x540 stage frames the other matrices shoot",
            adapter,
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            stageWidth = OptionMatrixPlan.Width, stageHeight = OptionMatrixPlan.Height,
            pose = PoseId,
            scene = "one held song note falling for 1.5 s, the scene N12(d) shoots the note material in",
            styles,
            parameters,
            floorChangedPixels = MinimumChangedPixels,
            sheet = new { File = sheetFile, PixelSha256 = sheetSha, Columns = columns, Rows = rows },
            cells = cells.Select(cell => new
            {
                cell.Material, cell.Parameter, cell.Listed, Value = Math.Round(cell.Value, 3), cell.File,
                cell.PixelSha256, cell.ArChanged, cell.WpfChanged, cell.GpuChanged, cell.ArLit, cell.ArPainted,
                cell.Claim, cell.Note
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<Cell> cells, string sheetFile)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N16 — AR note material</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 6px}p{line-height:1.55;color:#a9b6c7}table{border-collapse:collapse;margin:12px 0}")
            .Append("th,td{border:1px solid #263243;padding:5px 9px;text-align:left;font-variant-numeric:tabular-nums}")
            .Append("th{color:#c9d6e6;font-weight:600}td.y{color:#7df0b1}td.n{color:#ffd479}")
            .Append("img{max-width:100%;height:auto;border:1px solid #263243;margin-top:8px}</style>")
            .Append("<h1>Chất liệu nốt trong lớp AR (N16)</h1>")
            .Append($"<p>Cùng pose <code>{Encode(PoseId)}</code>, cùng camera, cùng cảnh nốt rơi, cùng seed; mỗi khung chỉ đổi ")
            .Append("một thiết lập so với khung \"default\" của chính chất liệu đó. Số là pixel đổi giữa hai khung, đo trên ")
            .Append($"ảnh AR và trên hai renderer sân khấu. Máy đo: {Encode(adapter)}.</p>")
            .Append("<table><tr><th>Chất liệu</th><th>Thiết lập</th><th>Giá trị</th><th>AR</th><th>WPF</th><th>GPU</th><th>Kết luận</th></tr>");
        foreach (var cell in cells)
            body.Append($"<tr><td>{Encode(cell.Material)}</td><td>{Encode(cell.Parameter)}</td><td>{cell.Value:0}</td>")
                .Append($"<td class=\"{(cell.ArChanged >= MinimumChangedPixels ? "y" : "n")}\">{cell.ArChanged:N0} px</td>")
                .Append($"<td>{cell.WpfChanged:N0} px</td><td>{cell.GpuChanged:N0} px</td>")
                .Append($"<td>{Encode(cell.Claim)}{(cell.Listed ? "" : " (không khai báo)")}</td></tr>");
        body.Append("</table>");
        body.Append("<h2>Tờ ảnh</h2><p>Mỗi cột một chất liệu; hàng đầu là chất liệu ở giá trị mặc định, sáu hàng sau là từng ")
            .Append("thiết lập bị đẩy tới đầu mút xa mặc định nhất.</p>")
            .Append($"<img src=\"{Encode(Path.GetFileName(sheetFile))}\" alt=\"N16 AR material\">")
            .Append("</html>");
        File.WriteAllText(Path.Combine(root, "ar-material", "index.html"), body.ToString());
    }
}
