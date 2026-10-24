using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Full lifecycle matrix for §11.3.2 — extends N17L pilot from 1 family to all 17.
/// N17 shoots each family at one time slice (Held 0.4s, Impact 0.20s). To flip
/// Unverified→Applied we need birth/mid/decay per family. This is the full matrix:
/// Hold/Approach 2 milestones, Impact 3, Release 2. Each milestone is off vs on
/// at same pose/camera/seed, WARP ar-oblique, 12px gate for alive, <12 for expired.
/// </summary>
internal static class ArLifecycleReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    private const int MinimumChangedPixels = 12;
    private const int MinimumOverlayPixels = 500;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();
    private const int BurstPitch = 60;
    private static readonly int[] HeldKeys = [48, 72];
    private const int ApproachPitch = 60, ReleasePitch = 60;
    private const int ReleaseLiftFrame = 12;

    private enum Stage { Held, Approach, Impact, Release }

    private sealed record Milestone(string Label, int Frames, double Seconds, double Dt);

    private static readonly Dictionary<Stage, Milestone[]> Milestones = new()
    {
        [Stage.Held] = new[]
        {
            new Milestone("H0 0.40s", 12, 12.0/30, 1.0/30),
            new Milestone("H1 1.00s", 30, 30.0/30, 1.0/30),
        },
        [Stage.Approach] = new[]
        {
            new Milestone("A0 0.25s", 15, 15.0/60, 1.0/60),
            new Milestone("A1 0.50s", 30, 30.0/60, 1.0/60),
        },
        [Stage.Impact] = new[]
        {
            new Milestone("T0 0.05s", 3, 3.0/60, 1.0/60),
            new Milestone("T1 0.20s", 12, 12.0/60, 1.0/60),
            new Milestone("T2 0.35s", 21, 21.0/60, 1.0/60),
        },
        [Stage.Release] = new[]
        {
            new Milestone("R0 0.40s", 24, 24.0/60, 1.0/60),
            new Milestone("R1 0.60s", 36, 36.0/60, 1.0/60),
        },
    };

    private static readonly (string Id, Stage Stage)[] Driven =
    [
        ("hold.bar", Stage.Held),
        ("hold.breath", Stage.Held),
        ("hold.vibration", Stage.Held),
        ("hold.color-cycle", Stage.Held),
        ("hold.arc", Stage.Held),
        ("particles.wisps", Stage.Held),
        ("particles.flames", Stage.Held),
        ("falling.trail", Stage.Approach),
        ("falling.shimmer", Stage.Approach),
        ("falling.pulse", Stage.Approach),
        ("impact.burst", Stage.Impact),
        ("particles.sparks", Stage.Impact),
        ("impact.wave", Stage.Impact),
        ("impact.flash", Stage.Impact),
        ("impact.morph", Stage.Impact),
        ("release.fx", Stage.Release),
        ("atmosphere.beams", Stage.Held),
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
    private sealed record Family(string Id, Stage Stage, DrivenSetting On, DrivenSetting Off, DrivenSetting Far, string OffLabel, IReadOnlyList<DrivenSetting> Pinned, IReadOnlyList<string> Unshot);
    private sealed class Shot
    {
        internal required string File { get; init; }
        internal required string PixelSha256 { get; init; }
        internal required int Lit { get; init; }
        internal required int PaintedCount { get; init; }
        internal required int Quads { get; init; }
        internal required int Demand { get; init; }
        internal byte[]? Pixels { get; set; }
    }
    private sealed record Cell(string Family, Stage Stage, string Milestone, string Parameter, string Row, double Seconds, string Choice, string File, string PixelSha256, int ArChanged, int ArLit, int ArPainted, int Quads, int Demand, string Reading, string Note);
    private sealed record SheetReport(string File, string Sha, int Columns, int Rows, IReadOnlyList<Cell> Cells);

    internal static string Summary { get; private set; } = "";

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("empty", nameof(output));
        var families = BuildFamilies();
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

            var reports = new List<SheetReport>();
            var shots = new List<Shot>();
            var milestoneSummaries = new List<string>();

            foreach (var stage in new[] { Stage.Held, Stage.Approach, Stage.Impact, Stage.Release })
            {
                var stageMilestones = Milestones[stage];
                var stageFamilies = families.Where(f => f.Stage == stage).ToArray();
                if (stageFamilies.Length == 0) continue;

                foreach (var milestone in stageMilestones)
                {
                    var pageFamilies = stageFamilies;
                    var captions = pageFamilies.SelectMany(f => new[] { $"{f.Id} {milestone.Label} off", $"{f.Id} {milestone.Label} on" }).ToArray();
                    // One sheet per milestone: top row off for each family, bottom row on — like pilot
                    var total = pageFamilies.Length * 2;
                    var cols = pageFamilies.Length;
                    var sheet = new ReviewSheet.Canvas(total, Width, Height, cols,
                        $"LIFECYCLE {stage.ToString().ToUpperInvariant()} {milestone.Label}", ReviewSheet.CaptionScaleFor(captions, Width));
                    var cells = new List<Cell>(total);

                    for (var col = 0; col < pageFamilies.Length; col++)
                    {
                        var family = pageFamilies[col];
                        var off = CaptureAr(root, renderer, pose, camera, Settings(family, family.Off), family, milestone, "off");
                        var on = CaptureAr(root, renderer, pose, camera, Settings(family, null), family, milestone, "on");
                        var isLastMilestone = milestone == stageMilestones.Last();
                        var isImpactT2 = stage == Stage.Impact && milestone.Label == "T2 0.35s";

                        // For non-expired milestones, require alive
                        if (!isImpactT2 || family.Id != "impact.flash")
                        {
                            if (on.PaintedCount < MinimumOverlayPixels && !isLastMilestone)
                                throw new InvalidDataException($"{family.Id} {milestone.Label} on painted {on.PaintedCount} < {MinimumOverlayPixels}");
                            if (on.Quads <= 0 && !isLastMilestone && family.Id != "falling.pulse")
                            {
                                // pulse has no own quads, witness is ArShapeCount; allow 0 quads for pulse but check changed
                            }
                        }

                        var changed = OptionMatrixPlan.ChangedPixels(off.Pixels!, on.Pixels!);
                        var isExpiredExpected = isImpactT2 && family.Id == "impact.flash";
                        if (isExpiredExpected)
                        {
                            if (changed >= MinimumChangedPixels)
                                throw new InvalidDataException($"{family.Id} {milestone.Label} expected expired <{MinimumChangedPixels} but got {changed}");
                        }
                        else
                        {
                            if (changed < MinimumChangedPixels)
                                throw new InvalidDataException($"{family.Id} {milestone.Label} off↔on {changed} < {MinimumChangedPixels}");
                            if (on.Quads <= 0 && family.Id != "falling.pulse")
                                throw new InvalidDataException($"{family.Id} {milestone.Label} on drew no quads");
                        }

                        shots.Add(off); shots.Add(on);
                        var idxOff = col; var idxOn = col + pageFamilies.Length;
                        sheet.Blit(idxOff, captions[col*2], off.Pixels!, Width, Height, premultiplied: false);
                        sheet.Blit(idxOn, captions[col*2+1], on.Pixels!, Width, Height, premultiplied: false);

                        cells.Add(new Cell(family.Id, stage, milestone.Label, "Show" + family.Id.Split('.')[1], "off", milestone.Seconds, "", off.File, off.PixelSha256, 0, off.Lit, off.PaintedCount, off.Quads, off.Demand, "Reference", "off"));
                        cells.Add(new Cell(family.Id, stage, milestone.Label, family.On.Name, "on", milestone.Seconds, family.On.Choice, on.File, on.PixelSha256, changed, on.Lit, on.PaintedCount, on.Quads, on.Demand, isExpiredExpected ? "expired" : "moves", isExpiredExpected ? "expired (flash 0.26s)" : $"{family.Id} on vs off"));

                        off.Pixels = null; on.Pixels = null;
                    }

                    var finished = sheet.Finish();
                    var layout = sheet.Sheet;
                    var sheetPath = $"ar-lifecycle/sheet-{stage.ToString().ToLowerInvariant()}-{milestone.Frames}f.png";
                    var sha = ReviewPng.Write(Path.Combine(root, sheetPath), finished, layout.Width, layout.Height,
                        $"LIFECYCLE {stage} {milestone.Label} {stageFamilies.Length*2} captures", PixelFormats.Pbgra32);
                    reports.Add(new SheetReport(sheetPath, sha, layout.Columns, layout.Rows, cells));
                    milestoneSummaries.Add($"{stage} {milestone.Label}: {cells.Count/2} families {cells.Where(c=>c.Row=="on").Sum(c=>c.ArChanged)/Math.Max(1,stageFamilies.Length)} avg px");
                }
            }

            VerifyShots(root, shots);
            Summary = string.Join("; ", milestoneSummaries);
            WriteManifest(root, renderer.AdapterName, families, reports, shots.Count);
            WriteHtmlIndex(root, renderer.AdapterName, reports);
            return shots.Count;
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
            var contract = all.SingleOrDefault(c => c.Id == id) ?? throw new InvalidDataException($"The lifecycle review drives '{id}', not in contract.");
            var choiceSwitch = contract.Parameters.FirstOrDefault(p => p.Kind == EffectParameterKind.Choice && OffValues.ContainsKey(p.SettingName));
            var toggle = choiceSwitch ?? contract.Parameters.FirstOrDefault(p => p.Kind == EffectParameterKind.Boolean) ?? throw new InvalidDataException($"{id} no switch");
            var others = contract.Parameters.Where(p => p.SettingName != toggle.SettingName).ToArray();
            var on = toggle.Kind == EffectParameterKind.Choice ? new DrivenSetting(toggle.SettingName, toggle.Kind, 0, OnValue(toggle.SettingName)) : DrivenSetting.Switch(toggle.SettingName, true);
            var off = toggle.Kind == EffectParameterKind.Choice ? new DrivenSetting(toggle.SettingName, toggle.Kind, 0, OffValues[toggle.SettingName]) : DrivenSetting.Switch(toggle.SettingName, false);
            var offLabel = toggle.Kind == EffectParameterKind.Choice ? OffValues[toggle.SettingName] : "off";
            IEnumerable<EffectParameterContract> candidates = others.Length == 1 ? others : others.Where(p => p.Kind == EffectParameterKind.Choice).Concat(others.Where(p => p.Kind == EffectParameterKind.Percentage));
            EffectParameterContract? second = null; DrivenSetting? far = null; DrivenSetting[] pinned = [];
            foreach (var candidate in candidates)
            {
                DrivenSetting? candidateFar = candidate.Kind switch
                {
                    EffectParameterKind.Percentage => new DrivenSetting(candidate.SettingName, candidate.Kind, candidate.Maximum ?? throw new InvalidDataException($"{id}: {candidate.SettingName} no max"), ""),
                    EffectParameterKind.Choice => new DrivenSetting(candidate.SettingName, candidate.Kind, 0, FarChoice(candidate.SettingName)),
                    _ => null,
                };
                if (candidateFar is null) continue;
                var candidatePins = others.Where(p => p.SettingName != candidate.SettingName).Select(Pin).OfType<DrivenSetting>().ToArray();
                var reference = OptionMatrixReview.BaseSettings();
                foreach (var pin in candidatePins) Apply(reference, pin);
                Apply(reference, on);
                reference.Clamp();
                var differs = candidateFar.Kind == EffectParameterKind.Choice ? ReadChoice(reference, candidateFar.Name) != candidateFar.Choice : Math.Abs(Read(reference, candidateFar.Name) - candidateFar.Number) > .001;
                if (!differs) continue;
                second = candidate; far = candidateFar; pinned = candidatePins; break;
            }
            if (second is null || far is null) throw new InvalidDataException($"{id}: no far");
            var unshot = others.Where(p => p.SettingName != second.SettingName && p.Kind == EffectParameterKind.Percentage).Select(p => p.SettingName).ToArray();
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
        _ => throw new InvalidDataException($"cannot drive choice '{setting}'"),
    };
    private static DrivenSetting? Pin(EffectParameterContract parameter) => parameter.Kind switch
    {
        EffectParameterKind.Boolean => DrivenSetting.Switch(parameter.SettingName, PinnedOn.Contains(parameter.SettingName)),
        EffectParameterKind.Choice => new DrivenSetting(parameter.SettingName, parameter.Kind, 0, OnValue(parameter.SettingName)),
        _ => null,
    };
    private static string OnValue(string setting) => OffValues.TryGetValue(setting, out var off) ? Choices(setting).First(v => v != off) : Choices(setting)[0];
    private static string FarChoice(string setting)
    {
        var current = ReadChoice(OptionMatrixReview.BaseSettings(), setting);
        return Choices(setting).LastOrDefault(v => v != current) ?? throw new InvalidDataException($"choice '{setting}' no far");
    }
    private static PianoVisualSettings Settings(Family family, DrivenSetting? row)
    {
        var settings = OptionMatrixReview.BaseSettings();
        foreach (var pinned in family.Pinned) Apply(settings, pinned);
        Apply(settings, family.On);
        if (row is not null) Apply(settings, row);
        settings.Clamp();
        if (row?.Name != family.On.Name && !IsOn(settings, family)) throw new InvalidDataException($"settings turned {family.On.Name} off");
        foreach (var pinned in family.Pinned) Verify(settings, pinned);
        if (row is not null) Verify(settings, row);
        return settings;
    }
    private static bool IsOn(PianoVisualSettings settings, Family family) => family.On.Kind == EffectParameterKind.Choice ? ReadChoice(settings, family.On.Name) != OffValues[family.On.Name] : Read(settings, family.On.Name) >= .5;
    private static void Verify(PianoVisualSettings settings, DrivenSetting setting)
    {
        if (setting.Kind == EffectParameterKind.Choice) { var read = ReadChoice(settings, setting.Name); if (read != setting.Choice) throw new InvalidDataException($"turned {setting.Name} = '{setting.Choice}' into '{read}'"); return; }
        var number = Read(settings, setting.Name); if (Math.Abs(number - setting.Number) > 0.001) throw new InvalidDataException($"turned {setting.Name} = {setting.Number} into {number}");
    }

    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, Family family, Milestone m, string label)
    {
        var (pixels, demand) = RenderAr(renderer, pose, camera, settings, family.Stage, m);
        if (pixels.Length != Width * Height * 4) throw new InvalidDataException($"AR returned {pixels.Length} bytes for {family.Id}/{m.Label}/{label}");
        var quads = Probe(renderer, family.Id);
        var relative = Path.Combine("ar-lifecycle", $"{OptionMatrixPlan.Slug(family.Id)}-{m.Frames}f-{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{family.Id}/{m.Label}/{label} lifecycle AR");
        return new Shot { File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels), PaintedCount = OptionMatrixPlan.ChangedPixels(pixels, camera), Quads = quads, Demand = demand, Pixels = pixels };
    }

    private static (int Frames, double Seconds) Timing(Stage stage) => stage switch
    {
        Stage.Held => (12, 1.0/30),
        Stage.Approach => (30, 1.0/60),
        Stage.Impact => (12, 1.0/60),
        Stage.Release => (24, 1.0/60),
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    private static (byte[] Pixels, int Demand) RenderAr(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera, PianoVisualSettings settings, Stage stage, Milestone milestone)
    {
        var piano = new PianoStage { Width = Width, Height = Height };
        piano.SetVisualSettings(settings);
        piano.Measure(new Size(Width, Height));
        piano.Arrange(new Rect(0, 0, Width, Height)); piano.UpdateLayout();
        var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
        feed.SetLook(GpuLook.From(settings, (pitch, track) => piano.AdjustColor(piano.NoteColor(pitch, track)), piano.KeyboardHeight / piano.ActualHeight, piano.AdjustColor, true));
        feed.SetStageHeight(Height);
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, pose.Corners!, Width, Height, out var problem, HeldParticleControlPlan.Fov) ?? throw new InvalidDataException($"AR effect calibration: {problem}");
        var ring = new CameraFrameRing();
        camera.CopyTo(ring.BeginWrite(camera.Length, out var slot), 0);
        ring.Publish(slot, Width, Height, System.Diagnostics.Stopwatch.GetTimestamp());
        feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1);
        feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, KeyLight: .6f, Quality: ArQualityTier.Ultra, ProjectedNotes: true, ShowLiveTrails: stage == Stage.Held, ShowDamperStrip: false, ShowRisingMidiNotes: false);
        var frames = milestone.Frames; var dt = milestone.Dt;
        var notes = stage == Stage.Approach ? ApproachNotes : Array.Empty<NoteEvent>();
        var playing = stage == Stage.Approach;
        var pressed = stage switch { Stage.Held => new HashSet<int>(HeldKeys), Stage.Release => new HashSet<int> { ReleasePitch }, _ => NoPressed, };
        var approachPosition = frames * dt;
        if (stage == Stage.Approach && ApproachNotes.Any(n => n.Start <= approachPosition))
            throw new InvalidDataException($"approach note at {ApproachNotes[0].Start}s but capture at {approachPosition}s");
        void BeforeFrame(int frame)
        {
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
            switch (stage)
            {
                case Stage.Held: foreach (var pitch in HeldKeys) feed.LiveNote(pitch, true, .9); break;
                case Stage.Impact: feed.Impact(BurstPitch, 1); break;
            }
        }
        var demand = 0;
        renderer.ObserveParticleStateForReview = (simulation, _) => demand = Math.Max(demand, simulation.ArParticleCount);
        try
        {
            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _, BeforeFrame, OptionMatrixPlan.Seed);
            return (pixels, demand);
        }
        finally { renderer.ObserveParticleStateForReview = null; }
    }

    private static readonly NoteEvent[] ApproachNotes =
    [
        new() { Pitch = ApproachPitch, Start = .9, Duration = .5, Velocity = 96, Track = 0 }
    ];

    private static int Probe(GpuStageRenderer renderer, string family) => family switch
    {
        "hold.bar" => renderer.ArLiveBarCount,
        "hold.breath" => renderer.ArLiveBarCount,
        "hold.vibration" => renderer.ArLiveBarCount,
        "hold.color-cycle" => renderer.ArLiveBarCount,
        "hold.arc" => renderer.ArHoldArcCount,
        "falling.pulse" => renderer.ArShapeCount,
        "impact.burst" => renderer.ArParticleFxCount,
        "particles.wisps" => renderer.ArParticleFxCount,
        "particles.flames" => renderer.ArParticleFxCount,
        "particles.sparks" => renderer.ArParticleFxCount,
        "impact.wave" => renderer.ArPulseCount,
        "impact.flash" => renderer.ArFlashCount,
        "impact.morph" => renderer.ArParticleFxCount,
        "release.fx" => renderer.ArReleaseEffectCount,
        "atmosphere.beams" => renderer.ArBeamCount,
        "falling.trail" => renderer.ArNoteTrailQuadCount,
        "falling.shimmer" => renderer.ArNoteShimmerQuadCount,
        _ => throw new InvalidDataException($"no witness for '{family}'"),
    };

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
            default: throw new InvalidDataException($"cannot drive '{setting.Name}'");
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
        _ => throw new InvalidDataException($"cannot read '{parameter}'")
    };

    private static string ReadChoice(PianoVisualSettings settings, string parameter) => parameter switch
    {
        "ImpactBurst" => settings.ImpactBurst,
        "ImpactWave" => settings.ImpactWave,
        "ImpactFlashStyle" => settings.ImpactFlashStyle,
        "ImpactMorph" => settings.ImpactMorph,
        "ReleaseEffect" => settings.ReleaseEffect,
        "FallingTrail" => settings.FallingTrail,
        _ => throw new InvalidDataException($"cannot read choice '{parameter}'")
    };

    private static void VerifyShots(string root, IReadOnlyList<Shot> shots)
    {
        foreach (var shot in shots)
        {
            var pixels = ReviewPng.Decode(Path.Combine(root, shot.File), Width, Height);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != shot.PixelSha256)
                throw new InvalidDataException($"{shot.File} no longer decodes");
        }
        if (shots.Select(s => s.File).Distinct(StringComparer.Ordinal).Count() != shots.Count)
            throw new InvalidDataException($" wrote {shots.Count} captures into fewer files");
    }

    private static void WriteManifest(string root, string adapter, IReadOnlyList<Family> families, IReadOnlyList<SheetReport> sheets, int total)
    {
        File.WriteAllText(Path.Combine(root, "ar-lifecycle", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 2,
            status = "lifecycle-matrix-not-claimed",
            claim = "each family at 2-3 lifetimes through calibrated AR overlay (off vs on, same pose/camera/seed)",
            notClaimed = "art approval, every value, real camera, binding change",
            renderer = "GPU-WARP",
            adapter,
            width = Width, height = Height,
            seed = OptionMatrixPlan.Seed,
            pose = "ar-oblique",
            milestones = Milestones.SelectMany(kv => kv.Value.Select(m => new { stage = kv.Key.ToString(), m.Label, m.Frames, m.Seconds })),
            families = families.Select(f => new { f.Id, scene = f.Stage.ToString(), f.On.Name, onValue = f.On.Kind == EffectParameterKind.Choice ? f.On.Choice : "on", offValue = f.OffLabel, pinned = f.Pinned.Select(s => new { setting = s.Name, value = s.Label }), unshot = f.Unshot }),
            floorChangedPixels = MinimumChangedPixels,
            floorOverlayPixels = MinimumOverlayPixels,
            cells = total,
            sheets = sheets.Select(r => new { r.File, r.Sha, r.Columns, r.Rows, cells = r.Cells.Select(c => new { c.Family, scene = c.Stage.ToString(), c.Milestone, c.Parameter, c.Row, Value = Math.Round(c.Seconds, 3), c.Choice, c.File, c.PixelSha256, c.ArChanged, c.ArLit, c.ArPainted, c.Quads, c.Demand, Capped = c.Demand > c.Quads, c.Reading, c.Note }) }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<SheetReport> sheets)
    {
        static string E(string v) => System.Net.WebUtility.HtmlEncode(v);
        var b = new StringBuilder();
        b.Append("<!doctype html><meta charset=utf-8><title>Lifecycle Matrix</title><style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px;padding:16px}table{border-collapse:collapse}th,td{border:1px solid #263243;padding:4px 8px}img{max-width:100%;height:auto;border:1px solid #263243}</style><h1>Ma trận vòng đời AR (full)</h1><p>Máy: " + E(adapter) + " — mỗi mốc là off vs on cùng pose/camera/seed WARP.</p>");
        foreach (var r in sheets)
        {
            b.Append($"<h2>{E(r.File)}</h2><table><tr><th>Family</th><th>Mốc</th><th>Hàng</th><th>AR px</th><th>Quad</th><th>Đọc</th></tr>");
            foreach (var c in r.Cells) b.Append($"<tr><td>{E(c.Family)}</td><td>{E(c.Milestone)}</td><td>{E(c.Row)}</td><td>{c.ArChanged:N0}</td><td>{c.Quads:N0}</td><td>{E(c.Reading)}</td></tr>");
            b.Append("</table>");
            b.Append($"<img src=\"{E(Path.GetFileName(r.File))}\" alt=\"{E(r.File)}\">");
        }
        File.WriteAllText(Path.Combine(root, "ar-lifecycle", "index.html"), b.ToString());
    }
}
