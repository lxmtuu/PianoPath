using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N8: the refraction slider on the six note materials, shot on both renderers at three levels — the acceptance
/// pictures the parity note asked for, and the pin that keeps the deliberate difference deliberate.
///
/// <para>
/// <c>NoteRefraction</c> is not one behaviour with two spellings. The software stage paints a one-pixel fringe
/// down the left edge of <em>every</em> non-satin material (<c>PianoStage.cs</c>, the <c>style switch</c> that
/// weights Neon/Gem/Crystal lower and everything else at 170) and multiplies the satin sheen by it. The Direct3D
/// shader only reads <c>NoteB.w</c> in the satin sheen and in three material branches — Solid's specular, Glass's
/// sweep and Gem's seam — while Neon, Fire and Crystal Outline never touch it. <c>EffectContracts</c> already says
/// so in words for Crystal; this review says it in pixels for all six, on both renderers, at the same scene,
/// seed and sample time, with the slider as the only difference between two captures of a row.
/// </para>
///
/// <para>
/// The claim is narrow on purpose. It proves the 0 → 50 → 100 slider moves the picture where the code says it
/// moves and cannot move it where the code says it does not; it does not grade the fringe, does not compare WPF
/// with GPU as pictures (the two renderers are allowed to look different — that is the point), does not touch
/// hardware frames, AR, other quality tiers or any default. The contact sheets are the part that is committed;
/// the eighteen native captures per renderer stay in the run's artifact.
/// </para>
/// </summary>
internal static class RefractionReview
{
    private const int Width = OptionMatrixPlan.Width, Height = OptionMatrixPlan.Height;
    /// <summary>The same six materials the settings offer, in their own order — never a copy of the list.</summary>
    internal static IReadOnlyList<string> Styles { get; } = PianoVisualSettings.NoteStyles;
    /// <summary>Off, half and full: three positions that bracket the slider's default of 35.</summary>
    internal static IReadOnlyList<int> Levels { get; } = [0, 50, 100];
    /// <summary>
    /// The materials whose GPU branch reads <c>NoteB.w</c>: Solid (specular seam), Glass (diagonal sweep) and Gem
    /// (specular seam), plus the satin sheen Solid and Glass share. Neon, Fire and Crystal Outline never read it,
    /// so their three captures must be the same bytes on the GPU — that equality is the contract, not a gap.
    /// </summary>
    internal static IReadOnlySet<string> GpuConsumers { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Solid", "Glass", "Gem"
    };
    private static readonly string[] RendererIds = ["WPF", "GPU-WARP"];
    /// <summary>Twelve changed pixels is more than a stray edge; the fringe alone clears it by far.</summary>
    private const int MinimumChangedPixels = 12;
    /// <summary>Six materials × three levels × two renderers.</summary>
    private const int ExpectedCaptures = 36;
    /// <summary>
    /// Three columns — the three levels — and six rows, one per material, so a row reads left to right as the
    /// slider's own travel. 640×360 is two-thirds of the capture, big enough that a one-pixel fringe survives the
    /// box filter into the committed page.
    /// </summary>
    private const int TileWidth = 640, TileHeight = 360, Columns = 3;

    private sealed class Shot
    {
        internal required string Style { get; init; }
        internal required int Level { get; init; }
        internal required string Renderer { get; init; }
        internal required string File { get; init; }
        internal required string PixelSha256 { get; init; }
        internal required int LitPixels { get; init; }
        /// <summary>Held only while the row is compared; dropped before the manifest is written.</summary>
        internal byte[]? Pixels { get; set; }
    }

    private sealed record Pair(string Style, string Renderer, int Left, int Right, int Changed);

    /// <summary>A sheet as the manifest names it: where it is, how big it is, and the pixels it decodes to.</summary>
    private sealed record WrittenSheet(string Renderer, string File, int Width, int Height, int Columns, int Rows,
        int Tiles, string PixelSha256);

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The refraction review output directory is empty.", nameof(output));
        if (Styles.Count != 6)
            throw new InvalidDataException(
                $"The refraction review pins the six note materials, but PianoVisualSettings.NoteStyles lists {Styles.Count}. " +
                "A new material needs a row here, a contract line in docs/REFRACTION-PARITY.md and its own picture.");
        if (Levels.Count != 3)
            throw new InvalidDataException($"The refraction review pins three slider levels, not {Levels.Count}.");
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The refraction review requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var captions = Styles.SelectMany(style => Levels.Select(level => $"{style} {level}")).ToArray();
            var captionScale = ReviewSheet.CaptionScaleFor(captions, TileWidth);
            var sheets = RendererIds.ToDictionary(id => id,
                id => new ReviewSheet.Canvas(Styles.Count * Levels.Count, TileWidth, TileHeight, Columns,
                    $"N8 REFRACTION {id}", captionScale));
            var cells = RendererIds.ToDictionary(id => id, _ => 0);
            var shots = new List<Shot>(ExpectedCaptures);
            var pairs = new List<Pair>();
            var contract = new List<(string Style, bool Wpf, bool Gpu)>();

            foreach (var style in Styles)
            {
                // One settings object per level; only the slider differs, so a changed pixel between two
                // captures of this row is the slider and nothing else.
                var level = Levels.Select(value => Settings(style, value)).ToArray();
                var frames = new Dictionary<string, byte[][]>
                {
                    ["WPF"] = level.Select(settings => OptionMatrixReview.CaptureWpf(OptionMatrixPlan.Scene.Falling, settings)).ToArray(),
                    ["GPU-WARP"] = level.Select(settings => OptionMatrixReview.CaptureGpu(renderer, OptionMatrixPlan.Scene.Falling, settings).Pixels).ToArray()
                };

                foreach (var rendererId in RendererIds)
                {
                    var row = new Shot[Levels.Count];
                    for (var index = 0; index < Levels.Count; index++)
                    {
                        var pixels = frames[rendererId][index];
                        if (pixels.Length != Width * Height * 4)
                            throw new InvalidDataException(
                                $"{rendererId} returned {pixels.Length} bytes for {style}/{Levels[index]}; expected {Width}×{Height} BGRA pixels.");
                        // The software stage hands out premultiplied BGRA, the Direct3D readback straight BGRA;
                        // writing each in its own format keeps the PNG's colours exact.
                        var format = rendererId == "WPF" ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
                        var relative = $"refraction/{OptionMatrixPlan.Slug(style)}/{Levels[index]}-{(rendererId == "WPF" ? "wpf" : "gpu-warp")}.png";
                        var shot = new Shot
                        {
                            Style = style,
                            Level = Levels[index],
                            Renderer = rendererId,
                            File = relative.Replace('\\', '/'),
                            PixelSha256 = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height,
                                $"{style} at NoteRefraction {Levels[index]} on {rendererId}", format),
                            LitPixels = OptionMatrixPlan.NonBlackPixels(pixels),
                            Pixels = pixels
                        };
                        shots.Add(shot);
                        row[index] = shot;
                        sheets[rendererId].Blit(cells[rendererId]++, $"{style} {Levels[index]}", pixels, Width, Height,
                            rendererId == "WPF");
                    }

                    if (row[0].LitPixels < Width * Height / 50)
                        throw new InvalidDataException(
                            $"The {style} baseline is nearly blank on {rendererId} ({row[0].LitPixels} lit pixels); " +
                            "a refraction difference against it would not mean anything.");

                    for (var left = 0; left < Levels.Count; left++)
                    for (var right = left + 1; right < Levels.Count; right++)
                    {
                        var changed = OptionMatrixPlan.ChangedPixels(row[left].Pixels!, row[right].Pixels!);
                        pairs.Add(new Pair(style, rendererId, Levels[left], Levels[right], changed));
                        var required = rendererId == "WPF" || GpuConsumers.Contains(style);
                        if (required && changed < MinimumChangedPixels)
                            throw new InvalidDataException(
                                $"{style} moved {changed} pixels on {rendererId} between NoteRefraction {Levels[left]} and " +
                                $"{Levels[right]} (floor {MinimumChangedPixels}): the slider reaches this material's branch " +
                                "but does not change its picture.");
                        // The other half of the contract: where the code says the GPU ignores the slider, a single
                        // changed pixel would mean some other pass picked it up behind the branch under test.
                        if (!required && changed != 0)
                            throw new InvalidDataException(
                                $"{style} moved {changed} pixels on {rendererId} between NoteRefraction {Levels[left]} and " +
                                $"{Levels[right]}, but its shader branch does not read NoteB.w — the WPF-only fringe would " +
                                "have become a GPU difference, or a second pass started consuming the slider.");
                    }

                    var moved = pairs.Where(pair => pair.Style == style && pair.Renderer == rendererId)
                        .ToDictionary(pair => (pair.Left, pair.Right), pair => pair.Changed);
                    if (rendererId == "WPF")
                        contract.Add((style, moved[(Levels[0], Levels[^1])] >= MinimumChangedPixels, GpuConsumers.Contains(style)));
                    foreach (var shot in row) shot.Pixels = null;
                }
            }

            if (shots.Count != ExpectedCaptures)
                throw new InvalidDataException($"The refraction review wrote {shots.Count} captures; exactly {ExpectedCaptures} are required.");

            var written = new List<WrittenSheet>(RendererIds.Length);
            foreach (var rendererId in RendererIds)
            {
                var layout = sheets[rendererId].Sheet;
                var file = $"refraction/sheet-{(rendererId == "WPF" ? "wpf" : "gpu-warp")}.png";
                var tiles = shots.Count(shot => shot.Renderer == rendererId);
                if (sheets[rendererId].Count != tiles)
                    throw new InvalidDataException(
                        $"{file} carries {sheets[rendererId].Count} pictures but the {rendererId} renderer wrote {tiles} captures; " +
                        "a sheet has to show the whole grid, because it is the copy that outlives the artifact.");
                written.Add(new WrittenSheet(rendererId, file, layout.Width, layout.Height, layout.Columns, layout.Rows,
                    tiles, ReviewPng.Write(Path.Combine(root, file), sheets[rendererId].Finish(), layout.Width,
                        layout.Height, $"{rendererId}: {sheets[rendererId].Count} captures in {layout.Columns}×{layout.Rows} cells",
                        PixelFormats.Pbgra32)));
            }

            WriteManifest(root, renderer.AdapterName, contract, pairs, shots, written);
            WriteHtmlIndex(root, renderer.AdapterName, contract, pairs, written);
            VerifyWrittenArtifacts(root, shots, written);
            return shots.Count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    /// <summary>
    /// The pinned scene with one material and one slider position. Shared baseline, so the N8 page and the N12
    /// matrix describe the same note; the read-back after <c>Clamp()</c> is the proof the slider was not moved.
    /// </summary>
    internal static PianoVisualSettings Settings(string style, int refraction)
    {
        if (!Styles.Contains(style))
            throw new InvalidDataException($"'{style}' is not one of the note materials the settings offer.");
        if (!Levels.Contains(refraction))
            throw new InvalidDataException($"{refraction} is not one of the three refraction levels this review shoots.");
        var settings = OptionMatrixReview.BaseSettings();
        settings.NoteStyle = style;
        settings.NoteRefraction = refraction;
        settings.Clamp();
        if (settings.NoteStyle != style || Math.Abs(settings.NoteRefraction - refraction) > 0.001)
            throw new InvalidDataException(
                $"The settings class turned {style} at NoteRefraction {refraction} into " +
                $"'{settings.NoteStyle}' at {settings.NoteRefraction}; the capture would not be the level it names.");
        return settings;
    }

    private static void WriteManifest(string root, string adapter,
        IReadOnlyList<(string Style, bool Wpf, bool Gpu)> contract, IReadOnlyList<Pair> pairs,
        IReadOnlyList<Shot> shots, IReadOnlyList<WrittenSheet> sheets)
    {
        File.WriteAllText(Path.Combine(root, "refraction", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            status = "n8-refraction-not-art-signoff",
            claim = "the NoteRefraction slider moves the picture on both renderers exactly where the code says it does: the WPF fringe/sheen for all six note materials, and the Direct3D branches that read NoteB.w for Solid, Glass and Gem only",
            notClaimed = "art approval, a WPF-versus-GPU picture match (the two renderers are allowed to differ), hardware frames, AR, other quality tiers or a default change",
            renderer = "WPF and GPU-WARP (forced)",
            adapter,
            width = Width, height = Height, fps = OptionMatrixPlan.Fps, seed = OptionMatrixPlan.Seed,
            pitch = OptionMatrixPlan.Pitch,
            scene = "one held song note falling for 1.5 s, the same scene the N12 note-style family uses",
            styles = Styles,
            styleSource = "PianoVisualSettings.NoteStyles",
            levels = Levels,
            minimumChangedPixels = MinimumChangedPixels,
            contract = contract.Select(row => new
            {
                row.Style,
                Wpf = row.Wpf ? "moves" : "does not move",
                Gpu = row.Gpu ? "moves" : "bit-identical",
                Rule = row.Gpu
                    ? "the shader branch reads NoteB.w, so the slider must move pixels on the GPU too"
                    : "the shader branch never reads NoteB.w; only the WPF fringe uses the slider, and that fringe is intentionally WPF-only"
            }),
            comparisons = pairs.Select(pair => new { pair.Style, pair.Renderer, pair.Left, pair.Right, pair.Changed }),
            sheets = sheets.Select(sheet => new
            {
                sheet.Renderer, sheet.File, sheet.Width, sheet.Height, sheet.Columns, sheet.Rows, sheet.Tiles,
                sheet.PixelSha256
            }),
            captureCount = shots.Count,
            captures = shots.Select(shot => new
            {
                shot.Style, shot.Level, shot.Renderer, shot.File, shot.PixelSha256, shot.LitPixels
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, string adapter,
        IReadOnlyList<(string Style, bool Wpf, bool Gpu)> contract, IReadOnlyList<Pair> pairs,
        IReadOnlyList<WrittenSheet> sheets)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N8 — refraction 6 × 3</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 6px}p{line-height:1.55;color:#a9b6c7}table{border-collapse:collapse;margin:12px 0}")
            .Append("th,td{border:1px solid #263243;padding:5px 9px;text-align:left;font-variant-numeric:tabular-nums}")
            .Append("th{color:#c9d6e6;font-weight:600}td.y{color:#7df0b1}td.n{color:#ffd479}")
            .Append("img{max-width:100%;height:auto;border:1px solid #263243;margin-top:8px}</style>")
            .Append("<h1>Refraction trên sáu vật liệu nốt (N8)</h1>")
            .Append($"<p>Cùng một cảnh, cùng seed, cùng mốc 1,5 s, cùng đầu vào; chỉ <code>NoteRefraction</code> đổi giữa 0, 50 và 100. ")
            .Append($"Máy đo: {Encode(adapter)}.</p>")
            .Append("<p><b>Hợp đồng:</b> WPF vẽ viền khúc xạ cho cả sáu chất liệu, nên cả sáu phải đổi ảnh. ")
            .Append("Shader Direct3D chỉ đọc <code>NoteB.w</code> ở Solid, Glass và Gem, nên ba chất liệu kia phải <b>giống hệt từng byte</b> ")
            .Append("trên GPU — đó là khác biệt có chủ ý đã chốt, không phải lỗi.</p>")
            .Append("<table><tr><th>Chất liệu</th><th>WPF 0→100</th><th>GPU 0→100</th><th>Quy tắc</th></tr>");
        foreach (var row in contract)
        {
            var wpf = pairs.First(pair => pair.Style == row.Style && pair.Renderer == "WPF" && pair.Left == Levels[0] && pair.Right == Levels[^1]);
            var gpu = pairs.First(pair => pair.Style == row.Style && pair.Renderer == "GPU-WARP" && pair.Left == Levels[0] && pair.Right == Levels[^1]);
            body.Append($"<tr><td>{Encode(row.Style)}</td><td class=\"y\">{wpf.Changed:N0} px</td>")
                .Append($"<td class=\"{(row.Gpu ? "y" : "n")}\">{gpu.Changed:N0} px</td>")
                .Append($"<td>{(row.Gpu ? "shader đọc NoteB.w" : "shader không đọc NoteB.w — viền chỉ có ở WPF")}</td></tr>");
        }
        body.Append("</table>");
        foreach (var sheet in sheets)
            body.Append($"<h2>Tờ ảnh {Encode(sheet.Renderer)}</h2>")
                .Append($"<p>{sheet.Tiles} khung trong lưới {sheet.Columns}×{sheet.Rows} ({sheet.Width}×{sheet.Height} px), ")
                .Append("mỗi hàng một chất liệu, mỗi cột một mức.</p>")
                .Append($"<img src=\"{Encode(sheet.File)}\" alt=\"N8 refraction {Encode(sheet.Renderer)}\">");
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "refraction", "index.html"), body.ToString());
    }

    private static void VerifyWrittenArtifacts(string root, IReadOnlyList<Shot> shots, IReadOnlyList<WrittenSheet> sheets)
    {
        foreach (var shot in shots)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, shot.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != shot.PixelSha256)
                throw new InvalidDataException($"{shot.File} no longer decodes to the pixels the manifest names.");
        }
        foreach (var sheet in sheets)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, sheet.File), sheet.Width, sheet.Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != sheet.PixelSha256)
                throw new InvalidDataException($"{sheet.File} no longer decodes to the pixels the manifest names.");
        }
    }
}
