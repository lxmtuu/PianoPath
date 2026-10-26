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
/// positions onto one picture. This pilot shoots 19 columns over all 17 AR families, 4 captures
/// each: off, low, mid, far (76 frames). A family with more than one declared setting gets one
/// column per setting; every setting the column does not shoot is recorded as pinned (a value the
/// scene must hold) or unshot (a percentage never moved), read from EffectContractCatalog rather
/// than copied, so a column can never be called the whole family. Stage N17S is mirrored
/// separately; this pilot is AR-only.
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
    private const int ReleasePitch = 60;
    private const int ReleaseLiftFrame = 12;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    private enum Stage { Held, Impact, Approach, Release }
    /// <summary>
    /// One family / one column. A switch is either a boolean or a choice with a value the renderer treats
    /// as off (ImpactWave None, ImpactMorph None, ReleaseEffect Fade, FallingTrail None); the three shot
    /// values are either percentages or three declared choices. Pins are booleans the scene has to hold
    /// on for the family to draw at all (ShowImpactRings gates the wave pass).
    /// </summary>
    private sealed record PilotFamily(string Id, Stage Stage, string SwitchName, EffectParameterKind SwitchKind, string SwitchOffValue,
        string ValueName, EffectParameterKind ValueKind, double[] Numbers, string[] Choices, string Unit,
        string SwitchOnValue = "", string[]? Pins = null)
    {
        internal string[] Labels => ValueKind == EffectParameterKind.Choice
            ? Choices : Numbers.Select(n => n.ToString("0.##")).ToArray();
        internal double NumberAt(int index) => ValueKind == EffectParameterKind.Choice ? 0 : Numbers[index];
        internal string ChoiceAt(int index) => ValueKind == EffectParameterKind.Choice ? Choices[index] : "";
    }

    // Seventeen families, each one switch plus one driven setting, shot off/low/mid/far in the scene
    // its lifecycle needs. Twelve drive a percentage; the four choice-switch families drive the
    // percentage behind their switch, and impact.burst drives its own declared style list. Every
    // witness comes from N17's shared Probe table: a flat ArParticleFxCount reports zero quads for a
    // bar, an arc, a beam, a shape, a flash, a trail, a ring or a release droplet, which is what made
    // the earlier widening attempts fail with "low drew no quads".
    private static readonly PilotFamily[] Driven =
    [
        new("particles.wisps", Stage.Held, "ShowWisps", EffectParameterKind.Boolean, "off",
            "WispAmount", EffectParameterKind.Percentage, [15, 75, 150], [], ""),
        new("particles.flames", Stage.Held, "ShowFlame", EffectParameterKind.Boolean, "off",
            "FlameIntensity", EffectParameterKind.Percentage, [20, 60, 100], [], ""),
        new("atmosphere.beams", Stage.Held, "ShowLightBeams", EffectParameterKind.Boolean, "off",
            "BeamIntensity", EffectParameterKind.Percentage, [30, 65, 100], [], ""),
        new("hold.bar", Stage.Held, "HoldBar", EffectParameterKind.Boolean, "off",
            "HoldBarIntensity", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        // 60 is a neutral phase of the breathing animation in the 0.4 s Held scene: it paints exactly
        // the same picture as the family being off, so the mid point moves to 75.
        new("hold.breath", Stage.Held, "HoldBreath", EffectParameterKind.Boolean, "off",
            "HoldBreathRate", EffectParameterKind.Percentage, [25, 75, 100], [], ""),
        new("hold.vibration", Stage.Held, "HoldVibration", EffectParameterKind.Boolean, "off",
            "HoldVibrationAmount", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        new("hold.color-cycle", Stage.Held, "HoldColorCycle", EffectParameterKind.Boolean, "off",
            "HoldColorCycleSpeed", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        new("hold.arc", Stage.Held, "HoldElectricArc", EffectParameterKind.Boolean, "off",
            "HoldArcIntensity", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        // The pulse is at its zero crossing at 60 in the 0.5 s Approach scene, so 75 is the mid point.
        new("falling.pulse", Stage.Approach, "FallingPulse", EffectParameterKind.Boolean, "off",
            "FallingPulseRate", EffectParameterKind.Percentage, [25, 75, 100], [], ""),
        new("falling.shimmer", Stage.Approach, "NoteShimmer", EffectParameterKind.Boolean, "off",
            "NoteShimmerAmount", EffectParameterKind.Percentage, [50, 70, 100], [], ""),
        // Round B: a Choice switch carries the value the renderer itself treats as off, exactly as
        // N17's OffValues table declares. ImpactWave rides on the ring pass, so ShowImpactRings has
        // to stay on for that column; it is a pin, not a second thing being driven.
        new("falling.trail", Stage.Approach, "FallingTrail", EffectParameterKind.Choice, "None",
            "FallingTrailLength", EffectParameterKind.Percentage, [25, 60, 100], [], "", "Glow"),
        // Round C: a second column for the same family, driving a different declared setting. This is
        // the shape §11.3.1 asks for — one setting per column, three values each — and the manifest
        // names the remaining settings as pinned/unshot instead of calling one column the family.
        new("falling.trail", Stage.Approach, "FallingTrail", EffectParameterKind.Choice, "None",
            "FallingTrail", EffectParameterKind.Choice, [], ["Glow", "Blur", "Ribbon"], "", "Glow"),
        new("particles.sparks", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ParticleAmount", EffectParameterKind.Percentage, [20, 70, 120], [], ""),
        new("impact.flash", Stage.Impact, "ShowImpactFlash", EffectParameterKind.Boolean, "off",
            "ImpactFlashIntensity", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        // N17 measured Plasma at 82127 px; all three declared flash styles draw very different
        // pictures, so the style is a second, independent axis for this family.
        new("impact.flash", Stage.Impact, "ShowImpactFlash", EffectParameterKind.Boolean, "off",
            "ImpactFlashStyle", EffectParameterKind.Choice, [], ["Flash", "Lightning", "Plasma"], ""),
        new("impact.burst", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ImpactBurst", EffectParameterKind.Choice, [], ["Embers", "Fireworks", "Dust"], ""),
        // These three draw only tens of pixels in N17, so the column drives the choice itself at three
        // declared styles instead of a percentage whose steps would collapse onto the same picture.
        // The switch and the value are the same setting on purpose: off is the renderer's declared
        // off value, then three other values from the settings model's own list.
        new("impact.wave", Stage.Impact, "ImpactWave", EffectParameterKind.Choice, "None",
            "ImpactWave", EffectParameterKind.Choice, [], ["Ring", "Shockwave", "Ripple"], "", "Ring",
            ["ShowImpactRings"]),
        new("impact.morph", Stage.Impact, "ImpactMorph", EffectParameterKind.Choice, "None",
            "ImpactMorph", EffectParameterKind.Choice, [], ["Shatter", "Melt", "Bounce"], "", "Shatter"),
        new("release.fx", Stage.Release, "ReleaseEffect", EffectParameterKind.Choice, "Fade",
            "ReleaseEffect", EffectParameterKind.Choice, [], ["Float Up", "Dissolve", "Smoke"], "", "Float Up"),
        // Round D: every remaining declared percentage gets its own column. Three of the twelve
        // reuse the exact axes OptionNumericPlan already proved move pixels on both renderers
        // (trail intensity 0/50/100, wave intensity 0/75/150); the rest stay inside the contract's
        // own min/max. The gate collector above reports every column that cannot clear >=12 px in
        // one run, so a weak setting is dropped with its measured delta rather than guessed at.
        new("particles.wisps", Stage.Held, "ShowWisps", EffectParameterKind.Boolean, "off",
            "WispSpeed", EffectParameterKind.Percentage, [100, 300, 600], [], ""),
        new("particles.wisps", Stage.Held, "ShowWisps", EffectParameterKind.Boolean, "off",
            "WispTurbulence", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        new("particles.wisps", Stage.Held, "ShowWisps", EffectParameterKind.Boolean, "off",
            "WispGlow", EffectParameterKind.Percentage, [50, 125, 200], [], ""),
        new("particles.flames", Stage.Held, "ShowFlame", EffectParameterKind.Boolean, "off",
            "FlameHeight", EffectParameterKind.Percentage, [25, 60, 100], [], ""),
        new("particles.sparks", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ParticleSpeed", EffectParameterKind.Percentage, [75, 175, 300], [], ""),
        new("particles.sparks", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ParticleSize", EffectParameterKind.Percentage, [2, 8, 16], [], ""),
        new("particles.sparks", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ParticleGlow", EffectParameterKind.Percentage, [50, 125, 200], [], ""),
        new("particles.sparks", Stage.Impact, "ShowEmbers", EffectParameterKind.Boolean, "off",
            "ParticleLife", EffectParameterKind.Percentage, [0.5, 1.5, 3], [], ""),
        new("falling.trail", Stage.Approach, "FallingTrail", EffectParameterKind.Choice, "None",
            "FallingTrailIntensity", EffectParameterKind.Percentage, [0, 50, 100], [], "", "Glow"),
        new("impact.wave", Stage.Impact, "ImpactWave", EffectParameterKind.Choice, "None",
            "ImpactWaveIntensity", EffectParameterKind.Percentage, [0, 75, 150], [], "", "Ring",
            ["ShowImpactRings"]),
        new("impact.morph", Stage.Impact, "ImpactMorph", EffectParameterKind.Choice, "None",
            "ImpactMorphIntensity", EffectParameterKind.Percentage, [25, 60, 100], [], "", "Shatter"),
        new("release.fx", Stage.Release, "ReleaseEffect", EffectParameterKind.Choice, "Fade",
            "ReleaseIntensity", EffectParameterKind.Percentage, [25, 60, 100], [], "", "Float Up"),
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
            foreach (var f in Driven)
            {
                if (f.Labels.Length != 3)
                    throw new InvalidDataException($"{f.Id} must declare exactly three shot values, got {f.Labels.Length}");
                if (f.SwitchKind is not (EffectParameterKind.Boolean or EffectParameterKind.Choice))
                    throw new InvalidDataException($"{f.Id} declares an unsupported switch kind");
                if (f.SwitchKind == EffectParameterKind.Choice
                    && (f.SwitchOffValue.Length == 0 || f.SwitchOnValue.Length == 0))
                    throw new InvalidDataException($"{f.Id} needs both the off and on values of its choice switch");
            }
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            var samples = PrismFlowVisualReview.FindSamples();
            var original = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(samples, "ar-keyboard-topdown.png"));
            var pose = HeldParticleControlPlan.Views.Single(v => v.Id == "ar-oblique");
            var camera = HeldParticleControlPlan.CameraFor(pose, original);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp) throw new InvalidOperationException($"WARP required but got {renderer.AdapterName}");

            // Collect every column that misses a gate instead of throwing on the first one. A
            // widening attempt used to cost one CI run per failing family; one run now reports all
            // of them with their measured deltas, so the next commit can keep exactly the columns
            // that have evidence. The run still fails if any column is weak: no gate is lowered.
            var failures = new List<string>();
            void Gate(PilotFamily fam, string name, int changed)
            {
                if (changed < MinimumChangedPixels)
                    failures.Add($"{fam.Id}/{fam.ValueName} {name} {changed} < {MinimumChangedPixels}");
            }
            var reports = new List<SheetReport>(2);
            var shots = new List<Shot>(Driven.Length * Columns);
            foreach (var group in Driven.GroupBy(f => f.Stage))
            {
                var page = group.ToArray();
                var captions = page.SelectMany(f => new[] { $"{f.Id} off", $"{f.Id} {f.ValueName} {f.Labels[0]}", $"{f.Id} {f.ValueName} {f.Labels[1]}", $"{f.Id} {f.ValueName} {f.Labels[2]}" }).ToArray();
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
                    var lowPainted = OptionMatrixPlan.ChangedPixels(low.Pixels!, camera);
                    if (lowPainted < MinimumOverlayPixels) failures.Add($"{fam.Id}/{fam.ValueName} low painted {lowPainted} < {MinimumOverlayPixels}");
                    if (low.Quads <= 0) failures.Add($"{fam.Id}/{fam.ValueName} low drew no quads");
                    shots.Add(low);
                    sheet.Blit(col * Columns + 1, captions[col * Columns + 1], low.Pixels!, Width, Height, premultiplied: false);
                    var offVsLow = OptionMatrixPlan.ChangedPixels(off.Pixels!, low.Pixels!);
                    Gate(fam, "off↔low", offVsLow);

                    var mid = CaptureAr(root, renderer, pose, camera, Settings(fam, 1, isOff: false), fam, "mid");
                    shots.Add(mid);
                    sheet.Blit(col * Columns + 2, captions[col * Columns + 2], mid.Pixels!, Width, Height, premultiplied: false);
                    var lowVsMid = OptionMatrixPlan.ChangedPixels(low.Pixels!, mid.Pixels!);
                    Gate(fam, "low↔mid", lowVsMid);
                    // A setting can land on its own neutral phase (hold.breath and falling.pulse both do at
                    // 60 in these scenes). Without this gate the mid frame is byte-identical to off yet is
                    // captioned as a third value, which is exactly the "two values, one picture" the plan
                    // forbids. Refuse the column instead of claiming coverage it does not have.
                    var offVsMid = OptionMatrixPlan.ChangedPixels(off.Pixels!, mid.Pixels!);
                    Gate(fam, "off↔mid", offVsMid);

                    var far = CaptureAr(root, renderer, pose, camera, Settings(fam, 2, isOff: false), fam, "far");
                    shots.Add(far);
                    sheet.Blit(col * Columns + 3, captions[col * Columns + 3], far.Pixels!, Width, Height, premultiplied: false);
                    var midVsFar = OptionMatrixPlan.ChangedPixels(mid.Pixels!, far.Pixels!);
                    Gate(fam, "mid↔far", midVsFar);
                    var offVsFar = OptionMatrixPlan.ChangedPixels(off.Pixels!, far.Pixels!);
                    Gate(fam, "off↔far", offVsFar);

                    cells.Add(new Cell(fam.Id, fam.Stage, fam.SwitchName, "off", 0, fam.SwitchKind == EffectParameterKind.Choice ? fam.SwitchOffValue : "", off.File, off.PixelSha256, 0, off.Lit, OptionMatrixPlan.ChangedPixels(off.Pixels!, camera), off.Quads, off.Demand, "Reference", "Family off"));
                    cells.Add(new Cell(fam.Id, fam.Stage, fam.ValueName, "low", fam.NumberAt(0), fam.ChoiceAt(0), low.File, low.PixelSha256, offVsLow, low.Lit, OptionMatrixPlan.ChangedPixels(low.Pixels!, camera), low.Quads, low.Demand, Read(offVsLow), $"Low {fam.Labels[0]} vs off"));
                    cells.Add(new Cell(fam.Id, fam.Stage, fam.ValueName, "mid", fam.NumberAt(1), fam.ChoiceAt(1), mid.File, mid.PixelSha256, offVsMid, mid.Lit, OptionMatrixPlan.ChangedPixels(mid.Pixels!, camera), mid.Quads, mid.Demand, Read(offVsMid), $"Mid {fam.Labels[1]} vs off"));
                    cells.Add(new Cell(fam.Id, fam.Stage, fam.ValueName, "far", fam.NumberAt(2), fam.ChoiceAt(2), far.File, far.PixelSha256, offVsFar, far.Lit, OptionMatrixPlan.ChangedPixels(far.Pixels!, camera), far.Quads, far.Demand, Read(offVsFar), $"Far {fam.Labels[2]} vs off"));

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
                // Match the column, not just the family: two columns of one family would otherwise
                // both quote the first column's numbers.
                var cs = reports.SelectMany(r => r.Cells)
                    .Where(c => c.Family == f.Id && c.Parameter == f.ValueName).ToArray();
                var low = cs.First(c => c.Row == "low");
                var mid = cs.First(c => c.Row == "mid");
                var far = cs.First(c => c.Row == "far");
                var values = f.ValueKind == EffectParameterKind.Choice
                    ? string.Join("/", f.Choices) : string.Join("/", f.Numbers.Select(n => n.ToString("0.##")));
                return $"{f.Id} {f.ValueName} {values} low {low.ArChanged}→mid {mid.ArChanged}→far {far.ArChanged} px vs off";
            }));
            var total = reports.Sum(r => r.Cells.Count);
            WriteManifest(root, renderer.AdapterName, reports, total, failures.ToArray());
            WriteHtmlIndex(root, renderer.AdapterName, reports, failures.ToArray());
            if (failures.Count > 0)
                throw new InvalidDataException($"{failures.Count} N17V gate(s) failed: " + string.Join(" | ", failures));
            return total;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static PianoVisualSettings Settings(PilotFamily fam, int? valueIndex, bool isOff)
    {
        var s = OptionMatrixReview.BaseSettings();
        // Pins are scene preconditions, not a second family: impact.wave rides on the ring pass, so
        // ShowImpactRings has to be on before the choice switch can take the picture anywhere.
        foreach (var pin in fam.Pins ?? []) Apply(s, new DrivenSetting(pin, EffectParameterKind.Boolean, 1, ""));
        if (fam.SwitchKind == EffectParameterKind.Boolean)
            Apply(s, new DrivenSetting(fam.SwitchName, EffectParameterKind.Boolean, isOff ? 0 : 1, ""));
        else
            Apply(s, new DrivenSetting(fam.SwitchName, EffectParameterKind.Choice, 0,
                isOff ? fam.SwitchOffValue : fam.SwitchOnValue));
        if (!isOff && valueIndex.HasValue)
        {
            if (fam.ValueKind == EffectParameterKind.Choice)
                Apply(s, new DrivenSetting(fam.ValueName, EffectParameterKind.Choice, 0, fam.Choices[valueIndex.Value]));
            else
                Apply(s, new DrivenSetting(fam.ValueName, fam.ValueKind, fam.Numbers[valueIndex.Value], ""));
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
            case "ShowLightBeams": settings.ShowLightBeams = setting.Number >= 0.5; break;
            case "BeamIntensity": settings.BeamIntensity = setting.Number; break;
            case "HoldBar": settings.HoldBar = setting.Number >= 0.5; break;
            case "HoldBarIntensity": settings.HoldBarIntensity = setting.Number; break;
            case "HoldBreath": settings.HoldBreath = setting.Number >= 0.5; break;
            case "HoldBreathRate": settings.HoldBreathRate = setting.Number; break;
            case "HoldVibration": settings.HoldVibration = setting.Number >= 0.5; break;
            case "HoldVibrationAmount": settings.HoldVibrationAmount = setting.Number; break;
            case "HoldColorCycle": settings.HoldColorCycle = setting.Number >= 0.5; break;
            case "HoldColorCycleSpeed": settings.HoldColorCycleSpeed = setting.Number; break;
            case "HoldElectricArc": settings.HoldElectricArc = setting.Number >= 0.5; break;
            case "HoldArcIntensity": settings.HoldArcIntensity = setting.Number; break;
            case "FallingPulse": settings.FallingPulse = setting.Number >= 0.5; break;
            case "FallingPulseRate": settings.FallingPulseRate = setting.Number; break;
            case "ShowImpactFlash": settings.ShowImpactFlash = setting.Number >= 0.5; break;
            case "ImpactFlashIntensity": settings.ImpactFlashIntensity = setting.Number; break;
            case "FallingTrail": settings.FallingTrail = setting.Choice; break;
            case "FallingTrailLength": settings.FallingTrailLength = setting.Number; break;
            case "ImpactBurst": settings.ImpactBurst = setting.Choice; break;
            case "ShowImpactRings": settings.ShowImpactRings = setting.Number >= 0.5; break;
            case "ImpactWave": settings.ImpactWave = setting.Choice; break;
            case "ImpactMorph": settings.ImpactMorph = setting.Choice; break;
            case "ReleaseEffect": settings.ReleaseEffect = setting.Choice; break;
            case "ImpactFlashStyle": settings.ImpactFlashStyle = setting.Choice; break;
            case "WispSpeed": settings.WispSpeed = setting.Number; break;
            case "WispTurbulence": settings.WispTurbulence = setting.Number; break;
            case "WispGlow": settings.WispGlow = setting.Number; break;
            case "FlameHeight": settings.FlameHeight = setting.Number; break;
            case "ParticleSpeed": settings.ParticleSpeed = setting.Number; break;
            case "ParticleSize": settings.ParticleSize = setting.Number; break;
            case "ParticleGlow": settings.ParticleGlow = setting.Number; break;
            case "ParticleLife": settings.ParticleLife = setting.Number; break;
            case "FallingTrailIntensity": settings.FallingTrailIntensity = setting.Number; break;
            case "ImpactWaveIntensity": settings.ImpactWaveIntensity = setting.Number; break;
            case "ImpactMorphIntensity": settings.ImpactMorphIntensity = setting.Number; break;
            case "ReleaseIntensity": settings.ReleaseIntensity = setting.Number; break;
            default: throw new InvalidDataException($"pilot cannot drive {setting.Name}");
        }
    }
    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, PilotFamily fam, string label)
    {
        var (pixels, demand) = RenderAr(renderer, pose, camera, settings, fam.Stage);
        var quads = Probe(renderer, fam.Id);
        // A family can own more than one column (one per declared setting), so the folder has to
        // carry the setting too or the second column would overwrite the first column's pictures.
        var relative = Path.Combine("ar-value-pilot", OptionMatrixPlan.Slug($"{fam.Id}-{fam.ValueName}"), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{fam.Id}/{label} pilot AR");
        return new Shot { File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels), Quads = quads, Demand = demand, Pixels = pixels };
    }
    private static (int Frames, double Seconds) Timing(Stage stage) => stage switch
    {
        Stage.Held => (12, 1.0 / 30),
        Stage.Impact => (12, 1.0 / 60),
        Stage.Approach => (30, 1.0 / 60),
        // Same as N17: a finger down for 0.2 s, lifted at frame 12, captured 0.2 s later while the
        // release droplets are still in the air (Float Up lives 0.7-1.2 s).
        Stage.Release => (24, 1.0 / 60),
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
            Stage.Release => new HashSet<int> { ReleasePitch },
            _ => NoPressed,
        };
        void BeforeFrame(int frame)
        {
            // The release scene is the only one whose finger moves; a note-off event, not an empty
            // pressed set, is what spawns the release effect.
            var held = stage == Stage.Release && frame >= ReleaseLiftFrame ? NoPressed : pressed;
            feed.SetDeterministicState(notes, (frame + 1) * dt, playing, held);
            feed.SetDeterministicBeatPulse(0);
            if (stage == Stage.Release)
            {
                if (frame == 0) feed.LiveNote(ReleasePitch, true, .9);
                if (frame == ReleaseLiftFrame) feed.LiveNote(ReleasePitch, false, .9);
                return;
            }
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
    /// <summary>
    /// What each column did <em>not</em> shoot, read from the same contract catalogue the product uses.
    /// A percentage the column never moves is unshot; a boolean or another choice is pinned to the
    /// neutral look (or to a declared pin) so the scene is valid. Without this block a reader could
    /// mistake one driven setting for the whole family — the exact over-claim §11.3/§5 forbids.
    /// </summary>
    private sealed record CoverageRow(string Family, string Column, string[] Declared, string[] Driven,
        string[] Pinned, string[] Unshot, string PinnedTo);

    private static CoverageRow[] Coverage()
    {
        var all = EffectContractCatalog.AllFamilies;
        return Driven.Select(fam =>
        {
            var contract = all.Single(candidate => candidate.Id == fam.Id);
            var driven = new HashSet<string>(StringComparer.Ordinal) { fam.SwitchName, fam.ValueName };
            var parameters = contract.Parameters.Select(parameter => parameter.SettingName).ToArray();
            var unknown = driven.Where(name => !parameters.Contains(name)).ToArray();
            if (unknown.Length > 0)
                throw new InvalidDataException($"{fam.Id}/{fam.ValueName} drives {string.Join(", ", unknown)} "
                    + "which the effect contract does not declare");
            return new CoverageRow(fam.Id, fam.ValueName, parameters, driven.ToArray(),
                parameters.Where(name => !driven.Contains(name)
                        && contract.Parameters.Any(p => p.SettingName == name && p.Kind != EffectParameterKind.Percentage))
                    .Concat(fam.Pins ?? Array.Empty<string>()).Distinct().ToArray(),
                parameters.Where(name => !driven.Contains(name)
                        && contract.Parameters.Any(p => p.SettingName == name && p.Kind == EffectParameterKind.Percentage))
                    .ToArray(),
                "OptionMatrixReview.BaseSettings() neutral look, plus the column's declared pins");
        }).ToArray();
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<SheetReport> sheets, int total, string[] failures)
    {
        File.WriteAllText(Path.Combine(root, "ar-value-pilot", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 2, status = "value-pilot-3-points-not-claimed", claim = "each driven setting at 3 values vs off, same scene/timing/seed; one setting per column",
            notClaimed = "art approval, every value, real camera, binding change, every setting of every family",
            renderer = "GPU-WARP", adapter, width = Width, height = Height, seed = OptionMatrixPlan.Seed, pose = "ar-oblique",
            coverage = Coverage().Select(c => new { c.Family, c.Column, c.Declared, c.Driven, c.Pinned, c.Unshot, c.PinnedTo }).ToArray(),
            gateFailures = failures,
            sheets = sheets.Select(r => new { r.File, r.Sha, r.Columns, r.Rows, cells = r.Cells.Select(c => new { c.Family, scene = c.Stage.ToString(), c.Parameter, c.Row, c.Value, c.Choice, c.File, c.PixelSha256, c.ArChanged, c.ArLit, c.ArPainted, c.Quads, c.Demand, Capped = c.Demand > c.Quads, c.Reading }) })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<SheetReport> sheets, string[] failures)
    {
        static string E(string v) => System.Net.WebUtility.HtmlEncode(v);
        var b = new StringBuilder();
        b.Append("<!doctype html><meta charset=utf-8><title>Value Pilot</title><style>body{font:14px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px;padding:16px}table{border-collapse:collapse}th,td{border:1px solid #263243;padding:4px 8px}</style><h1>AR value pilot — 3 điểm / setting</h1><p>Same pose/camera/seed, WARP, 4 cột off→low→mid→far. Máy: " + E(adapter) + "</p>");
        foreach (var r in sheets) { b.Append($"<h2>{E(r.File)}</h2><table><tr><th>Family</th><th>Row</th><th>Value</th><th>AR px vs off</th><th>Quads</th><th>Demand/Quads</th></tr>"); foreach (var c in r.Cells) b.Append($"<tr><td>{E(c.Family)}</td><td>{E(c.Row)}</td><td>{E(c.Choice.Length > 0 ? c.Choice : c.Value.ToString("0"))}</td><td>{c.ArChanged:N0}</td><td>{c.Quads:N0}</td><td>{c.Demand:N0}/{c.Quads:N0}</td></tr>"); b.Append("</table>"); b.Append($"<img src={E(Path.GetFileName(r.File))} style=max-width:100%><br>"); }
        b.Append("<h2>Coverage: what each column did not shoot</h2><p>One column drives one declared setting; "
            + "the rest are pinned to the neutral look or named as unshot. Read from EffectContractCatalog, "
            + "not copied here, so a setting added to the contract cannot be silently dropped.</p>"
            + "<table><tr><th>Family</th><th>Column</th><th>Driven</th><th>Pinned</th><th>Unshot</th></tr>");
        foreach (var c in Coverage())
            b.Append($"<tr><td>{E(c.Family)}</td><td>{E(c.Column)}</td>"
                + $"<td>{E(string.Join(", ", c.Driven))}</td><td>{E(string.Join(", ", c.Pinned))}</td>"
                + $"<td>{E(string.Join(", ", c.Unshot))}</td></tr>");
        b.Append("</table>");
        if (failures.Length > 0)
        {
            b.Append("<h2>Gate failures (this run did not pass)</h2><ul>");
            foreach (var f in failures) b.Append($"<li>{E(f)}</li>");
            b.Append("</ul>");
        }
        File.WriteAllText(Path.Combine(root, "ar-value-pilot", "index.html"), b.ToString());
    }
}
