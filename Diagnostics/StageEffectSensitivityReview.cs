using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N17S: the same two particle families that the AR overlay reports as capped, shot on the Live Stage
/// renderer where the 192-quad ceiling does not exist.
///
/// <para>
/// <see cref="ArEffectFamilyReview"/> shows two columns whose far row is the AR layer's own cap:
/// 480 alive / 192 drawn for <c>particles.wisps</c> and 264 / 192 for <c>particles.sparks</c>.
/// The numbers are honest — the page names the cap — but they leave the reviewer's real question
/// unanswered: does moving <c>WispAmount</c> 45 → 150 actually change anything the user sees?
/// On the overlay the answer is "the cap", on the stage it is not.
/// </para>
/// <para>
/// This page keeps the same pose of the question — same driven settings, same scene and timing,
/// same seed, same one-family-per-column isolation — and changes only the renderer that draws it:
/// the <c>GpuStageRenderer</c> stage path instead of the calibrated AR overlay. Stage sprites are
/// instanced and capped at <c>EffectBudget.GpuParticleCap = 24000</c>, so both ends of the
/// <c>WispAmount</c> range and both ends of <c>ParticleAmount</c> sit far below any ceiling.
/// A non-zero delta in this page is the setting reaching the picture without a cap in the way.
/// </para>
/// <para>
/// It is not a second claim on the AR binding and it does not change a default. It is the other
/// half of the answer the AR page could not give: the effect underneath is not dead, it is just
/// capped where it was first measured.
/// </para>
/// </summary>
internal static class StageEffectSensitivityReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    private const int MinimumChangedPixels = 12;
    private const int MinimumOverlayPixels = 500;
    private const int Columns = 3;
    private const int TileWidth = Width, TileHeight = Height;

    private static readonly int[] HeldKeys = [48, 72];
    private const int BurstPitch = 60, ApproachPitch = 60, ReleasePitch = 60;
    private const int ReleaseLiftFrame = 12;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();

    private enum Stage { Held, Impact }

    private static readonly (string Id, Stage Stage)[] Driven =
    [
        ("particles.wisps", Stage.Held),
        ("particles.flames", Stage.Held),
        ("particles.sparks", Stage.Impact),
    ];

    private static readonly IReadOnlyDictionary<string, string> OffValues = new Dictionary<string, string>
    {
        ["ImpactWave"] = "None",
        ["ImpactMorph"] = "None",
        ["ReleaseEffect"] = "Fade",
        ["FallingTrail"] = "None",
    };

    private static readonly IReadOnlySet<string> PinnedOn = new HashSet<string> { "ShowImpactRings" };

    private sealed record DrivenSetting(string Name, EffectParameterKind Kind, double Number, string Choice)
    {
        internal string Label => Kind switch
        {
            EffectParameterKind.Choice => Choice,
            EffectParameterKind.Boolean => Number >= .5 ? "on" : "off",
            _ => Number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        };
        internal static DrivenSetting Switch(string name, bool on) => new(name, EffectParameterKind.Boolean, on ? 1 : 0, "");
    }

    private sealed record Family(string Id, Stage Stage, DrivenSetting On, DrivenSetting Off, DrivenSetting Far,
        string OffLabel, IReadOnlyList<DrivenSetting> Pinned, IReadOnlyList<string> Unshot)
    {
        internal string Sheet => $"stage-effect/sheet-{this.Stage.ToString().ToLowerInvariant()}.png";
    }

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
        string File, string PixelSha256, int StageChanged, int StageLit, int Quads, int Demand,
        string Reading, string Note);

    private sealed record SheetReport(string File, string Sha, int Columns, int Rows, IReadOnlyList<Cell> Cells);

    internal static string Summary { get; private set; } = "";

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The stage effect review output directory is empty.", nameof(output));
        var families = BuildFamilies();
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The stage sensitivity review requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var reports = new List<SheetReport>(2);
            var shots = new List<Shot>(families.Length * Columns);
            foreach (var group in families.GroupBy(family => family.Stage))
            {
                var page = group.ToArray();
                var captions = page.SelectMany(family => new[]
                    {
                        $"{family.Id} on", $"{family.On.Name} {family.OffLabel}", $"{family.Far.Name} {family.Far.Label}"
                    }).ToArray();
                var sheet = new ReviewSheet.Canvas(page.Length * Columns, TileWidth, TileHeight, Columns,
                    $"N17S STAGE {group.Key.ToString().ToUpperInvariant()}", ReviewSheet.CaptionScaleFor(captions, TileWidth));
                var cells = new List<Cell>(page.Length * Columns);
                for (var column = 0; column < page.Length; column++)
                {
                    var family = page[column];
                    var rest = CaptureStage(root, renderer, Settings(family, null), family, "rest");
                    if (rest.Lit < MinimumOverlayPixels)
                        throw new InvalidDataException(
                            $"{family.Id}: its own stage frame has {rest.Lit} lit pixels (floor {MinimumOverlayPixels}); a picture this empty cannot show that a setting moved anything.");
                    if (rest.Quads <= 0)
                        throw new InvalidDataException(
                            $"{family.Id} drew no stage sprites in the {family.Stage} scene, so its three captures would all measure a family that never ran.");
                    shots.Add(rest);
                    sheet.Blit(column * Columns, captions[column * Columns], rest.Pixels!, Width, Height, premultiplied: false);
                    cells.Add(new Cell(family.Id, family.Stage, family.Id, "rest", 0, "", rest.File, rest.PixelSha256, 0,
                        rest.Lit, rest.Quads, rest.Demand, "Reference", "The family at the product's own values, on the stage renderer."));

                    var off = CaptureStage(root, renderer, Settings(family, family.Off), family, "off");
                    shots.Add(off);
                    sheet.Blit(column * Columns + 1, captions[column * Columns + 1], off.Pixels!, Width, Height, premultiplied: false);
                    var footprint = OptionMatrixPlan.ChangedPixels(off.Pixels!, rest.Pixels!);
                    if (footprint < MinimumChangedPixels)
                        throw new InvalidDataException(
                            $"{family.Id}: taking the family away changes {footprint} pixels (floor {MinimumChangedPixels}) in the {family.Stage} stage scene, so this column would measure a family the stage cannot see.");
                    cells.Add(new Cell(family.Id, family.Stage, family.On.Name, "off", 0, "", off.File, off.PixelSha256,
                        footprint, off.Lit, off.Quads, off.Demand, Read(footprint),
                        "The switch off; every other frame of this column has the family on."));

                    var far = CaptureStage(root, renderer, Settings(family, family.Far), family, "far");
                    shots.Add(far);
                    sheet.Blit(column * Columns + 2, captions[column * Columns + 2], far.Pixels!, Width, Height, premultiplied: false);
                    var changed = OptionMatrixPlan.ChangedPixels(far.Pixels!, rest.Pixels!);
                    if (changed < MinimumChangedPixels)
                        throw new InvalidDataException(
                            $"{family.Id}: its far end changes {changed} pixels (floor {MinimumChangedPixels}) against its own product value in the {family.Stage} stage scene; the far row would repeat the first one.");
                    var againstOff = OptionMatrixPlan.ChangedPixels(far.Pixels!, off.Pixels!);
                    if (againstOff < MinimumChangedPixels)
                        throw new InvalidDataException(
                            $"{family.Id}: its far row changes {changed} pixels against rest but only {againstOff} pixels against off; it would be the off row under another caption.");
                    cells.Add(new Cell(family.Id, family.Stage, family.Far.Name, "far", family.Far.Number, family.Far.Choice,
                        far.File, far.PixelSha256, changed, far.Lit, far.Quads, far.Demand, Read(changed),
                        $"The contract's far end, against the product's own value; {againstOff} px against the family switched off."));

                    off.Pixels = null;
                    far.Pixels = null;
                    rest.Pixels = null;
                }

                var finished = sheet.Finish();
                var layout = sheet.Sheet;
                var sha = ReviewPng.Write(Path.Combine(root, page[0].Sheet), finished, layout.Width, layout.Height,
                    $"N17S STAGE {group.Key}: {page.Length * Columns} captures in {layout.Columns}×{layout.Rows} cells",
                    PixelFormats.Pbgra32);
                reports.Add(new SheetReport(page[0].Sheet, sha, layout.Columns, layout.Rows, cells));
            }

            VerifyShots(root, shots);
            Summary = string.Join("; ", families.Select(family =>
            {
                var cells = reports.SelectMany(report => report.Cells).Where(cell => cell.Family == family.Id).ToArray();
                var off = cells.Single(cell => cell.Row == "off");
                var far = cells.Single(cell => cell.Row == "far");
                var label = far.Choice.Length > 0 ? far.Choice : far.Value.ToString("0");
                var cap = far.Demand > far.Quads ? $" (capped: {far.Demand} alive / {far.Quads} drawn)" : "";
                return $"{family.Id} {off.StageChanged} px off / {far.Parameter} {label} {far.StageChanged} px{cap}";
            }));
            var total = reports.Sum(report => report.Cells.Count);
            WriteManifest(root, renderer.AdapterName, families, reports, total);
            WriteHtmlIndex(root, renderer.AdapterName, families, reports);
            return total;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static Family[] BuildFamilies()
    {
        var all = EffectContractCatalog.AllFamilies;
        var built = new Family[Driven.Length];
        for (var index = 0; index < Driven.Length; index++)
        {
            var (id, stage) = Driven[index];
            var contract = all.SingleOrDefault(candidate => candidate.Id == id)
                ?? throw new InvalidDataException($"The stage sensitivity review drives '{id}', which the contract does not declare.");
            var choiceSwitch = contract.Parameters.FirstOrDefault(parameter =>
                parameter.Kind == EffectParameterKind.Choice && OffValues.ContainsKey(parameter.SettingName));
            var toggle = choiceSwitch
                ?? contract.Parameters.FirstOrDefault(parameter => parameter.Kind == EffectParameterKind.Boolean)
                ?? throw new InvalidDataException(
                    $"{id} declares neither a boolean nor a choice with a declared off value, so the review has no way to take the family out of the picture for its middle row.");
            var others = contract.Parameters.Where(parameter => parameter.SettingName != toggle.SettingName).ToArray();
            var on = toggle.Kind == EffectParameterKind.Choice
                ? new DrivenSetting(toggle.SettingName, toggle.Kind, 0, OnValue(toggle.SettingName))
                : DrivenSetting.Switch(toggle.SettingName, true);
            var off = toggle.Kind == EffectParameterKind.Choice
                ? new DrivenSetting(toggle.SettingName, toggle.Kind, 0, OffValues[toggle.SettingName])
                : DrivenSetting.Switch(toggle.SettingName, false);
            var offLabel = toggle.Kind == EffectParameterKind.Choice ? OffValues[toggle.SettingName] : "off";

            IEnumerable<EffectParameterContract> candidates = others.Length == 1
                ? others
                : others.Where(parameter => parameter.Kind == EffectParameterKind.Choice)
                    .Concat(others.Where(parameter => parameter.Kind == EffectParameterKind.Percentage));
            EffectParameterContract? second = null;
            DrivenSetting? far = null;
            DrivenSetting[] pinned = [];
            foreach (var candidate in candidates)
            {
                DrivenSetting? candidateFar = candidate.Kind switch
                {
                    EffectParameterKind.Percentage => new DrivenSetting(candidate.SettingName, candidate.Kind,
                        candidate.Maximum ?? throw new InvalidDataException($"{id}: {candidate.SettingName} has no maximum to shoot."), ""),
                    EffectParameterKind.Choice => new DrivenSetting(candidate.SettingName, candidate.Kind, 0, FarChoice(candidate.SettingName)),
                    _ => null,
                };
                if (candidateFar is null) continue;
                var candidatePins = others.Where(parameter => parameter.SettingName != candidate.SettingName)
                    .Select(Pin).OfType<DrivenSetting>().ToArray();
                var reference = OptionMatrixReview.BaseSettings();
                foreach (var pin in candidatePins) Apply(reference, pin);
                Apply(reference, on);
                reference.Clamp();
                var differs = candidateFar.Kind == EffectParameterKind.Choice
                    ? ReadChoice(reference, candidateFar.Name) != candidateFar.Choice
                    : Math.Abs(Read(reference, candidateFar.Name) - candidateFar.Number) > .001;
                if (!differs) continue;
                second = candidate; far = candidateFar; pinned = candidatePins;
                break;
            }
            if (second is null || far is null)
                throw new InvalidDataException(
                    $"{id}: every setting after its switch already sits at the value the reference frame uses, so the far row would repeat the first one.");
            var unshot = others.Where(parameter => parameter.SettingName != second.SettingName
                    && parameter.Kind == EffectParameterKind.Percentage)
                .Select(parameter => parameter.SettingName).ToArray();
            built[index] = new Family(id, stage, on, off, far, offLabel, pinned, unshot);
        }
        return built;
    }

    private static IReadOnlyList<string> Choices(string setting) => setting switch
    {
        "ImpactBurst" => PianoVisualSettings.ImpactBursts,
        "ImpactWave" => PianoVisualSettings.ImpactWaves,
        "ImpactFlashStyle" => PianoVisualSettings.ImpactFlashStyles,
        "ImpactMorph" => PianoVisualSettings.ImpactMorphs,
        "ReleaseEffect" => PianoVisualSettings.ReleaseEffects,
        "FallingTrail" => PianoVisualSettings.FallingTrails,
        _ => throw new InvalidDataException($"The stage sensitivity review has no declared list for '{setting}'."),
    };

    private static DrivenSetting? Pin(EffectParameterContract parameter) => parameter.Kind switch
    {
        EffectParameterKind.Boolean => DrivenSetting.Switch(parameter.SettingName, PinnedOn.Contains(parameter.SettingName)),
        EffectParameterKind.Choice => new DrivenSetting(parameter.SettingName, parameter.Kind, 0, OnValue(parameter.SettingName)),
        _ => null,
    };

    private static string OnValue(string setting) => OffValues.TryGetValue(setting, out var off)
        ? Choices(setting).First(value => value != off)
        : Choices(setting)[0];

    private static string FarChoice(string setting)
    {
        var current = ReadChoice(OptionMatrixReview.BaseSettings(), setting);
        return Choices(setting).LastOrDefault(value => value != current)
            ?? throw new InvalidDataException($"The choice '{setting}' declares no value other than '{current}' to shoot.");
    }

    private static PianoVisualSettings Settings(Family family, DrivenSetting? row)
    {
        var settings = OptionMatrixReview.BaseSettings();
        foreach (var pinned in family.Pinned) Apply(settings, pinned);
        Apply(settings, family.On);
        if (row is not null) Apply(settings, row);
        settings.Clamp();
        if (row?.Name != family.On.Name && !IsOn(settings, family))
            throw new InvalidDataException($"The settings class turned {family.On.Name} off; the stage scene would have no family in it.");
        foreach (var pinned in family.Pinned) Verify(settings, pinned);
        if (row is not null) Verify(settings, row);
        return settings;
    }

    private static bool IsOn(PianoVisualSettings settings, Family family) =>
        family.On.Kind == EffectParameterKind.Choice
            ? ReadChoice(settings, family.On.Name) != OffValues[family.On.Name]
            : Read(settings, family.On.Name) >= .5;

    private static void Verify(PianoVisualSettings settings, DrivenSetting setting)
    {
        if (setting.Kind == EffectParameterKind.Choice)
        {
            var read = ReadChoice(settings, setting.Name);
            if (read != setting.Choice)
                throw new InvalidDataException(
                    $"The settings class turned {setting.Name} = '{setting.Choice}' into '{read}'; the capture would not be the value its caption names.");
            return;
        }
        var number = Read(settings, setting.Name);
        if (Math.Abs(number - setting.Number) > 0.001)
            throw new InvalidDataException(
                $"The settings class turned {setting.Name} = {setting.Number} into {number}; the capture would not be the value its caption names.");
    }

    private static Shot CaptureStage(string root, GpuStageRenderer renderer, PianoVisualSettings settings, Family family, string label)
    {
        var (pixels, demand) = RenderStage(renderer, settings, family.Stage);
        if (pixels.Length != Width * Height * 4)
            throw new InvalidDataException(
                $"Stage returned {pixels.Length} bytes for {family.Id}/{label}; expected {Width}×{Height} BGRA pixels.");
        var quads = Probe(renderer, family.Id);
        var relative = Path.Combine("stage-effect", OptionMatrixPlan.Slug(family.Id), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{family.Id}/{label} on stage");
        return new Shot
        {
            File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels),
            Quads = quads, Demand = demand, Pixels = pixels
        };
    }

    private static (int Frames, double Seconds) Timing(Stage stage) => stage switch
    {
        Stage.Held => (12, 1.0 / 30),
        Stage.Impact => (12, 1.0 / 60),
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static (byte[] Pixels, int Demand) RenderStage(GpuStageRenderer renderer, PianoVisualSettings settings, Stage stage)
    {
        var piano = new PianoStage { Width = Width, Height = Height };
        piano.SetVisualSettings(settings);
        piano.Measure(new Size(Width, Height));
        piano.Arrange(new Rect(0, 0, Width, Height));
        piano.UpdateLayout();
        var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
        feed.SetLook(GpuLook.From(settings, (pitch, track) => piano.AdjustColor(piano.NoteColor(pitch, track)),
            piano.KeyboardHeight / piano.ActualHeight, piano.AdjustColor, false));
        feed.SetStageHeight(Height);
        var (frames, dt) = Timing(stage);
        var pressed = stage switch
        {
            Stage.Held => new HashSet<int>(HeldKeys),
            _ => NoPressed,
        };
        void BeforeFrame(int frame)
        {
            var held = pressed;
            feed.SetDeterministicState([], (frame + 1) * dt, false, held);
            feed.SetDeterministicBeatPulse(0);
            if (frame != 0) return;
            switch (stage)
            {
                case Stage.Held:
                    foreach (var pitch in HeldKeys) feed.LiveNote(pitch, true, .9);
                    break;
                case Stage.Impact:
                    feed.Impact(BurstPitch, 1);
                    break;
            }
        }
        var demand = 0;
        renderer.ObserveParticleStateForReview = (simulation, _) => demand = Math.Max(demand, simulation.ArParticleCount);
        try
        {
            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _,
                BeforeFrame, OptionMatrixPlan.Seed);
            return (pixels, demand);
        }
        finally { renderer.ObserveParticleStateForReview = null; }
    }

    private static int Probe(GpuStageRenderer renderer, string family) => family switch
    {
        "particles.wisps" => renderer.StageSpriteCount,
        "particles.flames" => renderer.StageSpriteCount,
        "particles.sparks" => renderer.StageSpriteCount,
        _ => throw new InvalidDataException(
            $"The stage sensitivity review has no witness counter for '{family}'; teach Probe() about it before the page claims to have shot it.")
    };

    private static string Read(int changed) => changed >= MinimumChangedPixels ? "moves" : "still";

    private static void Apply(PianoVisualSettings settings, DrivenSetting setting)
    {
        switch (setting.Name)
        {
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
            case "ShowEmbers": settings.ShowEmbers = setting.Number >= 0.5; break;
            case "ParticleAmount": settings.ParticleAmount = setting.Number; break;
            case "ParticleSpeed": settings.ParticleSpeed = setting.Number; break;
            case "ParticleSize": settings.ParticleSize = setting.Number; break;
            case "ParticleGlow": settings.ParticleGlow = setting.Number; break;
            case "ParticleLife": settings.ParticleLife = setting.Number; break;
            case "ShowWisps": settings.ShowWisps = setting.Number >= 0.5; break;
            case "WispAmount": settings.WispAmount = setting.Number; break;
            case "WispSpeed": settings.WispSpeed = setting.Number; break;
            case "WispTurbulence": settings.WispTurbulence = setting.Number; break;
            case "WispGlow": settings.WispGlow = setting.Number; break;
            case "ShowFlame": settings.ShowFlame = setting.Number >= 0.5; break;
            case "FlameIntensity": settings.FlameIntensity = setting.Number; break;
            case "FlameHeight": settings.FlameHeight = setting.Number; break;
            case "ImpactBurst": settings.ImpactBurst = setting.Choice; break;
            case "ShowImpactRings": settings.ShowImpactRings = setting.Number >= 0.5; break;
            case "ImpactWave": settings.ImpactWave = setting.Choice; break;
            case "ImpactWaveIntensity": settings.ImpactWaveIntensity = setting.Number; break;
            case "ShowImpactFlash": settings.ShowImpactFlash = setting.Number >= 0.5; break;
            case "ImpactFlashStyle": settings.ImpactFlashStyle = setting.Choice; break;
            case "ImpactFlashIntensity": settings.ImpactFlashIntensity = setting.Number; break;
            case "ImpactMorph": settings.ImpactMorph = setting.Choice; break;
            case "ImpactMorphIntensity": settings.ImpactMorphIntensity = setting.Number; break;
            case "ReleaseEffect": settings.ReleaseEffect = setting.Choice; break;
            case "ReleaseIntensity": settings.ReleaseIntensity = setting.Number; break;
            case "ShowLightBeams": settings.ShowLightBeams = setting.Number >= 0.5; break;
            case "BeamIntensity": settings.BeamIntensity = setting.Number; break;
            case "FallingTrail": settings.FallingTrail = setting.Choice; break;
            case "FallingTrailIntensity": settings.FallingTrailIntensity = setting.Number; break;
            case "FallingTrailLength": settings.FallingTrailLength = setting.Number; break;
            case "FallingGhost": settings.FallingGhost = setting.Number >= 0.5; break;
            case "FallingGhostAmount": settings.FallingGhostAmount = setting.Number; break;
            case "NoteShimmer": settings.NoteShimmer = setting.Number >= 0.5; break;
            case "NoteShimmerAmount": settings.NoteShimmerAmount = setting.Number; break;
            default:
                throw new InvalidDataException(
                    $"The stage sensitivity review cannot drive '{setting.Name}'; teach Apply() and Read() about it first.");
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
        "FallingPulse" => settings.FallingPulse ? 1 : 0,
        "FallingPulseRate" => settings.FallingPulseRate,
        "ShowEmbers" => settings.ShowEmbers ? 1 : 0,
        "ParticleAmount" => settings.ParticleAmount,
        "ParticleSpeed" => settings.ParticleSpeed,
        "ParticleSize" => settings.ParticleSize,
        "ParticleGlow" => settings.ParticleGlow,
        "ParticleLife" => settings.ParticleLife,
        "ShowWisps" => settings.ShowWisps ? 1 : 0,
        "WispAmount" => settings.WispAmount,
        "WispSpeed" => settings.WispSpeed,
        "WispTurbulence" => settings.WispTurbulence,
        "WispGlow" => settings.WispGlow,
        "ShowFlame" => settings.ShowFlame ? 1 : 0,
        "FlameIntensity" => settings.FlameIntensity,
        "FlameHeight" => settings.FlameHeight,
        "ShowImpactRings" => settings.ShowImpactRings ? 1 : 0,
        "ImpactWaveIntensity" => settings.ImpactWaveIntensity,
        "ShowImpactFlash" => settings.ShowImpactFlash ? 1 : 0,
        "ImpactFlashIntensity" => settings.ImpactFlashIntensity,
        "ImpactMorphIntensity" => settings.ImpactMorphIntensity,
        "ReleaseIntensity" => settings.ReleaseIntensity,
        "ShowLightBeams" => settings.ShowLightBeams ? 1 : 0,
        "BeamIntensity" => settings.BeamIntensity,
        "FallingTrailIntensity" => settings.FallingTrailIntensity,
        "FallingTrailLength" => settings.FallingTrailLength,
        "FallingGhost" => settings.FallingGhost ? 1 : 0,
        "FallingGhostAmount" => settings.FallingGhostAmount,
        "NoteShimmer" => settings.NoteShimmer ? 1 : 0,
        "NoteShimmerAmount" => settings.NoteShimmerAmount,
        _ => throw new InvalidDataException($"The stage sensitivity review cannot read '{parameter}'."),
    };

    private static string ReadChoice(PianoVisualSettings settings, string parameter) => parameter switch
    {
        "ImpactBurst" => settings.ImpactBurst,
        "ImpactWave" => settings.ImpactWave,
        "ImpactFlashStyle" => settings.ImpactFlashStyle,
        "ImpactMorph" => settings.ImpactMorph,
        "ReleaseEffect" => settings.ReleaseEffect,
        "FallingTrail" => settings.FallingTrail,
        _ => throw new InvalidDataException($"The stage sensitivity review cannot read choice '{parameter}'."),
    };

    private static void VerifyShots(string root, IReadOnlyList<Shot> shots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shot in shots)
        {
            if (!seen.Add(shot.File))
                throw new InvalidDataException($"The stage sensitivity review wrote {shot.File} twice; a sheet would carry the same picture under two captions.");
            var path = Path.Combine(root, shot.File);
            if (!File.Exists(path))
                throw new InvalidDataException($"The stage sensitivity review lists {shot.File} but the file was not written.");
            var pixels = ReviewPng.Decode(Path.Combine(root, shot.File), Width, Height);
            var sha = Convert.ToHexString(SHA256.HashData(pixels));
            if (!string.Equals(sha, shot.PixelSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Hash mismatch for stage file {shot.File}: manifest {shot.PixelSha256} vs file {sha}.");
        }
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<Family> families,
        IReadOnlyList<SheetReport> sheets, int total)
    {
        File.WriteAllText(Path.Combine(root, "stage-effect", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1,
            status = "n17s-stage-particle-sensitivity-measured-not-claimed",
            claim = "the same particle families that are capped on the AR overlay are shot on the Live Stage renderer in the same scene and timing, to show the setting reaches the picture without the 192-quad cap",
            notClaimed = "art approval, a proposal to raise the AR cap, every value of every range, a real camera, hardware frames, a binding change or a default change",
            renderer = "GPU-WARP",
            adapter,
            width = Width, height = Height,
            seed = OptionMatrixPlan.Seed,
            scenes = families.GroupBy(family => family.Stage).Select(group => new
            {
                scene = group.Key.ToString(),
                frames = Timing(group.Key).Frames,
                fps = (int)Math.Round(1 / Timing(group.Key).Seconds),
                families = group.Select(family => family.Id),
                description = group.Key switch
                {
                    Stage.Held => "two keys two octaves apart (C3 and C5) held for the whole run: the same held particle families as the AR page's Held sheet, but drawn by the stage path",
                    Stage.Impact => "one strike on C4 at the hit line, with no note and no finger: the same spark stream as the AR page's Impact sheet, drawn by the stage path",
                    _ => throw new ArgumentOutOfRangeException(nameof(group)),
                },
            }),
            families = families.Select(family => new
            {
                family.Id, scene = family.Stage.ToString(), family.On.Name, far = family.Far.Name,
                farValue = family.Far.Kind == EffectParameterKind.Choice ? family.Far.Choice : family.Far.Label,
                onValue = family.On.Kind == EffectParameterKind.Choice ? family.On.Choice : "on",
                offValue = family.OffLabel,
                pinned = family.Pinned.Select(setting => new { setting = setting.Name, value = setting.Label }),
                unshot = family.Unshot,
            }),
            floorChangedPixels = MinimumChangedPixels,
            floorOverlayPixels = MinimumOverlayPixels,
            cells = total,
            sheets = sheets.Select(report => new
            {
                report.File, report.Sha, report.Columns, report.Rows,
                cells = report.Cells.Select(cell => new
                {
                    cell.Family, scene = cell.Stage.ToString(), cell.Parameter, cell.Row, Value = Math.Round(cell.Value, 3),
                    cell.Choice, cell.File, cell.PixelSha256, cell.StageChanged, cell.StageLit, cell.Quads,
                    cell.Demand, Capped = cell.Demand > cell.Quads, cell.Reading, cell.Note
                })
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<Family> families,
        IReadOnlyList<SheetReport> sheets)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N17S — Stage particle sensitivity</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 6px}p{line-height:1.55;color:#a9b6c7}table{border-collapse:collapse;margin:12px 0}")
            .Append("th,td{border:1px solid #263243;padding:5px 9px;text-align:left;font-variant-numeric:tabular-nums}")
            .Append("th{color:#c9d6e6;font-weight:600}td.y{color:#7df0b1}td.n{color:#ffd479}")
            .Append("img{max-width:100%;height:auto;border:1px solid #263243;margin-top:8px}</style>")
            .Append("<h1>Hiệu ứng hạt nhạy tới đâu khi không bị trần — Stage (N17S)</h1>")
            .Append($"<p>Cùng timing, cùng seed, cùng một-family-một-cột như trang AR N17; khác mỗi chỗ là renderer: stage thay vì overlay đã hiệu chuẩn. Máy đo: {Encode(adapter)}.</p>")
            .Append("<p>Cột AR in 85 px / 1 775 px nhưng gắn nhãn <code>capped</code> vì 480/192 và 264/192. Cột dưới đây chứng minh hiệu ứng phía sau trần đó vẫn sống: cùng thiết lập, không có trần 192, pixel đổi tự nói.</p>")
            .Append("<table><tr><th>Family</th><th>Cảnh</th><th>Công tắc (bật / tắt)</th><th>Thiết lập xa</th>")
            .Append("<th>Ghim ở cả ba hàng</th><th>Không chụp ở đợt này</th></tr>");
        foreach (var family in families)
            body.Append($"<tr><td>{Encode(family.Id)}</td><td>{Encode(family.Stage.ToString())}</td>")
                .Append($"<td>{Encode(family.On.Name)} {Encode(family.On.Label)} / {Encode(family.OffLabel)}</td>")
                .Append($"<td>{Encode(family.Far.Name)} = {Encode(family.Far.Label)}</td>")
                .Append($"<td>{Encode(family.Pinned.Count == 0 ? "—" : string.Join(", ", family.Pinned.Select(setting => $"{setting.Name} {setting.Label}")))}</td>")
                .Append($"<td>{Encode(family.Unshot.Count == 0 ? "—" : string.Join(", ", family.Unshot))}</td></tr>");
        body.Append("</table>");
        foreach (var report in sheets)
        {
            body.Append($"<h2>Tờ ảnh — {Encode(report.File)}</h2>")
                .Append("<table><tr><th>Family</th><th>Thiết lập</th><th>Hàng</th><th>Giá trị</th><th>Stage px</th><th>Sprites</th>")
                .Append("<th>Hạt sống / đã vẽ (stage)</th><th>Đọc</th></tr>");
            foreach (var cell in report.Cells)
            {
                var value = cell.Choice.Length > 0 ? cell.Choice : cell.Value.ToString("0.###");
                var cappedClass = cell.Demand > cell.Quads ? " class=\"n\"" : "";
                body.Append($"<tr><td>{Encode(cell.Family)}</td><td>{Encode(cell.Parameter)}</td><td>{Encode(cell.Row)}</td>")
                    .Append($"<td>{Encode(value)}</td>")
                    .Append($"<td class=\"{(cell.StageChanged >= MinimumChangedPixels ? "y" : "n")}\">{cell.StageChanged:N0} px</td>")
                    .Append($"<td>{cell.Quads:N0}</td>")
                    .Append($"<td{cappedClass}>{cell.Demand:N0} / {cell.Quads:N0}</td>")
                    .Append($"<td>{Encode(cell.Reading)}</td></tr>");
            }
            body.Append("</table>");
            body.Append("<p>Ba cột: family đang bật ở giá trị của sản phẩm, cùng family bị tắt, và thiết lập thứ hai bị đẩy tới đầu xa của khoảng contract khai báo — y hệt trang AR, chỉ đổi renderer.</p>")
                .Append($"<img src=\"{Encode(Path.GetFileName(report.File))}\" alt=\"N17S {Encode(report.File)}\">");
        }
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "stage-effect", "index.html"), body.ToString());
    }

    // -----------------------------------------------------------------------------------------
    // N17V-S: the Live Stage half of the value matrix. The AR page above measures the overlay
    // through its 384-quad cap; these three particle families are the ones whose far rows N17
    // reports as capped, so the same 3-point question ("does each slider step actually reach
    // the picture?") needs an answer on the renderer with no cap in the way. Everything here is
    // borrowed from the sensitivity review above: the stage feed, Apply(), Probe() and the
    // shot/hash verifier. Only the column shape changes: off -> low -> mid -> far.
    // -----------------------------------------------------------------------------------------
    private const int ValuePilotColumns = 4;
    private sealed record ValueColumn(string Id, Stage Stage, string Switch, string Setting, double[] Values);
    private static readonly ValueColumn[] ValuePilot =
    [
        new("particles.wisps", Stage.Held, "ShowWisps", "WispAmount", [15, 75, 150]),
        new("particles.wisps", Stage.Held, "ShowWisps", "WispSpeed", [100, 300, 600]),
        new("particles.flames", Stage.Held, "ShowFlame", "FlameIntensity", [20, 60, 100]),
        new("particles.flames", Stage.Held, "ShowFlame", "FlameHeight", [25, 60, 100]),
        new("particles.sparks", Stage.Impact, "ShowEmbers", "ParticleAmount", [20, 70, 120]),
        new("particles.sparks", Stage.Impact, "ShowEmbers", "ParticleSize", [2, 8, 16]),
    ];
    internal static string ValuePilotSummary { get; private set; } = "";

    internal static int RenderValuePilot(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The stage value pilot output directory is empty.", nameof(output));
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root = Path.GetFullPath(output);
            Directory.CreateDirectory(root);
            using var renderer = GpuStageRenderer.Create(forceWarp: true);
            if (!renderer.IsWarp)
                throw new InvalidOperationException($"The stage value pilot requires WARP, but Direct3D created '{renderer.AdapterName}'.");

            var failures = new List<string>();
            void Gate(ValueColumn column, string name, int changed)
            {
                if (changed < MinimumChangedPixels)
                    failures.Add($"{column.Id}/{column.Setting} {name} {changed} < {MinimumChangedPixels}");
            }
            var reports = new List<SheetReport>(2);
            var shots = new List<Shot>(ValuePilot.Length * ValuePilotColumns);
            foreach (var group in ValuePilot.GroupBy(column => column.Stage))
            {
                var page = group.ToArray();
                var captions = page.SelectMany(column => new[]
                {
                    $"{column.Id} off", $"{column.Id} {column.Setting} {column.Values[0]:0.##}",
                    $"{column.Id} {column.Setting} {column.Values[1]:0.##}", $"{column.Id} {column.Setting} {column.Values[2]:0.##}",
                }).ToArray();
                var sheet = new ReviewSheet.Canvas(page.Length * ValuePilotColumns, TileWidth, TileHeight, ValuePilotColumns,
                    $"N17V-S STAGE VALUE {group.Key.ToString().ToUpperInvariant()}", ReviewSheet.CaptionScaleFor(captions, TileWidth));
                var cells = new List<Cell>(page.Length * ValuePilotColumns);
                for (var col = 0; col < page.Length; col++)
                {
                    var column = page[col];
                    var frames = new Shot[ValuePilotColumns];
                    frames[0] = CaptureValue(root, renderer, ValueSettings(column, null, true), column, "off");
                    for (var index = 0; index < 3; index++)
                        frames[index + 1] = CaptureValue(root, renderer, ValueSettings(column, index, false), column,
                            index == 0 ? "low" : index == 1 ? "mid" : "far");
                    foreach (var frame in frames) shots.Add(frame);
                    var labels = new[] { "off", "low", "mid", "far" };
                    for (var row = 0; row < ValuePilotColumns; row++)
                        sheet.Blit(col * ValuePilotColumns + row, captions[col * ValuePilotColumns + row],
                            frames[row].Pixels!, Width, Height, premultiplied: false);

                    if (frames[1].Lit < MinimumOverlayPixels)
                        failures.Add($"{column.Id}/{column.Setting} low lit {frames[1].Lit} < {MinimumOverlayPixels}");
                    if (frames[1].Quads <= 0) failures.Add($"{column.Id}/{column.Setting} low drew no stage sprites");
                    var offVsLow = OptionMatrixPlan.ChangedPixels(frames[0].Pixels!, frames[1].Pixels!);
                    var lowVsMid = OptionMatrixPlan.ChangedPixels(frames[1].Pixels!, frames[2].Pixels!);
                    var offVsMid = OptionMatrixPlan.ChangedPixels(frames[0].Pixels!, frames[2].Pixels!);
                    var midVsFar = OptionMatrixPlan.ChangedPixels(frames[2].Pixels!, frames[3].Pixels!);
                    var offVsFar = OptionMatrixPlan.ChangedPixels(frames[0].Pixels!, frames[3].Pixels!);
                    Gate(column, "off↔low", offVsLow);
                    Gate(column, "low↔mid", lowVsMid);
                    Gate(column, "off↔mid", offVsMid);
                    Gate(column, "mid↔far", midVsFar);
                    Gate(column, "off↔far", offVsFar);

                    cells.Add(new Cell(column.Id, column.Stage, column.Switch, "off", 0, "", frames[0].File, frames[0].PixelSha256,
                        0, frames[0].Lit, frames[0].Quads, frames[0].Demand, "Reference", "Family off"));
                    var deltas = new[] { 0, offVsLow, offVsMid, offVsFar };
                    for (var row = 1; row < ValuePilotColumns; row++)
                        cells.Add(new Cell(column.Id, column.Stage, column.Setting, labels[row], column.Values[row - 1], "",
                            frames[row].File, frames[row].PixelSha256, deltas[row], frames[row].Lit, frames[row].Quads,
                            frames[row].Demand, Read(deltas[row]), $"{labels[row]} {column.Values[row - 1]:0.##} vs off"));
                    foreach (var frame in frames) frame.Pixels = null;
                }
                var finished = sheet.Finish();
                var layout = sheet.Sheet;
                var sheetPath = $"stage-value-pilot/sheet-{group.Key.ToString().ToLowerInvariant()}.png";
                var sha = ReviewPng.Write(Path.Combine(root, sheetPath), finished, layout.Width, layout.Height,
                    $"N17V-S STAGE VALUE {group.Key}: {page.Length * ValuePilotColumns} captures", PixelFormats.Pbgra32);
                reports.Add(new SheetReport(sheetPath, sha, layout.Columns, layout.Rows, cells));
            }
            VerifyShots(root, shots);
            ValuePilotSummary = string.Join("; ", ValuePilot.Select(column =>
            {
                var cs = reports.SelectMany(r => r.Cells)
                    .Where(c => c.Family == column.Id && c.Parameter == column.Setting).ToArray();
                return $"{column.Id} {column.Setting} {string.Join("/", column.Values.Select(v => v.ToString("0.##")))} "
                    + $"low {cs.First(c => c.Row == "low").StageChanged}→mid {cs.First(c => c.Row == "mid").StageChanged}"
                    + $"→far {cs.First(c => c.Row == "far").StageChanged} px vs off";
            }));
            var total = reports.Sum(r => r.Cells.Count);
            WriteValuePilotManifest(root, renderer.AdapterName, reports, total, failures.ToArray());
            WriteValuePilotHtml(root, renderer.AdapterName, reports, failures.ToArray());
            if (failures.Count > 0)
                throw new InvalidDataException($"{failures.Count} stage value gate(s) failed: " + string.Join(" | ", failures));
            return total;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static PianoVisualSettings ValueSettings(ValueColumn column, int? index, bool isOff)
    {
        var settings = OptionMatrixReview.BaseSettings();
        Apply(settings, new DrivenSetting(column.Switch, EffectParameterKind.Boolean, isOff ? 0 : 1, ""));
        if (index.HasValue)
            Apply(settings, new DrivenSetting(column.Setting, EffectParameterKind.Percentage, column.Values[index.Value], ""));
        settings.Clamp();
        return settings;
    }

    private static Shot CaptureValue(string root, GpuStageRenderer renderer, PianoVisualSettings settings, ValueColumn column, string label)
    {
        var (pixels, demand) = RenderStage(renderer, settings, column.Stage);
        var quads = Probe(renderer, column.Id);
        var relative = Path.Combine("stage-value-pilot", OptionMatrixPlan.Slug($"{column.Id}-{column.Setting}"), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{column.Id}/{column.Setting}/{label} on stage");
        return new Shot
        {
            File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels),
            Quads = quads, Demand = demand, Pixels = pixels
        };
    }

    private static void WriteValuePilotManifest(string root, string adapter, IReadOnlyList<SheetReport> sheets, int total, string[] failures)
    {
        File.WriteAllText(Path.Combine(root, "stage-value-pilot", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1, status = "stage-value-pilot-3-points-not-claimed",
            claim = "each driven percentage at 3 values vs off on the Live Stage renderer, without the AR 384-quad cap",
            notClaimed = "art approval, every value, real camera, hardware frames, raising the AR cap, binding or default change",
            renderer = "GPU-WARP", adapter, width = Width, height = Height, seed = OptionMatrixPlan.Seed,
            stageCap = 24000, arCap = 384, gateFailures = failures,
            columns = ValuePilot.Select(c => new { c.Id, scene = c.Stage.ToString(), c.Switch, c.Setting, c.Values }),
            cells = total,
            sheets = sheets.Select(r => new { r.File, r.Sha, r.Columns, r.Rows, cells = r.Cells.Select(c => new
            {
                c.Family, scene = c.Stage.ToString(), c.Parameter, c.Row, c.Value, c.File, c.PixelSha256,
                c.StageChanged, c.StageLit, c.Quads, c.Demand, Capped = c.Demand > c.Quads, c.Reading
            }) })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteValuePilotHtml(string root, string adapter, IReadOnlyList<SheetReport> sheets, string[] failures)
    {
        static string E(string v) => System.Net.WebUtility.HtmlEncode(v);
        var b = new StringBuilder();
        b.Append("<!doctype html><meta charset=utf-8><title>Stage Value Pilot</title><style>body{font:14px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px;padding:16px}table{border-collapse:collapse}th,td{border:1px solid #263243;padding:4px 8px}</style>"
            + $"<h1>N17V-S stage value pilot — 3 điểm / setting</h1><p>Live Stage renderer, không có trần 384-quad của AR. Máy: {E(adapter)}</p>");
        foreach (var r in sheets)
        {
            b.Append($"<h2>{E(r.File)}</h2><table><tr><th>Family</th><th>Setting</th><th>Row</th><th>Value</th><th>px vs off</th><th>Quads</th><th>Demand/Quads</th></tr>");
            foreach (var c in r.Cells)
                b.Append($"<tr><td>{E(c.Family)}</td><td>{E(c.Parameter)}</td><td>{E(c.Row)}</td><td>{c.Value:0.##}</td>"
                    + $"<td>{c.StageChanged:N0}</td><td>{c.Quads:N0}</td><td>{c.Demand:N0}/{c.Quads:N0}</td></tr>");
            b.Append("</table>").Append($"<img src={E(Path.GetFileName(r.File))} style=max-width:100%><br>");
        }
        if (failures.Length > 0)
        {
            b.Append("<h2>Gate failures (this run did not pass)</h2><ul>");
            foreach (var f in failures) b.Append($"<li>{E(f)}</li>");
            b.Append("</ul>");
        }
        File.WriteAllText(Path.Combine(root, "stage-value-pilot", "index.html"), b.ToString());
    }

}
