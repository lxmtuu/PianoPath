using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// N17: the effect families that <c>EffectContracts</c> keeps <c>Unverified</c> on the AR overlay, shot through it the
/// way N16 shot the note materials.
///
/// <para>
/// The overlay is not unmeasured. <c>VerifyArNoteProjection</c> already flips the bar, breath, vibration and
/// colour-cycle switches, moves each of their four percentages between two ends, and asserts more than twenty changed
/// pixels for every one of them; it also measures the trail, the shimmer, the ghosts and the landing glow. What those
/// checks do not do is keep the pictures: they assert inside a forty-minute job and leave nothing a person can look
/// at. And two things are not measured at all — the hold arc, which is only ever <em>counted</em>, and the pulse,
/// which appears only inside a "busy" look nobody asserts on.
/// </para>
///
/// <para>
/// So this is a page, not a new claim. It holds the pose, the camera, the scene and the seed, changes one setting,
/// counts the pixels that moved and commits the frames. It does not flip a binding: the contract keeps these families
/// <c>Unverified</c> because one value per setting at one pose is not the value and lifecycle coverage an
/// <c>Applied</c> claim asks for, and this page does not change that arithmetic.
/// </para>
///
/// <para>
/// Each family is shot in the scene its own lifecycle needs, and a family never shares a scene with another family
/// that is switched on: one column lights <em>one</em> family and leaves every other one off, so a difference in that
/// column cannot come from a family the column is not about. The three rows of a column are the family at the
/// product's own values, the family switched off, and its second setting pushed to the far end of what the contract
/// declares — the top of a percentage range, or the last value of a choice list.
/// </para>
///
/// <para>
/// The claim is deliberately small: renderer branching and uniform delivery, on WARP, at one pose, one value per
/// setting. Not art approval, not every value of every range, not a real camera, not hardware frames, and no default
/// change.
/// </para>
/// </summary>
internal static class ArEffectFamilyReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    /// <summary>The same floor the other matrices use: more than a stray pixel is "the setting changed the picture".</summary>
    private const int MinimumChangedPixels = 12;
    /// <summary>
    /// How much of the camera picture the family's own frame has to repaint for a capture to mean anything. An AR frame
    /// is composited over the camera, so "lit pixels" is the whole picture and proves nothing; the painting is what
    /// the quads add. If the family went missing the rest frame would be nearly empty and every number in the column
    /// would be a comparison between two empty pictures.
    /// </summary>
    private const int MinimumOverlayPixels = 500;
    /// <summary>One column per row of a page: the family at the product's values, its switch off, its far end.</summary>
    private const int Columns = 3;
    private const int TileWidth = Width, TileHeight = Height;
    /// <summary>
    /// The pose every capture uses: the leaned one, not the reference pose the note reviews are calibrated against.
    /// A held arc is a stand-up ribbon, and the reference pose looks at the keyboard from straight above: the first
    /// run measured <em>eighteen</em> pixels between an arc at 70% and no arc at all, so the column could not say
    /// anything about the setting. Height needs an angle to be visible.
    /// </summary>
    private const string PoseId = "ar-oblique";
    /// <summary>
    /// Two keys held for the whole run: two live bars, and one arc between the two active pitches. They are two
    /// octaves apart on purpose. The first run held C4 and E4 — four semitones — and the arc between them was a
    /// forty-six-pixel sliver, correct and useless: a ribbon that short cannot show what its intensity does. The
    /// span is what the arc is made of, so the span has to be long.
    /// </summary>
    private static readonly int[] HeldKeys = [48, 72];
    /// <summary>The pitch a burst is struck on, and the one the approach scene's note is written for.</summary>
    private const int BurstPitch = 60, ApproachPitch = 60;
    private static readonly IReadOnlySet<int> NoPressed = new HashSet<int>();

    /// <summary>
    /// The scene a family's lifecycle needs. A held family lives on a bar under a finger; the pulse only exists on a
    /// note that has <em>not</em> been reached yet, because <c>AddSongNote</c> applies it while the note is not
    /// sounding; a burst is a strike and nothing else.
    /// </summary>
    private enum Stage
    {
        /// <summary>Two keys down for the whole run.</summary>
        Held,
        /// <summary>One song note falling towards the keys, not yet struck.</summary>
        Approach,
        /// <summary>One strike on the hit line, no note and no finger.</summary>
        Impact,
    }

    /// <summary>
    /// The families this page drives, in page order, each with the scene its lifecycle needs. Read against
    /// <c>EffectContractCatalog.AllFamilies</c> at the top of the run, so a family that leaves the contract fails here
    /// instead of being shot at settings nobody declares any more.
    /// </summary>
    private static readonly (string Id, Stage Stage)[] Driven =
    [
        ("hold.bar", Stage.Held),
        ("hold.breath", Stage.Held),
        ("hold.vibration", Stage.Held),
        ("hold.color-cycle", Stage.Held),
        ("hold.arc", Stage.Held),
        ("falling.pulse", Stage.Approach),
        ("impact.burst", Stage.Impact),
    ];

    /// <summary>One setting the page drives: its contract name, its kind, and the value the row shoots.</summary>
    private sealed record DrivenSetting(string Name, EffectParameterKind Kind, double Number, string Choice)
    {
        internal string Label => Kind == EffectParameterKind.Choice ? Choice : Number.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        internal static DrivenSetting Switch(string name, bool on) => new(name, EffectParameterKind.Boolean, on ? 1 : 0, "");
    }

    /// <summary>One family as the page draws it: where it is shot, its switch, and the setting the far row pushes.</summary>
    private sealed record Family(string Id, Stage Stage, DrivenSetting On, DrivenSetting Far)
    {
        internal string Sheet => $"ar-effect/sheet-{Stage.ToString().ToLowerInvariant()}.png";
    }

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

    private sealed record Cell(string Family, Stage Stage, string Parameter, string Row, double Value, string Choice,
        string File, string PixelSha256, int ArChanged, int ArLit, int ArPainted, int Quads, string Reading, string Note);

    private sealed record SheetReport(string File, string Sha, int Columns, int Rows, IReadOnlyList<Cell> Cells);

    /// <summary>The per-family numbers, for the suite's PASS line: the manifest is the record, this is the summary.</summary>
    internal static string Summary { get; private set; } = "";

    internal static int Render(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("The AR effect review output directory is empty.", nameof(output));
        var families = BuildFamilies();
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

            // One sheet per scene, so the numbers on a page always come from the same camera and the same moment.
            var reports = new List<SheetReport>(3);
            var shots = new List<Shot>(families.Count * Columns);
            foreach (var group in families.GroupBy(family => family.Stage))
            {
                var page = group.ToArray();
                var captions = page.SelectMany(family => new[]
                    {
                        $"{family.Id} on", $"{family.On.Name} off", $"{family.Far.Name} {family.Far.Label}"
                    }).ToArray();
                var sheet = new ReviewSheet.Canvas(page.Length * Columns, TileWidth, TileHeight, Columns,
                    $"N17 AR {group.Key.ToString().ToUpperInvariant()}", ReviewSheet.CaptionScaleFor(captions, TileWidth));
                var cells = new List<Cell>(page.Length * Columns);
                for (var column = 0; column < page.Length; column++)
                {
                    var family = page[column];
                    var rest = CaptureAr(root, renderer, pose, camera, Settings(family, null), family, "rest");
                    if (rest.Painted < MinimumOverlayPixels)
                        throw new InvalidDataException(
                            $"{family.Id}: its own frame repaints {rest.Painted} pixels of the camera picture in AR " +
                            $"(floor {MinimumOverlayPixels}); a picture this empty cannot show that a setting moved anything.");
                    if (rest.Quads <= 0)
                        throw new InvalidDataException(
                            $"{family.Id} drew no quads of its own in the {family.Stage} scene, so its three captures " +
                            "would all measure a family that never ran. The scene has to carry it before the page can judge it.");
                    shots.Add(rest);
                    sheet.Blit(column * Columns, captions[column * Columns], rest.Pixels!, Width, Height, premultiplied: false);
                    cells.Add(new Cell(family.Id, family.Stage, family.Id, "rest", 0, "", rest.File, rest.PixelSha256, 0,
                        rest.Lit, rest.Painted, rest.Quads, "Reference", "The family at the product's own values."));

                    // The switch, off: the only row that takes the whole family out of the picture.
                    var off = CaptureAr(root, renderer, pose, camera, Settings(family, family.On with { Number = 0, Choice = "" }),
                        family, "off");
                    shots.Add(off);
                    sheet.Blit(column * Columns + 1, captions[column * Columns + 1], off.Pixels!, Width, Height,
                        premultiplied: false);
                    // The family's own footprint: a non-zero counter proves the layer emitted quads, not that they landed
                    // anywhere. Ten ribbon segments were once counted over eighteen changed pixels. If taking the family
                    // away does not move the picture, this column cannot answer its question and has to say so.
                    var footprint = OptionMatrixPlan.ChangedPixels(off.Pixels!, rest.Pixels!);
                    if (footprint < MinimumChangedPixels)
                        throw new InvalidDataException(
                            $"{family.Id}: taking the family away changes {footprint} pixels (floor {MinimumChangedPixels}) " +
                            $"in the {family.Stage} scene at the pose '{PoseId}', so this column would measure a family the " +
                            "camera cannot see.");
                    cells.Add(new Cell(family.Id, family.Stage, family.On.Name, "off", 0, "", off.File, off.PixelSha256,
                        footprint, off.Lit, off.Painted, off.Quads, Read(footprint),
                        "The switch off; every other frame of this column has the family on."));

                    // The far end: top of a percentage range, or the last declared value of a choice list.
                    var far = CaptureAr(root, renderer, pose, camera, Settings(family, family.Far), family, "far");
                    shots.Add(far);
                    sheet.Blit(column * Columns + 2, captions[column * Columns + 2], far.Pixels!, Width, Height,
                        premultiplied: false);
                    var changed = OptionMatrixPlan.ChangedPixels(far.Pixels!, rest.Pixels!);
                    cells.Add(new Cell(family.Id, family.Stage, family.Far.Name, "far", family.Far.Number, family.Far.Choice,
                        far.File, far.PixelSha256, changed, far.Lit, far.Painted, far.Quads, Read(changed),
                        "The contract's far end, against the product's own value."));

                    off.Pixels = null;
                    far.Pixels = null;
                    rest.Pixels = null;
                }

                var finished = sheet.Finish();
                var layout = sheet.Sheet;
                var sha = ReviewPng.Write(Path.Combine(root, page[0].Sheet), finished, layout.Width, layout.Height,
                    $"N17 AR {group.Key}: {page.Length * Columns} captures in {layout.Columns}×{layout.Rows} cells",
                    PixelFormats.Pbgra32);
                reports.Add(new SheetReport(page[0].Sheet, sha, layout.Columns, layout.Rows, cells));
            }

            VerifyShots(root, shots);
            Summary = string.Join("; ", families.Select(family =>
            {
                var cells = reports.SelectMany(report => report.Cells).Where(cell => cell.Family == family.Id).ToArray();
                var off = cells.Single(cell => cell.Row == "off");
                var far = cells.Single(cell => cell.Row == "far");
                return $"{family.Id} {off.ArChanged} px off / {far.Parameter} {(far.Choice.Length > 0 ? far.Choice : far.Value.ToString("0"))} {far.ArChanged} px";
            }));
            var total = reports.Sum(report => report.Cells.Count);
            WriteManifest(root, renderer.AdapterName, families, reports, total);
            WriteHtmlIndex(root, renderer.AdapterName, families, reports);
            return total;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    /// <summary>
    /// The driven families, checked against the contract, with their two settings read out of it. A family the review
    /// cannot drive — no single switch, no single second setting, a choice with no known list — fails here at the top
    /// of the run rather than being shot at whatever the settings class keeps.
    /// </summary>
    private static Family[] BuildFamilies()
    {
        var all = EffectContractCatalog.AllFamilies;
        var built = new Family[Driven.Length];
        for (var index = 0; index < Driven.Length; index++)
        {
            var (id, stage) = Driven[index];
            var contract = all.SingleOrDefault(candidate => candidate.Id == id)
                ?? throw new InvalidDataException($"The AR effect review drives '{id}', which the contract does not declare.");
            if (contract.Parameters.Count != 2)
                throw new InvalidDataException(
                    $"{id} declares {contract.Parameters.Count} settings; the review drives one switch and one second " +
                    "setting, so a third would fall off the page without anyone noticing.");
            var toggle = contract.Parameters[0].Kind == EffectParameterKind.Boolean
                ? contract.Parameters[0]
                : contract.Parameters[1].Kind == EffectParameterKind.Boolean
                    ? contract.Parameters[1]
                    : throw new InvalidDataException($"{id} declares no boolean switch for the review to turn off.");
            var second = contract.Parameters.Single(parameter => parameter.SettingName != toggle.SettingName);
            var far = second.Kind switch
            {
                EffectParameterKind.Percentage => new DrivenSetting(second.SettingName, second.Kind,
                    second.Maximum ?? throw new InvalidDataException($"{id}: {second.SettingName} has no maximum to shoot."), ""),
                EffectParameterKind.Choice => new DrivenSetting(second.SettingName, second.Kind, 0, FarChoice(second.SettingName)),
                _ => throw new InvalidDataException($"{id}: the review cannot drive a {second.Kind} setting."),
            };
            built[index] = new Family(id, stage, DrivenSetting.Switch(toggle.SettingName, true), far);
        }
        return built;
    }

    /// <summary>
    /// The far end of a choice: the last value its own list declares that differs from what the product keeps. The
    /// allowed values stay owned by the settings model; this reads them, and the read-back check in
    /// <see cref="Settings"/> catches a value the model refuses.
    /// </summary>
    private static string FarChoice(string setting)
    {
        var choices = setting switch
        {
            "ImpactBurst" => PianoVisualSettings.ImpactBursts,
            _ => throw new InvalidDataException(
                $"The AR effect review cannot drive the choice '{setting}'; teach FarChoice() which list owns it."),
        };
        var current = ReadChoice(OptionMatrixReview.BaseSettings(), setting);
        return choices.LastOrDefault(value => value != current)
            ?? throw new InvalidDataException($"The choice '{setting}' declares no value other than '{current}' to shoot.");
    }

    /// <summary>
    /// The scene, with one family on and the rest off. Everything else — no background, no stars, no beats, no ambient
    /// layers — is the neutral look the option matrix starts from, so a difference between two captures of a column is
    /// the setting and nothing else.
    /// </summary>
    private static PianoVisualSettings Settings(Family family, DrivenSetting? row)
    {
        var settings = OptionMatrixReview.BaseSettings();
        Apply(settings, family.On);
        if (row is not null) Apply(settings, row);
        settings.Clamp();
        // The rest frame and the far row have to carry the family; only the off row may take it away.
        if (row?.Name != family.On.Name && Read(settings, family.On.Name) < .5)
            throw new InvalidDataException($"The settings class turned {family.On.Name} off; the scene would have no family in it.");
        if (row is not null) Verify(settings, row);
        return settings;
    }

    /// <summary>Reads a setting back out of a clamped settings object, so a value the model refused cannot be shot.</summary>
    private static void Verify(PianoVisualSettings settings, DrivenSetting setting)
    {
        if (setting.Kind == EffectParameterKind.Choice)
        {
            var read = ReadChoice(settings, setting.Name);
            if (read != setting.Choice)
                throw new InvalidDataException(
                    $"The settings class turned {setting.Name} = '{setting.Choice}' into '{read}'; the capture would not be " +
                    "the value its caption names.");
            return;
        }
        var number = Read(settings, setting.Name);
        if (Math.Abs(number - setting.Number) > 0.001)
            throw new InvalidDataException(
                $"The settings class turned {setting.Name} = {setting.Number} into {number}; the capture would not be " +
                "the value its caption names.");
    }

    private static Shot CaptureAr(string root, GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        PianoVisualSettings settings, Family family, string label)
    {
        var pixels = RenderAr(renderer, pose, camera, settings, family.Stage);
        if (pixels.Length != Width * Height * 4)
            throw new InvalidDataException(
                $"AR returned {pixels.Length} bytes for {family.Id}/{label}; expected {Width}×{Height} BGRA pixels.");
        var quads = Probe(renderer, family.Id);
        var relative = Path.Combine("ar-effect", OptionMatrixPlan.Slug(family.Id), $"{label}.png");
        var sha = ReviewPng.Write(Path.Combine(root, relative), pixels, Width, Height, $"{family.Id}/{label} in AR");
        return new Shot
        {
            File = relative.Replace('\\', '/'), PixelSha256 = sha, Lit = OptionMatrixPlan.NonBlackPixels(pixels),
            Painted = OptionMatrixPlan.ChangedPixels(pixels, camera), Quads = quads, Pixels = pixels
        };
    }

    /// <summary>The frames each scene runs, and the step between them.</summary>
    private static (int Frames, double Seconds) Timing(Stage stage) => stage switch
    {
        // The bars have to be fully grown and the arc has to have settled: 0.4 s of hold.
        Stage.Held => (12, 1.0 / 30),
        // The note's head sits 0.4 s above the keys at 0.5 s in, so it is in flight and not yet sounding.
        Stage.Approach => (30, 1.0 / 60),
        // 0.3 s after the strike: the burst has left the key and has not begun to fade.
        Stage.Impact => (18, 1.0 / 60),
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    /// <summary>
    /// One frame of the AR overlay: the calibrated pose, the scene's own state, and the family's look. The camera, the
    /// ring, the palette and the calibration are shared, so two captures of a column differ only in the settings.
    /// </summary>
    private static byte[] RenderAr(GpuStageRenderer renderer, HeldParticleControlPlan.View pose, byte[] camera,
        PianoVisualSettings settings, Stage stage)
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
            ProjectedNotes: true, ShowLiveTrails: stage == Stage.Held, ShowDamperStrip: false, ShowRisingMidiNotes: false);
        var (frames, dt) = Timing(stage);
        // The approach scene's note must be in flight when it is shot: AddSongNote applies the pulse only while a note
        // is not sounding, so a note whose onset has already passed would make the column measure nothing.
        var position = frames * dt;
        if (stage == Stage.Approach && ApproachNotes.Any(note => note.Start <= position))
            throw new InvalidDataException(
                $"The approach scene's note starts at {ApproachNotes[0].Start} s but the capture is at {position} s; the " +
                "note would already be sounding and its pulse would never be applied.");
        var notes = stage == Stage.Approach ? ApproachNotes : Array.Empty<NoteEvent>();
        var playing = stage == Stage.Approach;
        var pressed = stage == Stage.Held ? new HashSet<int>(HeldKeys) : NoPressed;
        void BeforeFrame(int frame)
        {
            feed.SetDeterministicState(notes, (frame + 1) * dt, playing, pressed);
            feed.SetDeterministicBeatPulse(0);
            if (frame != 0) return;
            // The scene's event, sent once because it stays true for the rest of the run: a finger that never lifts, or
            // a strike whose burst is still in the air.
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
        return GpuRenderLoop.RenderFrames(renderer, feed, Width, Height, frames - 1, dt, false, out _,
            BeforeFrame, OptionMatrixPlan.Seed);
    }

    /// <summary>
    /// The one note the approach scene shows: written to be <em>in flight</em> at the capture, never sounding. Its body
    /// is half a second of travel — about a fifth of the AR reach — so the quad is large enough for an alpha change to
    /// be a number rather than a rounding error.
    /// </summary>
    private static readonly NoteEvent[] ApproachNotes =
    [
        new() { Pitch = ApproachPitch, Start = .9, Duration = .5, Velocity = 96, Track = 0 }
    ];

    /// <summary>
    /// Which of the layer's own counters witnesses that this family drew something in its scene. Four hold families
    /// ride on the held bar's quad and the arc is a ribbon pass of its own; the pulse has no counter of its own because
    /// it multiplies the alpha of an existing note quad, so its witness is the note quad itself; a burst is a particle
    /// quad. A family the page does not know is a family nobody shot, so it throws instead of reporting zero.
    /// </summary>
    private static int Probe(GpuStageRenderer renderer, string family) => family switch
    {
        "hold.bar" => renderer.ArLiveBarCount,
        "hold.breath" => renderer.ArLiveBarCount,
        "hold.vibration" => renderer.ArLiveBarCount,
        "hold.color-cycle" => renderer.ArLiveBarCount,
        "hold.arc" => renderer.ArHoldArcCount,
        "falling.pulse" => renderer.ArShapeCount,
        "impact.burst" => renderer.ArParticleFxCount,
        _ => throw new InvalidDataException(
            $"The AR effect review has no witness counter for '{family}'; teach Probe() about it before the page " +
            "claims to have shot it.")
    };

    /// <summary>What a number means on the page: above the floor the setting reaches the picture, below it does not.</summary>
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
            case "ImpactBurst": settings.ImpactBurst = setting.Choice; break;
            default:
                throw new InvalidDataException(
                    $"The AR effect review cannot drive '{setting.Name}'; teach Apply() and Read() about it first.");
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
        _ => throw new InvalidDataException($"The AR effect review cannot read '{parameter}'.")
    };

    private static string ReadChoice(PianoVisualSettings settings, string parameter) => parameter switch
    {
        "ImpactBurst" => settings.ImpactBurst,
        _ => throw new InvalidDataException($"The AR effect review cannot read the choice '{parameter}'.")
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

    private static void WriteManifest(string root, string adapter, IReadOnlyList<Family> families,
        IReadOnlyList<SheetReport> sheets, int total)
    {
        File.WriteAllText(Path.Combine(root, "ar-effect", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 2,
            status = "n17-ar-effect-families-measured-not-claimed",
            claim = "each driven effect family is shot through the calibrated AR overlay in the scene its lifecycle needs, one setting at a time against the same family at the product's own values",
            notClaimed = "art approval, every value of every range, a real camera, hand occlusion, hardware frames, other quality tiers, a binding change or a default change",
            renderer = "GPU-WARP",
            adapter,
            width = Width, height = Height,
            seed = OptionMatrixPlan.Seed,
            pose = PoseId,
            scenes = families.GroupBy(family => family.Stage).Select(group => new
            {
                scene = group.Key.ToString(),
                frames = Timing(group.Key).Frames,
                fps = (int)Math.Round(1 / Timing(group.Key).Seconds),
                families = group.Select(family => family.Id),
                description = group.Key switch
                {
                    Stage.Held => "two keys two octaves apart (C3 and C5) held for the whole run: two live bars under the fingers and one arc between the two active pitches; no song, no background, no other effect family",
                    Stage.Approach => "one song note (C4) in flight towards the keys, not yet sounding, so the pulse is applied; no keys, no background, no other effect family",
                    Stage.Impact => "one strike on C4 at the hit line, with no note and no finger; the burst is the only content of the frame",
                    _ => throw new ArgumentOutOfRangeException(nameof(group)),
                },
            }),
            families = families.Select(family => new
            {
                family.Id, scene = family.Stage.ToString(), family.On.Name, far = family.Far.Name,
                farValue = family.Far.Kind == EffectParameterKind.Choice ? family.Far.Choice : family.Far.Label,
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
                    cell.Choice, cell.File, cell.PixelSha256, cell.ArChanged, cell.ArLit, cell.ArPainted, cell.Quads,
                    cell.Reading, cell.Note
                })
            })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteHtmlIndex(string root, string adapter, IReadOnlyList<Family> families,
        IReadOnlyList<SheetReport> sheets)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var body = new StringBuilder();
        body.Append("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>N17 — AR effect families</title>")
            .Append("<style>body{font:15px system-ui;background:#10141b;color:#e1e9f3;max-width:1400px;margin:24px auto;padding:16px}")
            .Append("h2{margin:30px 0 6px}p{line-height:1.55;color:#a9b6c7}table{border-collapse:collapse;margin:12px 0}")
            .Append("th,td{border:1px solid #263243;padding:5px 9px;text-align:left;font-variant-numeric:tabular-nums}")
            .Append("th{color:#c9d6e6;font-weight:600}td.y{color:#7df0b1}td.n{color:#ffd479}")
            .Append("img{max-width:100%;height:auto;border:1px solid #263243;margin-top:8px}</style>")
            .Append("<h1>Family hiệu ứng trong lớp AR (N17)</h1>")
            .Append($"<p>Cùng pose <code>{Encode(PoseId)}</code>, cùng camera, cùng seed; mỗi cột bật <strong>một</strong> family ")
            .Append("và mỗi khung chỉ đổi một thiết lập so với khung \"on\" của chính cột đó. Số là pixel đổi giữa hai khung. ")
            .Append($"Máy đo: {Encode(adapter)}.</p>")
            .Append("<table><tr><th>Family</th><th>Cảnh</th><th>Công tắc</th><th>Thiết lập xa</th></tr>");
        foreach (var family in families)
            body.Append($"<tr><td>{Encode(family.Id)}</td><td>{Encode(family.Stage.ToString())}</td>")
                .Append($"<td>{Encode(family.On.Name)}</td><td>{Encode(family.Far.Name)} = {Encode(family.Far.Label)}</td></tr>");
        body.Append("</table>");
        foreach (var report in sheets)
        {
            body.Append($"<h2>Tờ ảnh — {Encode(report.File)}</h2>")
                .Append("<table><tr><th>Family</th><th>Thiết lập</th><th>Hàng</th><th>Giá trị</th><th>AR</th><th>Quad</th><th>Đọc</th></tr>");
            foreach (var cell in report.Cells)
            {
                var value = cell.Choice.Length > 0 ? cell.Choice : cell.Value.ToString("0.###");
                body.Append($"<tr><td>{Encode(cell.Family)}</td><td>{Encode(cell.Parameter)}</td><td>{Encode(cell.Row)}</td>")
                    .Append($"<td>{Encode(value)}</td>")
                    .Append($"<td class=\"{(cell.ArChanged >= MinimumChangedPixels ? "y" : "n")}\">{cell.ArChanged:N0} px</td>")
                    .Append($"<td>{cell.Quads:N0}</td><td>{Encode(cell.Reading)}</td></tr>");
            }
            body.Append("</table>");
            body.Append("<p>Ba cột: family đang bật ở giá trị của sản phẩm, cùng family bị tắt, và thiết lập thứ hai bị đẩy tới ")
                .Append("đầu xa của khoảng contract khai báo.</p>")
                .Append($"<img src=\"{Encode(Path.GetFileName(report.File))}\" alt=\"N17 {Encode(report.File)}\">");
        }
        body.Append("</html>");
        File.WriteAllText(Path.Combine(root, "ar-effect", "index.html"), body.ToString());
    }
}
