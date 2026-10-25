using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Pilot for §11.3.1 — value matrix, 3 points per setting, not just off+far.
///
/// N17 shows each family at *one* far value (off vs far). To flip Unverified→Applied we need
/// low/mid/high per percentage/choice setting, to see clamp/rounding that collapses two slider
/// positions onto one picture. This pilot does that for 4 families (wisps/flames/sparks/shimmer),
/// 4 captures per family: off, low, mid, far (16 frames). Stage N17S is mirrored
/// separately; this pilot is AR-only to keep CI small (≈16 frames vs 51).
/// </summary>
internal static class ArValuePilotReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    private const int MinimumChangedPixels = 12;
    private const int MinimumOverlayPixels = 500;
    private const int Columns = 4;
    private const int TileWidth = Width, TileHeight = Height;
    private static readonly int[] HeldKeys = [48, 72];
    private const int BurstPitch = 60;
    private const int ApproachPitch = 60;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    private enum Stage { Held, Impact, Approach }
    private sealed record PilotFamily(string Id, Stage Stage, string SwitchName, EffectParameterKind SwitchKind, string SwitchOffValue,
        string ValueName, EffectParameterKind ValueKind, double[] Numbers, string[] Choices, string Unit);

    // Four families: the three particle families plus falling.shimmer at Approach 0.5 s (50/70/100).
    // The shimmer is not a particle, so its witness has to come from N17's shared Probe table; reading the
    // particle counter here is what made the earlier 4-family attempts fail with "low drew no quads".
    private static readonly PilotFamily[] Driven =
    [
        new("particles.wisps", Stage.Held, "ShowWisps", EffectParameterKind.Boolean, "off",
            "WispAmount", EffectParameterKind.Percentage, [15, 75, 150], [], ""),
        new("particles.flames", Stage.Held, "ShowFlame", EffectParameterKind.Boolean, "off",
            "FlameIntensity", EffectParameterKind.Percentage, [20, 60, 100], [], ""),
        new("particles.sparks", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ParticleAmount", EffectParameterKind.Percentage, [20, 70, 120], [], ""),
        new("falling.shimmer", Stage.Approach, "NoteShimmer", EffectParameterKind.Boolean, "off",
            "NoteShimmerAmount", EffectParameterKind.Percentage, [50, 70, 100], [], ""),
    ];

    private sealed class Shot
    {
        internal required string File { get; init; }
        internal required string PixelSha256 { get; init; }
        internal required int Lit { get; init; }
        internal required int Quads { get; init; }
        internal required int Demand { get; init; }
        internal byte[]? Pixels { get; set; }
    }
    private sealed record Cell(string Family, Stage Stage, string Parameter, string Row, double Value, string Choice,
        string File, string PixelSha256, int ArChanged, int ArLit, int ArPainted, int Quads, int Demand, string Reading, string Note);
    private sealed record SheetReport(string File, string Sha, int Columns, int Rows, IReadOnlyList<Cell> Cells);
    internal static string Summary { get; private set; } = "";

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("empty", nameof(output));
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            var samples = PrismFlowVisualReview.FindSamples();
            var original = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(samples, "ar-keyboard-topdown.png"));
            var pose = HeldParticleControlPlan.Views.Single(v => v.Id == "ar-oblique");
            var camera = HeldParticleControlPlan.CameraFor(pose, original);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp) throw new InvalidOperationException($"WARP required but got {renderer.AdapterName}");

            var reports = new List<SheetReport>(2);
            var shots = new List<Shot>(Driven.Length * Columns);
            foreach (var group in Driven.GroupBy(f => f.Stage))
            {
                var page = group.ToArray();
                var captions = page.SelectMany(f => new[] { $"{f.Id} off", $"{f.Id} {f.ValueName} {f.Numbers[0]:0}", $"{f.Id} {f.ValueName} {f.Numbers[1]:0}", $"{f.Id} {f.ValueName} {f.Numbers[2]:0}" }).ToArray();
                var sheet = new ReviewSheet.Canvas(page.Length * Columns, TileWidth, TileHeight, Columns,
                    $"VALUE PILOT AR {group.Key.ToString().ToUpperInvariant()}", ReviewSheet.CaptionScaleFor(captions, TileWidth));
                var cells = new List<Cell>(page.Length * Columns);
                for (var col = 0; col < page.Length; col++)
                {
                    var fam = page[col];
                    var off = CaptureAr(root, renderer, pose, camera, Settings(fam, null, isOff: true), fam, "off");
                    shots.Add(off);
                    sheet.Blit(col * Columns, captions[col * Columns], off.Pixels!, Width, Height, premultiplied: false);
                    var low = CaptureAr(root, renderer, pose, camera, Settings(fam, 0, isOff: false), fam, "low");
                    if (OptionMatrixPlan.ChangedPixels(low.Pixels!, camera) < MinimumOverlayPixels) throw new InvalidDataException($"{fam.Id} low frame painted {OptionMatrixPlan.ChangedPixels(low.Pixels!, camera)} < {MinimumOverlayPixels}");
                    if (low.Quads <= 0) throw new InvalidDataException($"{fam.Id} low drew no quads");
                    shots.Add(low);
                    sheet.Blit(col * Columns + 1, captions[col * Columns + 1], low.Pixels!, Width, Height, premultiplied: false);
                    var offVsLow = OptionMatrixPlan.ChangedPixels(off.Pixels!, low.Pixels!);
                    if (offVsLow < MinimumChangedPixels) throw new InvalidDataException($"{fam.Id} off↔low {offVsLow} < {MinimumChangedPixels}");

                    var mid = CaptureAr(root, renderer, pose, camera, Settings(fam, 1, isOff: false), fam, "mid");
                    shots.Add(mid);
                    sheet.Blit(col * Columns + 2, captions[col * Columns + 2], mid.Pixels!, Width, Height, premultiplied: false);
                    var lowVsMid = OptionMatrixPlan.ChangedPixels(low.Pixels!, mid.Pixels!);
                    if (lowVsMid < MinimumChangedPixels) throw new InvalidDataException($"{fam.Id} low↔mid {lowVsMid} < {MinimumChangedPixels}");

                    var far = CaptureAr(root, renderer, pose, camera, Settings(fam, 2, isOff: false), fam, "far");
                    shots.Add(far);
                    sheet.Blit(col * Columns + 3, captions[col * Columns + 3], far.Pixels!, Width, Height, premultiplied: false);
                    var midVsFar = OptionMatrixPlan.ChangedPixels(mid.Pixels!, far.Pixels!);
                    if (midVsFar < MinimumChangedPixels) throw new InvalidDataException($"{fam.Id} mid↔far {midVsFar} < {MinimumChangedPixels}");
                    var offVsFar = OptionMatrixPlan.ChangedPixels(off.Pixels!, far.Pixels!);
                    if (offVsFar < MinimumChangedPixels) throw new InvalidDataException($"{fam.Id} off↔far {offVsFar} < {MinimumChangedPixels}");

                    cells.Add(new Cell(fam.Id, fam.Stage, fam.SwitchName, "off", 0, "", off.File, off.PixelSha256, 0, off.Lit, OptionMatrixPlan.ChangedPixels(off.Pixels!, camera), off.Quads, off.Demand, "Reference", "Family off"));
                    cells.Add(new Cell(fam.Id, fam.Stage, fam.ValueName, "low", fam.Numbers[0], "", low.File, low.PixelSha256, offVsLow, low.Lit, OptionMatrixPlan.ChangedPixels(low.Pixels!, camera), low.Quads, low.Demand, Read(offVsLow), $"Low {fam.Numbers[0]:0} vs off"));
                    cells.Add(new Cell(fam.Id, fam.Stage, fam.ValueName, "mid", fam.Numbers[1], "", mid.File, mid.PixelSha256, OptionMatrixPlan.ChangedPixels(mid.Pixels!, off.Pixels!), mid.Lit, OptionMatrixPlan.ChangedPixels(mid.Pixels!, camera), mid.Quads, mid.Demand, Read(OptionMatrixPlan.ChangedPixels(mid.Pixels!, off.Pixels!)), $"Mid {fam.Numbers[1]:0} vs off"));
                    cells.Add(new Cell(fam.Id, fam.Stage, fam.ValueName, "far", fam.Numbers[2], "", far.File, far.PixelSha256, offVsFar, far.Lit, OptionMatrixPlan.ChangedPixels(far.Pixels!, camera), far.Quads, far.Demand, Read(offVsFar), $"Far {fam.Numbers[2]:0} vs off"));

                    off.Pixels = null; low.Pixels = null; mid.Pixels = null; far.Pixels = null;
                }
                var finished = sheet.Finish();
                var layout = sheet.Sheet;
                var sheetPath = $"ar-value-pilot/sheet-{group.Key.ToString().ToLowerInvariant()}.png";
                var sha = ReviewPng.Write(Path.Combine(root, sheetPath), finished, layout.Width, layout.Height,
                    $"VALUE PILOT AR {group.Key}: {page.Length * Columns} captures", PixelFormats.Pbgra32);
                reports.Add(new SheetReport(sheetPath, sha, layout.Columns, layout.Rows, cells));
            }
            VerifyShots(root, shots);
            Summary = string.Join("; ", Driven.Select(f =>
            {
                var cs = reports.SelectMany(r => r.Cells).Where(c => c.Family == f.Id).ToArray();
                var low = cs.First(c => c.Row == "low");
                var mid = cs.First(c => c.Row == "mid");
                var far = cs.First(c => c.Row == "far");
                return $"{f.Id} {f.ValueName} low {low.ArChanged}→mid {mid.ArChanged}→far {far.ArChanged} px vs off";
            }));
            var total = reports.Sum(r => r.Cells.Count);
            WriteManifest(root, renderer.AdapterName, reports, total);
            WriteHtmlIndex(root, renderer.AdapterName, reports);
            return total;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static PianoVisualSettings Settings(PilotFamily fam, int? valueIndex, bool isOff)
    {
        var s = OptionMatrixReview.BaseSettings();
        if (fam.SwitchKind == EffectParameterKind.Boolean)
        {
            Apply(s, new DrivenSetting(fam.SwitchName, EffectParameterKind.Boolean, isOff ? 0 : 1, ""));
        }
        else throw new InvalidDataException("pilot switch must be boolean");
        if (!isOff && valueIndex.HasValue)
        {
            var v = fam.Numbers[valueIndex.Value];
            Apply(s, new DrivenSetting(fam.ValueName, fam.ValueKind, v, ""));
        }
        s.Clamp();
        return s;
    }
    private sealed record DrivenSetting(string Name, EffectParameterKind Kind, double Number, string Choice);
    private static void Apply(PianoVisualSettings settings, DrivenSetting setting)
    {
        switch (setting.Name)
        {
            case "ShowWisps": settings.ShowWisps = setting.Number >= 0.5; break;
            case "WispAmount": settings.WispAmount = setting.Number; break;
            case "ShowFlame": settings.ShowFlame = setting.Number >= 0.5; break;
            case "FlameIntensity": settings.FlameIntensity = setting.Number; break;
            case "ShowEmbers": settings.ShowEmbers = setting.Number >= 0.5; break;
            case "ParticleAmount": settings.ParticleAmount = setting.Number; break;
            case "NoteShimmer": settings.NoteShimmer = setting.Number >= 0.5; break;
            case "NoteShimmerAmount": settings.NoteShimmerAmount = setting.Number; break;
            default: throw new InvalidDataException($"pilot cannot drive {setting.Name}");
        }
    }
    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, PilotFamily fam, string label)
    {
        var (pixels, demand) = RenderAr(renderer, pose, camera, settings, fam.Stage);
        var quads = Probe(renderer, fam.Id);
        var relative = Path.Combine("ar-value-pilot", OptionMatrixPlan.Slug(fam.Id), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{fam.Id}/{label} pilot AR");
        return new Shot { File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels), Quads = quads, Demand = demand, Pixels = pixels };
    }
    private static (int Frames, double Seconds) Timing(Stage stage) => stage switch
    {
        Stage.Held => (12, 1.0 / 30),
        Stage.Impact => (12, 1.0 / 60),
        Stage.Approach => (30, 1.0 / 60),
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };
    private static readonly NoteEvent[] ApproachNotes = [new() { Pitch = ApproachPitch, Start = .9, Duration = .5, Velocity = 96, Track = 0 }];
    private static (byte[] Pixels, int Demand) RenderAr(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, Stage stage)
    {
        var piano = new PianoStage { Width = Width, Height = Height };
        piano.SetVisualSettings(settings);
        piano.Measure(new Size(Width, Height));
        piano.Arrange(new Rect(0, 0, Width, Height)); piano.UpdateLayout();
        var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
        feed.SetLook(GpuLook.From(settings, (pitch, track) => piano.AdjustColor(piano.NoteColor(pitch, track)), piano.KeyboardHeight / piano.ActualHeight, piano.AdjustColor, true));
        feed.SetStageHeight(Height);
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, pose.Corners!, Width, Height, out var problem, HeldParticleControlPlan.Fov) ?? throw new InvalidDataException($"calib {problem}");
        var ring = new CameraFrameRing();
        camera.CopyTo(ring.BeginWrite(camera.Length, out var slot), 0);
        ring.Publish(slot, Width, Height, System.Diagnostics.Stopwatch.GetTimestamp());
        feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, KeyLight: .6f, Quality: ArQualityTier.Ultra, ProjectedNotes: true, ShowLiveTrails: stage == Stage.Held, ShowDamperStrip: false, ShowRisingMidiNotes: false);
        var (frames, dt) = Timing(stage);
        var position = frames * dt;
        if (stage == Stage.Approach && ApproachNotes.Any(n => n.Start <= position))
            throw new InvalidDataException($"Approach note starts {ApproachNotes[0].Start}s but capture at {position}s would be sounding");
        var notes = stage == Stage.Approach ? ApproachNotes : Array.Empty<NoteEvent>();
        var playing = stage == Stage.Approach;
        var pressed = stage switch
        {
            Stage.Held => new HashSet<int>(HeldKeys),
            _ => NoPressed,
        };
        void BeforeFrame(int frame)
        {
            feed.SetDeterministicState(notes, (frame + 1) * dt, playing, pressed);
            feed.SetDeterministicBeatPulse(0);
            if (frame != 0) return;
            if (stage == Stage.Held) foreach (var p in HeldKeys) feed.LiveNote(p, true, .9);
            else if (stage == Stage.Impact) feed.Impact(BurstPitch, 1);
        }
        var demand = 0;
        renderer.ObserveParticleStateForReview = (sim, _) => demand = Math.Max(demand, sim.ArParticleCount);
        try
        {
            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _, BeforeFrame, OptionMatrixPlan.Seed);
            return (pixels, demand);
        }
        finally { renderer.ObserveParticleStateForReview = null; }
    }
    // Delegate to N17's witness table instead of assuming every driven family is a particle family. A flat
    // ArParticleFxCount reads zero for shimmer, beams, flashes and hold bars even when they drew quads.
    private static int Probe(GpuStageRenderer renderer, string family) => ArEffectFamilyReview.Probe(renderer, family);
    private static string Read(int c) => c >= MinimumChangedPixels ? "moves" : "still";
    private static void VerifyShots(string root, IReadOnlyList<Shot> shots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in shots)
        {
            if (!seen.Add(s.File)) throw new InvalidDataException($"dup {s.File}");
            var path = Path.Combine(root, s.File);
            if (!File.Exists(path)) throw new InvalidDataException($"missing {s.File}");
            var pixels = ReviewPng.Decode(path, Width, Height);
            var sha = Convert.ToHexString(SHA256.HashData(pixels));
            if (!string.Equals(sha, s.PixelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"hash {s.File}");
        }
    }
    private static void WriteManifest(string root, string adapter, IReadOnlyList<SheetReport> sheets, int total)
    {
        File.WriteAllText(Path.Combine(root, "ar-value-pilot", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1, status = "value-pilot-3-points-not-claimed", claim = "each driven family at 3 values vs off, same scene/timing/seed",
            notClaimed = "art approval, every value, real camera, binding change",
            renderer = "GPU-WARP", adapter, width = Width, height = Height, seed = OptionMatrixPlan.Seed, pose = "ar-oblique",
            sheets = sheets.Select(r => new { r.File, r.Sha, r.Columns, r.Rows, cells = r.Cells.Select(c => new { c.Family, scene = c.Stage.ToString(), c.Parameter, c.Row, c.Value, c.Choice, c.File, c.PixelSha256, c.ArChanged, c.ArLit, c.ArPainted, c.Quads, c.Demand, Capped = c.Demand > c.Quads, c.Reading }) })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<SheetReport> sheets)
    {
        static string E(string v) => System.Net.WebUtility.HtmlEncode(v);
        var b = new StringBuilder();
        b.Append("<!doctype html><meta charset=utf-8><title>Value Pilot</title><style>body{font:14px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px;padding:16px}table{border-collapse:collapse}th,td{border:1px solid #263243;padding:4px 8px}</style><h1>AR value pilot — 3 điểm / setting</h1><p>Same pose/camera/seed, WARP, 4 cột off→low→mid→far. Máy: " + E(adapter) + "</p>");
        foreach (var r in sheets) { b.Append($"<h2>{E(r.File)}</h2><table><tr><th>Family</th><th>Row</th><th>Value</th><th>AR px vs off</th><th>Quads</th><th>Demand/Quads</th></tr>"); foreach (var c in r.Cells) b.Append($"<tr><td>{E(c.Family)}</td><td>{E(c.Row)}</td><td>{c.Value:0}</td><td>{c.ArChanged:N0}</td><td>{c.Quads:N0}</td><td>{c.Demand:N0}/{c.Quads:N0}</td></tr>"); b.Append("</table>"); b.Append($"<img src={E(Path.GetFileName(r.File))} style=max-width:100%><br>"); }
        File.WriteAllText(Path.Combine(root, "ar-value-pilot", "index.html"), b.ToString());
    }
}
