using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N12(e): the numeric sliders, shot at three values each on the software stage and on the engine.
///
/// <para>
/// The flat matrix answers "does this listed value draw?". A slider cannot be answered that way, and it hides
/// a different failure: a clamp inside one drawing path makes two different slider values render the same
/// picture, while each of them still differs from "off" — so a review built only on "differs from the baseline"
/// would call it green. The software stage had exactly that: its held bar floored <c>Note width</c> at 60, so a
/// 30% preset drew a 60% bar on WPF and a 30% bar on the engine. This review therefore holds one rule:
/// <b>every pair of values on one axis must move pixels on both renderers.</b>
/// </para>
///
/// <para>
/// Each capture also reads its own setting back after <c>Clamp()</c> and fails if the product silently moved
/// it, so a plan value outside the slider range becomes a red run instead of a manifest that lies. The scene,
/// the input and every other switch come from the discrete family the axis names; nothing here is a second
/// copy of the render loop — <see cref="OptionMatrixReview.CaptureWpf"/> and
/// <see cref="OptionMatrixReview.CaptureGpu"/> are the same functions the flat matrix uses.
/// </para>
///
/// <para>
/// The output is renderer-branching evidence, not art approval, not a hardware or performance claim, and not an
/// AR projectile: the AR half is N12(d).
/// </para>
/// </summary>
internal static class OptionNumericReview
{
    private const int Width = OptionMatrixPlan.Width, Height = OptionMatrixPlan.Height;
    /// <summary>The flat matrix's floor. Smaller than the smallest real difference this review asks about.</summary>
    private const int MinimumChangedPixels = 12;
    private static readonly string[] RendererIds = ["WPF", "GPU-WARP"];
    /// <summary>The whole review on one page: six pictures to a row, a quarter of the capture size each.</summary>
    private const int SheetTileWidth = Width / 4, SheetTileHeight = Height / 4, SheetColumns = 6;

    /// <summary>Every caption the page writes: one axis, one value and one renderer per picture.</summary>
    private static IEnumerable<string> Captions() =>
        OptionNumericPlan.Axes.SelectMany(axis => axis.Values.SelectMany(value => RendererIds
            .Select(renderer => $"{axis.Id} {OptionNumericPlan.Label(value)} {renderer}")));

    private sealed record Shot(string Axis, string Setting, double Value, string Renderer, string File,
        string PixelSha256, double SampleSeconds, int Frames, int Sprites, int Notes)
    {
        /// <summary>Held only while the axis is compared; dropped before the manifest is written.</summary>
        internal byte[]? Pixels { get; set; }
    }

    /// <summary>The contact sheet, as the manifest names it. One page carries all nine axes and both renderers.</summary>
    private sealed record Sheet(string File, int Width, int Height, int Columns, int Rows, int Tiles,
        string PixelSha256);

    /// <summary>
    /// What the run measured: the capture count and, for every axis and renderer, the pair of values that
    /// differed least. The suite prints these so a log answers "how close was the tightest pair?" without the
    /// artifact, which is what a reviewer asks when a gate is set at a floor.
    /// </summary>
    internal sealed record Result(int Captures, IReadOnlyList<(string Axis, string Renderer, string Left, string Right, int Changed)> Pairs)
    {
        /// <summary>The tightest pair per axis and renderer, in axis order.</summary>
        internal IReadOnlyList<(string Axis, string Renderer, string Left, string Right, int Changed)> Tightest =>
            Pairs.GroupBy(pair => (pair.Axis, pair.Renderer))
                .Select(group => group.OrderBy(pair => pair.Changed).First())
                .ToArray();
    }

    internal static Result Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The numeric option output directory is empty.", nameof(output));
        if (OptionNumericPlan.Axes.Count == 0) throw new InvalidDataException("The numeric plan has no axes; the review would never run.");
        var expected = OptionNumericPlan.CaptureCount;
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The numeric option review requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var shots = new List<Shot>(expected);
            var pairs = new List<(string Axis, string Renderer, string Left, string Right, int Changed)>();
            // One page for the whole review: a reviewer sees every axis against every other at once, and the page
            // is small enough to commit, which the 54 native captures are not.
            var sheet = new ReviewSheet.Canvas(expected, SheetTileWidth, SheetTileHeight, SheetColumns,
                "N12(E) NUMERIC AXES", ReviewSheet.CaptionScaleFor(Captions(), SheetTileWidth));
            var cell = 0;
            foreach (var axis in OptionNumericPlan.Axes)
            {
                var family = axis.Arrange;
                var values = axis.Values.Distinct().Order().ToArray();
                if (values.Length != axis.Values.Count || axis.Values.Count < 2)
                    throw new InvalidDataException($"Numeric axis '{axis.Id}' must name at least two distinct values, in ascending order.");
                var perValue = new List<Shot[]>(values.Length);
                foreach (var value in values)
                {
                    var settings = OptionMatrixReview.Settings(family, axis.FamilyValue);
                    Apply(settings, axis.Setting, value);
                    settings.Clamp();
                    var drawn = Read(settings, axis.Setting);
                    if (Math.Abs(drawn - value) > 1e-9)
                        throw new InvalidDataException(
                            $"{axis.Id}: the plan asks for {value.ToString(CultureInfo.InvariantCulture)}, but the product " +
                            $"clamps {axis.Setting} to {drawn.ToString(CultureInfo.InvariantCulture)}. The plan must stay inside " +
                            "the slider's range, or the capture would be filed under a value it never drew.");
                    var wpf = OptionMatrixReview.CaptureWpf(axis.Scene, settings);
                    var (gpu, counters) = OptionMatrixReview.CaptureGpu(renderer, axis.Scene, settings);
                    var pair = new[]
                    {
                        Store(root, axis, value, "WPF", wpf, counters.Sprites, counters.Notes, premultiplied: true),
                        Store(root, axis, value, "GPU-WARP", gpu, counters.Sprites, counters.Notes, premultiplied: false)
                    };
                    foreach (var shot in pair)
                        sheet.Blit(cell++, $"{axis.Id} {OptionNumericPlan.Label(shot.Value)} {shot.Renderer}",
                            shot.Pixels!, Width, Height, shot.Renderer == "WPF");
                    perValue.Add(pair);
                    shots.AddRange(pair);
                }

                foreach (var rendererId in RendererIds)
                {
                    var column = perValue.Select(pair => pair.Single(shot => shot.Renderer == rendererId)).ToArray();
                    for (var left = 0; left < column.Length; left++)
                    for (var right = left + 1; right < column.Length; right++)
                    {
                        var changed = OptionMatrixPlan.ChangedPixels(column[left].Pixels!, column[right].Pixels!);
                        pairs.Add((axis.Id, rendererId, OptionNumericPlan.Label(column[left].Value),
                            OptionNumericPlan.Label(column[right].Value), changed));
                        if (changed < MinimumChangedPixels)
                        {
                            var a = column[left].Value.ToString(CultureInfo.InvariantCulture);
                            var b = column[right].Value.ToString(CultureInfo.InvariantCulture);
                            throw new InvalidDataException(
                                $"{axis.Id}: {axis.Setting} {a} and {b} moved {changed} pixels on {rendererId} " +
                                $"(floor {MinimumChangedPixels}). Two different slider values on one path are one picture: " +
                                "something is clamping, rounding or ignoring the value.");
                        }
                    }
                }
                // The frames of this axis are no longer needed once every pair has been compared.
                foreach (var shot in shots) shot.Pixels = null;
            }

            if (shots.Count != expected)
                throw new InvalidDataException($"The numeric review wrote {shots.Count} captures; exactly {expected} are required.");
            var written = WriteSheet(root, sheet);
            WriteManifest(root, renderer.AdapterName, shots, pairs, written);
            WriteHtmlIndex(root, shots, pairs, written);
            VerifyWrittenArtifacts(root, shots, written);
            return new Result(shots.Count, pairs);
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static Shot Store(string root, OptionNumericPlan.Axis axis, double value, string rendererId,
        byte[] pixels, int sprites, int notes, bool premultiplied)
    {
        var label = OptionNumericPlan.Label(value);
        var relative = Path.Combine("option-numeric", axis.Id, rendererId == "WPF" ? "wpf" : "gpu-warp", $"{label}.png");
        // The software stage hands out premultiplied BGRA and the Direct3D readback straight BGRA; writing both
        // through one format would darken every WPF capture that carries alpha.
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height,
            $"{axis.Id}/{label} on {rendererId}", premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32);
        return new Shot(axis.Id, axis.Setting, value, rendererId, relative.Replace('\\', '/'), sha,
            axis.Seconds, axis.Frames, sprites, notes) { Pixels = pixels };
    }

    /// <summary>
    /// Writes the one contact sheet of this review: every axis, both renderers, in plan order.
    /// </summary>
    private static Sheet WriteSheet(string root, ReviewSheet.Canvas canvas)
    {
        var layout = canvas.Sheet;
        const string file = "option-numeric/sheet.png";
        var sha = ReviewPng.Write(Path.Combine(root, file), canvas.Finish(), layout.Width, layout.Height,
            $"N12(e): {canvas.Count} numeric captures in {layout.Columns}×{layout.Rows} cells", PixelFormats.Pbgra32);
        return new Sheet(file, layout.Width, layout.Height, layout.Columns, layout.Rows, canvas.Count, sha);
    }

    private static void VerifyWrittenArtifacts(string root, IReadOnlyList<Shot> shots, Sheet sheet)
    {
        foreach (var shot in shots)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, shot.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != shot.PixelSha256)
                throw new InvalidDataException($"{shot.File} no longer decodes to the pixels the manifest names.");
        }
        var page = ReviewPng.Decode(Path.Combine(root, sheet.File), sheet.Width, sheet.Height);
        if (Convert.ToHexString(SHA256.HashData(page)) != sheet.PixelSha256)
            throw new InvalidDataException($"{sheet.File} no longer decodes to the pixels the manifest names.");
        if (sheet.Tiles != shots.Count)
            throw new InvalidDataException(
                $"{sheet.File} carries {sheet.Tiles} pictures but the review wrote {shots.Count} captures; the " +
                "sheet is the copy that outlives the artifact, so it has to carry all of them.");
    }

    /// <summary>Sets one numeric property. The settings come from the discrete family, so everything else is fixed.</summary>
    private static void Apply(PianoVisualSettings settings, string setting, double value)
    {
        switch (setting)
        {
            case "NoteWidth": settings.NoteWidth = value; break;
            case "NoteFallSpeed": settings.NoteFallSpeed = value; break;
            case "FallingTrailIntensity": settings.FallingTrailIntensity = value; break;
            case "ImpactWaveIntensity": settings.ImpactWaveIntensity = value; break;
            case "RingSize": settings.RingSize = value; break;
            case "ParticleAmount": settings.ParticleAmount = value; break;
            case "ImpactFlashIntensity": settings.ImpactFlashIntensity = value; break;
            case "ReleaseIntensity": settings.ReleaseIntensity = value; break;
            default:
                throw new InvalidDataException($"Numeric axis names an unknown setting '{setting}'.");
        }
    }

    /// <summary>Reads the property back after the clamp, to prove the capture drew the value it is filed under.</summary>
    private static double Read(PianoVisualSettings settings, string setting) => setting switch
    {
        "NoteWidth" => settings.NoteWidth,
        "NoteFallSpeed" => settings.NoteFallSpeed,
        "FallingTrailIntensity" => settings.FallingTrailIntensity,
        "ImpactWaveIntensity" => settings.ImpactWaveIntensity,
        "RingSize" => settings.RingSize,
        "ParticleAmount" => settings.ParticleAmount,
        "ImpactFlashIntensity" => settings.ImpactFlashIntensity,
        "ReleaseIntensity" => settings.ReleaseIntensity,
        _ => throw new InvalidDataException($"Numeric axis names an unknown setting '{setting}'.")
    };

    private static void WriteManifest(string root, string adapter, IReadOnlyList<Shot> shots,
        List<(string Axis, string Renderer, string Left, string Right, int Changed)> pairs, Sheet sheet)
    {
        File.WriteAllText(Path.Combine(root, "option-numeric", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            status = "n12e-numeric-options-not-art-signoff",
            claim = "every pair of values on each numeric axis moves pixels on both the WPF stage and the Direct3D stage on WARP at one fixed scene: no hidden floor, clamp or rounding collapses two slider positions onto one picture",
            notClaimed = "art approval, linearity/monotonicity of the response, AR projection (N12(d)), hardware frames, other quality tiers or a default change",
            readBack = "each capture reads its setting back after Clamp() and fails when the product moved it, so a capture is always filed under the value it drew",
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            pitch = OptionMatrixPlan.Pitch, strength = Math.Round(OptionMatrixPlan.Strength, 6),
            adapter, minimumChangedPixels = MinimumChangedPixels,
            input = "one held song note (falling), one live press held for the whole capture (held), one live hit (impact) or one press/release (release); no OS MIDI callback",
            axes = OptionNumericPlan.Axes.Select(axis => new
            {
                axis.Id, axis.Setting, axis.Family, axis.FamilyValue, Scene = axis.Scene.ToString(),
                axis.Minimum, axis.Maximum, axis.Values, axis.Frames,
                SampleSeconds = Math.Round(axis.Seconds, 4),
                arrangedValues = OptionMatrixPlan.CapturedValues(OptionMatrixPlan.FamilyById(axis.Family))
            }),
            pairs = pairs.Select(pair => new { pair.Axis, pair.Renderer, pair.Left, pair.Right, pair.Changed }),
            // The one file of this review that is committed: all nine axes, both renderers, captioned.
            sheet = new { sheet.File, sheet.Width, sheet.Height, sheet.Columns, sheet.Rows, sheet.Tiles, sheet.PixelSha256 },
            captureCount = shots.Count,
            captures = shots.Select(shot => new
            {
                shot.Axis, shot.Setting, shot.Value, shot.Renderer, shot.File, shot.PixelSha256,
                shot.SampleSeconds, shot.Frames, shot.Sprites, shot.Notes
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, IReadOnlyList<Shot> shots,
        List<(string Axis, string Renderer, string Left, string Right, int Changed)> pairs, Sheet sheet)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new System.Text.StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N12(e) — tuỳ chọn số</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1500px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 6px}p{line-height:1.55;color:#a9b6c7}table{border-collapse:collapse;margin:10px 0}")
            .Append("td,th{border:1px solid #263243;padding:2px 8px;font-variant-numeric:tabular-nums}")
            .Append("figure{margin:0}figcaption{padding:4px 0;color:#8fa3ba}img{width:100%;height:auto;border:1px solid #263243}")
            .Append(".row{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.pair{color:#7fd2a0}</style>")
            .Append("<h1>Tuỳ chọn số (N12(e)) — hai mức khác nhau không được ra cùng một khung hình</h1>")
            .Append("<p>Chín trục số, ba giá trị mỗi trục, mỗi giá trị chụp trên WPF và D3D/WARP ở cùng cảnh, cùng input, ")
            .Append("cùng seed. Ảnh native 960×540, không cắt, không ghép. Mỗi cặp trong một trục phải đổi ≥")
            .Append(MinimumChangedPixels).Append(" px trên <b>cả hai</b> renderer. Đây là bằng chứng renderer đọc đúng ")
            .Append("giá trị slider, không phải nghiệm thu mỹ thuật hay tuyến tính của phản ứng.</p>")
            .Append($"<p>Tờ ảnh tiếp xúc của cả chín trục: <a href=\"{Encode(sheet.File)}\">{sheet.Tiles} khung</a> trong lưới ")
            .Append($"{sheet.Columns}×{sheet.Rows}, {sheet.Width}×{sheet.Height} px — mỗi hàng một trục, cặp WPF ↔ GPU cạnh nhau. ")
            .Append("Đây là bản được commit vào repository.</p>")
            .Append($"<figure><img src=\"{Encode(sheet.File)}\" alt=\"tờ ảnh tiếp xúc N12(e)\"></figure>");
        foreach (var axis in OptionNumericPlan.Axes)
        {
            var column = shots.Where(shot => shot.Axis == axis.Id).ToArray();
            body.Append($"<h2>{Encode(axis.Id)} — {Encode(axis.Setting)} ({axis.Minimum:0.##}…{axis.Maximum:0.##})</h2>")
                .Append($"<p>Cảnh {Encode(axis.Scene.ToString())}, {axis.Frames} khung ({(axis.Seconds * 1000).ToString("0", CultureInfo.InvariantCulture)} ms). ")
                .Append("Cặp nhỏ nhất mỗi renderer: ");
            foreach (var rendererId in RendererIds)
            {
                var floor = pairs.Where(pair => pair.Axis == axis.Id && pair.Renderer == rendererId)
                    .OrderBy(pair => pair.Changed).First();
                body.Append($"<span class=\"pair\">{Encode(rendererId)} {Encode(floor.Left)}↔{Encode(floor.Right)} = {floor.Changed:N0} px</span>; ");
            }
            body.Append("</p><div class=\"row\">");
            foreach (var rendererId in RendererIds)
            foreach (var shot in column.Where(shot => shot.Renderer == rendererId))
                body.Append($"<figure><figcaption>{Encode(rendererId)} · {Encode(OptionNumericPlan.Label(shot.Value))} · sprites {shot.Sprites}, notes {shot.Notes}</figcaption>")
                    .Append($"<img loading=\"lazy\" src=\"{Encode(shot.File)}\" alt=\"{Encode(axis.Id)} {Encode(OptionNumericPlan.Label(shot.Value))} {Encode(rendererId)}\"></figure>");
            body.Append("</div>");
        }
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "option-numeric", "index.html"), body.ToString());
    }
}
