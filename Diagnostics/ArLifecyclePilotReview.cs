using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Pilot for §11.3.2 — lifecycle matrix, not just one time slice.
/// N17 shoots each family at one frame (Held 0.4s, Impact 0.20s). To flip
/// Unverified→Applied we need birth/mid/decay per family, to catch a
/// *LifeSeconds that is too short (impact.flash 0.26s shot at 0.30s gave 0 px).
/// This pilot does that for one impact family only (impact.flash), 3 milestones
/// × off/on = 6 captures on AR ar-oblique, same camera/seed as N17.
/// </summary>
internal static class ArLifecyclePilotReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    private const int MinimumChangedPixels = 12;
    private const int MinimumOverlayPixels = 500;
    private const int Columns = 3;
    private const int Rows = 2;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    private const int BurstPitch = 60;

    private sealed record Milestone(string Label, int Frames, double Seconds);
    private static readonly Milestone[] Milestones =
    [
        new("T0 0.05s", 3, 3.0/60),   // Growth 55ms not finished
        new("T1 0.20s", 12, 12.0/60), // as N17
        new("T2 0.35s", 21, 21.0/60), // flash StyledFlash 0.26s already dead, only flare 0.38s remains
    ];

    private sealed class Shot(string file, string pixelSha256, int lit, int painted, int quads, int demand, byte[]? pixels)
    {
        public string File => file; public string PixelSha256 => pixelSha256; public int Lit => lit; public int Painted => painted; public int Quads => quads; public int Demand => demand; public byte[]? Pixels { get; set; } = pixels;
    }
    private sealed record Cell(string Family, string Milestone, string Parameter, string Row, double Seconds, string Choice,
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

            var captions = new string[Columns * Rows];
            for (var c = 0; c < Columns; c++) { captions[c] = $"impact.flash {Milestones[c].Label} off"; captions[c+Columns] = $"impact.flash {Milestones[c].Label} Plasma"; }
            var sheet = new ReviewSheet.Canvas(Columns * Rows, Width, Height, Columns,
                "LIFECYCLE PILOT AR impact.flash", ReviewSheet.CaptionScaleFor(captions, Width));
            var cells = new List<Cell>(6);
            var shots = new List<Shot>(6);
            var milestoneResults = new List<(Milestone m, int changed, int quads, int demand)>();

            for (var col = 0; col < Columns; col++)
            {
                var m = Milestones[col];
                var off = CaptureAr(root, renderer, pose, camera, Settings(isOff:true), m, "off");
                shots.Add(off);
                sheet.Blit(col, captions[col], off.Pixels!, Width, Height, premultiplied:false);
                var on = CaptureAr(root, renderer, pose, camera, Settings(isOff:false), m, "on");
                if (on.Painted < MinimumOverlayPixels && m.Label != "T2 0.35s") throw new InvalidDataException($"impact.flash {m.Label} on painted {on.Painted} < {MinimumOverlayPixels}");
                shots.Add(on);
                sheet.Blit(col+Columns, captions[col+Columns], on.Pixels!, Width, Height, premultiplied:false);

                var changed = OptionMatrixPlan.ChangedPixels(off.Pixels!, on.Pixels!);
                // T0/T1 must move, T2 must be expired (<12)
                if (m.Label == "T2 0.35s")
                {
                    if (changed >= MinimumChangedPixels) throw new InvalidDataException($"impact.flash T2 expected expired <{MinimumChangedPixels} but got {changed}");
                }
                else
                {
                    if (changed < MinimumChangedPixels) throw new InvalidDataException($"impact.flash {m.Label} off↔on {changed} < {MinimumChangedPixels}");
                    if (on.Quads <= 0) throw new InvalidDataException($"impact.flash {m.Label} on drew no quads");
                }
                milestoneResults.Add((m, changed, on.Quads, on.Demand));
                cells.Add(new Cell("impact.flash", m.Label, "ShowImpactFlash", "off", m.Seconds, "", off.File, off.PixelSha256, 0, off.Lit, off.Painted, off.Quads, off.Demand, "Reference", "Family off"));
                cells.Add(new Cell("impact.flash", m.Label, "ImpactFlashStyle", "Plasma", m.Seconds, "Plasma", on.File, on.PixelSha256, changed, on.Lit, on.Painted, on.Quads, on.Demand, changed >= MinimumChangedPixels ? "moves" : "expired", changed >= MinimumChangedPixels ? "Plasma vs off" : "Plasma expired (flash 0.26s)"));
                off.Pixels = null; on.Pixels = null;
            }
            var finished = sheet.Finish();
            var layout = sheet.Sheet;
            var sheetPath = "ar-lifecycle-pilot/sheet-impact.png";
            var sha = ReviewPng.Write(Path.Combine(root, sheetPath), finished, layout.Width, layout.Height,
                "LIFECYCLE PILOT AR impact.flash: 6 captures (3 milestones × off/on)", PixelFormats.Pbgra32);
            var report = new SheetReport(sheetPath, sha, layout.Columns, layout.Rows, cells);
            VerifyShots(root, shots);
            Summary = string.Join("; ", milestoneResults.Select(r => $"{r.m.Label} {r.changed}px {r.demand}/{r.quads}"));
            WriteManifest(root, renderer.AdapterName, new[] { report }, 6);
            WriteHtmlIndex(root, renderer.AdapterName, new[] { report });
            return 6;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static PianoVisualSettings Settings(bool isOff)
    {
        var s = OptionMatrixReview.BaseSettings();
        s.ShowImpactFlash = !isOff;
        s.ImpactFlashStyle = "Plasma";
        s.ImpactFlashIntensity = 80;
        s.ShowImpactRings = true;
        s.Clamp();
        return s;
    }

    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, Milestone m, string label)
    {
        var (pixels, demand) = RenderAr(renderer, pose, camera, settings, m);
        var quads = renderer.ArFlashCount;
        var relative = Path.Combine("ar-lifecycle-pilot", $"impact.flash-{m.Frames}f-{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"impact.flash/{m.Label}/{label} pilot AR");
        return new Shot(relative.Replace('\\','/'), sha, OptionMatrixPlan.NonBlackPixels(pixels), OptionMatrixPlan.ChangedPixels(pixels, camera), quads, demand, pixels);
    }

    private static (byte[] Pixels, int Demand) RenderAr(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, Milestone m)
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
        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, KeyLight:.6f, Quality:ArQualityTier.Ultra, ProjectedNotes:true, ShowLiveTrails:false, ShowDamperStrip:false, ShowRisingMidiNotes:false);
        void BeforeFrame(int frame)
        {
            // 1/60s per frame, impact at frame 0 then run m.Frames
            feed.SetDeterministicState([], (frame+1)/60.0, false, NoPressed);
            feed.SetDeterministicBeatPulse(0);
            if (frame==0) feed.Impact(BurstPitch, 1);
        }
        var demand = 0;
        // ArParticleCount not relevant for flash; keep demand as max ArFlashCount? But we track via ArFlashCount directly; demand = quads for flash
        renderer.ObserveParticleStateForReview = (sim,_) => demand = Math.Max(demand, sim.ArParticleCount);
        try
        {
            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, m.Frames-1, 1.0/60, false, out _, BeforeFrame, OptionMatrixPlan.Seed);
            return (pixels, demand);
        }
        finally { renderer.ObserveParticleStateForReview = null; }
    }

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
        File.WriteAllText(Path.Combine(root, "ar-lifecycle-pilot", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema=1, status="lifecycle-pilot-not-claimed", claim="impact.flash at 3 milestones vs off, same scene/timing/seed",
            notClaimed="art approval, every family, real camera, binding change",
            renderer="GPU-WARP", adapter, width=Width, height=Height, seed=OptionMatrixPlan.Seed, pose="ar-oblique",
            milestones=Milestones.Select(m=> new {m.Label, m.Frames, m.Seconds}),
            sheets=sheets.Select(r=> new {r.File, r.Sha, r.Columns, r.Rows, cells=r.Cells.Select(c=> new {c.Family,c.Milestone,c.Parameter,c.Row,c.Seconds,c.Choice,c.File,c.PixelSha256,c.ArChanged,c.ArLit,c.ArPainted,c.Quads,c.Demand,c.Reading})})
        }, new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<SheetReport> sheets)
    {
        static string E(string v)=> System.Net.WebUtility.HtmlEncode(v);
        var b=new StringBuilder();
        b.Append("<!doctype html><meta charset=utf-8><title>Lifecycle Pilot</title><style>body{font:14px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px;padding:16px}table{border-collapse:collapse}th,td{border:1px solid #263243;padding:4px 8px}</style><h1>AR lifecycle pilot — impact.flash 3 mốc</h1><p>Same pose/camera/seed, WARP, 2 hàng off→Plasma × 3 mốc 0.05/0.20/0.35s. Máy: "+E(adapter)+"</p>");
        foreach(var r in sheets){ b.Append($"<h2>{E(r.File)}</h2><table><tr><th>Milestone</th><th>Row</th><th>AR px vs off</th><th>Quads</th></tr>"); foreach(var c in r.Cells) b.Append($"<tr><td>{E(c.Milestone)}</td><td>{E(c.Row)}</td><td>{c.ArChanged:N0}</td><td>{c.Quads:N0}</td></tr>"); b.Append("</table>"); b.Append($"<img src={E(Path.GetFileName(r.File))} style=max-width:100%><br>"); }
        File.WriteAllText(Path.Combine(root, "ar-lifecycle-pilot", "index.html"), b.ToString());
    }
}
