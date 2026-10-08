using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// Only the two shapes the ghost check counts: the namespace would make Path ambiguous with System.IO.Path.
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>In-app smoke and regression checks, runnable with PianoPath.exe --verify.</summary>
internal static partial class VerificationSuite
{
    /// <summary>
    /// The lines a run reports. Each one is appended to the log as it is recorded — not only at the end — so a
    /// machine that dies inside a check (a native call that takes the process with it) still leaves the trail up
    /// to the step that killed it, which is the only thing that can be diagnosed from a CI runner.
    /// </summary>
    private sealed class VerificationLog : List<string>
    {
        internal string? Path { get; set; }

        /// <summary>Records one line: it joins the run's report and, when a log is open, the file itself.</summary>
        internal new void Add(string line)
        {
            base.Add(line);
            if (Path is null) return;
            try { File.AppendAllText(Path, line + Environment.NewLine); } catch { }
        }
    }

    private static readonly VerificationLog Results = new();
    private static int _assertions;

    public static void Run(string[] args, App app)
    {
        // The expectations below spell numbers the invariant way ("0.5"), but the checks format them with the
        // machine's regional format. A Windows set to Vietnamese (decimal comma) then failed a dozen of them
        // ("62@2+0,5") for no product reason, while CI on en-US stayed green. Pin the culture for the whole run.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Results.Clear(); _assertions = 0;
        var logOption = args.FirstOrDefault(a => a.StartsWith("--verify-log=", StringComparison.Ordinal));
        var reviewOutputOption = args.FirstOrDefault(a => a.StartsWith("--verify-review-out=", StringComparison.Ordinal));
        var reviewOutput = reviewOutputOption is null ? null : reviewOutputOption["--verify-review-out=".Length..];
        Results.Path = logOption is null ? Path.Combine(System.IO.Path.GetTempPath(), "keyflow-verification.log") : logOption[13..];
            try { VerifyMidiImport(); VerifyMeter(); VerifyMusicXmlImport(); VerifyHandSplitInference(); VerifyHandTracking(); VerifyKeyboardLayout(); VerifyGuardedStart(); VerifyPresetShareCodes(); VerifyVisualSettings(); VerifyShaderPipeline(); VerifyGpuStage(); VerifyDeterministicGpuFrame(); VerifyFrameBudget(); VerifyLitKeyTilesBakeInBackground(); VerifyAviVideoRecorder(); VerifyPianoPedalModel(); VerifyFlacReader(); VerifySfzEngine(); VerifySalamanderPiano(); VerifyStereoHallReverb(); VerifyConvolutionReverb(); VerifyAudioOutputSelection(); VerifyMidiDevicesAndKeyboardMap(); VerifyCameraOverlay(); VerifyArCameraLayer(); VerifyArRecording(); VerifyArDelay(); VerifyArLatency(); if (reviewOutputOption is not null) VerifyPhase1VisualReview(reviewOutput!); }
        catch (Exception ex) { Finish(app, args, ex); return; }

        var startupWindow = new MainWindow(loadSalamanderOnStart: false) { WindowState = WindowState.Normal, Width = 1240, Height = 780, SuppressErrorDialogs = true };
        // These checks verify the first-run onboarding without touching a user's 748 MB install. Audio itself
        // is exercised below with generated SFZ/WAV fixtures, so the smoke test never downloads anything.
        startupWindow.UseSoftwareForSession();
        startupWindow.ContentRendered += (_, _) =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var settingsPanel = (Border)startupWindow.FindName("SettingsPanel");
                    var settingsTabs = (TabControl)startupWindow.FindName("SettingsTabs");
                    settingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Audio);
                    settingsPanel.Visibility = Visibility.Visible;
                    startupWindow.UpdateLayout();
                    var audio = (PianoAudioEngine)Field(startupWindow, "_audio");
                    var instrumentLabel = (TextBlock)startupWindow.FindName("InstrumentStatusLabel");
                    var downloadButton = (Button)startupWindow.FindName("SalamanderButton");
                    var installed = SalamanderPiano.IsInstalled;
                    var expectedStatus = installed ? "SALAMANDER GRAND · INSTALLED" : "SALAMANDER GRAND · SETUP REQUIRED";
                    Assert(!audio.HasInstrument && instrumentLabel.Text == Loc.T(expectedStatus), "A deliberately unloaded first-run window should show the Salamander setup state, never a fallback instrument.");
                    Assert(downloadButton.IsVisible && downloadButton.IsEnabled, "The Salamander download/repair action should be visible and available in Audio settings.");
                    Assert(((ToggleButton)startupWindow.FindName("ReverbToggle")).IsChecked == true && audio.ReverbEnabled, "The piano should start with hall reverb active in the UI and audio engine.");
                    Assert(((ToggleButton)startupWindow.FindName("ResonanceToggle")).IsChecked == true && audio.StringResonanceEnabled, "The piano should start with sympathetic string resonance active in the UI and audio engine.");
                    Results.Add("PASS WPF startup: first-run Salamander onboarding is explicit, does not auto-download, and starts without any fallback instrument.");
                    startupWindow.Close();
                    VerifyPracticeWindow(args, app);
                }
                catch (Exception ex) { Finish(app, args, ex); }
            };
            timer.Start();
        };
        startupWindow.Show();
    }

    private static void VerifyPracticeWindow(string[] args, App app)
    {
        var window = new MainWindow(loadSalamanderOnStart: false) { WindowState = WindowState.Normal, Width = 1240, Height = 780, SuppressErrorDialogs = true };
        window.UseSoftwareForSession(); // same determinism as the startup window above
        // The checks below read the layout right after a change. With Windows animations on (every normal desktop)
        // the chrome rows are still easing towards their target at that moment, so the checks failed there while a CI
        // server, which runs with animations off, passed. Screenshot runs already switch the motion off for this reason.
        window.DisableChromeMotion();
        window.ContentRendered += (_, _) =>
        {
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                try { VerifyWpfInteractions(window, () => Finish(app, args, null), ex => Finish(app, args, ex)); }
                catch (Exception ex) { Finish(app, args, ex); }
            };
            delay.Start();
        };
        window.Show();
    }

    private static void VerifyMidiImport()
    {
        var path = Path.Combine(Path.GetTempPath(), "keyflow-fixture.mid");
        File.WriteAllBytes(path, CreateFormatOneMidi()); var song = MidiReader.ReadSong(path); var notes = song.Notes;
        Assert(notes.Count == 3 && notes.All(n => n.Pitch != 36), "MIDI format 1 should read notes from multiple tracks and skip the percussion channel.");
        Assert(song.BeatsPerBar == 4 && song.BeatTimes.Count >= 3 && Math.Abs(song.BeatTimes[1] - .5) < .001 && Math.Abs(song.BeatTimes[2] - .75) < .001, "The beat grid should follow the tempo map for the metronome.");
        Assert(song.TrackNames.TryGetValue(0, out var trackName) && trackName == "Lead", "Track name meta events should be exposed for the track picker.");
        Assert(NoteTimeline.FirstIndexAtOrAfter(notes, .5) == 2 && NoteTimeline.FirstIndexAtOrAfter(notes, 9) == 3 && NoteTimeline.FirstIndexAtOrAfter(notes, -1) == 0, "Binary search over sorted note starts should find the first note at or after a time.");
        Assert(MidiReader.Read(path).Count == 3, "The note-only reader should stay compatible.");
        var middleC = notes.Single(n => n.Pitch == 60);
        Assert(middleC.Track == 0 && middleC.Velocity == 100, "MIDI pitch, track and velocity should be retained.");
        Assert(Math.Abs(middleC.Start) < .001 && Math.Abs(middleC.Duration - .5) < .001, "Default tempo should map ticks to seconds correctly.");
        Assert(Math.Abs(notes.Single(n => n.Pitch == 64).Duration - .25) < .001, "Short note duration should be retained.");
        var changed = notes.Single(n => n.Pitch == 67);
        Assert(Math.Abs(changed.Start - .5) < .001 && Math.Abs(changed.Duration - .25) < .001, "Tempo changes should affect following notes.");
        // Format 2 is a set of independent sequences, so its patterns play one after another: the second note
        // starts where the first pattern ended instead of on top of it, and the beat grid keeps running.
        File.WriteAllBytes(path, CreateFormatTwoMidi()); var patterns = MidiReader.ReadSong(path);
        var patternLine = string.Join(", ", patterns.Notes.Select(note => note.Pitch + "@" + note.Start.ToString("0.###")));
        Assert(patterns.Notes.Count == 2 && patterns.Notes[0].Pitch == 60 && Math.Abs(patterns.Notes[0].Start) < .001
                && patterns.Notes[1].Pitch == 67 && Math.Abs(patterns.Notes[1].Start - .5) < .001
                && Math.Abs(patterns.Notes[1].Duration - .5) < .001 && patterns.Notes[1].Track == 1,
            $"A format 2 file should play its patterns one after another, not stacked on one another (read {patternLine}).");
        Assert(patterns.BeatTimes.SequenceEqual([0, .5]),
            $"The beat grid of a format 2 file should run across the patterns instead of restarting or overlapping ({string.Join(", ", patterns.BeatTimes)}).");
        Assert(patterns.TrackNames.TryGetValue(1, out var patternName) && patternName == "Lead" && patterns.TrackNames.Count == 2,
            "Track names should stay tied to the track they were written in once the patterns are laid end to end.");

        // An SMPTE division names ticks per second, so ticks are absolute time: the tempo change moves the
        // metronome and not the notes.
        File.WriteAllBytes(path, CreateSmpteMidi()); var smpte = MidiReader.ReadSong(path);
        var smpteLine = string.Join(", ", smpte.Notes.Select(note => note.Pitch + "@" + note.Start.ToString("0.###") + "+" + note.Duration.ToString("0.###")));
        Assert(smpte.Notes.Count == 2 && Math.Abs(smpte.Notes[0].Start) < .001 && Math.Abs(smpte.Notes[0].Duration - .5) < .001
                && Math.Abs(smpte.Notes[1].Start - 1) < .001 && Math.Abs(smpte.Notes[1].Duration - .5) < .001,
            $"An SMPTE division should read 500 ticks as half a second whatever the tempo says (read {smpteLine}).");
        Assert(smpte.BeatTimes.Count == 5 && Math.Abs(smpte.BeatTimes[1] - .5) < .001 && Math.Abs(smpte.BeatTimes[2] - 1) < .001
                && Math.Abs(smpte.BeatTimes[3] - 1.25) < .001 && Math.Abs(smpte.BeatTimes[4] - 1.5) < .001,
            $"An SMPTE file has no tick grid, so its beats come from the tempo map: half seconds, then quarter seconds after the change ({string.Join(", ", smpte.BeatTimes)}).");

        // A header this reader cannot use is refused instead of read as noise: an unknown format, an unknown
        // SMPTE frame rate, and an SMPTE division that counts no ticks in a frame.
        var unusable = new[] { CreateHeaderMidi(3, 480), CreateHeaderMidi(1, 0xE520), CreateHeaderMidi(1, 0xE700) };
        var refused = 0;
        foreach (var bytes in unusable)
        {
            File.WriteAllBytes(path, bytes);
            try { MidiReader.ReadSong(path); } catch (InvalidDataException) { refused++; }
        }
        Assert(refused == unusable.Length && MidiReader.ReadSong(WriteFormatOneMidi(path)).Notes.Count == 3,
            $"An unknown MIDI format (3) and an unusable time division (27 frames, or zero ticks in a frame) must be refused, while a good file still reads (refused {refused} of {unusable.Length}).");

        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("BAD!")); var rejected = false;
        try { MidiReader.Read(path); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Invalid MIDI headers should be rejected cleanly.");
        Results.Add("PASS MIDI: multi-track import, tempo map, beat grid, track names, percussion skip, timing, velocity, format 2 patterns played end to end, SMPTE division as absolute time, and malformed input.");
    }

    /// <summary>
    /// Hand-split inference is pure arithmetic over the notes, so it is checked without a window: the
    /// two-hand case splits inside the gap (and prefers middle C inside a wide gap), a one-hand song
    /// keeps the split the user chose, a stray short note cannot define a hand, and the answer does not
    /// depend on call order.
    /// </summary>
    private static void VerifyHandSplitInference()
    {
        static List<NoteEvent> Notes(IEnumerable<(int Pitch, double Duration)> entries) =>
            entries.Select(entry => new NoteEvent { Pitch = entry.Pitch, Duration = entry.Duration }).ToList();
        var twoHands = Notes([.. Enumerable.Range(36, 13).Select(pitch => (pitch, .5)), .. Enumerable.Range(67, 18).Select(pitch => (pitch, .5))]);
        var split = HandSplit.Infer(twoHands, 55);
        Assert(split == HandSplit.MiddleC, $"A song with a wide gap between the hands should split at middle C, not at {split}.");
        Assert(HandSplit.Infer(twoHands, 55) == split, "The inferred split must not depend on how often it is computed.");

        // A tight, unmistakable two-hand arrangement: the split lands in the gap between 50 and 70.
        var separated = Notes([.. Enumerable.Range(40, 11).Select(pitch => (pitch, 1.0)), .. Enumerable.Range(70, 11).Select(pitch => (pitch, 1.0))]);
        var separatedSplit = HandSplit.Infer(separated, 60);
        Assert(separatedSplit == HandSplit.MiddleC, $"A wide gap between the hands should split at middle C, not at {separatedSplit}.");

        var oneHand = Notes(Enumerable.Range(72, 8).Select(pitch => (pitch, .5)));
        Assert(HandSplit.Infer(oneHand, 55) == 55, "A song that lives in one hand should keep the split point the user chose.");
        Assert(HandSplit.Infer([], 55) == 55 && HandSplit.Infer([], 500) == 108 && HandSplit.Infer([], 5) == 21,
            "An empty song should keep the chosen split, clamped to the playable range.");

        // One short note far below a long one-hand passage must not create a left hand out of nothing.
        var stray = Notes([.. Enumerable.Range(60, 5).Select(pitch => (pitch, 1.0)), (30, .04)]);
        Assert(HandSplit.Infer(stray, 62) == 62, "A stray short note must not move the split point.");
        // Weighting: a slow bass line of long notes counts as much as a fast melody of short ones.
        var weighted = Notes([.. Enumerable.Range(45, 4).Select(pitch => (pitch, 2.0)), .. Enumerable.Range(76, 16).Select(pitch => (pitch, .25))]);
        Assert(HandSplit.Infer(weighted, 55) == HandSplit.MiddleC, "A slow bass under a fast melody should still split at middle C.");
        // Hands that overlap through a smooth run of notes cannot be told apart from a single line.
        var overlapping = Notes(Enumerable.Range(48, 20).Select(pitch => (pitch, .5)));
        Assert(HandSplit.Infer(overlapping, 58) == 58, "A song whose hands overlap through a smooth run should keep the chosen split.");
        var narrowGap = Notes([.. Enumerable.Range(50, 6).Select(pitch => (pitch, 1.0)), .. Enumerable.Range(60, 6).Select(pitch => (pitch, 1.0))]);
        Assert(HandSplit.Infer(narrowGap, 54) == 54, "Four empty semitones between the hands are too few to read as a hand separation.");
        var justEnough = Notes([.. Enumerable.Range(50, 6).Select(pitch => (pitch, 1.0)), .. Enumerable.Range(61, 6).Select(pitch => (pitch, 1.0))]);
        Assert(HandSplit.Infer(justEnough, 54) == HandSplit.MiddleC, "Five empty semitones between the hands are enough to read as a hand separation.");
        Results.Add("PASS hand split: two-hand clustering with middle-C tie-break, one-hand and stray-note protection, weighting by sounding time and a deterministic answer.");
    }

    /// <summary>
    /// Share codes are pure data, so they are checked without a window: a look survives the round trip,
    /// the code carries the version prefix and no local image path, a wrapped code pasted back from a chat
    /// still reads, and every way a code can be wrong is refused with a reason instead of throwing.
    /// </summary>
    private static void VerifyPresetShareCodes()
    {
        var look = VisualPresets.FindBuiltIn("Ice Crystal")!.Settings.Clone();
        look.NoteGlow = 123; look.HandSplitPitch = 55; look.BackgroundImagePath = @"C:\pictures\mine.png"; look.BackgroundMode = "Image";
        var code = VisualPresetShare.Encode(look);
        Assert(code.StartsWith(VisualPresetShare.Prefix, StringComparison.Ordinal) && code.Length > 80 && !code.Contains('+') && !code.Contains('/') && !code.Contains('\n'),
            "A share code should start with the version prefix and be one line of URL-safe text.");

        Assert(VisualPresetShare.TryDecode(code, out var decoded, out var reason) && reason.Length == 0, "A code that was just produced must decode.");
        Assert(decoded.NoteGlow == 123 && decoded.HandSplitPitch == 55 && decoded.NoteStyle == look.NoteStyle && decoded.PresetName == look.PresetName,
            "A decoded look should carry every setting of the original, so the two machines render the same stage.");
        Assert(decoded.BackgroundImagePath == "" && decoded.BackgroundMode == "Solid",
            "A share code must not carry a local image path; a look that used an image falls back to its solid background.");

        var wrapped = VisualPresetShare.Prefix + "\n  " + code[VisualPresetShare.Prefix.Length..] + "\n";
        Assert(VisualPresetShare.TryDecode(wrapped, out var unwrapped, out _) && unwrapped.NoteGlow == 123,
            "A code pasted back from a chat window, wrapped and indented, should still read.");

        Assert(!VisualPresetShare.TryDecode("", out _, out var empty) && empty == "The code is empty."
                && !VisualPresetShare.TryDecode(null, out _, out var missing) && missing == "The code is empty.",
            "An empty code should be refused with the reason for it.");
        Assert(!VisualPresetShare.TryDecode("hello there", out _, out var foreign) && foreign == "This does not look like a Keyflow look code."
                && !VisualPresetShare.TryDecode("KEYFLOW-LOOK-2:AAAA", out _, out var other) && other == "The code was made by a different version of Keyflow.",
            "Text that is not a Keyflow look, and a code from another format version, should be refused with different reasons.");
        Assert(!VisualPresetShare.TryDecode(VisualPresetShare.Prefix + "not base64!!", out _, out var damaged) && damaged == "The code is damaged: its text is not base64."
                && !VisualPresetShare.TryDecode(VisualPresetShare.Prefix + "AAAA", out _, out var undecodable) && undecodable == "The code is damaged: its payload cannot be decompressed.",
            "A damaged code should be refused and say whether the text or the payload was the problem.");
        var junk = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not gzipped json")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert(!VisualPresetShare.TryDecode(VisualPresetShare.Prefix + junk, out _, out var payload) && payload == "The code is damaged: its payload cannot be decompressed.",
            "Base64 that is not a gzipped look should be refused as a damaged payload.");
        var tooLong = VisualPresetShare.Prefix + new string('A', VisualPresetShare.MaxCodeLength);
        Assert(!VisualPresetShare.TryDecode(tooLong, out _, out var oversize) && oversize == "The code is longer than a Keyflow look code can be.",
            "A code beyond the size limit should be refused before anything is decoded.");

        // A hand-edited payload is clamped like a settings file, so no code can reach an impossible state.
        var crazy = VisualPresets.NeonViolet(); crazy.NoteFallSpeed = 900_000; crazy.ParticleAmount = 900; crazy.Palette = "not-a-palette";
        Assert(VisualPresetShare.TryDecode(VisualPresetShare.Encode(crazy), out var clamped, out _)
                && clamped.NoteFallSpeed == 1000 && clamped.ParticleAmount == 120 && clamped.Palette == "Spectrum",
            "A look whose values are out of range should arrive clamped, exactly like a loaded settings file.");
        Results.Add("PASS look share codes: version-prefixed URL-safe text, exact round trip without the local image path, wrapped codes tolerated, and empty, foreign, oversized and damaged codes refused with a reason.");
    }

    private static void VerifyVisualSettings()
    {
        // Automated runs must not write to the folder that holds the user's own look and presets.
        var realDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keyflow");
        Assert(!string.Equals(Path.GetFullPath(PianoVisualSettingsStore.SettingsDirectory), Path.GetFullPath(realDirectory), StringComparison.OrdinalIgnoreCase)
                && PianoVisualSettingsStore.SettingsDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                && PianoVisualSettingsStore.SettingsPath.StartsWith(PianoVisualSettingsStore.SettingsDirectory, StringComparison.OrdinalIgnoreCase)
                && VisualPresetStore.Default.Directory.StartsWith(PianoVisualSettingsStore.SettingsDirectory, StringComparison.OrdinalIgnoreCase),
            $"A verification run must keep its fixtures out of the user's settings folder (using {PianoVisualSettingsStore.SettingsDirectory}).");
        var defaults = new PianoVisualSettings();
        Assert(!defaults.BackgroundGradient && !defaults.BackgroundGuide && !defaults.ShowStars && defaults.BackgroundImagePath == "", "A fresh visual profile should open on a black stage with no image or decorative background layers.");
        var migrated = PianoVisualSettings.FromJson("{\"BackgroundGradient\":true,\"BackgroundGuide\":true,\"ShowStars\":true,\"BackgroundImagePath\":\"C:\\\\piano.png\"}");
        Assert(!migrated.BackgroundGradient && !migrated.BackgroundGuide && !migrated.ShowStars && migrated.BackgroundAppearanceVersion == 2 && migrated.BackgroundImagePath == "C:\\piano.png" && migrated.BackgroundMode == "Image",
            "Legacy settings should switch to the black stage while preserving an optional selected image path and enabling image mode for it.");
        var settings = new PianoVisualSettings { NoteFallSpeed = 5000, ParticleAmount = 500, Palette = "not-a-palette", NoteGlow = 126.5, NoteStyle = "Plasma", NoteDirection = "Sideways", ColorMode = "??", KeyboardStyle = "", BackgroundMode = "Blue", ShadingQuality = "Ultra", TrackColors = ["#FFFFFF"] };
        settings.Clamp();
        Assert(settings.NoteFallSpeed == 1000 && settings.ParticleAmount == 120 && settings.Palette == "Spectrum" && settings.NoteStyle == "Neon" && settings.NoteDirection == "Down" && settings.ColorMode == "Gradient" && settings.KeyboardStyle == "Studio" && settings.BackgroundMode == "Solid" && settings.ShadingQuality == "Balanced" && settings.TrackColors.Count == 8 && settings.TrackColors[0] == "#FFFFFF",
            "Visual settings should clamp unsafe ranges, reject unknown palettes, styles, shading levels and modes, and pad the track palette.");
        var restored = PianoVisualSettings.FromJson(settings.ToJson());
        Assert(restored.NoteGlow == 126.5 && restored.ParticleAmount == 120 && restored.Palette == "Spectrum" && restored.NoteDirection == "Down" && restored.TrackColors.SequenceEqual(settings.TrackColors), "Visual settings should round-trip through the persisted JSON format.");
        var legacyStyleIds = new[] { "Solid", "Neon", "Glass", "Fire" };
        Assert(PianoVisualSettings.NoteStyles.Take(legacyStyleIds.Length).SequenceEqual(legacyStyleIds)
                && PianoVisualSettings.NoteStyles.SequenceEqual([.. legacyStyleIds, "Gem", "CrystalOutline"]),
            "New note materials must be appended so the four shipped GPU style IDs keep their original values.");
        foreach (var material in new[] { "Gem", "CrystalOutline" })
        {
            var roundTrip = PianoVisualSettings.FromJson(new PianoVisualSettings { NoteStyle = material, Notes3D = true }.ToJson());
            Assert(roundTrip.NoteStyle == material && roundTrip.Notes3D,
                $"The new material {material} should survive the existing JSON format without a schema migration.");
        }
        VerifyEffectContractPilot();
        VerifyAccessibilityTogglePolicy();
        VerifyImpactFxPilot();
        VerifyFallingPilot();
        VerifyHoldPilot();
        VerifyAmbientPilot();
        VerifyAdapterSelection();
        Run(nameof(VerifyPresets), () => VerifyPresets());
        Assert(ColorPickerWindow.FromHsv(0, 1, 1) == Colors.Red && ColorPickerWindow.FromHsv(120, 1, 1) == Colors.Lime && ColorPickerWindow.FromHsv(240, 1, 1) == Colors.Blue, "The color picker should correctly convert the primary HSV hues.");
        var purple = ColorPickerWindow.ToHsv(Color.FromRgb(128, 0, 128));
        Assert(Math.Abs(purple.Hue - 300) < .01 && Math.Abs(purple.Saturation - 1) < .01 && ColorPickerWindow.ToHex(ColorPickerWindow.FromHsv(purple.Hue, purple.Saturation, purple.Value)) == "#800080", "The color picker should round-trip custom RGB colors through HSV and hex.");
        Results.Add("PASS stage settings: automated runs isolated from the user settings folder, black background defaults/migration, color picker HSV/hex conversion, range limits and JSON round-trip.");
    }

    /// <summary>
    /// Cross-checks the typed Gem/Crystal Outline pilot against the shipped setting strings, GPU indices and
    /// existing clamps. This verifies metadata compatibility only; the separate WPF/GPU image checks remain
    /// responsible for rendering evidence, while recording and AR retain their conservative contract states.
    /// </summary>
    private static void VerifyEffectContractPilot()
    {
        var contracts = EffectContractCatalog.NoteMaterialPilot;
        Assert(EffectContractCatalog.IsPilotWellFormed && contracts.Count == 3,
            "The typed note-material pilot should contain the three well-formed, uniquely aliased materials.");
        Assert(AccessibilityContractResolver.IsCatalogWellFormed,
            "The global accessibility toggle catalogue should be stable, unique and off by default.");

        foreach (var contract in contracts)
        {
            var alias = contract.LegacyAliases.Single();
            Assert(EffectContractCatalog.FindByLegacyAlias(alias)?.Id == contract.Id
                    && EffectContractCatalog.FindById(contract.Id)?.LegacyAliases.Contains(alias) == true,
                $"The stable contract ID {contract.Id} should resolve to its exact persisted style alias {alias}.");
            Assert(contract.GpuStyleId >= 0 && contract.GpuStyleId < PianoVisualSettings.NoteStyles.Length
                    && PianoVisualSettings.NoteStyles[contract.GpuStyleId] == alias,
                $"The contract for {alias} must keep the GPU style ID already assigned by NoteStyles.");
            var jsonRoundTrip = PianoVisualSettings.FromJson(new PianoVisualSettings { NoteStyle = alias }.ToJson());
            Assert(jsonRoundTrip.NoteStyle == alias,
                $"Adding the typed contract must not migrate the existing JSON value {alias}.");
            Assert(contract.Phase == EffectPhase.NoteMaterial
                    && contract.Trigger == EffectTrigger.NoteBodyRendered
                    && contract.Lifecycle == EffectLifecycle.WhileNoteVisible
                    && contract.Anchor == EffectAnchor.NoteBody
                    && contract.Clock == EffectClock.None
                    && contract.SeedPolicy == EffectSeedPolicy.None,
                $"The {alias} material should be a deterministic, note-body effect with an explicit render lifecycle.");
            Assert(contract.Capabilities.WpfStage.State == EffectBackendState.Native
                    && contract.Capabilities.GpuStage.State == EffectBackendState.Native
                    && contract.Capabilities.Recording.State == EffectBackendState.Unverified
                    && contract.Capabilities.ArOverlay.State == EffectBackendState.Fallback
                    && contract.Capabilities.ArOverlay.FallbackId == "ar.note.flat-quad",
                $"The {alias} contract must separate native WPF/GPU, unverified recording, and the actual flat AR fallback.");
            Assert(contract.Accessibility.FlashRisk == EffectFlashRisk.None
                    && contract.Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.NoIndependentAnimation
                    && contract.Accessibility.NonColorCue != EffectShapeCue.None,
                $"The {alias} material should declare its intrinsic flash/motion behavior and a non-color shape cue.");
            Assert(new[] { contract.Cost.WpfStage, contract.Cost.Recording, contract.Cost.ArOverlay }
                    .All(cost => cost.State == EffectCostState.Unmeasured && cost.P95Milliseconds is null && cost.IsWellFormed),
                $"The {alias} contract must keep WPF/recording/AR costs unmeasured until their own evidence exists.");
            var expectedGpuP95 = alias == "Gem" ? 0.02688 : alias == "Solid" ? 0.02356 : 0.0268;
            Assert(contract.Cost.GpuStage.State == EffectCostState.Measured
                    && contract.Cost.GpuStage.P95Milliseconds == expectedGpuP95
                    && contract.Cost.GpuStage.EvidenceId is { Length: > 0 } evidence
                    && evidence.Contains("NOTE-MATERIAL-GPU-BENCHMARK", StringComparison.Ordinal)
                    && contract.Cost.GpuStage.IsWellFormed,
                $"The {alias} GPU-stage cost must be the reviewed physical-adapter measurement cited from docs/NOTE-MATERIAL-GPU-BENCHMARK.md, not an invented number.");
            var defaultLook = AccessibilityContractResolver.Resolve(contract.Accessibility, false, false, false);
            Assert(defaultLook.Flash == FlashSuppression.NoChange
                    && defaultLook.Motion == MotionSuppression.NoChange
                    && !defaultLook.NeedsNonColorCueWork && defaultLook.IsWellFormed,
                $"The {alias} material must keep its current look while every accessibility toggle is off.");
            Assert(AccessibilityContractResolver.Resolve(contract.Accessibility, false, true, false).Flash != FlashSuppression.NoChange
                    || contract.Accessibility.FlashRisk == EffectFlashRisk.None,
                $"The {alias} flash-safety resolution must act on any intrinsic flash under Reduce Flashes.");

            foreach (var parameter in contract.Parameters)
            {
                var expectedGpuBinding = alias == "CrystalOutline" && parameter.SettingName == "NoteRefraction"
                    ? EffectParameterBindingState.Ignored
                    : EffectParameterBindingState.Applied;
                Assert(parameter.Bindings.WpfStage == EffectParameterBindingState.Applied
                        && parameter.Bindings.GpuStage == expectedGpuBinding
                        && parameter.Bindings.Recording == EffectParameterBindingState.Unverified
                        && parameter.Bindings.ArOverlay == EffectParameterBindingState.Ignored,
                    $"The {alias} input {parameter.SettingName} should describe its actual per-output binding, including AR's flat fallback.");
                var property = typeof(PianoVisualSettings).GetProperty(parameter.SettingName);
                Assert(property is not null, $"The {alias} parameter {parameter.SettingName} should refer to an existing setting.");
                if (property is null) continue;
                if (parameter.Kind == EffectParameterKind.Boolean)
                {
                    Assert(property.PropertyType == typeof(bool) && parameter.Minimum is null && parameter.Maximum is null,
                        $"The {alias} toggle {parameter.SettingName} should be Boolean and have no numeric clamp.");
                    continue;
                }

                var hasRange = property.PropertyType == typeof(double) && parameter.Minimum is double && parameter.Maximum is double;
                Assert(hasRange, $"The {alias} numeric parameter {parameter.SettingName} should have a double setting and a finite clamp range.");
                if (!hasRange) continue;
                var below = new PianoVisualSettings();
                property.SetValue(below, parameter.Minimum!.Value - 1000);
                below.Clamp();
                var above = new PianoVisualSettings();
                property.SetValue(above, parameter.Maximum!.Value + 1000);
                above.Clamp();
                Assert((double)property.GetValue(below)! == parameter.Minimum!.Value
                        && (double)property.GetValue(above)! == parameter.Maximum!.Value,
                    $"The existing settings clamp for {parameter.SettingName} should match the {alias} contract range {parameter.Minimum}..{parameter.Maximum}.");
            }
        }

        Results.Add("PASS effect contract metadata: Gem and CrystalOutline retain their exact JSON aliases and GPU IDs; existing clamps match; recording stays Unverified, AR names the flat-quad fallback, and isolated cost remains unmeasured. This is metadata coherence, not a new renderer/performance gate.");
    }

    /// <summary>
    /// Cross-checks the impact-FX contract pilot (wave/flash/burst/morph) against the settings model and the
    /// safety resolver: IDs unique and well-formed, parameters name real settings with the right kind, WPF/GPU
    /// Native matches the code both renderers ship, every cost stays Unmeasured, and Reduce Flashes acts on
    /// the flash by its own risk while leaving the flash-less wave alone and conservatively suppressing the
    /// un-analysed spark burst.
    /// </summary>
    private static void VerifyImpactFxPilot()
    {
        var pilot = EffectContractCatalog.ImpactFxPilot;
        Assert(EffectContractCatalog.IsImpactPilotWellFormed && pilot.Count == 4,
            "The impact-FX pilot should contain the four well-formed, uniquely identified families.");
        foreach (var contract in pilot)
        {
            Assert(contract.Phase == EffectPhase.Impact
                    && contract.Trigger == EffectTrigger.NoteOn
                    && contract.Lifecycle == EffectLifecycle.FixedDuration
                    && contract.Clock == EffectClock.RenderElapsed,
                $"{contract.Id} should be a fixed-duration, note-on impact effect on the render clock.");
            Assert(contract.Capabilities.WpfStage.State == EffectBackendState.Native
                    && contract.Capabilities.GpuStage.State == EffectBackendState.Native
                    && contract.Capabilities.Recording.State == EffectBackendState.Unverified
                    && contract.Capabilities.ArOverlay.State == EffectBackendState.Unverified,
                $"{contract.Id} is drawn by both stage renderers in shipped code; recording and AR stay Unverified.");
            Assert(new[] { contract.Cost.WpfStage, contract.Cost.GpuStage, contract.Cost.Recording, contract.Cost.ArOverlay }
                    .All(cost => cost.State == EffectCostState.Unmeasured && cost.P95Milliseconds is null),
                $"{contract.Id} must not carry an invented cost: no isolated probe has measured it.");
            Assert(contract.Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.NoIndependentAnimation,
                $"{contract.Id} is event-driven decay with no idle animation.");
            foreach (var parameter in contract.Parameters)
            {
                var property = typeof(PianoVisualSettings).GetProperty(parameter.SettingName);
                Assert(property is not null, $"{contract.Id} parameter {parameter.SettingName} should refer to an existing setting.");
                if (property is null) continue;
                var kindOk = parameter.Kind switch
                {
                    EffectParameterKind.Boolean => property.PropertyType == typeof(bool),
                    EffectParameterKind.Choice => property.PropertyType == typeof(string),
                    EffectParameterKind.Percentage => property.PropertyType == typeof(double),
                    _ => false,
                };
                Assert(kindOk, $"{contract.Id} parameter {parameter.SettingName} should declare the setting's real type.");
            }
        }
        var byId = pilot.ToDictionary(contract => contract.Id, StringComparer.Ordinal);
        Assert(AccessibilityContractResolver.Resolve(byId["impact.flash"].Accessibility, false, true, false).Flash != FlashSuppression.NoChange,
            "Reduce Flashes must act on the impact flash by its declared Local risk.");
        Assert(AccessibilityContractResolver.Resolve(byId["impact.wave"].Accessibility, false, true, false).Flash == FlashSuppression.NoChange,
            "Reduce Flashes must leave the flash-less impact wave alone.");
        Assert(AccessibilityContractResolver.Resolve(byId["impact.burst"].Accessibility, false, true, false).Flash == FlashSuppression.Suppress,
            "The un-analysed spark burst must resolve conservatively to suppression under Reduce Flashes.");
        Assert(AccessibilityContractResolver.Resolve(byId["impact.morph"].Accessibility, false, false, false).Flash == FlashSuppression.NoChange
                && AccessibilityContractResolver.Resolve(byId["impact.morph"].Accessibility, false, false, false).Motion == MotionSuppression.NoChange,
            "The key morph should keep its current behavior while every accessibility toggle is off.");
        Results.Add("PASS impact-FX contract metadata: wave/flash/burst/morph name their real settings with the right kinds, both stage renderers are native, recording/AR stay Unverified, all costs stay Unmeasured, and the flash-safety resolution acts exactly on the declared risks. Metadata coherence only; no renderer changed.");
    }

    /// <summary>
    /// Cross-checks the falling-FX pilot: trail/ghost derive from the note's travel, pulse and shimmer own
    /// their clock; the shimmer is honestly GPU-only (the WPF stage never reads NoteShimmer) and therefore
    /// Unsupported there; Reduced Motion freezes exactly the intrinsically animated families; every cost
    /// stays Unmeasured. Metadata coherence only; no renderer changed.
    /// </summary>
    private static void VerifyFallingPilot()
    {
        var pilot = EffectContractCatalog.FallingFxPilot;
        Assert(EffectContractCatalog.IsFallingPilotWellFormed && pilot.Count == 3,
            "The falling-FX pilot should contain the three well-formed, uniquely identified families.");
        var byId = pilot.ToDictionary(contract => contract.Id, StringComparer.Ordinal);
        Assert(byId["falling.trail"].Capabilities.WpfStage.State == EffectBackendState.Native
                && byId["falling.trail"].Capabilities.GpuStage.State == EffectBackendState.Native
                && byId["falling.pulse"].Capabilities.WpfStage.State == EffectBackendState.Native
                && byId["falling.pulse"].Capabilities.GpuStage.State == EffectBackendState.Native,
            "Trail and pulse are drawn by both stage renderers in shipped code.");
        Assert(byId["falling.shimmer"].Capabilities.WpfStage.State == EffectBackendState.Unsupported
                && byId["falling.shimmer"].Capabilities.GpuStage.State == EffectBackendState.Native,
            "The shimmer must stay honest: GPU-native, and Unsupported on the WPF stage that never reads NoteShimmer.");
        Assert(byId["falling.trail"].Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.NoIndependentAnimation
                && byId["falling.pulse"].Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.IntrinsicAnimation
                && byId["falling.shimmer"].Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.IntrinsicAnimation,
            "Trails derive from the note's travel; the pulse and the shimmer animate on their own clock.");
        Assert(AccessibilityContractResolver.Resolve(byId["falling.pulse"].Accessibility, true, false, false).Motion == MotionSuppression.FreezeIntrinsicAnimation
                && AccessibilityContractResolver.Resolve(byId["falling.shimmer"].Accessibility, true, false, false).Motion == MotionSuppression.FreezeIntrinsicAnimation
                && AccessibilityContractResolver.Resolve(byId["falling.trail"].Accessibility, true, false, false).Motion == MotionSuppression.NoChange,
            "Reduced Motion must freeze exactly the intrinsically animated families and leave the travel-derived trail alone.");
        Assert(AccessibilityContractResolver.Resolve(byId["falling.shimmer"].Accessibility, false, true, false).Flash == FlashSuppression.Suppress
                && AccessibilityContractResolver.Resolve(byId["falling.trail"].Accessibility, false, true, false).Flash == FlashSuppression.NoChange,
            "The twinkling shimmer's un-analysed flash risk resolves conservatively; the flash-less trail stays untouched.");
        foreach (var contract in pilot)
        {
            Assert(new[] { contract.Cost.WpfStage, contract.Cost.GpuStage, contract.Cost.Recording, contract.Cost.ArOverlay }
                    .All(cost => cost.State == EffectCostState.Unmeasured && cost.P95Milliseconds is null),
                $"{contract.Id} must not carry an invented cost: no isolated probe has measured it.");
            foreach (var parameter in contract.Parameters)
            {
                var property = typeof(PianoVisualSettings).GetProperty(parameter.SettingName);
                Assert(property is not null, $"{contract.Id} parameter {parameter.SettingName} should refer to an existing setting.");
                if (property is null) continue;
                var kindOk = parameter.Kind switch
                {
                    EffectParameterKind.Boolean => property.PropertyType == typeof(bool),
                    EffectParameterKind.Choice => property.PropertyType == typeof(string),
                    EffectParameterKind.Percentage => property.PropertyType == typeof(double),
                    _ => false,
                };
                Assert(kindOk, $"{contract.Id} parameter {parameter.SettingName} should declare the setting's real type.");
            }
        }
        Results.Add("PASS falling-FX contract metadata: trail/ghost travel-derived, pulse/shimmer own their clock and freeze under Reduced Motion, the shimmer is honestly GPU-only with WPF Unsupported, the shimmer's flash risk resolves conservatively, and no cost is invented. Metadata coherence only; no renderer changed.");
    }

    /// <summary>
    /// Cross-checks the hold-FX pilot: the bar mirrors the note's remaining duration while breathing,
    /// vibration, hue cycling and arc flicker own their clock and freeze under Reduced Motion; the arc's
    /// un-analysed flicker resolves to suppression under Reduce Flashes; both renderers are native for all
    /// five and no cost is invented. Metadata coherence only; no renderer changed.
    /// </summary>
    private static void VerifyHoldPilot()
    {
        var pilot = EffectContractCatalog.HoldFxPilot;
        Assert(EffectContractCatalog.IsHoldPilotWellFormed && pilot.Count == 5,
            "The hold-FX pilot should contain the five well-formed, uniquely identified families.");
        var byId = pilot.ToDictionary(contract => contract.Id, StringComparer.Ordinal);
        foreach (var contract in pilot)
        {
            Assert(contract.Phase == EffectPhase.Hold && contract.Trigger == EffectTrigger.NoteHeld
                    && contract.Lifecycle == EffectLifecycle.WhileNoteHeld,
                $"{contract.Id} should live exactly as long as the key stays down.");
            Assert(contract.Capabilities.WpfStage.State == EffectBackendState.Native
                    && contract.Capabilities.GpuStage.State == EffectBackendState.Native
                    && contract.Capabilities.Recording.State == EffectBackendState.Unverified
                    && contract.Capabilities.ArOverlay.State == EffectBackendState.Unverified,
                $"{contract.Id} is drawn by both stage renderers in shipped code; recording and AR stay Unverified.");
            Assert(new[] { contract.Cost.WpfStage, contract.Cost.GpuStage, contract.Cost.Recording, contract.Cost.ArOverlay }
                    .All(cost => cost.State == EffectCostState.Unmeasured && cost.P95Milliseconds is null),
                $"{contract.Id} must not carry an invented cost.");
            foreach (var parameter in contract.Parameters)
            {
                var property = typeof(PianoVisualSettings).GetProperty(parameter.SettingName);
                Assert(property is not null, $"{contract.Id} parameter {parameter.SettingName} should refer to an existing setting.");
                if (property is null) continue;
                var kindOk = parameter.Kind switch
                {
                    EffectParameterKind.Boolean => property.PropertyType == typeof(bool),
                    EffectParameterKind.Choice => property.PropertyType == typeof(string),
                    EffectParameterKind.Percentage => property.PropertyType == typeof(double),
                    _ => false,
                };
                Assert(kindOk, $"{contract.Id} parameter {parameter.SettingName} should declare the setting's real type.");
            }
        }
        Assert(byId["hold.bar"].Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.NoIndependentAnimation
                && byId["hold.bar"].Clock == EffectClock.SongPosition,
            "The hold bar mirrors the note's remaining duration; it owns no clock of its own.");
        foreach (var id in new[] { "hold.breath", "hold.vibration", "hold.color-cycle", "hold.arc" })
        {
            Assert(byId[id].Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.IntrinsicAnimation,
                $"{id} animates on its own clock and must be classified as intrinsic animation.");
            Assert(AccessibilityContractResolver.Resolve(byId[id].Accessibility, true, false, false).Motion == MotionSuppression.FreezeIntrinsicAnimation,
                $"Reduced Motion must freeze {id}.");
        }
        Assert(AccessibilityContractResolver.Resolve(byId["hold.bar"].Accessibility, true, false, false).Motion == MotionSuppression.NoChange,
            "Reduced Motion must leave the duration-derived hold bar alone.");
        Assert(AccessibilityContractResolver.Resolve(byId["hold.arc"].Accessibility, false, true, false).Flash == FlashSuppression.Suppress,
            "The flickering arc's un-analysed flash risk resolves conservatively to suppression.");
        Assert(AccessibilityContractResolver.Resolve(byId["hold.breath"].Accessibility, false, true, false).Flash == FlashSuppression.NoChange,
            "Reduce Flashes must leave the flash-less breathing glow alone.");
        Results.Add("PASS hold-FX contract metadata: bar/hold-duration-derived, breath/vibration/color-cycle/arc own their clock and freeze under Reduced Motion, the arc's flicker resolves conservatively under Reduce Flashes, both renderers native, no cost invented. Metadata coherence only; no renderer changed.");
    }

    /// <summary>
    /// Cross-checks the ambient/atmosphere pilot: every member owns its clock and freezes under Reduced
    /// Motion; shooting stars are the second GPU-only member (the WPF stage has no meteor pass) and stay
    /// Unsupported there; the star twinkle's un-analysed flash risk resolves conservatively while the slow
    /// layers keep the sky untouched under Reduce Flashes; no cost is invented. Metadata coherence only.
    /// </summary>
    private static void VerifyAmbientPilot()
    {
        var pilot = EffectContractCatalog.AmbientFxPilot;
        Assert(EffectContractCatalog.IsAmbientPilotWellFormed && pilot.Count == 8,
            "The ambient pilot should contain the eight well-formed, uniquely identified families.");
        var byId = pilot.ToDictionary(contract => contract.Id, StringComparer.Ordinal);
        foreach (var contract in pilot)
        {
            Assert(contract.Phase == EffectPhase.Ambient && contract.Trigger == EffectTrigger.SceneTick
                    && contract.Lifecycle == EffectLifecycle.Continuous && contract.Anchor == EffectAnchor.Stage,
                $"{contract.Id} should be a continuous stage-wide layer.");
            Assert(contract.Accessibility.ReducedMotionBehavior == EffectReducedMotionBehavior.IntrinsicAnimation
                    && AccessibilityContractResolver.Resolve(contract.Accessibility, true, false, false).Motion == MotionSuppression.FreezeIntrinsicAnimation,
                $"Reduced Motion must freeze {contract.Id}: every ambient member owns its clock.");
            Assert(new[] { contract.Cost.WpfStage, contract.Cost.GpuStage, contract.Cost.Recording, contract.Cost.ArOverlay }
                    .All(cost => cost.State == EffectCostState.Unmeasured && cost.P95Milliseconds is null),
                $"{contract.Id} must not carry an invented cost.");
            foreach (var parameter in contract.Parameters)
            {
                var property = typeof(PianoVisualSettings).GetProperty(parameter.SettingName);
                Assert(property is not null, $"{contract.Id} parameter {parameter.SettingName} should refer to an existing setting.");
                if (property is null) continue;
                var kindOk = parameter.Kind switch
                {
                    EffectParameterKind.Boolean => property.PropertyType == typeof(bool),
                    EffectParameterKind.Choice => property.PropertyType == typeof(string),
                    EffectParameterKind.Percentage => property.PropertyType == typeof(double),
                    _ => false,
                };
                Assert(kindOk, $"{contract.Id} parameter {parameter.SettingName} should declare the setting's real type.");
            }
        }
        Assert(byId["atmosphere.shooting-stars"].Capabilities.WpfStage.State == EffectBackendState.Unsupported
                && byId["atmosphere.shooting-stars"].Capabilities.GpuStage.State == EffectBackendState.Native,
            "Shooting stars are GPU-only: the WPF stage has no meteor pass, so no Native claim may stand there.");
        foreach (var id in new[] { "ambient.energy", "ambient.nature", "ambient.light", "ambient.cosmic", "atmosphere.stars", "atmosphere.petals", "atmosphere.beams" })
        {
            Assert(byId[id].Capabilities.WpfStage.State == EffectBackendState.Native
                    && byId[id].Capabilities.GpuStage.State == EffectBackendState.Native,
                $"{id} is drawn by both stage renderers in shipped code.");
        }
        Assert(AccessibilityContractResolver.Resolve(byId["atmosphere.stars"].Accessibility, false, true, false).Flash == FlashSuppression.Suppress,
            "The twinkling star field's un-analysed flash risk resolves conservatively to suppression.");
        Assert(AccessibilityContractResolver.Resolve(byId["atmosphere.beams"].Accessibility, false, true, false).Flash == FlashSuppression.NoChange
                && AccessibilityContractResolver.Resolve(byId["ambient.energy"].Accessibility, false, true, false).Flash == FlashSuppression.NoChange,
            "Reduce Flashes must leave the slow, flash-less sky layers untouched.");
        Results.Add("PASS ambient contract metadata: the four ambient layers, stars, petals and beams are native on both renderers, shooting stars stay honestly GPU-only, every member freezes under Reduced Motion, the star twinkle resolves conservatively under Reduce Flashes, and no cost is invented. Metadata coherence only; no renderer changed.");
    }

    /// <summary>
    /// Verifies the persisted accessibility toggles and the settings-level override policy they drive: old JSON
    /// loads with every toggle off, toggles off reproduce the saved look byte-for-byte, and toggles on override
    /// exactly their reviewed channels on a copy without mutating the user's settings. High Contrast is
    /// metadata-only until renderer shape-cue work exists, which this check pins as an explicit decision.
    /// </summary>
    private static void VerifyAccessibilityTogglePolicy()
    {
        var legacyJson = string.Join('\n', new PianoVisualSettings { BloomIntensity = 50 }.ToJson()
            .Split('\n')
            .Where(line => !line.Contains("ReducedMotion", StringComparison.Ordinal)
                && !line.Contains("ReduceFlashes", StringComparison.Ordinal)
                && !line.Contains("HighContrast", StringComparison.Ordinal)));
        var legacy = PianoVisualSettings.FromJson(legacyJson);
        Assert(!legacyJson.Contains("ReducedMotion", StringComparison.Ordinal)
                && !legacy.ReducedMotion && !legacy.ReduceFlashes && !legacy.HighContrast
                && legacy.BloomIntensity == 50,
            "Accessibility keys saved by older builds must load with every toggle off and keep the saved look.");

        // User settings always arrive through FromJson (migrated, clamped), e.g. BackgroundAppearanceVersion
        // has already advanced to 2; the policy is checked against that loaded state.
        var busy = PianoVisualSettings.FromJson(new PianoVisualSettings
        {
            ShowImpactFlash = true, ImpactFlashIntensity = 80,
            BackgroundMotion = "Aurora", HaloPulse = true, FallingPulse = true, HoldBreath = true,
            NoteShimmer = true, ShowStars = true, ShootingStars = true, ShowPetals = true, ShowLightBeams = true,
            HoldVibration = true, HoldColorCycle = true, HoldElectricArc = true,
            AmbientEnergy = "Nebula", AmbientEnergyAmount = 60,
        }.ToJson());
        var identity = busy.WithAccessibilityTogglesApplied();
        Assert(identity.ToJson() == busy.ToJson() && !ReferenceEquals(identity, busy),
            "With every toggle off the applied settings must be an untouched copy, preserving the signed-off default look.");

        busy.ReducedMotion = true;
        var calmed = busy.WithAccessibilityTogglesApplied();
        Assert(calmed.BackgroundMotion == "None" && !calmed.HaloPulse && !calmed.FallingPulse && !calmed.HoldBreath
                && !calmed.NoteShimmer && !calmed.ShowStars && !calmed.ShootingStars && !calmed.ShowPetals && !calmed.ShowLightBeams
                && !calmed.HoldVibration && !calmed.HoldColorCycle && !calmed.HoldElectricArc && calmed.AmbientEnergyAmount == 0
                && calmed.ShowImpactFlash && calmed.ImpactFlashIntensity == 80,
            "Reduced Motion must freeze every catalogued self-animated family — pulses, shimmer, the whole ambient sky, vibration, colour cycle and arc — and leave the flash channel alone.");

        busy.ReducedMotion = false;
        busy.ReduceFlashes = true;
        var unflashed = busy.WithAccessibilityTogglesApplied();
        Assert(!unflashed.ShowImpactFlash && unflashed.ImpactFlashIntensity == 0
                && !unflashed.ShowEmbers && !unflashed.ShowStars
                && unflashed.BackgroundMotion == "Aurora" && unflashed.HaloPulse && unflashed.FallingPulse && unflashed.HoldBreath
                && unflashed.ShowPetals && unflashed.ShootingStars && unflashed.ShowLightBeams && unflashed.AmbientEnergyAmount == 60,
            "Reduce Flashes must silence the flash channel, suppress the un-analysed spark burst and star twinkle, and leave every motion-only layer exactly as the user set it.");
        Assert(unflashed.ImpactWaveIntensity == busy.ImpactWaveIntensity,
            "Reduce Flashes must leave the flash-less impact wave's intensity exactly as the user set it.");

        busy.ReduceFlashes = false;
        busy.HighContrast = true;
        var outlined = busy.WithAccessibilityTogglesApplied();
        Assert(outlined.NoteEdge == 160 && outlined.NoteEdgeWidth == 80
                && outlined.ShowImpactFlash && outlined.ImpactFlashIntensity == 80 && outlined.BackgroundMotion == "Aurora",
            "High Contrast must lift the note outline to the shape-cue floors and leave the flash and motion channels alone.");
        var alreadyStrong = PianoVisualSettings.FromJson(new PianoVisualSettings { NoteEdge = 185, NoteEdgeWidth = 90, HighContrast = true }.ToJson());
        var kept = alreadyStrong.WithAccessibilityTogglesApplied();
        Assert(kept.NoteEdge == 185 && kept.NoteEdgeWidth == 90,
            "High Contrast must keep an outline that is already stronger than the shape-cue floors.");

        Assert(busy.ShowImpactFlash && busy.ImpactFlashIntensity == 80 && busy.BackgroundMotion == "Aurora"
                && busy.HaloPulse && busy.FallingPulse && busy.HoldBreath,
            "Applying the accessibility policy must never mutate the user's own settings object.");

        Results.Add("PASS accessibility toggle policy: legacy JSON loads toggles-off, toggles-off keep the saved look byte-for-byte, Reduced Motion freezes every catalogued self-animated family (pulses, shimmer, ambient sky, stars, petals, meteors, beams, vibration, colour cycle, arc) while Reduce Flashes silences the flash channel and suppresses the un-analysed burst and star twinkle, High Contrast lifts the note outline to its shape-cue floors without weakening stronger looks, and user settings are never mutated. The stage chokepoint applies this policy to both renderers; the dock probe in the settings check proves the live wiring.");
    }

    /// <summary>
    /// --bench-adapter pins a hardware run to one GPU by name, so a hybrid laptop can be measured on its
    /// dGPU as well as its iGPU. The selection rules must be strict: only a unique case-insensitive
    /// substring selects an adapter; anything else keeps the OS default (no request) or fails the run with
    /// a message naming the candidates (no match, ambiguous match) — never measure a GPU silently.
    /// </summary>
    private static void VerifyAdapterSelection()
    {
        string[] hybrid = ["AMD Radeon(TM) Graphics", "NVIDIA GeForce RTX 4050 Laptop GPU"];
        Assert(GpuAdapterSelection.Select(hybrid, null).Index == -1 && GpuAdapterSelection.Select(hybrid, null).Problem is null
                && GpuAdapterSelection.Select(hybrid, "   ").Index == -1,
            "With no adapter request the benchmark must keep the OS default adapter.");
        Assert(GpuAdapterSelection.Select(hybrid, "rtx 4050").Index == 1 && GpuAdapterSelection.Select(hybrid, "radeon").Index == 0
                && GpuAdapterSelection.Select(hybrid, "rtx 4050").Problem is null,
            "A unique case-insensitive substring must select exactly that adapter.");
        var none = GpuAdapterSelection.Select(hybrid, "Intel Arc");
        var ambiguous = GpuAdapterSelection.Select(new[] { "NVIDIA GeForce RTX 4050", "NVIDIA GeForce RTX 4090" }, "NVIDIA");
        Assert(none.Index == -1 && none.Problem is not null && none.Problem.Contains("RTX 4050", StringComparison.Ordinal),
            "An adapter name matching nothing must fail with a message naming every adapter on the machine.");
        Assert(ambiguous.Index == -1 && ambiguous.Problem is not null && ambiguous.Problem.Contains("longer name", StringComparison.Ordinal),
            "An ambiguous adapter name must fail instead of picking a GPU at random.");
        Results.Add("PASS adapter selection: --bench-adapter resolves only a unique case-insensitive substring, keeps the OS default when not asked, and fails with the machine's adapter list on no match or an ambiguous one.");
    }

    /// <summary>
    /// A key that starts sounding needs its own ray-traced overlay tile (tens of milliseconds each). Pressing
    /// keys must not block the UI thread on that bake, and the tiles must still arrive shortly afterwards.
    /// </summary>
    private static void VerifyLitKeyTilesBakeInBackground()
    {
        var stage = new PianoStage();
        stage.Measure(new Size(1550, 900)); stage.Arrange(new Rect(0, 0, 1550, 900)); stage.UpdateLayout();
        stage.SetVisualSettings(new PianoVisualSettings());
        double DrawMilliseconds()
        {
            var timer = Stopwatch.StartNew();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) InvokeReturn(stage, "TryDrawShadedKeyboard", dc, 1550d, 200d, 690d);
            return timer.Elapsed.TotalMilliseconds;
        }
        DrawMilliseconds();   // the one-off base bake of the whole keyboard
        var tiles = (System.Collections.ICollection)Field(stage, "_shadedTiles");
        stage.SetState([], 0, false, new HashSet<int> { 48, 52, 55, 60, 64, 67 });
        var pressMilliseconds = DrawMilliseconds();
        Assert(pressMilliseconds < 100, $"Pressing six new keys must not block on their tile bakes ({pressMilliseconds:0.0} ms on the UI thread).");
        for (var wait = 0; wait < 240 && tiles.Count < 6; wait++)
        {
            stage.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(25);
        }
        Assert(tiles.Count >= 6, $"The overlay tiles of the sounding keys should be baked in the background and arrive ({tiles.Count} of 6).");
        Results.Add($"PASS lit-key tiles: a six-key press cost {pressMilliseconds:0.0} ms on the UI thread; tiles arrived from the background worker.");
    }

    /// <summary>Exercises the ray-traced keyboard: shading maths, the bake cache key and real pixel output.</summary>
    private static void VerifyShaderPipeline()
    {
        // ---- shading maths -------------------------------------------------------------------
        Assert(ShaderMath.ToLinear(0) == 0 && ShaderMath.ToLinear(255) > .99 && ShaderMath.EncodeSrgb(0, 0) == 0 && ShaderMath.EncodeSrgb(1, 0) == 255,
            "The sRGB transfer functions should map both ends of the range exactly.");
        Assert(Math.Abs(ShaderMath.EncodeSrgb(ShaderMath.ToLinear(186), 0) - 186) <= 1, "An 8-bit color should survive the linear/sRGB round trip.");
        Assert(ShaderMath.AcesTonemap(Vec3.Zero).X == 0 && ShaderMath.AcesTonemap(new Vec3(6, 6, 6)).X < 1 && ShaderMath.AcesTonemap(new Vec3(6, 6, 6)).X > .9,
            "The ACES filmic curve should compress highlights into the display range instead of clipping to pure white.");
        Assert(ShaderMath.DistributionGgx(.35, .85) > 0 && ShaderMath.GeometrySmith(.35, .6, .6) > 0
                && Math.Abs(ShaderMath.FresnelSchlick(1, new Vec3(.5, .5, .5)).X - .5) < 1e-9 && ShaderMath.FresnelSchlick(0, new Vec3(.5, .5, .5)).X == 1,
            "The GGX distribution, Smith geometry term and Schlick fresnel should stay physical.");
        Assert(ShaderMath.Hash(11, 23, 5) == ShaderMath.Hash(11, 23, 5) && ShaderMath.Hash(11, 23, 5) != ShaderMath.Hash(11, 24, 5),
            "Shadow and occlusion jitter must be deterministic so a cached bake does not shimmer between frames.");

        // ---- scene and bake cache key --------------------------------------------------------
        var look = VisualPresets.NeonViolet();
        var scene = PianoShaderScene.From(look, 640, 160, "Balanced");
        Assert(!PianoKeyboardRenderer.IsEnabled("Off") && PianoKeyboardRenderer.IsEnabled("Fast") && PianoKeyboardRenderer.IsEnabled("Balanced") && PianoKeyboardRenderer.IsEnabled("Cinematic"),
            "Only the Off shading level should bypass the ray-traced keyboard.");
        var unrelated = look.Clone(); unrelated.ParticleAmount = 99; unrelated.NoteFallSpeed = 800; unrelated.NoteGlow = 10;
        Assert(PianoShaderScene.From(unrelated, 640, 160, "Balanced").Signature() == scene.Signature(),
            "Settings the shader does not read must not invalidate the baked keyboard, or every unrelated slider would cost a re-bake.");
        var related = look.Clone(); related.ShaderShadows = 12;
        Assert(PianoShaderScene.From(related, 640, 160, "Balanced").Signature() != scene.Signature(),
            "Changing a shading parameter must produce a new signature so the keyboard re-bakes.");

        // ---- base bake -----------------------------------------------------------------------
        var baked = PianoKeyboardRenderer.Render(scene, new KeyLightState(), 0, 0, 640, 160, -1);
        Assert(baked is not null, "The keyboard bake should produce a bitmap.");
        if (baked is null) return;
        // Non-nullable copy so the local helper below does not have to re-prove the null state.
        BitmapSource bake = baked;
        var stride = bake.PixelWidth * 4;
        var buffer = new byte[stride * bake.PixelHeight];
        bake.CopyPixels(buffer, stride, 0);
        var darkest = 255; var brightest = 0; var opaque = 0;
        for (var i = 0; i < buffer.Length / 4; i++)
        {
            if (buffer[i * 4 + 3] == 255) opaque++;
            var luma = (buffer[i * 4] + buffer[i * 4 + 1] * 2 + buffer[i * 4 + 2]) / 4;
            if (luma < darkest) darkest = luma;
            if (luma > brightest) brightest = luma;
        }
        Assert(opaque == buffer.Length / 4, "The base keyboard bake must be fully opaque so it can cover the stage bed.");
        Assert(brightest - darkest > 60, $"A shaded keyboard must span a real luminance range (darkest={darkest}, brightest={brightest}).");

        double ColumnBand(double fromWorld, double toWorld)
        {
            var x0 = (int)(fromWorld / PianoShaderScene.WorldWidth * bake.PixelWidth);
            var x1 = (int)(toWorld / PianoShaderScene.WorldWidth * bake.PixelWidth);
            long total = 0; var count = 0;
            for (var y = bake.PixelHeight / 3; y < bake.PixelHeight * 2 / 3; y++)
                for (var x = x0; x < x1; x++)
                {
                    var offset = y * stride + x * 4;
                    total += (buffer[offset] + buffer[offset + 1] * 2 + buffer[offset + 2]) / 4; count++;
                }
            return count == 0 ? 0 : total / (double)count;
        }
        var ebony = ColumnBand(24.75, 25.25); var ivory = ColumnBand(25.45, 25.95);
        Assert(ivory > 20 && ebony < ivory * .8, $"Ebony keys must shade darker than the ivory beside them (ebony={ebony:0.0}, ivory={ivory:0.0}).");

        // ---- overlay tile for one sounding key ----------------------------------------------
        var lit = new KeyLightState(); lit.Light(60, Color.FromRgb(255, 40, 40), 1);
        var viewX = (int)(22.4 / PianoShaderScene.WorldWidth * 640);
        var viewWidth = (int)(2.2 / PianoShaderScene.WorldWidth * 640) * 2;
        var renderedTile = PianoKeyboardRenderer.Render(scene, lit, viewX, 0, viewWidth, 160, 60);
        Assert(renderedTile is not null, "A sounding key should produce an overlay tile.");
        if (renderedTile is null) return;
        BitmapSource tile = renderedTile;
        var tileStride = tile.PixelWidth * 4;
        var tilePixels = new byte[tileStride * tile.PixelHeight];
        tile.CopyPixels(tilePixels, tileStride, 0);
        var transparent = 0; var solid = 0;
        for (var i = 0; i < tilePixels.Length / 4; i++)
        {
            if (tilePixels[i * 4 + 3] == 0) transparent++;
            else if (tilePixels[i * 4 + 3] == 255) solid++;
        }
        Assert(transparent > 0 && solid > 0, $"An overlay tile must cover its own key opaquely and fade out before the neighbouring keys (opaque={solid}, clear={transparent}).");

        var pixelScale = tile.PixelWidth / (double)viewWidth;
        long warmth = 0; var compared = 0;
        for (var y = 0; y < tile.PixelHeight; y++)
        {
            for (var x = 0; x < tile.PixelWidth; x++)
            {
                var offset = y * tileStride + x * 4;
                if (tilePixels[offset + 3] != 255) continue;
                var baseX = (int)(viewX + x / pixelScale); var baseY = (int)(y / pixelScale);
                if (baseX >= bake.PixelWidth || baseY >= bake.PixelHeight) continue;
                var baseOffset = baseY * stride + baseX * 4;
                warmth += (tilePixels[offset + 2] - tilePixels[offset]) - (buffer[baseOffset + 2] - buffer[baseOffset]);
                compared++;
            }
        }
        Assert(compared > 100 && warmth / (double)compared > 10,
            $"A sounding key must come out warmer in its overlay tile than in the unlit base bake (delta={warmth / (double)Math.Max(1, compared):0.0} over {compared} pixels).");
        Results.Add("PASS shader pipeline: sRGB/ACES/GGX maths, deterministic jitter, bake cache signature, opaque shaded keyboard bake, ebony-vs-ivory light transport and per-key emissive overlay tiles.");
    }

    /// <summary>
    /// The hand tracker, read rather than watched: the skin rule on single colours, the hand it finds in a frame
    /// the check paints itself, the finger count on column profiles made by hand, and the key a position lands
    /// on. The frame is the same shape a camera hands over — 32-bit pixels, top row first — so what is measured
    /// here is what the pump measures from a real camera.
    /// </summary>
    /// <summary>
    /// The keyboard geometry has one home, <see cref="KeyboardLayout"/>, and the stage, the GPU engine and the shaded
    /// keys all read the layout being drawn from it: the full keyboard by default, and a different one the moment
    /// <see cref="KeyboardLayout.Use"/> says so. (The geometry itself is checked on any machine by
    /// <c>KeyboardLayoutTests</c>.) The layout is always put back, so a failure here cannot leak a keyboard into the
    /// checks after it.
    /// </summary>
    private static void VerifyKeyboardLayout()
    {
        var full = KeyboardLayout.Standard88;
        Assert(ReferenceEquals(KeyboardLayout.Active, full), "A fresh run draws the full keyboard.");
        Assert(full.WhiteCount == PianoShaderScene.WhiteKeys && full.FirstPitch == PianoStage.FirstPitch && full.KeyCount == PianoStage.KeyCount,
            "The stage and the shaded keyboard should report the range of the layout being drawn.");
        for (var pitch = 0; pitch < 128; pitch++)
        {
            Assert(Math.Abs(PianoStage.KeyCenterOf(pitch) - full.CenterX(pitch)) < 1e-12,
                $"The stage should land pitch {pitch} where the layout puts it.");
            Assert(Math.Abs(full.WorldCenters[pitch] / full.WhiteCount - full.CenterX(pitch)) < 1e-9,
                $"The shaded keyboard should put pitch {pitch} where the layout puts it.");
        }
        var scene = PianoShaderScene.From(VisualPresets.NeonViolet(), 640, 160, "Balanced");
        Assert(ReferenceEquals(scene.Keys, full), "A scene keeps the keyboard it was built for.");
        Assert(HandTracker.KeyPitch(.5, full) == full.PitchAt(.5, false), "A tracked hand lands on the key the layout puts under that position.");

        // The setting: a known size names its layout, an unknown one falls back to the full keyboard, and a look
        // being applied never changes the player's keyboard.
        var sixtyOne = new PianoVisualSettings { KeyboardSize = "61" };
        Assert(sixtyOne.KeyboardLayoutChoice.KeyCount == 61 && sixtyOne.KeyboardLayoutChoice.FirstPitch == 36, "The size 61 should name the 61-key layout.");
        var odd = new PianoVisualSettings { KeyboardSize = "73" }; odd.Clamp();
        Assert(odd.KeyboardSize == "88" && odd.KeyboardLayoutChoice.KeyCount == 88, "A keyboard size the app does not draw should fall back to 88 keys.");
        var applied = new PianoVisualSettings { KeyboardSize = "61" }; applied.CopyFrom(new PianoVisualSettings { KeyboardSize = "49" });
        Assert(applied.KeyboardSize == "61", "Applying a look or a preset must keep the player's keyboard size.");
        Assert(PianoVisualSettings.KeyboardSizes.SequenceEqual(KeyboardLayout.All.Select(layout => layout.KeyCount.ToString())),
            "The sizes the settings offer should be exactly the layouts the app has.");

        // The overlay's own settings: defaults agree with ArOptions, wild values are pulled into range, and a look being
        // applied (a preset, a community look, a pasted code) neither switches the overlay on nor retunes it.
        var arDefault = new PianoVisualSettings();
        Assert(!arDefault.ArEnabled && arDefault.ArOptionsValue == ArOptions.Default,
            "A fresh install has the overlay off, and the settings' defaults are the ones ArOptions names.");
        var arWild = new PianoVisualSettings { ArEnabled = true, ArMode = "Karaoke", ArOpacity = double.NaN, ArLookahead = 99, ArKeyLight = -5, ArDelay = 9000 }; arWild.Clamp();
        Assert(arWild.ArEnabled && arWild.ArMode == "Learn" && arWild.ArOpacity == ArOptions.OpacityDefault && arWild.ArLookahead == ArOptions.LookaheadMax && arWild.ArKeyLight == ArOptions.KeyLightMin && arWild.ArDelay == ArOptions.DelayMax,
            "Out-of-range overlay values should be pulled into range when settings load.");
        var arKept = new PianoVisualSettings { ArEnabled = true, ArMode = "Perform", ArOpacity = 70, ArLookahead = 2, ArKeyLight = 30, ArDelay = 120 };
        arKept.CopyFrom(new PianoVisualSettings { ArEnabled = false, ArMode = "Learn", ArOpacity = 100, ArLookahead = 5, ArKeyLight = 90, ArDelay = 0 });
        Assert(arKept.ArEnabled && arKept.ArMode == "Perform" && arKept.ArOpacity == 70 && arKept.ArLookahead == 2 && arKept.ArKeyLight == 30 && arKept.ArDelay == 120,
            "Applying a look must keep every overlay setting.");
        Assert(MainWindow.FormatSetting(nameof(PianoVisualSettings.ArDelay), 120) == "120 ms" && MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.ArDelay), "85 ms", out var typedDelay) && typedDelay == 85,
            "The camera delay is shown and typed in milliseconds, not as a percentage.");
        var cameraKept = new PianoVisualSettings { CameraSourceLink = @"\\?\usb#vid_046d&pid_0825#1#{x}\global", CameraVideoPath = @"C:\clips\hands.mp4", CameraCorner = "Top left" };
        cameraKept.CopyFrom(new PianoVisualSettings { CameraSourceLink = "other", CameraVideoPath = "", CameraCorner = "Bottom right" });
        Assert(cameraKept.CameraSourceLink.StartsWith(@"\\?\usb", StringComparison.Ordinal) && cameraKept.CameraVideoPath == @"C:\clips\hands.mp4" && cameraKept.CameraCorner == "Bottom right",
            "Applying a look must keep the camera the player picked and the video file, and still change how the picture is framed.");
        Assert(VisualPresetShare.TryDecode(VisualPresetShare.Encode(cameraKept), out var cameraShared, out _) && cameraShared.CameraSourceLink == "" && cameraShared.CameraVideoPath == "" && cameraShared.CameraCorner == "Bottom right",
            "A shared look carries neither the camera's device path nor the video file's path.");
        var arCode = VisualPresetShare.Encode(arKept);
        Assert(VisualPresetShare.TryDecode(arCode, out var arShared, out _) && arShared.ArMode == "Perform" && arShared.ArLookahead == 2,
            "A look code carries the overlay values like any other setting, and reads them back in range.");
        var arJson = PianoVisualSettings.FromJson(arKept.ToJson());
        Assert(arJson.ArOptionsValue == arKept.ArOptionsValue, "The overlay settings should survive a save and a load.");

        try
        {
            var narrow = KeyboardLayout.ForKeyCount(61);
            var stageForSize = new PianoStage();
            stageForSize.SetVisualSettings(sixtyOne);
            Assert(ReferenceEquals(KeyboardLayout.Active, narrow), "Handing the stage a 61-key setting should make the 61-key layout the one being drawn.");
            KeyboardLayout.Use(full);
            Assert(KeyboardLayout.Use(narrow) && !KeyboardLayout.Use(narrow), "Using a layout reports the change once.");
            Assert(PianoStage.FirstPitch == 36 && PianoStage.KeyCount == 61, "The stage should follow the layout that is being drawn.");
            Assert(Math.Abs(PianoStage.KeyCenterOf(36) - .5 / 36) < 1e-12 && Math.Abs(PianoStage.KeyCenterOf(96) - 35.5 / 36) < 1e-12,
                "On a 61-key keyboard the lowest and highest keys should sit half a white key from the edges.");
            var gpu = new GpuSceneLayout(640, 360, .2f);
            Assert(ReferenceEquals(gpu.Keys, narrow) && Math.Abs(gpu.Lane - 640f / 61) < 1e-4 && Math.Abs(gpu.X(36) - 640 * .5 / 36) < 1e-3,
                "The GPU engine should lay a 61-key keyboard out in 61 lanes, from the same centres as the stage.");
            var other = PianoShaderScene.From(VisualPresets.NeonViolet(), 640, 160, "Balanced");
            Assert(ReferenceEquals(other.Keys, narrow) && other.Signature() != scene.Signature()
                    && !PianoShaderScene.SceneKey.Capture(other).Equals(PianoShaderScene.SceneKey.Capture(scene)),
                "A different keyboard is a different bake, in both the signature and the cache key.");
            Assert(ReferenceEquals(scene.Keys, full), "A scene built before the switch keeps drawing its own keyboard.");
            var bake = PianoKeyboardRenderer.Render(other, new KeyLightState(), 0, 0, 640, 160, -1);
            Assert(bake is not null && bake.PixelWidth > 0, "The 61-key keyboard should shade into a bitmap.");
        }
        finally { KeyboardLayout.Use(full); }
        Assert(PianoStage.KeyCount == 88 && ReferenceEquals(KeyboardLayout.Active, full), "The full keyboard is back after the check.");
        Results.Add("PASS keyboard layout: the full layout agrees with the stage and the shaded keyboard on the range and the centre of every key, and switching to 61 keys moves the stage, the GPU lanes and the shaded keyboard together, with a scene keeping the keyboard it was built for.");
    }

    private static void VerifyHandTracking()
    {
        // The skin rule: a light skin tone is skin, and the colours a stage or a wall is made of are not.
        Assert(HandTracker.IsSkin(120, 150, 200, 0), "A light skin tone should be read as skin.");
        Assert(!HandTracker.IsSkin(0, 255, 0, 14) && !HandTracker.IsSkin(255, 255, 255, 14) && !HandTracker.IsSkin(255, 0, 0, 14)
                && !HandTracker.IsSkin(128, 128, 128, 14) && !HandTracker.IsSkin(0, 0, 255, 14),
            "The key colour, white, grey and blue should not be read as skin, however wide the window is opened.");
        // Sensitivity really moves the window: a colour one step outside it is skin with the window wide and
        // nothing with it narrow, which is what the slider promises.
        var outside = (Blue: 0, Green: 0, Red: 0);
        foreach (var blue in new[] { 0, 40, 80, 120, 160, 200, 240 })
        {
            foreach (var green in new[] { 0, 40, 80, 120, 160, 200, 240 })
            {
                foreach (var red in new[] { 40, 80, 120, 160, 200, 240 })
                {
                    if (HandTracker.IsSkin((byte)blue, (byte)green, (byte)red, 0)) continue;
                    if (!HandTracker.IsSkin((byte)blue, (byte)green, (byte)red, 14)) continue;
                    outside = (blue, green, red);
                }
            }
        }
        Assert(outside != (0, 0, 0) && !HandTracker.IsSkin((byte)outside.Blue, (byte)outside.Green, (byte)outside.Red, -14),
            $"A colour the skin rule only accepts with the window opened wide should be refused again when it is narrowed (tried {outside}).");

        // A frame with nothing hand-shaped in it, and a frame painted with a hand: a palm with three fingers.
        var empty = new byte[160 * 120 * 4];
        for (var index = 3; index < empty.Length; index += 4) empty[index] = 255;
        Assert(!HandTracker.Track(empty, 160, 120, 50).Found, "A frame with no skin in it should report no hand.");
        var hand = new byte[160 * 120 * 4];
        Array.Copy(empty, hand, hand.Length);
        void Paint(int left, int top, int right, int bottom)
        {
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var pixel = (y * 160 + x) * 4;
                    hand[pixel] = 120; hand[pixel + 1] = 150; hand[pixel + 2] = 200;
                }
            }
        }
        Paint(50, 60, 110, 110);                       // the palm
        Paint(55, 20, 65, 60); Paint(75, 20, 85, 60); Paint(95, 20, 105, 60);   // three fingers
        var reading = HandTracker.Track(hand, 160, 120, 50);
        Assert(reading.Found && Math.Abs(reading.CenterX - .5) < .08 && reading.CenterY > .4,
            $"The painted hand should be found near the middle of the frame (found {reading.Found} at {reading.CenterX:0.###}, {reading.CenterY:0.###}).");
        Assert(reading.Fingers == 3, $"A palm with three fingers up should count three fingers (counted {reading.Fingers}).");
        Assert(reading.Width > .3 && reading.Width < .45 && reading.Height > .6 && reading.Coverage > .1,
            $"The hand's box and its share of the picture should match what was painted (width {reading.Width:0.###}, height {reading.Height:0.###}, coverage {reading.Coverage:0.###}).");
        // The same hand with the fingers folded away is a hand with nothing to count.
        var closed = new byte[160 * 120 * 4];
        Array.Copy(empty, closed, closed.Length);
        for (var y = 60; y < 110; y++)
            for (var x = 50; x < 110; x++)
            {
                var pixel = (y * 160 + x) * 4;
                closed[pixel] = 120; closed[pixel + 1] = 150; closed[pixel + 2] = 200;
            }
        var fist = HandTracker.Track(closed, 160, 120, 50);
        Assert(fist.Found && fist.Fingers == 0,
            $"A flat hand with no fingers standing up should be found and counted as none (found {fist.Found}, counted {fist.Fingers}).");

        // The finger count itself, on profiles written by hand: the palm line is where the hand mostly stands,
        // and only a dip below the halfway line opens the hand into two.
        Assert(HandTracker.CountFingers([]) == 0 && HandTracker.CountFingers([3, 3, 3]) == 0,
            "An empty or tiny profile has no fingers in it.");
        Assert(HandTracker.CountFingers([11, 11, 11, 11]) == 0, "A profile with no rise at all is a closed hand, not one finger.");
        Assert(HandTracker.CountFingers([11, 11, 11, 11, 19, 19]) == 1, "One stretch standing above the palm line is one finger.");
        Assert(HandTracker.CountFingers([11, 11, 11, 11, 19, 19, 11, 19, 19]) == 2,
            "Two stretches with the palm line between them are two fingers.");
        Assert(HandTracker.CountFingers([11, 11, 11, 11, 19, 19, 14, 14, 19, 19]) == 2,
            "A dip that reaches below the halfway line opens the hand into two fingers, even if it is not all the way down to the palm.");

        // Where the hand is across the frame becomes a key of the keyboard the stage draws.
        Assert(HandTracker.KeyPitch(0, PianoStage.FirstPitch, PianoStage.KeyCount) == PianoStage.FirstPitch
                && HandTracker.KeyPitch(1, PianoStage.FirstPitch, PianoStage.KeyCount) == PianoStage.FirstPitch + PianoStage.KeyCount - 1,
            "The ends of the picture should land on the ends of the keyboard.");
        Assert(HandTracker.KeyPitch(.5, PianoStage.FirstPitch, PianoStage.KeyCount)
                == PianoStage.FirstPitch + (int)Math.Round((PianoStage.KeyCount - 1) * .5),
            "The middle of the picture should land on the middle of the keyboard.");
        Assert(HandTracker.KeyPitch(-5, PianoStage.FirstPitch, PianoStage.KeyCount) == PianoStage.FirstPitch
                && HandTracker.KeyPitch(9, PianoStage.FirstPitch, PianoStage.KeyCount) == PianoStage.FirstPitch + PianoStage.KeyCount - 1,
            "A position off the picture should be held at the ends of the keyboard rather than pointing at nothing.");
        Results.Add($"PASS hand tracking: the skin rule and the sensitivity window, a painted hand found at the middle of the frame with three fingers counted, a closed hand with none, the finger count on written profiles, and positions turned into keys.");
    }

    /// <summary>
    /// Opening a take must not be able to take the window down with it. The media stack is native code that can
    /// stop answering instead of failing — a verification run on such a machine showed the calls simply never
    /// coming back — so the window opens an MP4 take behind <see cref="HangGuard"/>, keeps the take as AVI when
    /// the machine stops answering, and says so. These checks exercise the guard itself, with a job that finishes
    /// and one that does not, and the recorder's refusal of a take it cannot open.
    /// </summary>
    private static void VerifyGuardedStart()
    {
        var ran = false;
        Assert(HangGuard.Run(() => ran = true, TimeSpan.FromSeconds(2)) && ran,
            "Work that finishes at once should be reported as finished, and should really have run.");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var cameBack = HangGuard.Run(() => Thread.Sleep(3000), TimeSpan.FromMilliseconds(200));
        clock.Stop();
        Assert(!cameBack && clock.Elapsed < TimeSpan.FromSeconds(2),
            $"Work that never comes back should be reported after its limit rather than waited out ({clock.Elapsed.TotalSeconds:0.##} s).");

        var thrown = false;
        try { HangGuard.Run(() => throw new InvalidOperationException("the guard should pass this on"), TimeSpan.FromSeconds(2)); }
        catch (InvalidOperationException) { thrown = true; }
        Assert(thrown, "Whatever the guarded work threw should come back to the caller that asked for it.");

        // A take that cannot be opened for an ordinary reason is refused as such — not reported as a machine that
        // stopped answering, and not left hanging: the empty path is a plain argument the recorder checks first.
        var take = Mp4Recorder.TryOpen(string.Empty, 64, 48, 15, withAudio: false, TimeSpan.FromSeconds(5), out var failure, out var stopped);
        Assert(take is null && !stopped && failure is not null,
            "A take that cannot be opened should be refused with the reason the recorder gave, and only a machine that stops answering should be reported as such.");
    }

    private static void VerifyAviVideoRecorder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-test-{Guid.NewGuid():N}.avi");
        bool mjpeg;
        try
        {
            using (var recorder = new AviVideoRecorder(path, 64, 48, 12))
            {
                mjpeg = recorder.UsesMjpeg;
                var frame = new byte[AviVideoRecorder.BgrStride(64) * 48];
                for (var y = 0; y < 48; y++) for (var x = 0; x < 64; x++)
                {
                    var pixel = (47 - y) * AviVideoRecorder.BgrStride(64) + x * 3;
                    frame[pixel] = (byte)(x * 4); frame[pixel + 1] = (byte)(y * 5); frame[pixel + 2] = (byte)(255 - x * 3);
                }
                for (var i = 0; i < 4; i++) { frame[2] = (byte)(40 + i * 40); recorder.WriteBgrFrame(frame); }
            }
            var bytes = File.ReadAllBytes(path); var text = Encoding.ASCII.GetString(bytes);
            Assert(bytes.Length > 1000 && text.StartsWith("RIFF", StringComparison.Ordinal) && text.Substring(8, 4) == "AVI ", "The recorder should finalize a non-empty RIFF/AVI file.");
            var movi = text.IndexOf("movi", StringComparison.Ordinal);
            var frameChunks = movi < 0 ? 0 : CountToken(text[(movi + 4)..], "00dc") + CountToken(text[(movi + 4)..], "00db");
            Assert(frameChunks >= 4, "The AVI should contain all four submitted video frames.");
            Results.Add($"PASS video recording: 4 frames finalized into AVI ({(mjpeg ? "MJPEG" : "uncompressed RGB")}).");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// The webcam overlay away from any camera: where the picture sits for each corner and stage, how a frame
    /// becomes top-down BGRA (a bottom-up camera and a mirror), which pixels the green key removes, and what
    /// the reader answers when there is nothing to read. The last part writes a clip with the application's own
    /// AVI recorder and reads a frame back, so the Media Foundation path is exercised on a file the check owns.
    /// </summary>
    private static void VerifyCameraOverlay()
    {
        const double stageWidth = 1000, stageHeight = 600;
        var margin = CameraOverlay.Margin(stageWidth);
        var box = CameraOverlay.Place("Bottom left", stageWidth, stageHeight, 30, 16.0 / 9);
        Assert(Math.Abs(box.Width - 300) < .001 && Math.Abs(box.Height - 168.75) < .001
                && Math.Abs(box.Left - margin) < .001 && Math.Abs(box.Bottom - (stageHeight - margin)) < .001,
            $"A bottom-left overlay should be a 16:9 picture of 30% of the stage width, inset by the margin (got {box}).");
        var opposite = CameraOverlay.Place("Top right", stageWidth, stageHeight, 30, 16.0 / 9);
        Assert(Math.Abs(opposite.Right - (stageWidth - margin)) < .001 && Math.Abs(opposite.Top - margin) < .001
                && Math.Abs(opposite.Width - box.Width) < .001,
            "The opposite corner should mirror the first one at the same inset and size.");
        Assert(CameraOverlay.Place("Sideways", stageWidth, stageHeight, 30, 16.0 / 9) == box,
            "An unknown corner must fall back to the bottom left instead of drawing off the stage.");
        var portrait = CameraOverlay.Place("Top left", stageWidth, stageHeight, 60, 9.0 / 16);
        Assert(portrait.Height <= stageHeight * .8 + .001, $"A tall frame must never cover more than 80% of the stage height (got {portrait.Height}).");
        var wide = CameraOverlay.Place("Bottom right", stageWidth, stageHeight, 60, 4);
        Assert(wide.Width <= stageWidth - 2 * margin + .001 && wide.Height <= stageHeight * .8 + .001,
            "A very wide frame should be trimmed to the stage instead of running off it.");
        var empty = CameraOverlay.Place("Bottom right", 0, 0, 30, 16.0 / 9);
        Assert(empty.Width == 0 && empty.Height == 0 && empty.Left >= 0 && empty.Top >= 0,
            $"A stage of no size must produce an empty, non-negative rectangle (got {empty}).");
        Assert(CameraOverlay.Place("Top right", stageWidth, stageHeight, 30, 0).Height > 0,
            "A frame whose aspect ratio is unknown should fall back to 16:9 rather than collapsing.");

        // A two-pixel-wide, two-row picture: red then green on the first row, blue then white on the second.
        var frame = new byte[16];
        frame[2] = 255; frame[3] = 255; frame[5] = 255; frame[7] = 255;
        frame[8] = 255; frame[11] = 255; frame[12] = 255; frame[13] = 255; frame[14] = 255; frame[15] = 255;
        var straight = new byte[16];
        CameraOverlay.CopyFrame(frame, 8, 2, 2, straight, mirror: false);
        Assert(straight.SequenceEqual(frame), "A top-down frame with a positive stride should be copied exactly as it arrived.");
        var mirrored = new byte[16];
        CameraOverlay.CopyFrame(frame, 8, 2, 2, mirrored, mirror: true);
        Assert(mirrored[1] == 255 && mirrored[5] == 0 && mirrored[2] == 0 && mirrored[6] == 255
                && mirrored[9] == 255 && mirrored[13] == 0,
            "Mirroring should swap the two columns of every row and leave the rows themselves in order.");
        var bottomUp = new byte[16];
        Array.Copy(frame, 8, bottomUp, 0, 8); Array.Copy(frame, 0, bottomUp, 8, 8);
        var upright = new byte[16];
        CameraOverlay.CopyFrame(bottomUp, -8, 2, 2, upright, mirror: false);
        Assert(upright.SequenceEqual(frame), "A negative stride must mean the rows arrive bottom-up and be turned upright.");
        var dull = new byte[16];
        Array.Copy(frame, dull, 16); for (var index = 3; index < 16; index += 4) dull[index] = 0;
        var opaque = new byte[16];
        CameraOverlay.CopyFrame(dull, 8, 2, 2, opaque, mirror: false);
        Assert(opaque[3] == 255 && opaque[7] == 255 && opaque[15] == 255 && dull[3] == 0,
            "A frame must reach the stage opaque whatever the camera put in its fourth byte, and the source must be left alone.");
        var unknown = new byte[16];
        CameraOverlay.CopyFrame(frame, 0, 2, 2, unknown, mirror: false);
        Assert(unknown.SequenceEqual(frame), "A stride Media Foundation did not report should be read as Width × 4.");
        var tight = new byte[8];
        CameraOverlay.CopyFrame(frame, 8, 2, 2, tight, mirror: false);
        Assert(tight.SequenceEqual(frame.Take(8)), "A destination with room for one row must be filled up to its end and no further.");

        Assert(!CameraOverlay.IsKeyed(0, 255, 0, 0), "Tolerance zero must turn the key off, whatever the pixel is.");
        Assert(CameraOverlay.IsKeyed(0, 255, 0, 30) && CameraOverlay.IsKeyed(20, 240, 20, 30),
            "Pure green and a near-green should be removed at a normal tolerance.");
        Assert(!CameraOverlay.IsKeyed(60, 200, 60, 30) && CameraOverlay.IsKeyed(60, 200, 60, 100),
            "The tolerance should decide how far from the key colour a pixel may be, so a pale green survives a narrow setting and goes at a wide one.");
        Assert(!CameraOverlay.IsKeyed(128, 128, 128, 100) && !CameraOverlay.IsKeyed(255, 255, 255, 100) && !CameraOverlay.IsKeyed(30, 40, 200, 100),
            "Grey, white and blue must survive the widest tolerance: green has to dominate before a pixel is keyed.");
        var keying = new byte[12];
        keying[1] = 255; keying[3] = 255;
        keying[4] = 60; keying[5] = 200; keying[6] = 60; keying[7] = 255;
        keying[8] = 128; keying[9] = 128; keying[10] = 128; keying[11] = 255;
        Assert(CameraOverlay.ApplyKey(keying, 3, 30) == 1 && keying[3] == 0 && keying[7] == 255 && keying[11] == 255,
            "Removing the key should clear the alpha of the keyed pixel only, which is the whole removal.");
        Assert(CameraOverlay.ApplyKey(keying, 3, 100) == 1 && keying[7] == 0 && keying[11] == 255,
            "A wider tolerance should take the pale green too and still leave the grey pixel alone.");
        Assert(CameraOverlay.ApplyKey(keying, 3, 0) == 0, "With the key off nothing may be cleared, not even a pure green.");
        Assert(Math.Abs(CameraOverlay.OpacityFactor(0)) < 1e-9 && Math.Abs(CameraOverlay.OpacityFactor(100) - 1) < 1e-9 && Math.Abs(CameraOverlay.OpacityFactor(250) - 1) < 1e-9,
            "The opacity percent should map onto a 0–1 factor and clamp anything above 100.");
        Assert(CameraFrameReader.DisplayRate == 60 && Math.Abs(CameraFrameReader.FileFrameSeconds - 1.0 / 30) < 1e-9,
            "The window should show a new picture at most 60 times a second, and a file with no times of its own should be read at 30 fps.");

        var missing = Path.Combine(Path.GetTempPath(), $"keyflow-no-such-video-{Guid.NewGuid():N}.mp4");
        Assert(CameraFrameReader.OpenFile(missing, out var missingError) is null && missingError is { Length: > 0 },
            "A video file that is not there must come back as a sentence for the dock, not as an exception.");
        var devices = CameraFrameReader.Devices(out var deviceError);
        Assert(CameraFrameReader.Available || CameraFrameReader.StartupError is { Length: > 0 },
            "Whether the media stack can run must always be answered with a reason when it cannot.");
        if (devices.Count == 0 && CameraFrameReader.Available) Assert(deviceError is null,
            $"A machine with no camera must be reported as an empty list rather than a failure (got “{deviceError}”).");
        Assert(devices.All(device => device.Name.Length > 0), "Every camera the machine reports should carry a name for the picker.");

        // The reader on a real file: the application's own recorder writes the clip, the reader reads it back,
        // and the frame arrives as 32-bit BGRA of the size the clip was written with. A machine whose media
        // stack cannot decode the clip must say so instead of failing the check.
        var clip = Path.Combine(Path.GetTempPath(), $"keyflow-overlay-clip-{Guid.NewGuid():N}.avi");
        try
        {
            using (var recorder = new AviVideoRecorder(clip, 64, 48, 12))
            {
                var picture = new byte[AviVideoRecorder.BgrStride(64) * 48];
                for (var i = 0; i < picture.Length; i += 3) { picture[i] = 200; picture[i + 1] = 40; picture[i + 2] = 40; }
                for (var index = 0; index < 3; index++) recorder.WriteBgrFrame(picture);
            }
            var reader = CameraFrameReader.OpenFile(clip, out var clipError);
            if (reader is null) Results.Add($"PASS camera overlay clip: the clip could not be decoded here ({clipError}).");
            else
            {
                using (reader)
                {
                    var pixels = new byte[reader.Width * reader.Height * 4];
                    var got = reader.TryReadFrame(pixels, mirror: false, out var width, out var height);
                    Assert(got && width == 64 && height == 48 && reader.Frames == 1 && pixels[3] == 255,
                        $"The overlay reader should read an opaque frame out of the application's own clip ({reader.Width} × {reader.Height}, frame={reader.Frames}, alpha={pixels[3]}, error=“{reader.Error}”).");
                    Assert(pixels[0] > 150 && pixels[1] < 120 && pixels[2] < 120, "A frame should arrive as BGRA in the order the clip was written in.");
                    Assert(reader.Mode.Width == 64 && reader.Mode.Height == 48 && reader.Mode.Label.Length > 0,
                        $"The reader should report the mode it delivers, 64 × 48 here (got “{reader.Mode.Label}”).");
                    Assert(reader.LastFrameTicks > 0 && reader.LastFrameTicks <= System.Diagnostics.Stopwatch.GetTimestamp(),
                        "A frame should be stamped with the clock reading at which it arrived.");
                    var firstTime = reader.LastSampleTime;
                    var more = reader.TryReadFrame(pixels, mirror: false, out _, out _);
                    Assert(!more || reader.LastSampleTime >= firstTime && reader.Frames == 2,
                        $"The time Media Foundation writes on a frame should not go backwards ({firstTime} then {reader.LastSampleTime}).");
                    var keyed = CameraOverlay.ApplyKey(pixels, width * height, 30);
                    Assert(keyed == 0, "A picture with no green in it must survive the key untouched.");
                }
                Results.Add("PASS camera overlay clip: a clip written by the recorder was read back through the overlay reader.");
            }
        }
        finally { if (File.Exists(clip)) File.Delete(clip); }

        Results.Add($"PASS camera overlay: corner placement, bottom-up and mirrored frames, the green key by tolerance, the opacity factor, {devices.Count} camera(s) on this machine, and the reader's answers for a missing file and for a clip.");
    }

    /// <summary>
    /// The webcam overlay through the dock and the stage: the Camera &amp; FX page carries the switch and the
    /// rows, a corner picked there is stored as the English identifier the stage reads, out-of-range values are
    /// clamped instead of trusted, and the frame the reader produced is keyed and drawn in the corner the
    /// settings ask for — read back from the rendered stage, not asserted from the settings.
    /// </summary>
    /// <summary>
    /// Handling the hand layer from the window the user has: the switch and the slider in the dock, the settings
    /// behind them, the pump really running the tracker over a frame the check paints, the status line saying what
    /// was seen, and the stage painting the marker only when the layer is on and a hand was found.
    /// </summary>
    private static void VerifyHandTrackingDock(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings)
    {
        var beforeJson = visualSettings.ToJson(); var beforeName = visualSettings.PresetName;
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        Assert(toggles.ContainsKey(nameof(PianoVisualSettings.ShowHandTracking)),
            "The Camera & FX page should expose hand tracking as a layer switch of its own.");
        Assert(sliders.ContainsKey(nameof(PianoVisualSettings.HandTrackingSensitivity)),
            "The tracker's sensitivity belongs on a slider, so it can be found by sliding.");
        toggles[nameof(PianoVisualSettings.ShowHandTracking)].IsChecked = true;
        Assert(visualSettings.ShowHandTracking, "Switching hand tracking on should reach the settings the pump reads.");
        sliders[nameof(PianoVisualSettings.HandTrackingSensitivity)].Value = 100;
        Assert(Math.Abs(visualSettings.HandTrackingSensitivity - 100) < .001, "The sensitivity slider should reach the settings.");
        visualSettings.HandTrackingSensitivity = 500; visualSettings.Clamp();
        Assert(Math.Abs(visualSettings.HandTrackingSensitivity - 100) < .001, "A sensitivity outside its range should be clamped rather than trusted.");

        // The frame path the app really uses: a painted hand goes through the pump, the tracker finds it and the
        // stage is told, and the dock's line says what it saw.
        var frame = new byte[160 * 120 * 4];
        for (var index = 3; index < frame.Length; index += 4) frame[index] = 255;
        void Paint(int left, int top, int right, int bottom)
        {
            for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                {
                    var pixel = (y * 160 + x) * 4;
                    frame[pixel] = 120; frame[pixel + 1] = 150; frame[pixel + 2] = 200;
                }
        }
        Paint(50, 60, 110, 110); Paint(55, 20, 65, 60); Paint(75, 20, 85, 60); Paint(95, 20, 105, 60);
        Invoke(window, "InjectCameraFrame", frame, 160, 120);
        SetField(window, "_lastHandTrackTicks", 0L); // the tracker runs at most 30 times a second: let this frame through
        Invoke(window, "PumpCameraFrame");
        Assert(stage.HasHand || Field(stage, "_hand") is not null,
            "A frame with a hand in it, pumped the way the camera pumps frames, should reach the stage.");
        var status = (TextBlock?)Field(window, "_handStatus");
        Assert(status is { Text.Length: > 0 }, "The Camera & FX page should say what the tracker is seeing.");

        // The marker: drawn when the layer is on and a hand was found, and nothing at all when it is off.
        visualSettings.ShowHandTracking = true; visualSettings.HandTrackingSensitivity = 50;
        stage.SetVisualSettings(visualSettings);
        stage.SetHandReading(HandTracker.Track(frame, 160, 120, 50));
        int MarkerInk()
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) Invoke(stage, "DrawHandMarker", dc, 1000d, 460d, 1000d / PianoStage.KeyCount);
            var bitmap = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[1000 * 600 * 4];
            bitmap.CopyPixels(pixels, 4000, 0);
            var lit = 0;
            for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] > 40) lit++;
            return lit;
        }
        var marked = MarkerInk();
        visualSettings.ShowHandTracking = false; stage.SetVisualSettings(visualSettings);
        var unmarked = MarkerInk();
        Assert(marked > unmarked + 200,
            $"The hand marker should paint over the key the hand is over ({marked} inked pixels marked, {unmarked} unmarked).");
        visualSettings.ShowHandTracking = true; stage.SetVisualSettings(visualSettings);
        stage.SetHandReading(null);
        Assert(!stage.HasHand && MarkerInk() == 0, "With no hand found there should be no marker at all.");

        visualSettings.CopyFrom(PianoVisualSettings.FromJson(beforeJson), keepBackgroundImage: false); visualSettings.PresetName = beforeName;
        SetField(window, "_cameraSignature", "");
        Invoke(window, "RefreshSettingControls"); stage.SetVisualSettings(visualSettings);
        Results.Add("PASS hand tracking dock: the switch and the sensitivity slider, the clamp, a painted hand carried through the camera pump into the stage, the dock's own line, and the marker drawn over the hand's key only while the layer is on.");
    }

    private static void VerifyCameraOverlayDock(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings)
    {
        var beforeJson = visualSettings.ToJson(); var beforeName = visualSettings.PresetName;
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        var choices = (Dictionary<string, ComboBox>)Field(window, "_visualChoices");
        Assert(toggles.ContainsKey(nameof(PianoVisualSettings.ShowCameraOverlay)),
            "The Camera & FX page should expose the webcam overlay as a layer switch.");
        Assert(sliders.ContainsKey(nameof(PianoVisualSettings.CameraSize)) && sliders.ContainsKey(nameof(PianoVisualSettings.CameraOpacity))
                && sliders.ContainsKey(nameof(PianoVisualSettings.CameraKeyTolerance)),
            "The overlay's size, opacity and key tolerance belong on sliders, so they can be found by sliding.");
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.CameraSourceLink)) && choices.ContainsKey(nameof(PianoVisualSettings.CameraCorner)),
            "The overlay should offer a camera picker and the four-corner picker.");
        var corner = choices[nameof(PianoVisualSettings.CameraCorner)];
        Assert(corner.Items.Count == CameraOverlay.Corners.Length, $"The corner picker should offer every corner (found {corner.Items.Count}).");
        corner.SelectedValue = "Top left";
        Assert(visualSettings.CameraCorner == "Top left", "Picking a corner should store the English identifier the stage reads, not the translated caption.");
        visualSettings.CameraCorner = "Bottom right"; visualSettings.CameraSize = 5; visualSettings.CameraOpacity = 500; visualSettings.CameraKeyTolerance = -20;
        visualSettings.Clamp();
        Assert(visualSettings.CameraCorner == "Bottom right" && visualSettings.CameraSize == 15 && visualSettings.CameraOpacity == 100 && visualSettings.CameraKeyTolerance == 0,
            $"Out-of-range overlay settings should be clamped rather than trusted (got size {visualSettings.CameraSize}, opacity {visualSettings.CameraOpacity}, key {visualSettings.CameraKeyTolerance}).");
        visualSettings.CameraCorner = "Sideways"; visualSettings.Clamp();
        Assert(visualSettings.CameraCorner == "Bottom left", "A stored corner the overlay does not know must fall back to the bottom left.");
        visualSettings.CameraSize = 25; visualSettings.CameraOpacity = 100; visualSettings.CameraKeyTolerance = 30; visualSettings.CameraCorner = "Top right";

        // The switch really reaches the renderer and the window answers for the source it found.
        var wasShowing = visualSettings.ShowCameraOverlay;
        toggles[nameof(PianoVisualSettings.ShowCameraOverlay)].IsChecked = true;
        Assert(visualSettings.ShowCameraOverlay, "Switching the overlay on should reach the stage settings, not only the checkbox.");
        var status = (TextBlock?)Field(window, "_cameraStatus");
        Assert(status is { Text.Length: > 0 }, "The Camera & FX page should say what the overlay is doing, in the active language.");
        Invoke(window, "RefreshCameraOverlayStatus", false);
        Assert(status!.Text.Length > 0 && status.Text != Loc.T("The overlay is off. Switch Camera overlay on to put the picture over the stage."),
            "While the overlay is on the page must not still claim it is off.");

        // The frame path the app uses: a red frame through the pump reaches the bitmap and the stage; the same
        // frame filled with the key colour comes back see-through, and the stage paints it in the chosen corner.
        var red = new byte[64 * 36 * 4];
        for (var index = 0; index < red.Length; index += 4) { red[index + 2] = 255; red[index + 3] = 255; }
        Invoke(window, "InjectCameraFrame", red, 64, 36);
        Invoke(window, "PumpCameraFrame");
        var painted = (WriteableBitmap?)Field(window, "_cameraBitmap");
        Assert(painted is { PixelWidth: 64, PixelHeight: 36 }, "The pump should build a bitmap the size of the frame the reader produced.");
        var pixels = new byte[64 * 36 * 4];
        painted!.CopyPixels(pixels, 64 * 4, 0);
        Assert(pixels[3] == 255 && pixels[2] == 255 && pixels[0] == 0, "A red frame with no green in it must reach the stage untouched.");
        Assert(stage.HasCameraFrame, "The stage should hold the frame the pump produced.");
        var area = CameraOverlay.Place(visualSettings.CameraCorner, 1000, 600, visualSettings.CameraSize, 64.0 / 36);
        int Offset(double x, double y) => ((int)y * 1000 + (int)x) * 4;
        var inside = Offset(area.Left + area.Width / 2, area.Top + area.Height / 2);
        var canvas = new byte[1000 * 600 * 4];
        var drawn = new DrawingVisual();
        using (var dc = drawn.RenderOpen()) Invoke(stage, "DrawCameraOverlay", dc, 1000d, 600d);
        var shot = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32); shot.Render(drawn); shot.CopyPixels(canvas, 4000, 0);
        Assert(canvas[inside + 3] == 255 && canvas[inside + 2] > 200,
            $"The stage should paint the frame inside its corner rectangle (alpha {canvas[inside + 3]} at {inside / 4}).");
        Assert(canvas[Offset(area.Left - 6, area.Top + area.Height / 2) + 3] == 0 && canvas[Offset(60, 560) + 3] == 0,
            "Nothing of the overlay may be painted outside its rectangle.");
        var green = new byte[64 * 36 * 4];
        for (var index = 0; index < green.Length; index += 4) { green[index + 1] = 255; green[index + 3] = 255; }
        Invoke(window, "InjectCameraFrame", green, 64, 36);
        Invoke(window, "PumpCameraFrame");
        painted.CopyPixels(pixels, 64 * 4, 0);
        Assert(pixels[3] == 0 && pixels[1] == 0, "The key should make a green frame see-through before it reaches the stage.");
        var keyed = new byte[1000 * 600 * 4];
        var keyedVisual = new DrawingVisual();
        using (var dc = keyedVisual.RenderOpen()) Invoke(stage, "DrawCameraOverlay", dc, 1000d, 600d);
        var keyedShot = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32); keyedShot.Render(keyedVisual); keyedShot.CopyPixels(keyed, 4000, 0);
        Assert(keyed[inside + 3] == 0, "A keyed frame must leave the stage behind it visible.");
        stage.SetCameraFrame(null);
        Assert(!stage.HasCameraFrame, "Clearing the frame should stop the overlay from drawing.");
        toggles[nameof(PianoVisualSettings.ShowCameraOverlay)].IsChecked = wasShowing;
        visualSettings.CopyFrom(PianoVisualSettings.FromJson(beforeJson), keepBackgroundImage: false); visualSettings.PresetName = beforeName;
        SetField(window, "_cameraSignature", string.Join("|", visualSettings.ShowCameraOverlay, visualSettings.CameraSourceLink, visualSettings.CameraVideoPath, false));
        Invoke(window, "RefreshSettingControls"); stage.SetVisualSettings(visualSettings);
        // The AR calibration follows what is open: nothing without a camera, the keyboard size of the settings, the settings folder.
        var arSession = window.ArCalibration;
        Assert(arSession.Store.FilePath.StartsWith(PianoVisualSettingsStore.SettingsDirectory, StringComparison.OrdinalIgnoreCase),
            "The AR calibrations are kept in the settings folder.");
        Assert(arSession.State == ArCalibrationState.NoSource, "With no camera open there is nothing to calibrate against.");
        var arSize = visualSettings.KeyboardSize;
        Invoke(window, "ApplyArSource", "verification camera", 1280, 720);
        Assert(arSession.State == ArCalibrationState.NotCalibrated && arSession.Snapshot.KeyCount == visualSettings.KeyboardLayoutChoice.KeyCount,
            "An opened camera that was never calibrated asks for a calibration for the keyboard in the settings.");
        visualSettings.KeyboardSize = visualSettings.KeyboardSize == "61" ? "49" : "61";
        Invoke(window, "SyncCameraOverlay");
        Assert(arSession.Snapshot.KeyCount == visualSettings.KeyboardLayoutChoice.KeyCount,
            "Changing the keyboard size must move the calibration to the new size's.");
        visualSettings.KeyboardSize = arSize;
        Invoke(window, "ApplyArSource", null!, 0, 0);
        Assert(arSession.State == ArCalibrationState.NoSource, "Closing the camera leaves no calibration in force.");

        // The overlay's switch carried out: nothing without the switch, the camera and the engine's layers once a camera is open and
        // calibrated, the stage's own notes and keys stepping aside, and the engine the only reader of the camera's frames.
        var gpuFeed = (GpuStageFeed)Field(window, "_gpuFeed");
        var engineRuns = window.GpuLoop is { Error: null };
        Invoke(window, "UpdateArFeed");
        Assert(window.ArPlanNow.Status == ArStatus.Off && gpuFeed.Camera is null && gpuFeed.Ar is null && !stage.ArOverlayActive,
            "With the AR switch off the overlay leaves the stage alone.");
        Assert(toggles.ContainsKey(nameof(PianoVisualSettings.ArEnabled)) && choices.ContainsKey(nameof(PianoVisualSettings.ArMode))
                && sliders.ContainsKey(nameof(PianoVisualSettings.ArOpacity)) && sliders.ContainsKey(nameof(PianoVisualSettings.ArLookahead))
                && sliders.ContainsKey(nameof(PianoVisualSettings.ArKeyLight)) && sliders.ContainsKey(nameof(PianoVisualSettings.ArDelay)),
            "The Camera & FX page should carry the AR switch, the mode picker and the four sliders.");
        var arText = () => ((TextBlock)Field(window, "_arStatus")).Text;
        Assert(arText() == Loc.T("The AR overlay is off. Switch it on to play on your real keyboard."), "The AR card should say the overlay is off.");
        var arWas = visualSettings.ArEnabled;
        toggles[nameof(PianoVisualSettings.ArEnabled)].IsChecked = true;
        Assert(visualSettings.ArEnabled, "Switching the overlay on in the dock should reach the settings.");
        Assert(engineRuns ? arText().StartsWith(Loc.T("Waiting for the camera."), StringComparison.Ordinal)
                : arText() == Loc.T("The AR overlay needs the Direct3D 11 engine; the software renderer is drawing the stage."),
            $"The AR card should say what the overlay waits for ({arText()}).");
        Assert(window.ArPlanNow.Status == (engineRuns ? ArStatus.NoCamera : ArStatus.NeedsGpu) && gpuFeed.Camera is null,
            "Switched on without an open camera, the overlay waits for one; without the engine it says it needs it.");
        (double X, double Y)[] arCorners = [(70, 292), (1214, 268), (1236, 421), (52, 449)];     // a key area is a key area for any keyboard size
        Invoke(window, "ApplyArSource", "verification camera", 1280, 720);
        Invoke(window, "UpdateArFeed");
        Assert(window.ArPlanNow.Status == (engineRuns ? ArStatus.NeedsCalibration : ArStatus.NeedsGpu) && gpuFeed.Ar is null && !stage.ArOverlayActive
                && (gpuFeed.Camera is not null) == engineRuns,
            "An open camera that was not calibrated shows as the background, with the stage as it was and no notes or keys on it.");
        Assert(window.ArCalibration.Save(arCorners, out _) == ArSaveResult.Saved, "The sample key area should be accepted as a calibration.");
        Invoke(window, "UpdateArFeed");
        Assert(window.ArPlanNow.Status == (engineRuns ? ArStatus.Active : ArStatus.NeedsGpu) && (gpuFeed.Ar is not null) == engineRuns
                && stage.ArOverlayActive == engineRuns && (gpuFeed.Camera is not null) == engineRuns,
            "A calibrated camera draws the overlay and the stage's own notes and keys step aside.");
        if (engineRuns)
        {
            Assert(arText().Contains("1280 × 720", StringComparison.Ordinal), $"The AR card should say what the overlay draws on ({arText()}).");
            sliders[nameof(PianoVisualSettings.ArOpacity)].Value = 40;
            Assert(visualSettings.ArOpacity == 40 && Math.Abs(gpuFeed.Ar!.Opacity - .4f) < 1e-3, "The opacity slider should reach the layer the engine draws.");
            sliders[nameof(PianoVisualSettings.ArLookahead)].Value = 2.5;
            var valueBoxes = (Dictionary<string, TextBox>)Field(window, "_visualValueBoxes");
            Assert(valueBoxes[nameof(PianoVisualSettings.ArLookahead)].Text == $"{2.5:0.0} s" && Math.Abs(gpuFeed.Ar!.Notes.Reach / gpuFeed.Ar.Notes.FallSpeed - 2.5) < 1e-9,
                "The lookahead slider should show seconds and set how far above the keys notes come into view.");
            choices[nameof(PianoVisualSettings.ArMode)].SelectedValue = ArModes.Perform;
            Assert(visualSettings.ArMode == ArModes.Perform && !gpuFeed.Ar!.ShowNotes, "The mode picker should reach the layer the engine draws.");
            choices[nameof(PianoVisualSettings.ArMode)].SelectedValue = ArModes.Learn;
            Assert(ReferenceEquals(gpuFeed.Camera!.Ring, Field(window, "_cameraFrames")) && gpuFeed.Ar!.Calibration == window.ArCalibration.Current,
                "The engine reads the camera's own ring and draws on the calibration in force.");
            var frameRing = (CameraFrameRing)Field(window, "_cameraFrames");
            frameRing.BeginWrite(16, out var arSlot); frameRing.Publish(arSlot, 2, 2, 1);
            Invoke(window, "PumpCameraFrame");
            Assert(frameRing.TryTake(out _), "The window's own pump leaves the frames to the engine while the overlay is drawn.");
            visualSettings.BackgroundMode = "ChromaGreen";
            Invoke(window, "UpdateArFeed");
            Assert(window.ArPlanNow.Status == ArStatus.ChromaBackground && gpuFeed.Camera is null && gpuFeed.Ar is null && !stage.ArOverlayActive,
                "A green-screen background takes the camera's place, so the overlay steps aside.");
            visualSettings.BackgroundMode = "Solid";
        }
        Invoke(window, "ResetArCalibration_Click", window, new RoutedEventArgs());
        Assert(window.ArCalibration.State is ArCalibrationState.NotCalibrated or ArCalibrationState.NoSource && window.ArCalibration.Current is null,
            "RESET CALIBRATION should forget the calibration in force.");
        visualSettings.ArEnabled = arWas; visualSettings.ArMode = ArModes.Learn; visualSettings.ArOpacity = 100; visualSettings.ArLookahead = 3; visualSettings.ArKeyLight = 60;
        Invoke(window, "RefreshSettingControls");
        Invoke(window, "ApplyArSource", null!, 0, 0);
        Invoke(window, "SyncCameraOverlay");
        Assert(window.ArPlanNow.Status == ArStatus.Off && gpuFeed.Camera is null && gpuFeed.Ar is null && !stage.ArOverlayActive,
            "Switching the overlay off puts the stage back as it was.");
        Results.Add("PASS camera overlay dock: the switch, the camera and corner pickers, the size/opacity/key sliders, the clamp and the corner fallback, the pump's key and the frame drawn in the chosen corner of the rendered stage.");
    }

    /// <summary>Checks the shared sympathetic string-resonance stage; CC 64 damper behaviour is covered by the SFZ engine fixture.</summary>
    private static void VerifyPianoPedalModel()
    {
        // Sympathetic resonance: the string bank rings after the dry burst stops while the pedal is down,
        // chokes as soon as the pedal lifts, and is bit-identical dry when the feature is off.
        var resonance = new StringResonance(22050);
        var silence = new float[512 * 2];
        for (var i = 0; i < 24; i++) { Array.Clear(silence, 0, silence.Length); resonance.Process(silence, 512, 1); }
        var burst = new float[512 * 2];
        for (var i = 0; i < 64; i++) { burst[i * 2] = .8f; burst[i * 2 + 1] = .8f; }
        resonance.Process(burst, 512, 1);
        var ringing = false; var block = new float[512 * 2];
        for (var i = 0; i < 8; i++) { Array.Clear(block, 0, block.Length); resonance.Process(block, 512, 1); if (block.Any(sample => Math.Abs(sample) > 1e-6f)) ringing = true; }
        Assert(ringing, "Open strings should keep ringing after the dry signal stops while the pedal is down.");
        var residual = 0f;
        for (var i = 0; i < 48; i++)
        {
            Array.Clear(block, 0, block.Length); resonance.Process(block, 512, 0);
            if (i == 47) residual = block.Max(Math.Abs);
        }
        Assert(residual < 1e-4f, $"Lifting the pedal should let the dampers choke the resonating strings (residual={residual}).");
        var disabled = new StringResonance(22050) { Enabled = false };
        var dry = new float[512 * 2]; for (var i = 0; i < dry.Length; i++) dry[i] = (float)Math.Sin(i * .1) * .5f;
        var expected = (float[])dry.Clone();
        disabled.Process(dry, 512, 1);
        Assert(dry.SequenceEqual(expected), "Disabled string resonance must leave the mix bit-identical.");
        using (var engine = new PianoAudioEngine())
        {
            Assert(engine.StringResonanceEnabled, "String resonance should default to on, matching the UI toggle.");
            engine.StringResonanceEnabled = false;
            Assert(!engine.StringResonanceEnabled, "The string-resonance toggle should reach the audio engine.");
        }
        Results.Add("PASS piano effects: sympathetic string resonance rings under the pedal, damps on release and bypasses bit-identically when disabled.");
    }

    /// <summary>
    /// The SFZ sample engine end to end: WAV decoding (16/24-bit PCM and float32), parser inheritance,
    /// #define/#include and note-name spellings, velocity layers, round-robins, release samples, loops and
    /// continuous damper behaviour. Fixtures are generated into the temp folder, so verification needs no sample set.
    /// </summary>
    private static void VerifySfzEngine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "keyflow-test-sfz");
        Directory.CreateDirectory(dir);
        WriteTestWav(Path.Combine(dir, "a4.wav"), channels: 2, bits: 16, rate: 22050, frames: 11025, wave: i => Math.Sin(i * 2 * Math.PI * 440 / 22050) * .33);
        WriteTestWav(Path.Combine(dir, "a5.wav"), channels: 2, bits: 16, rate: 22050, frames: 11025, wave: i => Math.Sin(i * 2 * Math.PI * 880 / 22050) * .33);
        WriteTestWav(Path.Combine(dir, "loop.wav"), channels: 1, bits: 16, rate: 22050, frames: 11025, wave: i => Math.Sin(i * 2 * Math.PI * 220 / 22050) * .33);
        WriteTestWav(Path.Combine(dir, "rel.wav"), channels: 1, bits: 16, rate: 22050, frames: 2205, wave: i => Math.Sin(i * 2 * Math.PI * 3000 / 22050) * .2);
        WriteTestWav(Path.Combine(dir, "deep.wav"), channels: 1, bits: 24, rate: 22050, frames: 1000, wave: i => i / 1000.0);
        WriteTestWav(Path.Combine(dir, "float.wav"), channels: 1, bits: 32, rate: 22050, frames: 100, wave: _ => .5);

        var deep = WavReader.Read(Path.Combine(dir, "deep.wav"));
        var deepOk = true;
        for (var i = 0; i < 1000; i++) if (deep.Samples[i] != (short)((int)Math.Round(i / 1000.0 * 8388607) >> 8)) deepOk = false;
        Assert(deepOk && deep.SampleRate == 22050 && deep.Channels == 1, "24-bit PCM WAV files should decode to the matching 16-bit samples.");
        var floats = WavReader.Read(Path.Combine(dir, "float.wav"));
        Assert(floats.Samples.All(s => s == 16384), "Float32 WAV files should decode with full-scale mapped to 16-bit.");

        File.WriteAllText(Path.Combine(dir, "incl.txt"), "<group> trigger=release\n<region> sample=rel.wav lokey=a0 hikey=c8 rt_decay=0\n");
        File.WriteAllText(Path.Combine(dir, "piano.sfz"),
            "// Keyflow SFZ test instrument\n#define $ROOT 69\n<control> note_offset=0 label_cc7=Vol set_cc7=100\n" +
            "<curve> curve_index=7 v000=1\n<global> ampeg_release=1 amp_veltrack=0\n" +
            "<group> lokey=60 hikey=79 pitch_keycenter=$ROOT\n" +
            "<region> sample=a4.wav lovel=1 hivel=79\n<region> sample=a5.wav lovel=80 hivel=127\n" +
            "<region> sample=loop.wav key=50 pitch_keycenter=50 loop_mode=loop_continuous loop_start=512 loop_end=10512\n" +
            "<group> lokey=40 hikey=40 pitch_keycenter=40 seq_length=2\n" +
            "<region> sample=a4.wav seq_position=1\n<region> sample=a4.wav seq_position=2 volume=-20\n" +
            "#include \"incl.txt\"\n");
        var instrument = SfzReader.Read(Path.Combine(dir, "piano.sfz"));
        Assert(instrument.Name == "piano" && instrument.Regions.Count == 6 && instrument.Regions.Count(r => r.TriggerRelease) == 1, "The SFZ parser should flatten groups, includes and defines into the full region list.");
        var layer = instrument.Regions[0];
        Assert(layer.LoKey == 60 && layer.HiKey == 79 && layer.Root == 69 && layer.LoVel == 1 && layer.HiVel == 79 && Math.Abs(layer.ReleaseSeconds - 1) < 1e-9 && layer.AmpVeltrack == 0, "Regions should inherit group, global and #define values under their own opcodes.");
        var release = instrument.Regions.Single(r => r.TriggerRelease);
        Assert(release.LoKey == 21 && release.HiKey == 108, "Note names (a0, c8) should parse like MIDI numbers do.");

        // Velocity layers: a soft and a loud strike play different recordings (440 Hz vs 880 Hz here).
        var soft = new SfzSynthesizer(instrument, 22050); soft.NoteOn(0, 69, 40);
        var loud = new SfzSynthesizer(instrument, 22050); loud.NoteOn(0, 69, 120);
        var softBlock = new float[4410 * 2]; soft.Render(softBlock, 4410);
        var loudBlock = new float[4410 * 2]; loud.Render(loudBlock, 4410);
        var softCross = ZeroCrossings(softBlock); var loudCross = ZeroCrossings(loudBlock);
        Assert(softCross > 100 && loudCross > softCross * 1.6 && loudCross < softCross * 2.4, $"Velocity should pick the matching sample layer (zero crossings {softCross} soft vs {loudCross} loud).");

        // Release samples: the damper-fall noise sounds when the string is damped — not while the pedal holds it.
        var synth = new SfzSynthesizer(instrument, 22050);
        synth.NoteOn(0, 69, 100); var block = new float[2205 * 2]; synth.Render(block, 2205);
        Assert(synth.ActiveVoiceCount == 1, "A single key should start one voice until its damper falls.");
        synth.NoteOff(0, 69);
        Assert(synth.ActiveVoiceCount == 2, "Releasing an undamped key should add the trigger=release sample.");
        var tail = new float[33075 * 2]; synth.Render(tail, 33075);
        Assert(synth.ActiveVoiceCount == 0, "SFZ voices should be freed after the release envelope and the release sample end.");
        var pedal = new SfzSynthesizer(instrument, 22050);
        pedal.ProcessMidi(0, 0xB0, 64, 127); pedal.NoteOn(0, 69, 100); pedal.Render(block, 2205); pedal.NoteOff(0, 69);
        Assert(pedal.ActiveVoiceCount == 1, "While the pedal holds the string open there is no damper fall and no release sample.");
        pedal.ProcessMidi(0, 0xB0, 64, 0);
        Assert(pedal.ActiveVoiceCount == 2, "Dropping the pedal damps the string and fires its release sample.");
        // Half-pedalling is timed on the looped region: an unlooped half-second fixture would run off its
        // sample end long before the stretched tail does (real piano samples ring for ten seconds and more).
        var half = new SfzSynthesizer(instrument, 22050);
        half.ProcessMidi(0, 0xB0, 64, 40); half.NoteOn(0, 50, 100); half.Render(block, 2205); half.NoteOff(0, 50);
        half.Render(tail, 33075);
        Assert(half.ActiveVoiceCount == 1, "The SFZ engine should honour the same half-pedal release stretching as the piano engine.");
        half.ProcessMidi(0, 0xB0, 64, 0);
        var drain = new float[22050 * 2]; half.Render(drain, 22050);
        Assert(half.ActiveVoiceCount == 0, "Dropping the pedal should let the dampers end the stretched SFZ tail quickly.");

        // Loops: a held note keeps sounding long after its half-second sample would have run out.
        var looper = new SfzSynthesizer(instrument, 22050); looper.NoteOn(0, 50, 100);
        var held = new float[44100 * 2]; looper.Render(held, 44100);
        Assert(looper.ActiveVoiceCount == 1 && held.Skip(44100 * 2 - 400).Any(s => s != 0), "loop_continuous should keep a held note ringing past the sample end.");

        // Round-robin: the second strike of the same key plays the alternate (-20 dB) take.
        var robin = new SfzSynthesizer(instrument, 22050);
        robin.NoteOn(0, 40, 100); robin.Render(block, 2205); var firstPeak = Peak(block);
        robin.NoteOff(0, 40); robin.Render(tail, 33075);
        robin.NoteOn(0, 40, 100); robin.Render(block, 2205); var secondPeak = Peak(block);
        Assert(firstPeak > secondPeak * 5 && secondPeak > 0, $"Round-robin regions should alternate takes ({firstPeak:0.0000} then {secondPeak:0.0000}).");

        using (var engine = new PianoAudioEngine())
        {
            Assert(!engine.HasInstrument, "The audio engine should remain silent until its SFZ instrument is loaded.");
            engine.LoadSfzInstrument(Path.Combine(dir, "piano.sfz"));
            Assert(engine.HasInstrument && engine.InstrumentName == "piano", "The audio engine should load the SFZ instrument into the live pipeline.");
            engine.UnloadInstrument();
            Assert(!engine.HasInstrument, "Unloading the piano should return to silent mode.");
        }
        File.WriteAllText(Path.Combine(dir, "broken.sfz"), "<region> sample=missing.wav\n");
        var missing = false;
        try { _ = SfzReader.Read(Path.Combine(dir, "broken.sfz")); } catch (FileNotFoundException) { missing = true; }
        Assert(missing, "A missing SFZ sample should be reported with its path rather than loaded silent.");

        // Pedal noises: on_locc64 one-shots fire on the pedal's own edges and never answer the keyboard.
        File.WriteAllText(Path.Combine(dir, "pedal.sfz"),
            "<group> on_locc64=127 on_hicc64=127 volume=-12\n<region> lorand=0 hirand=0.5 sample=rel.wav\n<region> lorand=0.5 hirand=1 sample=rel.wav\n" +
            "<group> on_locc64=0 on_hicc64=0\n<region> sample=rel.wav\n");
        var pedalSet = SfzReader.Read(Path.Combine(dir, "pedal.sfz"));
        Assert(pedalSet.Regions.Count == 3 && pedalSet.Regions.All(r => r.IsCc64Trigger), "on_locc64/on_hicc64 regions should parse as CC-triggered one-shots.");
        var thump = new SfzSynthesizer(pedalSet, 22050);
        thump.ProcessMidi(0, 0xB0, 64, 127);
        Assert(thump.ActiveVoiceCount == 1, "Flooring the pedal should fire exactly one down-thump take (lorand picks it).");
        thump.NoteOn(0, 60, 100); thump.ProcessMidi(0, 0xB0, 64, 127);
        Assert(thump.ActiveVoiceCount == 1, "CC-triggered regions must not answer NoteOn, and a repeated CC value is not a new edge.");
        thump.ProcessMidi(0, 0xB0, 64, 0);
        Assert(thump.ActiveVoiceCount == 2, "Lifting the pedal should fire the up-thump sample.");
        var thumps = new float[11025 * 2]; thump.Render(thumps, 11025);
        Assert(thump.ActiveVoiceCount == 0 && thumps.Any(s => s != 0), "Pedal noises are audible one-shots that end with their samples.");

        Results.Add("PASS SFZ engine: WAV 16/24/float decoding, parser inheritance with #define/#include, velocity layers, round-robins, release samples gated by the damper, loops, half-pedalling, pedal-noise one-shots and engine integration.");
    }

    /// <summary>
    /// The hand-rolled FLAC decoder, against a stream this method assembles bit by bit: a STREAMINFO
    /// header and one frame with a constant subframe. The fixed, LPC and stereo decode paths are proven
    /// bit-exact against libFLAC by the test project's committed fixtures; this startup check makes sure
    /// the container walk itself never regresses on the machine that will load downloaded FLAC samples.
    /// </summary>
    private static void VerifyFlacReader()
    {
        var bytes = new List<byte> { (byte)'f', (byte)'L', (byte)'a', (byte)'C' };
        int accumulator = 0, filled = 0;
        void W(uint value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                accumulator = accumulator << 1 | (int)(value >> i & 1);
                if (++filled == 8) { bytes.Add((byte)accumulator); accumulator = 0; filled = 0; }
            }
        }
        W(1, 1); W(0, 7); W(34, 24);                                // last metadata block: STREAMINFO, 34 bytes
        W(16, 16); W(16, 16); W(0, 24); W(0, 24);                   // block and frame size ranges
        W(22050, 20); W(0, 3); W(15, 5); W(0, 4); W(16, 32);        // rate, mono, 16-bit, 16 samples total
        for (var i = 0; i < 4; i++) W(0, 32);                       // MD5, unused
        W(0x3FFE, 14); W(0, 2); W(6, 4); W(0, 4); W(0, 4); W(4, 3); W(0, 1); // frame: 8-bit block size follows, mono, 16-bit
        W(0, 8); W(15, 8); W(0, 8);                                 // frame number, block size 16, CRC-8 (unchecked)
        W(0, 1); W(0, 6); W(0, 1); W(1234, 16);                     // constant subframe holding 1234
        while (filled != 0) W(0, 1);                                // byte alignment
        W(0, 16);                                                   // frame CRC-16 (unchecked)
        var decoded = FlacReader.Read(bytes.ToArray());
        Assert(decoded.SampleRate == 22050 && decoded.Channels == 1 && decoded.Samples.Length == 16 && decoded.Samples.All(s => s == 1234),
            "The FLAC reader should decode a hand-assembled constant-subframe stream exactly.");
        var refused = false;
        try { _ = FlacReader.Read(new byte[] { 1, 2, 3, 4 }); } catch (InvalidDataException) { refused = true; }
        Assert(refused, "A file that is not FLAC should be refused with a clear error.");
        Results.Add("PASS FLAC reader: container walk, STREAMINFO and constant-subframe decode on a hand-assembled stream; non-FLAC input refused.");
    }

    /// <summary>
    /// The downloadable Salamander piano's bookkeeping: the pinned manifest and the generated SFZ mapping
    /// must agree with each other, because the downloader verifies files against the former and the
    /// engine plays the latter. No network is touched here.
    /// </summary>
    private static void VerifySalamanderPiano()
    {
        var entries = SalamanderPiano.ParseManifest();
        Assert(entries.Length == 641 && entries.Sum(e => e.Size) == SalamanderPiano.TotalBytes,
            "The Salamander manifest should list all 641 samples and add up to the advertised download size.");
        Assert(entries.All(e => e.BlobSha.Length == 40 && e.Size > 0 && e.Path.StartsWith("Samples/", StringComparison.Ordinal) && e.Path.EndsWith(".flac", StringComparison.Ordinal))
            && entries.Select(e => e.Path).Distinct().Count() == entries.Length,
            "Every manifest entry should carry a hash, a size and a unique Samples/ path.");
        var names = entries.Select(e => e.Path["Samples/".Length..]).ToHashSet(StringComparer.Ordinal);
        var referenced = SalamanderPiano.BuildSfz().Split('\n')
            .Where(line => line.StartsWith("<region>", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("sample=", StringComparison.Ordinal) + "sample=".Length)..].Trim())
            .ToList();
        Assert(referenced.Count == 641 && referenced.Distinct().Count() == 641 && referenced.All(names.Contains),
            "The generated Salamander mapping should reference every downloaded sample exactly once.");
        Results.Add("PASS Salamander piano: 641-file manifest sums to the advertised size and the generated SFZ mapping references each sample exactly once.");
    }

    private static int ZeroCrossings(float[] interleaved)
    {
        var crossings = 0;
        for (var i = 2; i < interleaved.Length; i += 2) if (interleaved[i] > 0 != interleaved[i - 2] > 0) crossings++;
        return crossings;
    }

    private static double Peak(float[] interleaved)
    {
        var peak = 0.0;
        foreach (var sample in interleaved) peak = Math.Max(peak, Math.Abs(sample));
        return peak;
    }

    /// <summary>Writes a small PCM (16/24-bit) or float32 WAVE file; every channel carries the same wave.</summary>
    private static void WriteTestWav(string path, int channels, int bits, int rate, int frames, Func<int, double> wave)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        var bytesPerSample = bits / 8; var dataSize = frames * channels * bytesPerSample;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((uint)(36 + dataSize)); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16u); w.Write((ushort)(bits == 32 ? 3 : 1)); w.Write((ushort)channels);
        w.Write((uint)rate); w.Write((uint)(rate * channels * bytesPerSample)); w.Write((ushort)(channels * bytesPerSample)); w.Write((ushort)bits);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write((uint)dataSize);
        for (var i = 0; i < frames; i++)
        {
            var value = wave(i);
            for (var c = 0; c < channels; c++)
            {
                if (bits == 16) w.Write((short)Math.Clamp((int)Math.Round(value * 32767), short.MinValue, short.MaxValue));
                else if (bits == 24)
                {
                    var scaled = (int)Math.Round(value * 8388607);
                    w.Write((byte)scaled); w.Write((byte)(scaled >> 8)); w.Write((byte)(scaled >> 16));
                }
                else w.Write((float)value);
            }
        }
    }

    private static void VerifyStereoHallReverb()
    {
        var impulse = new short[4410 * 2]; impulse[0] = 24000; impulse[1] = 12000;
        new StereoHallReverb(44100).Process(impulse, 4410);
        Assert(impulse.Skip(1000 * 2).Any(sample => sample != 0), "Hall reverb should create a stereo decay after the dry piano impulse.");
        var dry = new short[4410 * 2]; dry[0] = 24000; dry[1] = 12000;
        new StereoHallReverb(44100) { Enabled = false }.Process(dry, 4410);
        Assert(dry.Take(2).SequenceEqual(new short[] { 24000, 12000 }) && dry.Skip(2).All(sample => sample == 0), "Turning reverb off should preserve the dry signal and produce no reflections.");
        var ringing = new StereoHallReverb(44100);
        var tailImpulse = new short[4410 * 2]; tailImpulse[0] = 24000; tailImpulse[1] = 12000;
        ringing.Process(tailImpulse, 4410);
        ringing.Enabled = false;
        var fading = new short[4410 * 2]; ringing.Process(fading, 4410);
        Assert(fading.Any(sample => sample != 0), "Bypassing the reverb while it rings should fade the existing tail out instead of cutting it.");
        // The tail fades over ~120 ms, i.e. across two 100 ms blocks; give the bypass a bounded
        // number of blocks to reach digital silence, then require it to stay silent afterwards.
        var settled = new short[4410 * 2];
        for (var block = 0; block < 5; block++)
        {
            Array.Clear(settled);
            ringing.Process(settled, 4410);
            if (settled.All(sample => sample == 0)) break;
        }
        Assert(settled.All(sample => sample == 0), "Once the faded tail ends, the bypassed reverb must stay silent.");
        var after = new short[4410 * 2]; ringing.Process(after, 4410);
        Assert(after.All(sample => sample == 0), "A settled bypassed reverb must keep the dry path silent.");
        Results.Add("PASS reverb: stereo room tail, stable dry path, selectable bypass and a faded tail on switch-off.");
    }

    /// <summary>
    /// The convolution reverb against its mathematical contract: a dirac impulse response must hand the
    /// dry signal back exactly one 512-frame partition later (the dry path itself untouched), a fresh
    /// disabled instance must be a bit-exact passthrough, and the built-in responses must be deterministic,
    /// stereo, and the length their rooms claim. The bypass fade mirrors the classic hall's behaviour.
    /// </summary>
    private static void VerifyConvolutionReverb()
    {
        // A dirac IR turns convolution into a pure delay, so every sample of the wet path is predictable.
        var dirac = new WavData { SampleRate = 44100, Channels = 2, Samples = new short[] { 32767, 32767 } };
        var reverb = new ConvolutionReverb(dirac, 44100, wet: .5f);
        const int frames = 2048;
        var rng = new Random(905);
        var input = new float[frames * 2];
        for (var i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() * .6 - .3);
        var buffer = (float[])input.Clone();
        reverb.Process(buffer, frames);
        var worst = 0f;
        for (var i = 0; i < frames * 2; i++)
        {
            var expected = i < 512 * 2 ? 0f : input[i - 512 * 2] * .5f;
            worst = Math.Max(worst, Math.Abs(buffer[i] - input[i] - expected));
        }
        Assert(worst < 2e-3f, $"A dirac response must echo the dry signal one block later through the FFT path (worst error {worst}).");
        var untouched = (float[])input.Clone();
        new ConvolutionReverb(dirac, 44100, wet: .5f) { Enabled = false }.Process(untouched, frames);
        Assert(untouched.SequenceEqual(input), "A disabled convolution reverb must be a bit-exact passthrough.");
        var hall = ReverbImpulse.ConcertHall(44100);
        Assert(hall is { Channels: 2, SampleRate: 44100 } && hall.Frames > 2 * 44100, "The generated concert hall should be a stereo tail beyond two seconds.");
        Assert(hall.Samples.SequenceEqual(ReverbImpulse.ConcertHall(44100).Samples), "The generated concert hall must be deterministic.");
        var board = ReverbImpulse.Soundboard(44100);
        Assert(board is { Channels: 2 } && board.Frames > 0 && board.Frames < 44100 / 2, "The soundboard response should be a short close-mic tail.");
        var ringing = new ConvolutionReverb(hall, 44100, wet: .32f);
        var tail = new float[4410 * 2]; tail[0] = tail[1] = .75f;
        ringing.Process(tail, 4410);
        Assert(tail.Skip(1200 * 2).Any(sample => sample != 0), "The concert hall must ring after the dry impulse has passed.");
        ringing.Enabled = false;
        var fading = new float[4410 * 2]; ringing.Process(fading, 4410);
        Assert(fading.Any(sample => sample != 0), "Bypassing the convolver while it rings should fade the tail, not cut it.");
        var settled = new float[4410 * 2];
        for (var block = 0; block < 5; block++)
        {
            Array.Clear(settled);
            ringing.Process(settled, 4410);
            if (settled.All(sample => sample == 0)) break;
        }
        Assert(settled.All(sample => sample == 0), "Once the faded tail ends, the bypassed convolver must fall silent.");
        var after = new float[4410 * 2]; ringing.Process(after, 4410);
        Assert(after.All(sample => sample == 0), "A settled bypassed convolver must keep the dry path silent.");
        Results.Add($"PASS convolution reverb: dirac-exact FFT path, bit-exact bypass, {hall.Frames / 44100.0:0.0} s generated hall and {board.Frames * 1000 / 44100} ms soundboard.");
    }

    /// <summary>
    /// The output-backend plumbing that behaves identically on every machine: the stable ids the settings
    /// file and the output combo share, the settings clamp that normalises hand-edited values, an ASIO
    /// registry read that must never throw (CI runners have no <c>HKLM\SOFTWARE\ASIO</c> key at all), and
    /// the float render path's parity with the 16-bit path — what WASAPI and ASIO play must be what the
    /// recording's WAV stores. Opening real devices is deliberately not attempted here: a headless runner
    /// has none, and the silent-machine behaviour is already covered by the startup check.
    /// </summary>
    private static void VerifyAudioOutputSelection()
    {
        Assert(AudioOutputChoice.Parse(AudioOutputChoice.WasapiSharedId) is { Kind: AudioBackendKind.WasapiShared } shared && shared.Id == AudioOutputChoice.WasapiSharedId, "The WASAPI shared id should round-trip through parse.");
        Assert(AudioOutputChoice.Parse(AudioOutputChoice.WasapiExclusiveId).Kind == AudioBackendKind.WasapiExclusive, "The WASAPI exclusive id should parse to its backend.");
        Assert(AudioOutputChoice.Parse(AudioOutputChoice.WaveOutId).Kind == AudioBackendKind.WaveOut, "The waveOut id should parse to its backend.");
        var asio = AudioOutputChoice.Parse("asio:Focusrite USB ASIO");
        Assert(asio.Kind == AudioBackendKind.Asio && asio.AsioDriver == "Focusrite USB ASIO" && asio.Id == "asio:Focusrite USB ASIO", "An ASIO id should keep the driver name through a parse round-trip.");
        Assert(AudioOutputChoice.ForAsio("X").Id == "asio:X", "ForAsio should build the same id the combo stores.");
        const string endpointId = @"\\?\SWD#MMDEVAPI#{voicemeeter}:capture";
        var endpointChoice = AudioOutputChoice.ForWasapiEndpoint(endpointId, exclusive: true);
        Assert(AudioOutputChoice.Parse(endpointChoice.Id) is { Kind: AudioBackendKind.WasapiExclusive, EndpointId: endpointId }, "A named Windows/Voicemeeter endpoint should round-trip by stable id.");
        Assert(AudioOutputChoice.Parse("qwancy").Kind == AudioBackendKind.Auto && AudioOutputChoice.Parse(null).Id == AudioOutputChoice.AutoId && AudioOutputChoice.Parse("asio:").Kind == AudioBackendKind.Auto, "Unknown or empty output ids must parse as Auto, never throw.");
        var settings = new PianoVisualSettings { AudioOutputMode = "not-a-backend", AudioLatencyMs = 400, AudioMasterGain = 8 };
        settings.Clamp();
        Assert(settings.AudioOutputMode == AudioOutputChoice.AutoId && settings.AudioLatencyMs == 50 && settings.AudioMasterGain == 2, "The settings clamp should normalize the backend, latency target and output gain.");
        _ = AsioDriverRegistry.Names; // most machines have no HKLM\SOFTWARE\ASIO key; reading it must not throw
        Assert(AsioDriverRegistry.ClassIdOf("keyflow-not-a-driver") is null, "A driver name that is not installed should answer null, not throw.");

        var fixtureDirectory = Path.Combine(Path.GetTempPath(), "keyflow-audio-output-sfz");
        Directory.CreateDirectory(fixtureDirectory);
        var samplePath = Path.Combine(fixtureDirectory, "a4.wav");
        WriteTestWav(samplePath, channels: 2, bits: 16, rate: 22050, frames: 11025, wave: i => Math.Sin(i * 2 * Math.PI * 440 / 22050) * .33);
        var sfzPath = Path.Combine(fixtureDirectory, "audio-output.sfz");
        File.WriteAllText(sfzPath, "<region> sample=a4.wav key=69 pitch_keycenter=69 amp_veltrack=0 ampeg_release=0.2");
        var synth = new SfzSynthesizer(SfzReader.Read(sfzPath), 22050);
        synth.NoteOn(0, 69, 100);
        var floats = new float[2205 * 2]; synth.Render(floats, 2205);
        Assert(floats.Any(sample => sample != 0) && floats.All(sample => float.IsFinite(sample) && sample is >= -1f and <= 1f), "The SFZ float render should produce finite audio within [-1, 1].");

        var pcmReverb = new short[4410 * 2]; pcmReverb[0] = 24000; pcmReverb[1] = 12000;
        new StereoHallReverb(44100).Process(pcmReverb, 4410);
        var floatReverb = new float[4410 * 2]; floatReverb[0] = 24000 / 32768f; floatReverb[1] = 12000 / 32768f;
        new StereoHallReverb(44100).Process(floatReverb, 4410);
        var reverbDelta = 0.0;
        for (var i = 0; i < pcmReverb.Length; i++) reverbDelta = Math.Max(reverbDelta, Math.Abs(pcmReverb[i] - Math.Round((double)floatReverb[i] * short.MaxValue)));
        Assert(reverbDelta <= 1, $"The float and 16-bit reverb paths should agree to the LSB (max delta {reverbDelta}).");
        Results.Add("PASS audio output: stable backend ids, clamped settings, safe ASIO registry reads, an audible bounded SFZ float path, and float/PCM parity for reverb.");
    }

    private static void VerifyMidiDevicesAndKeyboardMap()
    {
        Assert(MainWindow.MapComputerKey(Key.A) == 48 && MainWindow.MapComputerKey(Key.K) == 69, "Computer keyboard map should span the expected octave.");
        Assert(MainWindow.MapComputerKey(Key.Q) == -1, "Unmapped computer keys should be ignored.");
        var on = MidiDeviceService.PackNoteMessage(60, 100, true, 2);
        Assert((on & 255) == 0x92 && ((on >> 8) & 127) == 60 && ((on >> 16) & 127) == 100, "MIDI output Note On bytes should be correct.");
        var off = MidiDeviceService.PackNoteMessage(60, 0, false, 2);
        Assert((off & 255) == 0x82 && ((off >> 8) & 127) == 60, "MIDI output Note Off bytes should be correct.");
        using var service = new MidiDeviceService(); var messages = new List<(int pitch, int velocity, bool on)>();
        var pedals = new List<(PianoPedal pedal, bool down)>();
        var levels = new List<(PianoPedal pedal, int value)>();
        service.NoteChanged += (pitch, velocity, pressed) => messages.Add((pitch, velocity, pressed));
        service.PedalChanged += (pedal, down) => pedals.Add((pedal, down));
        service.PedalLevelChanged += (pedal, value) => levels.Add((pedal, value));
        service.ProcessNativeInputMessage(0x3C1, (UIntPtr)on); // MIM_OPEN must not be parsed as a note.
        service.ProcessNativeInputMessage(MidiDeviceService.MidiInputDataMessage, (UIntPtr)on);
        service.ProcessNativeInputMessage(MidiDeviceService.MidiInputDataMessage, (UIntPtr)off);
        service.ProcessShortMessage(0x00407BB0);
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(67, 127));
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(66, 127));
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(64, 127));
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(67, 0));
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(66, 0));
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(64, 0));
        Assert(messages.Count == 2 && messages[0] == (60, 100, true) && messages[1] == (60, 0, false), "The WinMM MIM_DATA callback should translate packed Note On/Off messages and ignore other callback messages.");
        Assert(pedals.Count == 6 && pedals.Take(3).Select(x => x.pedal).SequenceEqual([PianoPedal.Soft, PianoPedal.Sostenuto, PianoPedal.Sustain]) && pedals.All(x => x.down == pedals.IndexOf(x) < 3), "MIDI CC 67/66/64 should report real-time soft, sostenuto and sustain pedal edges.");
        Assert(MidiDeviceService.ControllerFor(PianoPedal.Sustain) == 64 && MidiDeviceService.ControllerFor(PianoPedal.Sostenuto) == 66 && MidiDeviceService.ControllerFor(PianoPedal.Soft) == 67, "Pedal output should use the standard MIDI controller numbers.");
        // A continuous pedal sends the whole 0..127 range; retain each raw value and only emit on/off edges.
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(64, 127));
        service.ProcessShortMessage(MidiDeviceService.PackControllerMessage(64, 45));
        Assert(levels.Count == 8 && levels[6] == (PianoPedal.Sustain, 127) && levels[7] == (PianoPedal.Sustain, 45) && levels.Take(6).Select(x => x.value).SequenceEqual([127, 127, 127, 0, 0, 0]), "Pedal messages should also report their raw controller value for half-pedalling.");
        Assert(pedals.Count == 8 && pedals[6] == (PianoPedal.Sustain, true) && pedals[7] == (PianoPedal.Sustain, false), "A half-pedal value below 64 should transition to 'up' for the on/off consumers.");
        var controllerMessage = MidiDeviceService.PackControllerMessage(64, 127, 3);
        Assert((controllerMessage & 255) == 0xB3 && ((controllerMessage >> 8) & 127) == 64 && ((controllerMessage >> 16) & 127) == 127, "MIDI pedal output should pack controller, value and channel correctly.");
        var ins = MidiDeviceService.Inputs; var outs = MidiDeviceService.Outputs;
        Assert(ins.All(x => !string.IsNullOrWhiteSpace(x)) && outs.All(x => !string.IsNullOrWhiteSpace(x)), "Windows MIDI endpoints should enumerate with valid names.");
        if (outs.Count > 0)
        {
            // Windows lists endpoints it will not always open (Microsoft GS Wavetable Synth on a session
            // without audio, a port held by another process). That is an environment limit, not a defect.
            try
            {
                using var output = new MidiDeviceService(); output.OpenOutput(0); output.SendNote(69, 64, true); Thread.Sleep(90); output.SendNote(69, 0, false);
                output.SendController(67, 127); output.SendController(66, 127); output.SendController(64, 127);
                output.SendController(67, 0); output.SendController(66, 0); output.SendController(64, 0);
                Results.Add($"PASS native MIDI output: sent Note On/Off and three-pedal CC events to {outs[0]}.");
            }
            catch (Exception ex) { Results.Add($"NOTE the MIDI output {outs[0]} is listed but could not be opened here ({ex.Message}); the app resets the picker instead of failing."); }
        }
        Results.Add($"PASS MIDI devices: key map, input/output encoding, inputs=[{string.Join(", ", ins)}], outputs=[{string.Join(", ", outs)}].");
    }

    private static void VerifyWpfInteractions(MainWindow window, Action completed, Action<Exception> failed)
    {
        var stage = (PianoStage)window.FindName("Stage"); var mode = (ComboBox)window.FindName("ModeCombo"); var tracks = (ComboBox)window.FindName("TrackCombo");
        var panel = (Border)window.FindName("SettingsPanel"); var overlay = (Grid)window.FindName("LiveChromeOverlay"); var recordButton = (Button)window.FindName("RecordButton");
        var rowDefinitions = ((Grid)window.Content).RowDefinitions;
        var source = PresentationSource.FromVisual(window)!;
        void PressEscape() => Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        void MoveMouse() { SetField(window, "_lastPointerPoint", null!); Invoke(window, "Window_MouseMove", window, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent }); }
        void GoIdle() { SetField(window, "_lastPointerActivity", DateTime.UtcNow.AddSeconds(-4)); Invoke(window, "CheckChromeIdle"); }
        Assert(panel.Visibility == Visibility.Collapsed && overlay.Visibility == Visibility.Visible, "Live Play should start immersive with settings hidden and the idle toolbar available.");
        GoIdle();
        Assert(overlay.Visibility == Visibility.Collapsed && rowDefinitions[0].Height.Value == 0 && rowDefinitions[2].Height.Value == 0, $"An idle pointer should hide the toolbar, menu and REC so only the stage remains (rows {rowDefinitions[0].Height.Value} and {rowDefinitions[2].Height.Value}, expected 0).");
        MoveMouse();
        Assert(overlay.Visibility == Visibility.Visible && rowDefinitions[0].Height.Value > 0 && recordButton.Visibility == Visibility.Visible && panel.Visibility == Visibility.Collapsed, "Mouse movement should bring the toolbar, menu and REC back without opening settings.");
        GoIdle(); PressEscape();
        Assert(panel.Visibility == Visibility.Visible && overlay.Visibility == Visibility.Collapsed && rowDefinitions[0].Height.Value > 0, "Escape should open the Stage Design panel directly, even from the idle stage.");
        GoIdle();
        Assert(panel.Visibility == Visibility.Collapsed && rowDefinitions[0].Height.Value == 0 && rowDefinitions[2].Height.Value == 0, "An idle pointer should also hide an open settings panel.");
        MoveMouse();
        Assert(panel.Visibility == Visibility.Visible && overlay.Visibility == Visibility.Collapsed && recordButton.Visibility == Visibility.Collapsed, "Mouse movement should restore the settings panel that idle hid.");
        PressEscape();
        Assert(panel.Visibility == Visibility.Collapsed && overlay.Visibility == Visibility.Visible && recordButton.Visibility == Visibility.Collapsed, "Escape should close Stage Design and restore the live toolbar.");
        MoveMouse(); PressEscape(); PressEscape();
        Assert(panel.Visibility == Visibility.Collapsed && overlay.Visibility == Visibility.Visible, "Escape should toggle the settings panel.");
        var visualSettings = (PianoVisualSettings)Field(window, "_visualSettings"); var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        var stageVisual = (PianoVisualSettings)Field(stage, "_visual");
        Assert(sliders.Count >= 30 && stageVisual.ToJson() == visualSettings.WithAccessibilityTogglesApplied().ToJson(),
            "The detailed scene, note, particle and camera controls should drive the renderer configuration, adjusted only by the accessibility policy.");
        // Live wiring of the global toggles: a Reduced Motion switch flipped in the dock must reach the
        // stage through the policy, and flipping it back restores the user's own motion setting.
        visualSettings.BackgroundMotion = "Aurora";
        visualSettings.ReducedMotion = true;
        stage.SetVisualSettings(visualSettings);
        Assert(((PianoVisualSettings)Field(stage, "_visual")).BackgroundMotion == "None",
            "Turning on Reduced motion should freeze the stage's background motion through the accessibility policy.");
        visualSettings.ReducedMotion = false;
        visualSettings.BackgroundMotion = "None";
        stage.SetVisualSettings(visualSettings);
        Assert(((PianoVisualSettings)Field(stage, "_visual")).BackgroundMotion == "None"
                && ((PianoVisualSettings)Field(stage, "_visual")).ToJson() == visualSettings.WithAccessibilityTogglesApplied().ToJson(),
            "Turning Reduced motion back off should restore the user's own motion setting on the stage.");
        var colorInputs = (Dictionary<string, TextBox>)Field(window, "_visualColorInputs"); var colorButtons = (Dictionary<string, Button>)Field(window, "_visualColorButtons");
        Assert(colorInputs.ContainsKey(nameof(PianoVisualSettings.NoteColorStart)) && colorInputs.ContainsKey(nameof(PianoVisualSettings.NoteColorEnd)) && colorInputs.ContainsKey(nameof(PianoVisualSettings.HaloColor)) && colorInputs.ContainsKey(nameof(PianoVisualSettings.LeftHandColor)) && colorButtons.Count >= 6 && colorButtons.Count == colorInputs.Count,
            "Live design settings should provide an interactive color picker for the note gradient, hands, halo, keys and background colors.");
        Run(nameof(VerifyDeterministicPreview), () => VerifyDeterministicPreview(stage));
        Run(nameof(VerifySettingsDock), () => VerifySettingsDock(window, stage, visualSettings));
        Run(nameof(VerifyLanguageSwitching), () => VerifyLanguageSwitching(window));
        Run(nameof(VerifyAccessibility), () => VerifyAccessibility(window));
        Run(nameof(VerifyDockAccessibility), () => VerifyDockAccessibility(window));
        Run(nameof(VerifySettingsHistory), () => VerifySettingsHistory(window));
        Run(nameof(VerifySettingsProfile), () => VerifySettingsProfile(window));
        Run(nameof(VerifyPresetSharing), () => VerifyPresetSharing(window));
        Run(nameof(VerifyPresetThumbnails), () => VerifyPresetThumbnails(window));
        Run(nameof(VerifyCommunityPresets), () => VerifyCommunityPresets(window));
        Run(nameof(VerifyUserShellThemes), () => VerifyUserShellThemes(window));
        Run(nameof(VerifyPngSequenceRecorder), () => VerifyPngSequenceRecorder(window));
        Run(nameof(VerifyRecordingAudioTrack), () => VerifyRecordingAudioTrack(window));
        Run(nameof(VerifyMp4Recorder), () => VerifyMp4Recorder(window));
        Run(nameof(VerifyCameraOverlayDock), () => VerifyCameraOverlayDock(window, stage, visualSettings));
        Run(nameof(VerifyCalibrationWindow), () => VerifyCalibrationWindow(window, visualSettings));
        Run(nameof(VerifyHandTrackingDock), () => VerifyHandTrackingDock(window, stage, visualSettings));
        Run(nameof(VerifySheetLayer), () => VerifySheetLayer(window, stage, visualSettings));
        Run(nameof(VerifySongFolderLibrary), () => VerifySongFolderLibrary(window));
        Run(nameof(VerifyBackgroundImageLoad), () => VerifyBackgroundImageLoad(window, stage, visualSettings));
        var frameCapture = (byte[])window.GetType().GetMethod("CaptureStageBgr", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [64, 48])!;
        Assert(frameCapture.Length == AviVideoRecorder.BgrStride(64) * 48, "The on-screen piano stage should render into correctly-strided video frames.");
        var glowSlider = sliders[nameof(PianoVisualSettings.NoteGlow)]; var originalGlow = glowSlider.Value; glowSlider.Value = 127;
        Assert(visualSettings.NoteGlow == 127 && ((PianoVisualSettings)Field(stage, "_visual")).NoteGlow == 127, "Adjusting note bloom should update the stage renderer immediately.");
        glowSlider.Value = originalGlow; ((DispatcherTimer)Field(window, "_settingsSaveTimer")).Stop();
        var wasShowingEmbers = visualSettings.ShowEmbers;
        // The claim is about bursts this impact would add, so anything still in the air from a check above is
        // cleared first: flames left burning by an earlier preset would otherwise keep emitting on the clock.
        stage.ClearTransient();
        visualSettings.ShowEmbers = false; stage.SetVisualSettings(visualSettings); stage.Impact(60);
        Assert(stage.SparkCount == 0, "Turning off the ember layer should stop new particle bursts.");
        visualSettings.ShowEmbers = wasShowingEmbers; stage.SetVisualSettings(visualSettings);
        Run(nameof(VerifyEmbersShell), () => VerifyEmbersShell(window, visualSettings));
        var piano = (PianoAudioEngine)Field(window, "_audio"); var instrumentLabel = (TextBlock)window.FindName("InstrumentStatusLabel");
        var midi = (MidiDeviceService)Field(window, "_midi"); var inputCombo = (ComboBox)window.FindName("InputDeviceCombo");
        Assert(MidiDeviceService.Inputs.Count == 0 ? inputCombo.SelectedIndex == 0 && !midi.InputOpen : inputCombo.SelectedIndex > 0 && midi.InputOpen,
            "The first detected physical MIDI input should be opened automatically and remain visibly selected.");
        Invoke(window, "RefreshDevices_Click", window, new RoutedEventArgs());
        Assert(MidiDeviceService.Inputs.Count == 0 ? inputCombo.SelectedIndex == 0 && !midi.InputOpen : inputCombo.SelectedIndex > 0 && midi.InputOpen,
            "Refreshing the MIDI list should preserve or auto-select an available input instead of silently switching to computer-only mode.");
        inputCombo.SelectedIndex = 0;
        Invoke(window, "RefreshDevices_Click", window, new RoutedEventArgs());
        Assert(inputCombo.SelectedIndex == 0 && !midi.InputOpen,
            "An explicit 'Computer keyboard only' choice must survive a device refresh instead of snapping back to a MIDI input.");
        var expectedInstrumentStatus = SalamanderPiano.IsInstalled ? "SALAMANDER GRAND · INSTALLED" : "SALAMANDER GRAND · SETUP REQUIRED";
        var salamanderButton = (Button)window.FindName("SalamanderButton");
        Assert(!piano.HasInstrument && instrumentLabel.Text == Loc.T(expectedInstrumentStatus)
                && salamanderButton.Visibility == Visibility.Visible && salamanderButton.IsEnabled,
            "The initial UI must offer Salamander setup in Audio settings and remain silent without the single supported piano.");
        var reverb = (ToggleButton)window.FindName("ReverbToggle");
        Assert(reverb.IsChecked == true && piano.ReverbEnabled, "The built-in concert room reverb should start enabled.");
        reverb.IsChecked = false; Assert(!piano.ReverbEnabled, "The reverb control should bypass the live audio effect.");
        reverb.IsChecked = true; Assert(piano.ReverbEnabled, "Reverb should be switchable back on while the piano is running.");
        Assert(!(bool)Field(window, "_playing") && ((IEnumerable<NoteEvent>)Field(window, "_notes")).Count() == 0 && !((Button)window.FindName("PlayButton")).IsEnabled, "Startup should be live-play only with no demo MIDI or automatic transport.");
        var wasLiveShowEmbers = visualSettings.ShowEmbers;
        var wasLiveParticleAmount = visualSettings.ParticleAmount;
        var wasLiveParticleResponse = visualSettings.ParticleResponse;
        var wasLiveImpactBurst = visualSettings.ImpactBurst;
        // Concert Gold deliberately uses a low particle budget. Set an explicit test budget so this
        // input regression verifies that the burst path works, not whichever preset happens to be loaded.
        stage.ClearTransient();
        visualSettings.ShowEmbers = true; visualSettings.ParticleAmount = 24;
        visualSettings.ParticleResponse = 100; visualSettings.ImpactBurst = "Dust";
        stage.SetVisualSettings(visualSettings);
        Invoke(window, "PressNote", 60, 90);
        Assert(!(bool)Field(window, "_playing") && Math.Abs((double)Field(window, "_position")) < .001, "A live piano key must not start or advance MIDI playback.");
        Assert(stage.LiveTrailCount == 1 && stage.SparkCount >= 12, "A keypress should create a short falling note and a visible impact burst.");
        Assert(!piano.HasInstrument, "Keyboard input should not synthesize a replacement piano voice in silent mode.");
        Assert((int)Field(window, "_hits") == 0 && (int)Field(window, "_misses") == 0, "Free-play notes should not affect song practice scoring.");
        midi.ProcessNativeInputMessage(MidiDeviceService.MidiInputDataMessage, (UIntPtr)MidiDeviceService.PackNoteMessage(61, 90, true));
        midi.ProcessNativeInputMessage(MidiDeviceService.MidiInputDataMessage, (UIntPtr)MidiDeviceService.PackControllerMessage(67, 127));
        var livePosition = (double)Field(window, "_position"); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Assert(!(bool)Field(window, "_playing") && Math.Abs((double)Field(window, "_position") - livePosition) < .001, "MIDI Note On should remain live-only and leave the song playhead stopped.");
                Assert(stage.LiveTrailCount == 2 && stage.SparkCount > 0, "MIDI Note On should create its own animation and sparks.");
                visualSettings.ShowEmbers = wasLiveShowEmbers; visualSettings.ParticleAmount = wasLiveParticleAmount;
                visualSettings.ParticleResponse = wasLiveParticleResponse; visualSettings.ImpactBurst = wasLiveImpactBurst;
                stage.SetVisualSettings(visualSettings);
                Assert(stage.FirstLiveTrailY > 0 && stage.FirstLiveTrailY < stage.ActualHeight - stage.KeyboardHeight, "A pressed-key note should fall down its own lane while the song playhead stays still.");
                Assert(((ToggleButton)window.FindName("SoftPedalToggle")).IsChecked == true && ((HashSet<PianoPedal>)Field(window, "_pedalsDown")).Contains(PianoPedal.Soft), "Incoming MIDI CC 67 should update the soft pedal control immediately.");
                var heldLength = stage.LiveTrailHeightFor(60);
                Assert(heldLength > 28, "A held key should extend its falling note over time.");
                Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.A) { RoutedEvent = Keyboard.KeyDownEvent });
                Assert(((HashSet<int>)Field(window, "_pressed")).Contains(48), "Computer key down should press its mapped piano key.");
                Invoke(window, "Window_KeyUp", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.A) { RoutedEvent = Keyboard.KeyUpEvent });
                Assert(!((HashSet<int>)Field(window, "_pressed")).Contains(48), "Computer key up should release the same piano key.");
                Invoke(window, "ReleaseNote", 60); var releasedLength = stage.LiveTrailHeightFor(60); stage.Advance(.05);
                Assert(Math.Abs(stage.LiveTrailHeightFor(60) - releasedLength) < .001, "Releasing the key should freeze its note length.");
                midi.ProcessNativeInputMessage(MidiDeviceService.MidiInputDataMessage, (UIntPtr)MidiDeviceService.PackNoteMessage(61, 0, false));

                var sustainToggle = (ToggleButton)window.FindName("SustainPedalToggle"); sustainToggle.IsChecked = true;
                Invoke(window, "PressNote", 63, 90); stage.Advance(.05); var sustainLength = stage.LiveTrailHeightFor(63);
                Invoke(window, "ReleaseNote", 63); stage.Advance(.05);
                Assert(Math.Abs(stage.LiveTrailHeightFor(63) - sustainLength) < .001, "Sustain should extend the audio only; the visual bar should freeze on physical key release.");
                sustainToggle.IsChecked = false; var sustainReleasedLength = stage.LiveTrailHeightFor(63); stage.Advance(.05);
                Assert(Math.Abs(stage.LiveTrailHeightFor(63) - sustainReleasedLength) < .001, "Releasing sustain should not change the frozen visual note length.");

                var sostenutoToggle = (ToggleButton)window.FindName("SostenutoPedalToggle");
                Invoke(window, "PressNote", 64, 90); stage.Advance(.03); sostenutoToggle.IsChecked = true;
                Invoke(window, "ReleaseNote", 64); stage.Advance(.05); var sostenutoLength = stage.LiveTrailHeightFor(64);
                Assert(Math.Abs(sostenutoLength - (28 + .03 * visualSettings.NoteFallSpeed)) < .001, "Sostenuto should sustain audio without capturing extra visual note length after key release.");
                sostenutoToggle.IsChecked = false; var sostenutoReleasedLength = stage.LiveTrailHeightFor(64); stage.Advance(.05);
                Assert(Math.Abs(stage.LiveTrailHeightFor(64) - sostenutoReleasedLength) < .001, "Releasing sostenuto should end its captured visual note.");

                var softToggle = (ToggleButton)window.FindName("SoftPedalToggle"); softToggle.IsChecked = false; softToggle.IsChecked = true;
                Assert(((HashSet<PianoPedal>)Field(window, "_pedalsDown")).Contains(PianoPedal.Soft), "The soft pedal control should update in real time.");
                softToggle.IsChecked = false;

                var practiceSong = MainWindow.CreateDemoSong();
                SetField(window, "_allNotes", practiceSong); SetField(window, "_notes", practiceSong);
                Invoke(window, "PopulateTracks", false); Invoke(window, "UpdateSongUi"); Invoke(window, "UpdatePlaybackLabel");
                Assert(((Button)window.FindName("PlayButton")).IsEnabled, "Opening a MIDI song should enable the transport.");
                Invoke(window, "Play_Click", window, new RoutedEventArgs());
                Assert((bool)Field(window, "_playing"), "MIDI playback should start from an explicit Play command.");
                var songStart = (double)Field(window, "_position");
                var playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                playbackTimer.Tick += (_, _) =>
                {
                    playbackTimer.Stop();
                    try
                    {
                        Assert((double)Field(window, "_position") > songStart, "Explicit Play should advance the song playhead in real time.");
                        VerifySongControls(window, stage, mode, tracks);
                        // Last, because it opens a song of its own: the live-play checks above expect a pristine transport.
                        VerifySongLibrary(window);
                        VerifyHandSplitInferenceOnSong(window);
                        VerifyPracticeTempo(window);
                        VerifyPracticeHistory(window);
                        VerifyPracticeGhostAndChart(window);
                        completed();
                    }
                    catch (Exception ex) { failed(ex); }
                };
                playbackTimer.Start();
            }
            catch (Exception ex) { failed(ex); }
        };
        timer.Start();
    }

    private static void VerifyPresets()
    {
        var names = VisualPresets.BuiltIn.Select(p => p.Name).ToList();
        Assert(names.Count == 14 && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count && names[0] == VisualPresets.DefaultPresetName && names.Contains("Concert Gold"),
            "The 14 curated looks should have unique stable names, with the selected Concert default listed first.");
        foreach (var preset in VisualPresets.BuiltIn)
        {
            var copy = preset.Settings.Clone(); copy.Clamp();
            Assert(copy.ToJson() == preset.Settings.ToJson(), $"The built-in preset '{preset.Name}' should already be within the allowed ranges.");
            Assert(preset.Settings.PresetName == preset.Name && !string.IsNullOrWhiteSpace(preset.Description),
                $"The built-in preset '{preset.Name}' should carry its own stable name and localized description.");
        }
        var shippedPresets = VisualPresets.BuiltIn.Concat(CommunityPresets.All).ToArray();
        Assert(shippedPresets.All(p => PianoVisualSettings.ColorModes.Contains(p.Settings.ColorMode)
                && PianoVisualSettings.Palettes.Contains(p.Settings.Palette)
                && PianoVisualSettings.NoteStyles.Contains(p.Settings.NoteStyle)
                && PianoVisualSettings.NoteDirections.Contains(p.Settings.NoteDirection)
                && PianoVisualSettings.ImpactWaves.Contains(p.Settings.ImpactWave)
                && PianoVisualSettings.ImpactBursts.Contains(p.Settings.ImpactBurst)
                && PianoVisualSettings.ImpactMorphs.Contains(p.Settings.ImpactMorph)
                && PianoVisualSettings.ImpactFlashStyles.Contains(p.Settings.ImpactFlashStyle)
                && PianoVisualSettings.AmbientEnergies.Contains(p.Settings.AmbientEnergy)
                && PianoVisualSettings.AmbientNatures.Contains(p.Settings.AmbientNature)
                && PianoVisualSettings.AmbientLights.Contains(p.Settings.AmbientLight)
                && PianoVisualSettings.AmbientCosmics.Contains(p.Settings.AmbientCosmic)
                && PianoVisualSettings.FallingTrails.Contains(p.Settings.FallingTrail)
                && PianoVisualSettings.ReleaseEffects.Contains(p.Settings.ReleaseEffect)
                && PianoVisualSettings.KeyboardStyles.Contains(p.Settings.KeyboardStyle)
                && PianoVisualSettings.ShadingQualities.Contains(p.Settings.ShadingQuality)
                && PianoVisualSettings.PressedKeyColorModes.Contains(p.Settings.PressedKeyColorMode)
                && PianoVisualSettings.KeyLabelModes.Contains(p.Settings.KeyLabels)
                && PianoVisualSettings.BackgroundModes.Contains(p.Settings.BackgroundMode)
                && ShellThemes.All.Any(theme => theme.Id == p.Settings.ShellTheme)),
            "Every shipped look should choose values supported by the settings model, not values that only survive by being clamped to a fallback.");
        var concert = VisualPresets.FindBuiltIn("Concert Gold")!.Settings;
        var neon = VisualPresets.FindBuiltIn("Neon Violet")!.Settings;
        var learning = VisualPresets.FindBuiltIn("Two Hands")!.Settings;
        Assert(VisualPresets.CreateDefaultSettings().ToJson() == concert.ToJson()
                && concert.NoteStyle == "Solid" && concert.ColorMode == "PerHand" && concert.ShellTheme == ShellThemes.VelvetGoldId
                && concert.NoteGlow <= 50 && concert.BloomIntensity <= 40 && !concert.ShowImpactFlash,
            "Concert Gold should be the fresh-install/reset look: two readable hands, restrained glow and no bright impact flash.");
        Assert(neon.NoteStyle == "Neon" && neon.BackgroundMotion == "Nebula" && neon.BloomIntensity <= 55 && neon.ImpactFlashIntensity <= 25,
            "Neon Violet should keep its creator identity while staying inside the new glow/flash budget.");
        Assert(learning.ColorMode == "PerHand" && learning.ShowNoteLabels && learning.KeyLabels == "C"
                && !learning.ShowEmbers && !learning.ShowImpactRings && !learning.ShowImpactFlash
                && learning.AmbientEnergy == "None" && learning.AmbientNature == "None" && learning.AmbientLight == "None" && learning.AmbientCosmic == "None",
            "Two Hands should prioritize legible hand colors and note/key labels without decorative particles.");
        Assert(VisualPresets.FindBuiltIn("Inferno")!.Settings.NoteStyle == "Fire"
                && VisualPresets.FindBuiltIn("Aurora Rainbow")!.Settings.ColorMode == "RainbowPitch"
                && VisualPresets.FindBuiltIn("Green Screen")!.Settings.BackgroundMode == "ChromaGreen"
                && !VisualPresets.FindBuiltIn("Green Screen")!.Settings.ShowHalo
                && VisualPresets.FindBuiltIn("Classic Roll")!.Settings.ColorMode == "PerTrack",
            "The themed and utility looks should retain their defining renderer behavior after the visual refresh.");
        Assert(VisualPresets.BuiltIn.All(p => PianoVisualSettings.BackgroundMotions.Contains(p.Settings.BackgroundMotion)
                && PianoVisualSettings.HaloPulseStyles.Contains(p.Settings.HaloPulseStyle))
            && VisualPresets.FindBuiltIn("Neon Violet")!.Settings.BackgroundMotion == "Nebula"
            && VisualPresets.FindBuiltIn("Inferno")!.Settings.BackgroundMotion == "Ember Haze"
            && VisualPresets.FindBuiltIn("Aurora Rainbow")!.Settings.HaloPulseStyle == "Spectrum"
            && VisualPresets.FindBuiltIn("Ice Crystal")!.Settings.HaloPulseStyle == "Ripple"
            && VisualPresets.FindBuiltIn("Ocean Depths")!.Settings.BackgroundMotion == "Ocean Flow"
            && VisualPresets.FindBuiltIn("Retro Arcade")!.Settings.BackgroundMotion == "Retro Grid"
            && VisualPresets.FindBuiltIn("Classic Roll")!.Settings.BackgroundMotion == "None" && !VisualPresets.FindBuiltIn("Classic Roll")!.Settings.HaloPulse
            && VisualPresets.FindBuiltIn("Two Hands")!.Settings.BackgroundMotion == "None" && !VisualPresets.FindBuiltIn("Two Hands")!.Settings.HaloPulse
            && VisualPresets.FindBuiltIn("Green Screen")!.Settings.BackgroundMotion == "None" && !VisualPresets.FindBuiltIn("Green Screen")!.Settings.HaloPulse,
            "Every built-in look should use a valid, theme-matched motion profile, while learning, roll and chroma looks stay still.");
        var originalSettingsDirectory = PianoVisualSettingsStore.SettingsDirectory;
        var firstRunDirectory = Path.Combine(Path.GetTempPath(), "keyflow-verify-default-look-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(firstRunDirectory);
            PianoVisualSettingsStore.UseDirectory(firstRunDirectory);
            var fresh = PianoVisualSettingsStore.Load();
            Assert(fresh.PresetName == "Concert Gold" && fresh.ColorMode == "PerHand" && fresh.NoteStyle == "Solid",
                "A settings folder with no saved file should start on the curated Concert Gold look.");
            File.WriteAllText(PianoVisualSettingsStore.SettingsPath, "{ malformed settings");
            var recovered = PianoVisualSettingsStore.Load();
            Assert(recovered.PresetName == "Concert Gold" && recovered.ColorMode == "PerHand",
                "An unreadable settings file should recover to the safe curated default rather than a busy model fallback.");
            var legacyLook = new PianoVisualSettings { PresetName = "Neon Violet", NoteStyle = "Neon", NoteColorStart = "#7B5CFF" };
            PianoVisualSettingsStore.Save(legacyLook);
            var restored = PianoVisualSettingsStore.Load();
            Assert(restored.PresetName == "Neon Violet" && restored.NoteStyle == "Neon" && restored.NoteColorStart == "#7B5CFF",
                "Changing the shipped default must not rewrite an existing user's saved look.");
        }
        finally
        {
            PianoVisualSettingsStore.UseDirectory(originalSettingsDirectory);
            try { if (Directory.Exists(firstRunDirectory)) Directory.Delete(firstRunDirectory, true); } catch { }
        }
        var target = new PianoVisualSettings { BackgroundImagePath = "C:\\keep.png", NoteGlow = 1 };
        target.CopyFrom(VisualPresets.Inferno());
        Assert(target.NoteStyle == "Fire" && target.BackgroundImagePath == "C:\\keep.png" && !ReferenceEquals(target.TrackColors, VisualPresets.Inferno().TrackColors),
            "Applying a preset should copy every value but keep the user's background image and not share list instances.");
        var directory = Path.Combine(Path.GetTempPath(), "keyflow-verify-presets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VisualPresetStore(directory);
            Assert(store.LoadUserPresets().Count == 0, "A missing preset folder should simply yield no user presets.");
            var saved = store.Save("My: Look?", VisualPresets.AuroraRainbow());
            var loaded = store.LoadUserPresets();
            Assert(saved.Name == "My- Look-" && loaded.Count == 1 && loaded[0].Name == saved.Name && loaded[0].Settings.ColorMode == "RainbowPitch" && loaded[0].Settings.BackgroundImagePath == "" && !loaded[0].BuiltIn,
                "User presets should be sanitized, written as JSON and read back without the background image path.");
            var exportPath = Path.Combine(directory, "out", "export.json"); Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            VisualPresetStore.Export(VisualPresets.IceCrystal(), exportPath);
            var imported = VisualPresetStore.Import(exportPath);
            Assert(imported.Name == "Ice Crystal" && imported.Settings.NoteStyle == "Glass", "Exported presets should import with their name and values.");
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");
            Assert(store.LoadUserPresets().Count == 1 && store.Delete(saved) && store.LoadUserPresets().Count == 0, "A corrupt preset file should be skipped and deleting a user preset should remove its file.");
        }
        finally { try { Directory.Delete(directory, true); } catch { } }
        Assert(MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.HandSplitPitch), "C4", out var split) && split == 60 && MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.HandSplitPitch), "F#3", out var sharp) && sharp == 54
            && MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.NoteGlow), " 85 % ", out var percent) && percent == 85 && MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.ParticleLife), "0,75 s", out var life) && Math.Abs(life - .75) < 1e-9
            && !MainWindow.TryParseSettingValue(nameof(PianoVisualSettings.NoteGlow), "abc", out _),
            "Typed setting values should accept units, decimal commas and note names.");
        Results.Add("PASS presets: built-in looks, apply/copy semantics, user preset store, import/export and typed value parsing.");
    }

    private static void VerifySettingsDock(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings)
    {
        var tabs = (TabControl)window.FindName("SettingsTabs");
        var pageCount = SettingsPages.Order.Length;
        // The captions of the tab strip are translated, so the comparison runs through Loc too — a
        // check that reads an English literal would fail the moment the suite runs in Vietnamese.
        string Shown(string caption) => Loc.T(caption);
        Assert(tabs.Items.Count == pageCount && ((TabItem)tabs.Items[0]).Header.ToString() == Shown("Style") && ((TabItem)tabs.Items[SettingsPages.IndexOf(SettingsPages.Theme)]).Header.ToString() == Shown("Theme") && ((TabItem)tabs.Items[pageCount - 1]).Header.ToString() == Shown(SettingsPages.General),
            $"The settings dock should expose all {pageCount} categorized pages from Style to General.");
        Assert(SettingsPages.IndexOf(SettingsPages.Theme) == 1 && SettingsPages.IndexOf(SettingsPages.General) == pageCount - 1 && SettingsPages.IndexOf("Nope") < 0,
            "Settings page names should resolve to their tab-strip index so no code has to keep magic tab numbers.");
        // The navigation is grouped by intent. Order is derived from the sections, the XAML tab strip
        // must match that order, and the header printed above a page must be the header of the section
        // that owns it — so a page can never sit under a caption the code does not know about.
        var headers = tabs.Items.Cast<TabItem>().Select(item => item.Header?.ToString() ?? "").ToArray();
        // A header may decorate the page name (Camera → "Camera & FX") but must start with it, which
        // is the same tolerance tools/check_sources.py applies to the markup.
        var pagesMatch = headers.Length == SettingsPages.Order.Length
            && headers.Select((header, index) => header == Shown(SettingsPages.Order[index]) || header.StartsWith(Shown(SettingsPages.Order[index]) + " ", StringComparison.Ordinal)).All(match => match);
        Assert(pagesMatch && SettingsPages.Order.SequenceEqual(SettingsPages.Sections.SelectMany(section => section.Pages)),
            $"The dock should list exactly the pages of the settings catalogue, in catalogue order (found {string.Join(", ", headers)}).");
        foreach (var section in SettingsPages.Sections)
        {
            var first = SettingsPages.IndexOf(section.Pages[0]);
            Assert(first >= 0 && SettingsPages.GetSection((TabItem)tabs.Items[first]) == section.Label,
                $"The section “{section.Label}” should be carried by its first page in the tab strip.");
            foreach (var page in section.Pages) Assert(SettingsPages.SectionOf(page)?.Label == section.Label, $"Settings page {page} should resolve to the section that lists it.");
        }
        Assert(SettingsPages.SectionOf("Nope") is null, "An unknown page name should have no section.");
        Assert(SettingsPages.Sections[0].Pages.Contains(SettingsPages.Style) && SettingsPages.Sections[^1].Pages.Contains(SettingsPages.General),
            "The first section should open on Style and the last one should close on the pages of the application itself.");
        var choices = (Dictionary<string, ComboBox>)Field(window, "_visualChoices"); var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.NoteStyle)) && choices.ContainsKey(nameof(PianoVisualSettings.ColorMode)) && choices.ContainsKey(nameof(PianoVisualSettings.KeyboardStyle)) && choices.ContainsKey(nameof(PianoVisualSettings.BackgroundMode))
                && choices.ContainsKey(nameof(PianoVisualSettings.BackgroundMotion)) && choices.ContainsKey(nameof(PianoVisualSettings.HaloPulseStyle))
                && toggles.ContainsKey(nameof(PianoVisualSettings.ShowWisps)) && toggles.ContainsKey(nameof(PianoVisualSettings.HaloPulse)),
            "Note style, background motion, hit-line style, keyboard style and their effect switches should be editable from the dock.");
        Assert(PianoVisualSettings.NoteStyles.SequenceEqual(["Solid", "Neon", "Glass", "Fire", "Gem", "CrystalOutline"])
                && choices[nameof(PianoVisualSettings.NoteStyle)].Items.Count == PianoVisualSettings.NoteStyles.Length,
            "The Notes page picker should expose Gem and Crystal Outline as materials alongside the legacy styles.");
        var initialBackgroundMotion = visualSettings.BackgroundMotion;
        choices[nameof(PianoVisualSettings.BackgroundMotion)].SelectedValue = "Ocean Flow";
        Assert(visualSettings.BackgroundMotion == "Ocean Flow", "Choosing a procedural background should update the live visual settings.");
        choices[nameof(PianoVisualSettings.BackgroundMotion)].SelectedValue = initialBackgroundMotion;
        var initialHaloPulseStyle = visualSettings.HaloPulseStyle;
        choices[nameof(PianoVisualSettings.HaloPulseStyle)].SelectedValue = "Twin Comets";
        Assert(visualSettings.HaloPulseStyle == "Twin Comets", "Choosing a hit-line animation should update the live visual settings.");
        choices[nameof(PianoVisualSettings.HaloPulseStyle)].SelectedValue = initialHaloPulseStyle;
        var originalStyle = visualSettings.NoteStyle; var originalMode = visualSettings.ColorMode; var originalDepth = visualSettings.Notes3D;
        var settingsSaveTimer = (DispatcherTimer)Field(window, "_settingsSaveTimer");
        settingsSaveTimer.Stop();
        window.PreviewNoteStyle("Gem", "2d");
        Assert(visualSettings.NoteStyle == "Gem" && !visualSettings.Notes3D && !settingsSaveTimer.IsEnabled,
            "A command-line material preview should update the live stage in memory without arming settings auto-save.");
        window.PreviewNoteStyle("Crystal Outline", "3d");
        Assert(visualSettings.NoteStyle == "CrystalOutline" && visualSettings.Notes3D && !settingsSaveTimer.IsEnabled,
            "The preview should normalize the display name but keep the stable CrystalOutline ID, without saving it.");
        window.PreviewNoteStyle(originalStyle, originalDepth ? "3d" : "2d");
        var beforePresetPreview = visualSettings.ToJson();
        window.PreviewPreset("ConcertGold");
        Assert(visualSettings.PresetName == "Concert Gold" && !settingsSaveTimer.IsEnabled,
            "A command-line preset preview should also stay in memory and never arm the settings auto-save timer.");
        visualSettings.CopyFrom(PianoVisualSettings.FromJson(beforePresetPreview), keepBackgroundImage: true);
        Invoke(window, "RefreshSettingControls"); stage.SetVisualSettings(visualSettings);
        Invoke(window, "ApplyChromeTheme"); Invoke(window, "RefreshMenuStageLook");
        Run(nameof(VerifyNoteMaterialRendering), () => VerifyNoteMaterialRendering(stage, visualSettings));
        choices[nameof(PianoVisualSettings.NoteStyle)].SelectedValue = "Fire";
        Assert(visualSettings.NoteStyle == "Fire" && visualSettings.PresetModified, "Choosing a note style should update the renderer settings and flag the preset as modified.");
        choices[nameof(PianoVisualSettings.ColorMode)].SelectedValue = "PerHand";
        var left = (Color)ColorConverter.ConvertFromString(visualSettings.LeftHandColor)!; var right = (Color)ColorConverter.ConvertFromString(visualSettings.RightHandColor)!;
        Assert(stage.NoteColor(48, 0) == left && stage.NoteColor(72, 0) == right, "Per-hand coloring should split the keyboard at the configured pitch.");
        choices[nameof(PianoVisualSettings.ColorMode)].SelectedValue = "PerTrack";
        Assert(stage.NoteColor(60, 1) == (Color)ColorConverter.ConvertFromString(visualSettings.TrackColors[1])! && stage.NoteColor(60, 9) == (Color)ColorConverter.ConvertFromString(visualSettings.TrackColors[1])!, "Per-track coloring should use the track palette and wrap after eight tracks.");
        var colorRows = (System.Collections.IList)Field(window, "_settingRows");
        FrameworkElement RowElement(string property) => colorRows.Cast<object>().Where(r => (string?)r.GetType().GetField("Property")!.GetValue(r) == property).Select(r => (FrameworkElement)r.GetType().GetField("Element")!.GetValue(r)!).First();
        Assert(RowElement(nameof(PianoVisualSettings.LeftHandColor)).Visibility == Visibility.Collapsed && RowElement(nameof(PianoVisualSettings.TrackColors)).Visibility == Visibility.Visible && RowElement(nameof(PianoVisualSettings.NoteColorStart)).Visibility == Visibility.Collapsed,
            "Dependent rows should follow the selected color mode.");
        choices[nameof(PianoVisualSettings.ColorMode)].SelectedValue = originalMode; choices[nameof(PianoVisualSettings.NoteStyle)].SelectedValue = originalStyle;
        // Direction: the dock combo drives the live renderer, and the live bar model flips with it
        // (falling notes keep the 28 px spawn offset at the top; rising notes are born at the key line).
        var direction = choices[nameof(PianoVisualSettings.NoteDirection)];
        var originalDirection = visualSettings.NoteDirection;
        var flipTo = originalDirection == "Up" ? "Down" : "Up";
        direction.SelectedValue = flipTo;
        Assert(visualSettings.NoteDirection == flipTo && visualSettings.PresetModified, "Choosing the note direction should update the live stage settings and flag the preset as modified.");
        stage.SetVisualSettings(visualSettings);
        var fallSpeed = visualSettings.NoteFallSpeed;
        stage.AddLiveNote(60);
        for (var i = 0; i < 6; i++) stage.Advance(.05);
        var heldLength = stage.LiveTrailHeightFor(60);
        var expectedLength = flipTo == "Up" ? .3 * fallSpeed : 28 + .3 * fallSpeed;
        Assert(Math.Abs(heldLength - expectedLength) < 1, $"A held note in {flipTo} mode should match its bar model (length {heldLength:0} px vs {expectedLength:0} px expected).");
        stage.ReleaseLiveNote(60);
        direction.SelectedValue = originalDirection;
        stage.ClearTransient();
        // ---- Theme page: shell themes and the concert stage layers -------------------------------
        var chips = (Panel?)Field(window, "themeChipHost");
        Assert(chips is not null && chips.Children.Count == ShellThemes.Everything.Count() && chips.Children.OfType<Button>().All(b => b.Tag is Brush),
            "The Theme page should offer one palette chip per built-in interface theme.");
        // Ids are slugs derived from the theme name. Older releases stored sakura / noir / velvet and
        // the retired "Sakura Nocturne" name; those must still resolve and be rewritten on load.
        Assert(ShellThemes.Find("sakura").Id == ShellThemes.ConcertGrandId && ShellThemes.Find("noir").Id == ShellThemes.ConcertNoirId
                && ShellThemes.Find("velvet").Id == ShellThemes.VelvetGoldId && ShellThemes.Find("Sakura Nocturne").Id == ShellThemes.ConcertGrandId
                && ShellThemes.Find("Concert Noir").Id == ShellThemes.ConcertNoirId && ShellThemes.Find("nonsense").Id == ShellThemes.DefaultId,
            "Legacy theme ids and display names should resolve to the canonical concert themes.");
        Assert(PianoVisualSettings.FromJson("{\"ShellTheme\":\"sakura\"}").ShellTheme == ShellThemes.ConcertGrandId && ShellThemes.DefaultId == ShellThemes.All[0].Id,
            "Loading persisted settings should rewrite a legacy theme id to the canonical slug.");
        var beforeTheme = visualSettings.ShellTheme;
        visualSettings.ShellTheme = "velvet";
        Invoke(window, "ApplyChromeTheme");
        Assert(ShellThemeManager.Current.Id == ShellThemes.VelvetGoldId && (Color)Application.Current.Resources["AccentColor"] == ShellThemes.VelvetGold.Accent,
            "Choosing an interface theme should resolve its id and publish the accent colour into the application resources.");
        visualSettings.ShellTheme = beforeTheme;
        Invoke(window, "ApplyChromeTheme");
        Assert(ShellThemeManager.Current.Id == ShellThemes.Find(beforeTheme).Id, "Restoring the theme should republish the previous accents.");
        var petals = toggles[nameof(PianoVisualSettings.ShowPetals)];
        Assert(petals is not null && choices.ContainsKey(nameof(PianoVisualSettings.ChromeMotion)),
            "The Theme page should expose the ambient mote layer and the motion budget.");
        var wasPetals = visualSettings.ShowPetals; var wasAmount = visualSettings.PetalAmount;
        visualSettings.ShowPetals = true; visualSettings.PetalAmount = 60;
        stage.SetVisualSettings(visualSettings);
        Assert(stage.HasActiveEffects, "The ambient mote layer should keep the stage animating even without notes.");
        var concertVisual = new DrawingVisual();
        using (var dc = concertVisual.RenderOpen())
        {
            Invoke(stage, "DrawPetals", dc, 1280d, 480d);
        }
        Assert(stage.PetalCount > 0 && stage.PetalCount <= 150, $"The mote layer should draw a bounded number of petals (drew {stage.PetalCount}).");
        Assert(concertVisual.Drawing.Bounds.Height > 200 && concertVisual.Drawing.Bounds.Width > 100, "The ambient layer should actually paint geometry into the stage.");
        visualSettings.ShowPetals = wasPetals; visualSettings.PetalAmount = wasAmount;
        stage.SetVisualSettings(visualSettings);
        Run(nameof(VerifyImpactFx), () => VerifyImpactFx(window, stage, visualSettings, choices));
        Run(nameof(VerifyFallingFx), () => VerifyFallingFx(window, stage, visualSettings, choices));
        Run(nameof(VerifyHoldFx), () => VerifyHoldFx(window, stage, visualSettings));
        Run(nameof(VerifyReleaseFx), () => VerifyReleaseFx(window, stage, visualSettings, choices));
        Run(nameof(VerifyAmbientFx), () => VerifyAmbientFx(window, stage, visualSettings, choices));
        Run(nameof(VerifySmartFx), () => VerifySmartFx(window, stage, visualSettings));
        Run(nameof(VerifyThemes), () => VerifyThemes());
        var search = (TextBox)window.FindName("SettingsSearchBox");
        search.Text = "wisp";
        var rows = colorRows.Cast<object>().Select(r => (FrameworkElement)r.GetType().GetField("Element")!.GetValue(r)!).ToList();
        Assert(rows.Count > 40 && rows.Count(r => r.Visibility == Visibility.Visible) < rows.Count / 2, "Searching should hide the rows that do not match.");
        // The search answers to the words people actually type: the label, the synonyms of the setting
        // and the setting's own name, in any order and in either language (see SearchSynonyms).
        var speedRow = RowElement(nameof(PianoVisualSettings.NoteFallSpeed));
        search.Text = "tempo";
        Assert(speedRow.Visibility == Visibility.Visible, "The dock search should find the fall-speed slider by the synonym “tempo”, not only by its label.");
        // Several words are an AND, and each of them may sit in the caption or in the synonyms of the
        // same row: "fall" comes from the label, "tempo" from the synonym table, and a row that only
        // matches one of the two steps aside.
        search.Text = "fall tempo";
        Assert(speedRow.Visibility == Visibility.Visible && RowElement(nameof(PianoVisualSettings.ShowWisps)).Visibility == Visibility.Collapsed,
            "Several words should narrow the dock to the row that answers to all of them, in any order.");
        search.Text = "zzz-nothing-matches";
        Assert(rows.All(r => r.Visibility == Visibility.Collapsed), "A query that matches nothing should hide every row.");
        search.Text = "";
        Assert(rows.Count(r => r.Visibility == Visibility.Visible) > rows.Count / 2, "Clearing the search should restore the rows.");
        var presetList = (ListBox)window.FindName("PresetList");
        Assert(presetList.Items.Count >= VisualPresets.BuiltIn.Count, "The Style page should list every built-in preset.");
        var beforeName = visualSettings.PresetName; var beforeJson = visualSettings.ToJson();
        presetList.SelectedIndex = VisualPresets.BuiltIn.ToList().FindIndex(p => p.Name == "Classic Roll");
        Invoke(window, "ApplyPreset_Click", window, new RoutedEventArgs());
        Assert(visualSettings.PresetName == "Classic Roll" && !visualSettings.PresetModified && visualSettings.NoteStyle == "Solid" && !visualSettings.ShowEmbers
                && ((PianoVisualSettings)Field(stage, "_visual")).ToJson() == visualSettings.WithAccessibilityTogglesApplied().ToJson(),
            "Applying a preset should rewrite the live settings in place and the stage should draw exactly that look, adjusted only by the accessibility policy.");
        visualSettings.CopyFrom(PianoVisualSettings.FromJson(beforeJson), keepBackgroundImage: false); visualSettings.PresetName = beforeName;
        Invoke(window, "RefreshSettingControls"); stage.SetVisualSettings(visualSettings);
        Run(nameof(VerifyShadedStage), () => VerifyShadedStage(stage, choices, visualSettings));
        ((DispatcherTimer)Field(window, "_settingsSaveTimer")).Stop();
        Results.Add("PASS settings dock: thirteen pages grouped into four navigation sections, theme chips and the ambient mote layer, style/color-mode controls, per-hand and per-track colors, impact wave/flash FX, falling/hold/release FX, ambient layers, smart modulators, themes, search filter, preset application and the ray-traced keyboard switch.");
    }

    /// <summary>
    /// Interface language. The two shipped tables must cover exactly the same keys — a missing
    /// translation is invisible in the language it does not affect — and a switch must repaint the
    /// interface that is already on screen without rebuilding the window. Because switching is a
    /// plain re-render, the check also proves that nothing is remembered in the wrong language:
    /// the selected values, the stored settings and the dock search keep working across it.
    /// </summary>
    private static void VerifyLanguageSwitching(MainWindow window)
    {
        foreach (var language in Languages.All)
        {
            var missing = Loc.MissingFor(language);
            Assert(missing.Count == 0,
                $"{language.EnglishName} should translate every key of the English inventory ({missing.Count} missing, first: {missing.FirstOrDefault() ?? "-"}).");
        }
        Assert(Languages.Find("vi-VN").Id == "vi" && Languages.Find("vi").Id == "vi" && Languages.Find("Tiếng Việt").Id == "vi",
            "A stored language should resolve from an id, a culture tag or the native name.");
        Assert(Languages.Find("").Id == "en" && Languages.Find("xx").Id == "en" && Languages.Normalize("  VI ").Equals("vi", StringComparison.OrdinalIgnoreCase),
            "An unknown or empty language must fall back to English, never to a blank label.");

        var tabs = (TabControl)window.FindName("SettingsTabs");
        Func<string> firstHeader = () => (string)((TabItem)tabs.Items[0]).Header!;
        Loc.Apply("vi");
        var vietnameseCaption = Loc.T("Falling notes");
        Loc.ResetDiagnostics();
        Assert(Loc.Current.Id == "vi" && vietnameseCaption != "Falling notes", "Applying a language must change the captions the interface prints.");
        Assert(firstHeader() == Loc.T("Style"), "A window that is already open must repaint its labels when the language changes.");
        // The row of the "Falling notes" switch is found by its translated caption, not only by English.
        var search = (TextBox)window.FindName("SettingsSearchBox");
        var rows = (System.Collections.IList)Field(window, "_settingRows");
        FrameworkElement RowElement(string property) => rows.Cast<object>()
            .Where(candidate => (string?)candidate.GetType().GetField("Property")!.GetValue(candidate) == property)
            .Select(candidate => (FrameworkElement)candidate.GetType().GetField("Element")!.GetValue(candidate)!).First();
        search.Text = vietnameseCaption;
        Assert(RowElement(nameof(PianoVisualSettings.ShowNotes)).Visibility == Visibility.Visible,
            "The settings search should match a row by its translated caption as well as by the English one.");
        // Vietnamese words that are not part of any English caption still reach the row, because every
        // phrase of a row is matched in both languages: "tốc độ" finds the "Fall speed" slider.
        search.Text = "tốc độ";
        Assert(RowElement(nameof(PianoVisualSettings.NoteFallSpeed)).Visibility == Visibility.Visible,
            "The dock search should answer to words of the active language as well as to English ones.");
        search.Text = "";
        Assert(Loc.UntranslatedKeys.Count == 0,
            $"Every string the interface printed while in Vietnamese should have a translation ({Loc.UntranslatedKeys.FirstOrDefault() ?? "-"}).");
        // A printed string that is not a key of the English inventory can never be translated by any
        // table: it is either a caption that was reworded in the sources without adding the new key,
        // or a template built by concatenating pieces. Both are defects, so this is an assertion now
        // instead of the NOTE it used to be (see docs/LOCALIZATION.md §9). User-supplied text (a
        // device name, a file name, a preset the user typed) never reaches Loc in this window.
        var unknownKeys = Loc.UnknownKeys;
        Assert(unknownKeys.Count == 0,
            $"{unknownKeys.Count} printed string(s) are not keys of the English inventory — a reworded caption cannot be translated: {string.Join(" · ", unknownKeys.Take(5))}.");
        // The camera corners are stored as English ids, so the picker must print the translated caption while
        // still carrying the id: display text is never the stored value.
        string? Painted(object item, string name) => (string?)item.GetType().GetProperty(name)?.GetValue(item);
        var cornerChoices = (Dictionary<string, ComboBox>)Field(window, "_visualChoices");
        var cornerCombo = (ComboBox)cornerChoices[nameof(PianoVisualSettings.CameraCorner)];
        object[] PaintedCorners() => ((System.Collections.IEnumerable)cornerCombo.ItemsSource).Cast<object>().ToArray();
        bool TranslatedCorner(object item)
        {
            var value = Painted(item, "Value")!;
            var caption = Painted(item, "Caption");
            return caption is not null && caption != value && caption == Loc.T(value);
        }
        Assert(PaintedCorners().Length == CameraOverlay.Corners.Length
                && PaintedCorners().Select(item => Painted(item, "Value")).SequenceEqual(CameraOverlay.Corners)
                && PaintedCorners().All(TranslatedCorner),
            "The camera corner picker should offer each corner under the English id the settings file stores while printing the translated caption.");

        Loc.Apply("en");
        Assert(firstHeader() == "Style" && Loc.T("Falling notes") == "Falling notes", "Switching back to English must restore every caption.");
        Assert(PaintedCorners().Select(item => Painted(item, "Caption")).SequenceEqual(CameraOverlay.Corners),
            "Switching back to English must restore the camera corner captions to the ids they are stored as.");
        Loc.Apply("");
        Results.Add($"PASS localization: {Languages.All.Length} languages cover all {StringsEnglish.Table.Count} keys, a live switch repaints the open dock in both directions and the settings search answers to either language.");
    }

    /// <summary>
    /// Accessibility: a control that only shows a glyph has no accessible name of its own, so it takes
    /// one from the sentence it already carries (the dock builders name their rows, and
    /// <see cref="Loc.Track"/> mirrors a translated tooltip into <c>AutomationProperties.Name</c> for the
    /// marked controls). The check walks the window the way a screen reader does, then exercises the
    /// high-contrast branch of the theme manager, which a CI runner never actually turns on.
    /// </summary>
    /// <summary>
    /// Walks the elements the application builds itself: template parts (the thumb of a slider, the
    /// button of a combo box) carry their own automation peer and would only add noise to a check that
    /// asks whether every control a user can operate has a name.
    /// </summary>
    private static void WalkApplicationTree(DependencyObject root, Action<FrameworkElement> visit)
    {
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject node)
        {
            if (!seen.Add(node)) return;
            if (node is FrameworkElement { TemplatedParent: null } element) visit(element);
            foreach (var child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject logical) Walk(logical);
            // A logical child can be content rather than a visual (a Run inside a TextBlock caption, for
            // instance, which the search highlight adds), and VisualTreeHelper throws on those.
            if (node is not Visual and not System.Windows.Media.Media3D.Visual3D) return;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) Walk(VisualTreeHelper.GetChild(node, index));
        }
        Walk(root);
    }

    private static void VerifyAccessibility(MainWindow window)
    {
        var controls = new List<FrameworkElement>();
        WalkApplicationTree(window, controls.Add);
        static string VisibleLabel(FrameworkElement element) => element switch
        {
            ContentControl { Content: string text } => text,
            TextBlock { Text: string text } => text,
            _ => ""
        };
        static bool Readable(FrameworkElement element) =>
            VisibleLabel(element).Count(char.IsLetter) >= 2 || !string.IsNullOrWhiteSpace(AutomationProperties.GetName(element));
        var interactive = controls.Where(element => element is ButtonBase or TextBox or Slider or ComboBox or ListBox).ToList();
        var unnamed = interactive.Where(element => !Readable(element))
            .Select(element => element.GetType().Name + (string.IsNullOrEmpty(element.Name) ? "" : $" ({element.Name})"))
            .ToList();
        Assert(unnamed.Count == 0,
            $"{unnamed.Count} interactive control(s) have no accessible name: {string.Join(", ", unnamed.Take(6))}.");

        // The glyphs that used to be anonymous: the loop buttons show "A" / "B" and a clear-loop glyph
        // (the letter × left the UI with the other Unicode button captions); the tooltip a screen
        // reader hears is the localized sentence, not the letter.
        FrameworkElement? ByContent(string content) => controls.FirstOrDefault(element => element is ContentControl { Content: string text } && text == content);
        var loopA = ByContent("A"); var loopB = ByContent("B");
        var clearLoop = controls.FirstOrDefault(element => element is Button && AutomationProperties.GetName(element) == Loc.T("Clear the loop"));
        Assert(loopA is not null && loopB is not null && clearLoop is not null,
            "The transport should expose the A / B loop buttons and the clear-loop glyph that this check names.");
        Assert(AutomationProperties.GetName(loopA!) == Loc.T("Set the loop start at the playhead")
                && AutomationProperties.GetName(loopB!) == Loc.T("Set the loop end at the playhead")
                && AutomationProperties.GetName(clearLoop!) == Loc.T("Clear the loop"),
            "The loop glyph buttons should take their screen-reader name from their localized tooltip.");
        // The generated rows are named after their own caption, so the whole design dock is usable with
        // a screen reader, and the names follow the interface language like every other label.
        var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        Assert(AutomationProperties.GetName(sliders[nameof(PianoVisualSettings.NoteFallSpeed)]) == Loc.T("Fall speed"),
            "A generated slider should be named after its row label, in the active language.");
        var combos = (Dictionary<string, ComboBox>)Field(window, "_visualChoices");
        Assert(AutomationProperties.GetName(combos[nameof(PianoVisualSettings.NoteStyle)]) == Loc.T("Note style"),
            "A generated picker should be named after its row label, in the active language.");

        var dock = (Border)window.FindName("SettingsPanel");
        Assert(KeyboardNavigation.GetTabNavigation(dock) == KeyboardNavigationMode.Cycle,
            "The settings dock should keep Tab inside its own page instead of losing focus to the stage.");

        // High contrast: the published colours follow SystemColors while the user's chosen theme stays
        // the stored one, and the repaint is driven by the SystemParameters notification alone - a runner
        // never sees the real one, so the hook's body is called directly here.
        var chosen = ShellThemeManager.Current.Id;
        try
        {
            ShellThemeManager.ForceHighContrast = true;
            ShellThemeManager.OnSystemParametersChanged(nameof(SystemParameters.HighContrast));
            var windowBrush = Application.Current.Resources["WindowBrush"] as SolidColorBrush;
            Assert(ShellThemeManager.IsHighContrast
                    && (Color)Application.Current.Resources["AccentColor"] == SystemColors.HighlightColor
                    && windowBrush is not null && windowBrush.Color == SystemColors.WindowColor,
                "Switching high contrast on must repaint the chrome from the Windows system colours by itself, without a second Apply call.");
            Assert(ShellThemeManager.Current.Id == ShellThemes.Find(chosen).Id,
                "High contrast must not overwrite the theme the user chose.");
            // An unrelated system parameter must leave the palette alone: only the contrast switch repaints.
            ShellThemeManager.OnSystemParametersChanged("ClientAreaAnimation");
            Assert((Color)Application.Current.Resources["AccentColor"] == SystemColors.HighlightColor,
                "Only the contrast switch should repaint the chrome.");
        }
        finally
        {
            ShellThemeManager.ForceHighContrast = false;
            ShellThemeManager.OnSystemParametersChanged(nameof(SystemParameters.HighContrast));
        }
        Assert(!ShellThemeManager.IsHighContrast && ShellThemeManager.Current.Id == ShellThemes.Find(chosen).Id
                && (Color)Application.Current.Resources["AccentColor"] == ShellThemes.Find(chosen).Accent,
            "Turning high contrast off should republish the chosen concert theme through the same hook.");
        Results.Add("PASS accessibility: every interactive control carries a readable, localized name (glyph buttons through their tooltip, generated rows through their caption), the dock keeps Tab inside its page, and high contrast repaints the chrome from the Windows system colours the moment Windows reports the switch, without changing the chosen theme.");
    }

    /// <summary>
    /// Accessibility step 2: the dock at the size CI renders its previews at (1080x700) and the keyboard
    /// path through every page. Each page has to keep its rows inside the scrollable content and every
    /// control inside its own card - a longer word in another language must not push a slider out of
    /// reach - and Tab has to visit a page the way it is printed: the generated rows in the order they
    /// were registered, which is also the order their cards were added to the page.
    /// </summary>
    private static void VerifyDockAccessibility(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("SettingsTabs");
        var dock = (Border)window.FindName("SettingsPanel");
        if (dock.Visibility != Visibility.Visible) Invoke(window, "OpenSettingsPanel");
        // Leftover search text would collapse rows and make this check measure a page nobody sees.
        if (window.FindName("SettingsSearchBox") is TextBox search && search.Text.Length > 0) search.Text = "";
        window.UpdateLayout();
        Assert(dock.Visibility == Visibility.Visible, "The design dock has to be open while its compact layout and its tab order are measured.");
        Assert(tabs.Items.Count == SettingsPages.Order.Length, "Every page of the dock catalogue should have exactly one tab in the strip.");
        var rows = (System.Collections.IList)Field(window, "_settingRows");
        var catalogue = rows.Cast<object>().Select(row => (
            Page: (Panel)row.GetType().GetField("Page")!.GetValue(row)!,
            Card: (Border)row.GetType().GetField("Card")!.GetValue(row)!,
            Element: (FrameworkElement)row.GetType().GetField("Element")!.GetValue(row)!)).ToList();
        var (wasWidth, wasHeight, wasState, wasIndex) = (window.Width, window.Height, window.WindowState, tabs.SelectedIndex);
        var pages = 0; var reachable = 0; var measured = 0; var handBuilt = 0;
        try
        {
            window.WindowState = WindowState.Normal; window.Width = 1080; window.Height = 700; window.UpdateLayout();
            for (var index = 0; index < tabs.Items.Count; index++)
            {
                var tab = (TabItem)tabs.Items[index];
                tabs.SelectedIndex = index;
                // Keep the pointer "recent", otherwise the idle timer hides the dock while it is measured.
                SetField(window, "_lastPointerActivity", DateTime.UtcNow);
                window.UpdateLayout();
                var header = (string)tab.Header;
                var content = (ScrollViewer?)tab.Content;
                Assert(content is not null, $"The '{header}' dock page should scroll instead of clipping content that does not fit.");
                var page = content!;
                page.ScrollToTop(); window.UpdateLayout();
                // The controls a keyboard can reach on this page, in the order WPF would visit them.
                var focusable = new List<FrameworkElement>();
                WalkApplicationTree(page, element =>
                {
                    if (element.Focusable && element.IsVisible && element.IsEnabled && element is ButtonBase or TextBox or Slider or ComboBox or ListBox)
                        focusable.Add(element);
                });
                Assert(focusable.Count > 0, $"The '{header}' dock page should expose at least one control the keyboard can reach.");
                // The generated rows of this page, in the order they were registered, which is the order
                // the page prints them and the order they joined the visual tree.
                var host = InvokeReturn(window, "SettingsPageHost", index) as Panel;
                var pageRows = catalogue.Where(row => ReferenceEquals(row.Page, host)).ToList();
                // One card can hold several rows, so a control is matched to its row through the row
                // element itself; the walk above and this order are what Tab follows.
                int? RowOf(DependencyObject element)
                {
                    for (var position = 0; position < pageRows.Count; position++)
                        for (DependencyObject? node = element; node is Visual or System.Windows.Media.Media3D.Visual3D; node = VisualTreeHelper.GetParent(node))
                            if (ReferenceEquals(node, pageRows[position].Element)) return position;
                    return null;
                }
                var previous = -1; var own = 0;
                foreach (var control in focusable)
                {
                    if (RowOf(control) is not { } position) { handBuilt++; continue; }
                    Assert(position >= previous, $"On the '{header}' page Tab would reach row {position} before row {previous}, which is printed above it.");
                    previous = position; own++;
                }
                Assert(pageRows.Count == 0 || own > 0, $"The generated rows of the '{header}' page should be reachable with the keyboard, not only with the mouse.");
                var cards = new List<Border>();
                foreach (var row in pageRows) if (!cards.Contains(row.Card)) cards.Add(row.Card);
                foreach (var card in cards)
                {
                    var offset = card.TransformToAncestor(page).Transform(new Point(0, 0));
                    Assert(offset.Y >= -1 && offset.Y + card.ActualHeight <= page.ExtentHeight + 1,
                        $"Every '{header}' card should stay inside its scrollable content, not below it.");
                    // The viewport may still be sized for the layout from before the vertical bar appeared,
                    // so a card is allowed the width of that bar on top of the visible column.
                    Assert(card.ActualWidth <= page.ViewportWidth + SystemParameters.VerticalScrollBarWidth + 1,
                        $"Every '{header}' card should fit the scroll column at the compact window size.");
                    measured++;
                }
                foreach (var row in pageRows)
                {
                    var frame = row.Element.TransformToAncestor(row.Card).TransformBounds(new Rect(row.Element.RenderSize));
                    Assert(frame.Left >= -1 && frame.Top >= -1 && frame.Right <= row.Card.ActualWidth + 1 && frame.Bottom <= row.Card.ActualHeight + 1,
                        $"The control of a row on the '{header}' page should stay inside its card at the compact window size.");
                }
                pages++; reachable += focusable.Count;
            }
        }
        finally
        {
            tabs.SelectedIndex = wasIndex;
            window.Width = wasWidth; window.Height = wasHeight; window.WindowState = wasState; window.UpdateLayout();
        }
        Results.Add($"PASS dock accessibility: all {pages} pages keep their {reachable} keyboard-reachable controls inside the scroll column at 1080x700, each of the {measured} generated cards stays inside the scroll column ({handBuilt} hand-built controls sit outside the catalogue), and Tab walks a page in the order its rows are printed.");
    }

    /// <summary>
    /// Undo / redo: the dock keeps the last 32 states of <see cref="PianoVisualSettings"/> as JSON. A
    /// change is committed when the controls settle (the settings-save timer), which is what makes a
    /// slow slider drag one step; an undo moves the controls as well as the stored values, so stepping
    /// back is indistinguishable from the user having set the values again.
    /// </summary>
    private static void VerifySettingsHistory(MainWindow window)
    {
        var settings = (PianoVisualSettings)Field(window, "_visualSettings");
        var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        var glow = sliders[nameof(PianoVisualSettings.NoteGlow)];
        var width = sliders[nameof(PianoVisualSettings.NoteWidth)];
        var fall = sliders[nameof(PianoVisualSettings.NoteFallSpeed)];
        var (wasGlow, wasWidth, wasFall) = (glow.Value, width.Value, fall.Value);
        // Start from a clean slate: the earlier checks changed settings on purpose, and this one is
        // about the history itself, so it takes the current state as the only starting point.
        Invoke(window, "StartHistory");
        var before = settings.ToJson();
        glow.Value = Math.Clamp(wasGlow + 21, glow.Minimum, glow.Maximum); Commit(window);
        width.Value = Math.Clamp(wasWidth - 11, width.Minimum, width.Maximum); Commit(window);
        fall.Value = Math.Clamp(wasFall + 37, fall.Minimum, fall.Maximum); Commit(window);
        var changed = settings.ToJson();
        Assert(changed != before, "Moving three sliders should change the settings JSON.");
        for (var step = 0; step < 3; step++) Invoke(window, "UndoVisualSettings");
        Assert(settings.ToJson() == before, "Three undo steps should bring the settings JSON back to the starting state.");
        Assert(Math.Abs(glow.Value - wasGlow) < .001 && Math.Abs(width.Value - wasWidth) < .001 && Math.Abs(fall.Value - wasFall) < .001,
            "An undo should move the dock controls back, not only the stored values.");
        for (var step = 0; step < 3; step++) Invoke(window, "RedoVisualSettings");
        Assert(settings.ToJson() == changed, "Three redo steps should replay the three changes exactly.");
        for (var step = 0; step < 3; step++) Invoke(window, "UndoVisualSettings");
        Assert(settings.ToJson() == before, "A replayed change should be undoable like any other.");
        Invoke(window, "UndoVisualSettings");
        Assert(settings.ToJson() == before, "Undoing past the oldest state should leave the settings alone instead of corrupting them.");
        // Three rapid moves of one slider never settle: the history has to treat them as a single step.
        var glideFrom = settings.ToJson(); var glide = glow.Value;
        glow.Value = Math.Clamp(glide + 5, glow.Minimum, glow.Maximum);
        glow.Value = Math.Clamp(glide + 10, glow.Minimum, glow.Maximum);
        glow.Value = Math.Clamp(glide + 15, glow.Minimum, glow.Maximum);
        Invoke(window, "UndoVisualSettings");
        Assert(settings.ToJson() == glideFrom, "A slider drag that never settled should undo as one step.");
        Results.Add("PASS design history: settled changes undo and redo as exact JSON states, the controls follow, a drag is one step, and stepping past the oldest state is harmless.");
    }

    /// <summary>
    /// The settings profile: one JSON file carrying the stage settings, the interface language and the
    /// shell theme through the same methods the Import / Export buttons call. A file that is not a
    /// profile is rejected, and an id this build does not ship falls back to English / the default look
    /// instead of half-applying.
    /// </summary>
    /// <summary>
    /// The recent-songs library: the index lives in the folder of this run (never the user's own), keeps
    /// the newest twelve songs first, replaces an entry that is opened again instead of adding a second
    /// row, and treats a damaged file as "no songs". Reopening a row has to put the stored hand split,
    /// fall speed and tempo back on the very sliders the user would move, so the renderer, the settings
    /// file and the undo history all follow their ordinary paths.
    /// </summary>
    private static void VerifySongLibrary(MainWindow window)
    {
        var directory = PianoVisualSettingsStore.SettingsDirectory;
        Assert(SongLibrary.FilePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase),
            $"The song library must live in the settings folder of this run, not in the user's own (using {SongLibrary.FilePath}).");
        var host = (StackPanel)window.FindName("RecentSongHost");
        var empty = (TextBlock)window.FindName("RecentSongsEmpty");
        SongLibrary.Clear();
        window.RefreshRecentSongs();
        Assert(SongLibrary.Entries.Count == 0 && host.Children.Count == 0 && empty.Visibility == Visibility.Visible,
            "A cleared song library should show its empty-state sentence and no rows.");

        var path = Path.Combine(Path.GetTempPath(), "keyflow-library-song.mid");
        File.WriteAllBytes(path, CreateFormatOneMidi());
        if (SongLibrary.Find(path) is { } stale) SongLibrary.Forget(stale.Path);
        SongLibrary.Remember(path, "Library Song", 3, 2, 1.25, 120, 55, 700, 90, "Two Hands");
        var found = SongLibrary.Find(path);
        Assert(File.Exists(SongLibrary.FilePath) && found is { Notes: 3, Tracks: 2, BeatsPerMinute: 120, HandSplitPitch: 55, FallSpeed: 700, TempoPercent: 90, Preset: "Two Hands" },
            "A remembered song should keep the note, track and tempo facts read from the file plus the values it was played at.");

        for (var index = 0; index < SongLibrary.Capacity + 3; index++)
            SongLibrary.Remember(Path.Combine(Path.GetTempPath(), $"keyflow-library-{index}.mid"), $"Song {index}", 1, 1, 1, 100, 60, 500, 100, "");
        Assert(SongLibrary.Entries.Count == SongLibrary.Capacity && SongLibrary.Entries[0].Title == $"Song {SongLibrary.Capacity + 2}" && SongLibrary.Entries[^1].Title == "Song 3",
            "The library should keep the newest twelve songs, newest first.");
        SongLibrary.Remember(path, "Library Song", 3, 2, 1.25, 120, 55, 700, 90, "Two Hands");
        Assert(SongLibrary.Entries.Count == SongLibrary.Capacity && Path.GetFullPath(SongLibrary.Entries[0].Path) == Path.GetFullPath(path),
            "Opening a song that is already remembered should move its row to the front instead of adding a second one.");

        File.WriteAllText(SongLibrary.FilePath, "{ this is not the library");
        SongLibrary.Reload();
        Assert(SongLibrary.Entries.Count == 0, "A damaged library file should read as empty instead of failing a song or the app.");

        SongLibrary.Clear();
        SongLibrary.Remember(path, "Library Song", 3, 2, 1.25, 120, 55, 700, 90, "Two Hands");
        var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        var hand = sliders[nameof(PianoVisualSettings.HandSplitPitch)]; var speed = sliders[nameof(PianoVisualSettings.NoteFallSpeed)];
        var tempo = (Slider)window.FindName("TempoSlider");
        var hadHand = hand.Value; var hadSpeed = speed.Value; var hadTempo = tempo.Value;
        hand.Value = 72; speed.Value = 300; tempo.Value = 130;
        window.OpenSongFromLibrary(SongLibrary.Find(path)!);
        Assert(Math.Abs(hand.Value - 55) < .01 && Math.Abs(speed.Value - 700) < .01 && Math.Abs(tempo.Value - 90) < .01,
            "Reopening a remembered song should put its stored hand split, fall speed and tempo back on the controls.");
        var settings = (PianoVisualSettings)Field(window, "_visualSettings");
        Assert(Math.Abs(settings.HandSplitPitch - 55) < .01 && Math.Abs(settings.NoteFallSpeed - 700) < .01,
            "The values restored from the library should reach the visual settings the renderer reads.");
        Assert(SongLibrary.Entries.Count == 1 && SongLibrary.Entries[0] is { HandSplitPitch: 55, FallSpeed: 700, TempoPercent: 90 },
            "Reopening a song should keep exactly one row for it and remember the values it was played at again.");
        window.RefreshRecentSongs();
        Assert(host.Children.Count == 1 && empty.Visibility == Visibility.Collapsed,
            "The Play dialog should list the songs that were opened and hide its empty-state sentence.");

        for (var index = 0; index < MainWindow.RecentSongRows + 1; index++)
            SongLibrary.Remember(Path.Combine(Path.GetTempPath(), $"keyflow-library-row-{index}.mid"), $"Row {index}", 1, 1, 1, 100, 60, 500, 100, "");
        window.RefreshRecentSongs();
        Assert(host.Children.Count == MainWindow.RecentSongRows && SongLibrary.Entries.Count == MainWindow.RecentSongRows + 2,
            "The Play dialog should show the newest few songs while the library file keeps the full recent list.");

        SongLibrary.Forget(path);
        Assert(SongLibrary.Find(path) is null && SongLibrary.Entries.Count == MainWindow.RecentSongRows + 1,
            "Forgetting a song should drop its row from the list and from the file.");
        var missing = SongLibrary.Remember(Path.Combine(Path.GetTempPath(), "keyflow-library-missing.mid"), "Missing", 1, 1, 1, 100, 60, 500, 100, "");
        window.OpenSongFromLibrary(missing);
        Assert(SongLibrary.Find(missing.Path) is null,
            "A remembered file that is no longer on disk should be forgotten instead of being offered again.");

        SongLibrary.Clear(); window.RefreshRecentSongs();
        hand.Value = hadHand; speed.Value = hadSpeed; tempo.Value = hadTempo;
        Results.Add("PASS song library: isolated recent-songs index, newest-first twelve-song cap with per-path dedupe, damaged file tolerated, forgotten and vanished files dropped, and reopening a row restores hand split, fall speed and tempo through the controls.");
    }

    /// <summary>
    /// The slow-down curve of the practice session: off by default, a run of misses past the threshold
    /// steps the tempo down five percent, four correct notes in a row step it back up two percent, and
    /// the recovery never goes past the normal speed. The last part drives a real key press, so the
    /// wiring from scoring to the tempo control is covered too, not just the arithmetic.
    /// </summary>
    /// <summary>
    /// The inference as the user meets it: with the switch on, opening a MIDI file moves the hand split
    /// point to the value measured from that song and the recent list remembers it as inferred; opening
    /// the same file again reuses the remembered value instead of guessing a second time; with the
    /// switch off nothing is touched.
    /// </summary>
    private static void VerifyHandSplitInferenceOnSong(MainWindow window)
    {
        var settings = (PianoVisualSettings)Field(window, "_visualSettings");
        var sliders = (Dictionary<string, Slider>)Field(window, "_visualSliders");
        var split = sliders[nameof(PianoVisualSettings.HandSplitPitch)];
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        var toggle = toggles[nameof(PianoVisualSettings.InferHandSplit)];
        var path = Path.Combine(Path.GetTempPath(), "keyflow-split-song.mid");
        File.WriteAllBytes(path, CreateTwoHandMidi());
        var expected = HandSplit.Infer(MidiReader.ReadSong(path).Notes, 60);
        var hadSplit = split.Value; var hadInference = settings.InferHandSplit;
        SongLibrary.Forget(path);

        try
        {
            toggle.IsChecked = false; split.Value = 60; window.OpenMidiFile(path);
            Assert(Math.Abs(split.Value - 60) < .01 && SongLibrary.Find(path) is { SplitInferred: false, HandSplitPitch: 60 },
                "With the switch off, opening a song must keep the hand split the user chose and remember the song as not inferred.");

            toggle.IsChecked = true; split.Value = 45; window.OpenMidiFile(path);
            var entry = SongLibrary.Find(path);
            Assert(settings.InferHandSplit && Math.Abs(split.Value - expected) < .01 && Math.Abs(settings.HandSplitPitch - expected) < .01,
                $"With the switch on, opening a song must move the hand split to the inferred value ({expected}), not leave it at {split.Value}.");
            Assert(entry is { SplitInferred: true } && Math.Abs(entry.HandSplitPitch - expected) < .01,
                "The recent list should remember the inferred split as inferred, together with the song.");

            // A remembered inference wins over a fresh measurement, which is what keeps a song's split stable.
            SongLibrary.Remember(path, "Split Song", 6, 2, 1, 100, 45, 550, 100, "", splitInferred: true);
            split.Value = 60; window.OpenMidiFile(path);
            Assert(Math.Abs(split.Value - 45) < .01 && SongLibrary.Find(path) is { SplitInferred: true, HandSplitPitch: 45 },
                "Reopening a song whose split was inferred before must reuse the remembered value instead of measuring a new one.");

            toggle.IsChecked = false; split.Value = 55; window.OpenMidiFile(path);
            Assert(Math.Abs(split.Value - 55) < .01 && SongLibrary.Find(path) is { SplitInferred: false },
                "Turning the switch off must stop the inference, and the song stops being remembered as inferred.");
        }
        finally
        {
            SongLibrary.Forget(path);
            toggle.IsChecked = hadInference; split.Value = hadSplit; window.RefreshRecentSongs();
        }
        Results.Add("PASS hand split in the app: the switch applies the inferred value to the dock slider and the settings file, the recent list keeps it as inferred, a remembered inference is reused and the switch off leaves the chosen split alone.");
    }

    private static void VerifyPracticeTempo(MainWindow window)
    {
        var settings = (PianoVisualSettings)Field(window, "_visualSettings");
        var tempo = (Slider)window.FindName("TempoSlider");
        var toggle = (CheckBox)window.FindName("AutoPracticeTempoCheck");
        var threshold = (Slider)window.FindName("AutoPracticeMissSlider");
        Assert(!settings.PracticeAutoTempo && settings.PracticeMissThreshold == 3 && toggle.IsChecked == false && !threshold.IsEnabled,
            "Auto practice tempo should start off, with the threshold slider disabled, so ordinary playback is never touched.");
        var hadTempo = tempo.Value;
        tempo.Value = 100;

        // The tempo curve only reads the hit/miss flag; the place each note sat is what the ghost keeps.
        for (var index = 0; index < 10; index++) Invoke(window, "RecordPracticeNote", false, 60, 1);
        Assert(Math.Abs(tempo.Value - 100) < .01 && window.PracticeMissRun == 0,
            "With auto practice tempo off, misses must not touch the playback tempo.");

        toggle.IsChecked = true; threshold.Value = 2; tempo.Value = 100;
        Assert(settings.PracticeAutoTempo && threshold.IsEnabled, "Turning the switch on should store the flag and enable the threshold slider.");
        Invoke(window, "RecordPracticeNote", false, 60, 1); Invoke(window, "RecordPracticeNote", false, 60, 1);
        Assert(Math.Abs(tempo.Value - 100) < .01, "Two misses with a threshold of two must not slow the song down yet.");
        Invoke(window, "RecordPracticeNote", false, 60, 1);
        Assert(Math.Abs(tempo.Value - 95) < .01, "The miss past the threshold should drop the playback tempo by five percent.");
        for (var index = 0; index < 35; index++) Invoke(window, "RecordPracticeNote", false, 60, 1);
        Assert(Math.Abs(tempo.Value - 50) < .01, "Auto practice tempo must stop at the slow floor instead of dropping below it.");

        Invoke(window, "RecordPracticeNote", true, 60, 1);
        Assert(Math.Abs(tempo.Value - 50) < .01 && window.PracticeHitRun == 1, "One correct note only starts the recovery run; it must not move the tempo.");
        for (var index = 0; index < 3; index++) Invoke(window, "RecordPracticeNote", true, 60, 1);
        Assert(Math.Abs(tempo.Value - 52) < .01, "Four correct notes in a row should give two percent back.");
        for (var index = 0; index < 4 * 30; index++) Invoke(window, "RecordPracticeNote", true, 60, 1);
        Assert(Math.Abs(tempo.Value - 100) < .01, "Correct playing should bring the tempo back to normal and stop there.");

        Invoke(window, "RecordPracticeNote", true, 60, 1); Invoke(window, "RecordPracticeNote", false, 60, 1);
        Assert(window.PracticeHitRun == 0 && window.PracticeMissRun == 1, "A miss should clear the correct-note run, and the other way round.");
        Invoke(window, "ResetPracticeTempoRuns");
        Assert(window.PracticeMissRun == 0 && window.PracticeHitRun == 0, "Restarting the score should forget both practice runs.");

        // The wiring: key presses while a song plays must reach the same curve. The demo song is staged in
        // memory the way the other checks do it, so this block knows which pitches are misses and which one
        // is the hit, whatever song ran before it.
        var practiceSong = MainWindow.CreateDemoSong();
        SetField(window, "_allNotes", practiceSong); SetField(window, "_notes", practiceSong);
        SetField(window, "_position", .5);
        Invoke(window, "PopulateTracks", false); Invoke(window, "UpdateSongUi"); Invoke(window, "UpdatePlaybackLabel");
        threshold.Value = 1; tempo.Value = 100;
        SetField(window, "_playing", true);
        Invoke(window, "PressNote", 30, 90); Invoke(window, "ReleaseNote", 30);
        Assert(Math.Abs(tempo.Value - 100) < .01 && window.PracticeMissRun == 1, "The first missed key with a threshold of one must not slow the song down yet.");
        Invoke(window, "PressNote", 31, 90); Invoke(window, "ReleaseNote", 31);
        Assert(Math.Abs(tempo.Value - 95) < .01, "A second missed key must slow the playing song down by one step.");
        Invoke(window, "PressNote", 68, 90); Invoke(window, "ReleaseNote", 68);
        Assert(Math.Abs(tempo.Value - 95) < .01 && window.PracticeHitRun == 1, "A correct key during playback must start the recovery run without moving the tempo.");
        Invoke(window, "Stop"); SetField(window, "_playing", false);

        toggle.IsChecked = false; threshold.Value = 3; tempo.Value = hadTempo; Invoke(window, "ResetPracticeTempoRuns");
        Assert(!settings.PracticeAutoTempo && settings.PracticeMissThreshold == 3, "The practice tempo switches should keep their stored values when the check restores them.");
        Results.Add("PASS practice tempo: off by default, five-percent slow-down past the threshold with a floor, two-percent recovery after four correct notes with a ceiling, run bookkeeping and the real missed-key path.");
    }

    /// <summary>
    /// The practice history: one JSON line per finished run under the settings folder, newest first and
    /// capped, a damaged line skipped instead of failing the file, the best take per song, the exported
    /// HTML report with the runs and the per-song summary, and the app path — a take that graded
    /// something is recorded when the transport stops, exactly once.
    /// </summary>
    private static void VerifyPracticeHistory(MainWindow window)
    {
        var host = (StackPanel)window.FindName("PracticeHistoryHost");
        var empty = (TextBlock)window.FindName("HistoryEmptyLabel");
        var summary = (TextBlock)window.FindName("HistorySummaryLabel");
        Assert(PracticeHistory.FilePath.StartsWith(PianoVisualSettingsStore.SettingsDirectory, StringComparison.OrdinalIgnoreCase)
                && PracticeHistory.FilePath.Contains("history", StringComparison.OrdinalIgnoreCase),
            $"The practice history must live under the settings folder of this run (using {PracticeHistory.FilePath}).");

        PracticeHistory.Clear(); Invoke(window, "RefreshPracticeHistory");
        Assert(PracticeHistory.Runs.Count == 0 && host.Children.Count == 0 && empty.Visibility == Visibility.Visible && summary.Text.Length == 0,
            "A cleared practice history should print the empty state and no rows.");

        PracticeHistory.Record("Alpha", @"C:\songs\alpha.mid", 90, 10, 12);
        PracticeHistory.Record("Beta", @"C:\songs\beta.mid", 50, 50, 4);
        PracticeHistory.Record("Alpha", @"C:\songs\alpha.mid", 95, 5, 20);
        var runs = PracticeHistory.Runs;
        Assert(runs.Count == 3 && runs[0].Song == "Alpha" && Math.Abs(runs[0].Accuracy - 95) < .01
                && runs[1].Song == "Beta" && Math.Abs(runs[1].Accuracy - 50) < .01
                && runs[2].Song == "Alpha" && Math.Abs(runs[2].Accuracy - 90) < .01,
            "Recorded runs should be listed newest first, with their accuracy derived from the hits and misses: "
            + string.Join(", ", runs.Select(run => $"{run.Song} {run.Accuracy:0.#}%")));
        Assert(File.ReadAllLines(PracticeHistory.FilePath).Length == 3, "Every run should append exactly one line to the history file.");
        PracticeHistory.Reload();
        Assert(PracticeHistory.Runs.Count == 3 && PracticeHistory.Runs[0].Song == "Alpha" && PracticeHistory.Runs[0].BestStreak == 20,
            "The history should read back from its file in the same order, with every field intact.");
        Assert(PracticeHistory.BestFor(@"C:\songs\alpha.mid") is { Hits: 95, BestStreak: 20 } && PracticeHistory.BestFor(@"C:\songs\new.mid") is null && PracticeHistory.BestFor("") is null,
            "The best take of a song should be the one with the highest accuracy, and a song never played has none.");

        var report = Path.Combine(Path.GetTempPath(), "keyflow-practice-history.html");
        PracticeHistory.ExportReport(report);
        var html = File.ReadAllText(report);
        Assert(File.ReadAllBytes(report).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }),
            "The exported report should carry a UTF-8 BOM so Vietnamese titles open correctly.");
        Assert(html.Contains("<!DOCTYPE html>") && html.Contains(Loc.T("Keyflow practice history")) && html.Contains(Loc.T("Best per song"))
                && html.Contains("Alpha") && html.Contains("95") && html.Contains(Loc.T("Best streak")),
            "The exported report should hold the runs table and the per-song summary in the active language.");

        for (var index = 0; index < PracticeHistory.Capacity + 3; index++)
            PracticeHistory.Record($"Take {index}", "", 1, 1, 1);
        Assert(PracticeHistory.Runs.Count == PracticeHistory.Capacity && PracticeHistory.Runs[0].Song == $"Take {PracticeHistory.Capacity + 2}"
                && PracticeHistory.TotalRuns == PracticeHistory.Capacity + 6 && PracticeHistory.Summary().Count == PracticeHistory.Capacity + 5,
            "The history should retain only the newest runs in memory while all-time totals and per-song summaries cover the append-only archive.");
        File.AppendAllText(PracticeHistory.FilePath, "{ half a line" + Environment.NewLine);
        PracticeHistory.Reload();
        Assert(PracticeHistory.Runs.Count == PracticeHistory.Capacity && PracticeHistory.TotalRuns == PracticeHistory.Capacity + 6
                && PracticeHistory.Summary().Count == PracticeHistory.Capacity + 5,
            "A damaged line should be skipped without losing the rest of the archive's streaming aggregates.");

        PracticeHistory.Clear();
        for (var index = 0; index < MainWindow.HistoryRows + 3; index++) PracticeHistory.Record($"Row {index}", "", 8, 2, 4);
        Invoke(window, "RefreshPracticeHistory");
        Assert(host.Children.Count == MainWindow.HistoryRows && empty.Visibility == Visibility.Collapsed && summary.Text.Length > 0,
            "The dock page should print the newest runs, hide the empty state and summarise how many were recorded.");

        // The app path: a take that graded something is written when the transport stops, and only once.
        PracticeHistory.Clear();
        var song = MainWindow.CreateDemoSong();
        var hadLabel = (string)Field(window, "_songLabel"); var hadPath = (string)Field(window, "_songPath");
        SetField(window, "_allNotes", song); SetField(window, "_notes", song);
        SetField(window, "_songLabel", "Demo Run"); SetField(window, "_songPath", "");
        SetField(window, "_hits", 7); SetField(window, "_misses", 3); SetField(window, "_bestStreak", 5);
        SetField(window, "_playing", true);
        Invoke(window, "Stop");
        Assert(PracticeHistory.Runs.Count == 1 && PracticeHistory.Runs[0] is { Song: "Demo Run", Hits: 7, Misses: 3, BestStreak: 5 },
            "Stopping a take that graded notes should record its song, hits, misses and best streak.");
        Invoke(window, "Stop");
        Assert(PracticeHistory.Runs.Count == 1, "Stopping an already stopped transport must not record the same take twice.");
        SetField(window, "_hits", 0); SetField(window, "_misses", 0); SetField(window, "_playing", true);
        Invoke(window, "Stop");
        Assert(PracticeHistory.Runs.Count == 1, "A take that graded nothing must not be recorded.");

        PracticeHistory.Clear(); Invoke(window, "RefreshPracticeHistory");
        SetField(window, "_songLabel", hadLabel); SetField(window, "_songPath", hadPath);
        Assert(PracticeHistory.Runs.Count == 0 && host.Children.Count == 0 && empty.Visibility == Visibility.Visible && PracticeHistory.BestFor(hadPath) is null,
            "Clearing the history should leave no runs behind for the next check.");
        Results.Add("PASS practice history: isolated append-only file with one line per run, bounded newest-first in-memory runs plus all-time summaries, damaged line tolerated, best take per song, UTF-8 HTML report with a recent-runs note and the per-song summary, and the stop-the-transport recording path.");
    }

    /// <summary>
    /// The share box as the user meets it: COPY fills the box with the current look and applying that code
    /// elsewhere restores it, applying a code that a friend sent carries their settings across, and a code
    /// that is not a Keyflow look is refused with the reason printed in the dock instead of a dialog.
    /// </summary>
    private static void VerifyPresetSharing(MainWindow window)
    {
        var settings = (PianoVisualSettings)Field(window, "_visualSettings");
        // Pattern matching both finds the controls and narrows them, which keeps the nullable analysis of
        // this file honest: the check cannot run against a page that lost its share box.
        if (window.FindName("ShareCodeBox") is not TextBox box || window.FindName("ShareCodeStatusLabel") is not TextBlock status)
            throw new InvalidOperationException("The Style page should carry the share box and its status line.");
        var hadLook = settings.ToJson();
        var hadGlow = settings.NoteGlow; var hadStyle = settings.NoteStyle; var hadName = settings.PresetName;

        var code = window.RefreshShareCode();
        Assert(box.Text == code && code.StartsWith(VisualPresetShare.Prefix, StringComparison.Ordinal),
            "Copying a look should put its code in the box, ready to select.");

        // A different look, then the code that was taken before it: applying the code must bring it back.
        settings.NoteGlow = 7; settings.NoteStyle = "Fire"; settings.PresetName = "Tuned by hand";
        Invoke(window, "RefreshSettingControls");
        Assert(window.ApplyShareCode(code) && Math.Abs(settings.NoteGlow - hadGlow) < .01 && settings.NoteStyle == hadStyle && settings.PresetName == hadName,
            "Applying a code should carry the look it was taken from into the stage settings.");
        // The code is deterministic: the look that was just applied is the look that was copied, so the box
        // holds the very same text, and it must be the code of what is on screen right now.
        Assert(box.Text == code && box.Text == VisualPresetShare.Encode(settings) && status.Text.Contains(hadName, StringComparison.Ordinal),
            "After applying, the box should hold the code of what is now on screen and the status line should confirm it.");

        // What another person's code does, end to end.
        var friend = VisualPresets.FindBuiltIn("Inferno")!.Settings.Clone();
        friend.NoteGlow = 44; friend.NoteFallSpeed = 321;
        Assert(window.ApplyShareCode(VisualPresetShare.Encode(friend)) && settings.NoteStyle == "Fire"
                && Math.Abs(settings.NoteGlow - 44) < .01 && Math.Abs(settings.NoteFallSpeed - 321) < .01,
            "A code received from somebody else should apply their look to this window.");

        var before = settings.ToJson();
        Assert(!window.ApplyShareCode("not a code at all") && settings.ToJson() == before,
            "A code that is not a Keyflow look must be refused without touching the current settings.");
        Assert(status.Text.Contains(Loc.T("This does not look like a Keyflow look code."), StringComparison.Ordinal),
            "A refused code should print why it was refused in the dock.");

        // The whole look goes back, not just the fields this check moved: a check that leaves the window in
        // somebody else's preset makes every check after it depend on that preset.
        settings.CopyFrom(PianoVisualSettings.FromJson(hadLook));
        Invoke(window, "RefreshSettingControls");
        Assert(Math.Abs(settings.NoteGlow - hadGlow) < .01 && settings.NoteStyle == hadStyle && settings.PresetName == hadName,
            "The sharing check should hand the window back in the look it found.");
        Results.Add("PASS look sharing in the app: COPY fills the box, a pasted code carries a look in, a foreign code is refused with its reason in the dock and nothing is changed.");
    }

    /// <summary>
    /// How a time signature is felt, as against how it is written: a simple meter beats on its beat-type
    /// while a compound one is felt in threes, and the beat grid the readers hand the sheet follows that
    /// grouping — the beams, the rests, the bar lines and the metronome's downbeat all count the felt beat.
    /// </summary>
    private static void VerifyMeter()
    {
            // How a signature is felt: a simple meter beats on its beat-type, a compound one in threes.
            // Each signature has its own file, so the checks above keep reading the arrangement they wrote.
            var meterPath = Path.Combine(Path.GetTempPath(), "keyflow-meter.mid");
            Assert(Meter.Of(4, 4) == (4, 1) && Meter.Of(3, 4) == (3, 1) && Meter.Of(2, 2) == (2, 1) && Meter.Of(3, 8) == (3, 1)
                    && Meter.Of(6, 8) == (2, 3) && Meter.Of(9, 8) == (3, 3) && Meter.Of(12, 8) == (4, 3) && Meter.Of(6, 4) == (2, 3)
                    && Meter.Of(5, 4) == (5, 1) && Meter.Of(7, 8) == (7, 1)
                    && Math.Abs(Meter.BeatInQuarters(3, 8) - 1.5) < 1e-9 && Math.Abs(Meter.BeatInQuarters(1, 4) - 1) < 1e-9,
                "A simple meter should beat on its written beat-type while a compound one (six, nine or twelve) is felt in threes, which is the grouping a score beams by.");
            File.WriteAllBytes(meterPath, CreateSignatureMidi(6, 8)); var compound = MidiReader.ReadSong(meterPath);
            Assert(compound.BeatsPerBar == 2 && compound.BeatTimes.Count == 2
                    && Math.Abs(compound.BeatTimes[0]) < 1e-9 && Math.Abs(compound.BeatTimes[1] - .75) < 1e-6,
                $"A 6/8 bar should beat twice on dotted quarters rather than six times on eighths (found {compound.BeatsPerBar} beat(s) at {string.Join(", ", compound.BeatTimes.Select(time => time.ToString("0.###")))}).");
            File.WriteAllBytes(meterPath, CreateSignatureMidi(3, 4)); var triple = MidiReader.ReadSong(meterPath);
            Assert(triple.BeatsPerBar == 3 && triple.BeatTimes.Count == 3 && Math.Abs(triple.BeatTimes[1] - .5) < 1e-6,
                $"A 3/4 bar should keep three quarter beats (found {triple.BeatsPerBar} beat(s)).");
            File.WriteAllBytes(meterPath, CreateSignatureMidi(12, 8)); var twelve = MidiReader.ReadSong(meterPath);
            Assert(twelve.BeatsPerBar == 4 && Math.Abs(twelve.BeatTimes[1] - .75) < 1e-6,
                $"A 12/8 bar should be four dotted-quarter beats, the way a score groups it (found {twelve.BeatsPerBar}).");
    }


    /// <summary>
    /// MusicXML import: divisions and tempo become seconds, chords share an onset, backup and forward move
    /// the cursor, a tempo change moves everything after it, parts are simultaneous with their own names,
    /// the staves state the hand split (and nothing is invented when the hands overlap), compressed .mxl is
    /// read through its container, and a file that is not a score is refused with a reason.
    /// </summary>
    private static void VerifyMusicXmlImport()
    {
        // A piano measure of 2/4 at 60 bpm with two divisions per quarter, so a quarter note is exactly one
        // second: the right hand plays C4 then an E4+G4 chord, the left hand enters after a backup with a
        // quarter and an eighth plus a forward, and the second measure doubles the tempo.
        var twoHands = """
<score-partwise version="4.0">
  <work><work-title>Fixture Waltz</work-title></work>
  <identification><creator type="composer">Nobody</creator></identification>
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1">
    <measure number="1">
      <attributes><divisions>2</divisions><staves>2</staves><time><beats>2</beats><beat-type>4</beat-type></time></attributes>
      <direction><sound tempo="60"/></direction>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <note><pitch><step>E</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <note><chord/><pitch><step>G</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <backup><duration>4</duration></backup>
      <note><pitch><step>E</step><octave>3</octave></pitch><duration>2</duration><staff>2</staff></note>
      <note><pitch><step>G</step><octave>3</octave></pitch><duration>1</duration><staff>2</staff></note>
      <forward><duration>1</duration></forward>
    </measure>
    <measure number="2">
      <direction><sound tempo="120"/></direction>
      <note><pitch><step>D</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
      <backup><duration>2</duration></backup>
      <note><pitch><step>C</step><octave>3</octave></pitch><duration>2</duration><staff>2</staff></note>
      <note><rest/><duration>2</duration></note>
    </measure>
  </part>
</score-partwise>
""";
        var score = MusicXmlReader.Parse(twoHands);
        Assert(score.Title == "Fixture Waltz" && score.Composer == "Nobody" && score.MeasureCount == 2 && score.BeatsPerBar == 2,
            $"A score should carry its title, composer, measure count and time signature (found {score.Title}/{score.Composer}, {score.MeasureCount} measure(s), {score.BeatsPerBar} beats).");
        static string Describe(NoteEvent note) => $"{note.Pitch}@{note.Start:0.###}+{note.Duration:0.###}/t{note.Track}";
        var right = score.Notes.Where(note => note.Pitch >= 60).OrderBy(note => note.Start).Select(Describe).ToList();
        var left = score.Notes.Where(note => note.Pitch < 60).OrderBy(note => note.Start).Select(Describe).ToList();
        Assert(right.SequenceEqual(new[] { "60@0+1/t0", "64@1+1/t0", "67@1+1/t0", "62@2+0.5/t0" }) && left.SequenceEqual(new[] { "52@0+1/t0", "55@1+0.5/t0", "48@2+0.5/t0" }),
            $"Divisions, tempo, chords, backup and forward should place every note in seconds (right: {string.Join(", ", right)}; left: {string.Join(", ", left)}).");
        Assert(score.HandSplitPitch == 57 && score.SplitFromStaves,
            $"The staves should state the split between G3 and C4 as 57 (found {score.HandSplitPitch}).");
        Assert(score.BeatTimes.Count >= 4 && Math.Abs(score.BeatTimes[0]) < 1e-9 && Math.Abs(score.BeatTimes[1] - 1) < 1e-9
                && Math.Abs(score.BeatTimes[2] - 2) < 1e-9 && Math.Abs(score.BeatTimes[3] - 2.5) < 1e-9,
            $"The beat grid should follow the time signature and the tempo of each measure (found {string.Join(", ", score.BeatTimes.Take(4).Select(time => time.ToString("0.###")))}).");
        Assert(score.TrackNames[0] == "Piano" && score.ToSong().Notes.Count == score.Notes.Count && score.ToSong().BeatsPerBar == 2,
            "The part list should name the track a note carries, and the score should hand itself over as a song.");

        // A compound signature beats in threes here too: three quarters of music written in 6/8 are two beats,
        // and the sheet's beams, rests and bar lines follow that grouping rather than one beat per eighth.
        var compoundScore = """
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
  <part id="P1"><measure number="1">
    <attributes><divisions>2</divisions><time><beats>6</beats><beat-type>8</beat-type></time></attributes>
    <direction><sound tempo="60"/></direction>
    <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration></note>
    <note><pitch><step>D</step><octave>4</octave></pitch><duration>2</duration></note>
    <note><pitch><step>E</step><octave>4</octave></pitch><duration>2</duration></note>
  </measure></part>
</score-partwise>
""";
        var compoundXml = MusicXmlReader.Parse(compoundScore);
        // The bar itself is two beats half a bar apart, and the grid keeps that step past the last note so the
        // metronome and the sheet still have a beat to stand on (three quarters of music fill a 6/8 bar).
        Assert(compoundXml.BeatsPerBar == 2 && compoundXml.BeatTimes.Count >= 3
                && Math.Abs(compoundXml.BeatTimes[0]) < 1e-9 && Math.Abs(compoundXml.BeatTimes[1] - 1.5) < 1e-9
                && Math.Abs(compoundXml.BeatTimes[2] - 3) < 1e-9,
            $"A 6/8 score should beat twice on dotted quarters at the measure's tempo and keep that step past the bar (found {compoundXml.BeatsPerBar} beat(s) at {string.Join(", ", compoundXml.BeatTimes.Take(4).Select(time => time.ToString("0.###")))}).");
        Assert(compoundXml.Notes.Count == 3 && Math.Abs(compoundXml.Notes[^1].Start - 2) < 1e-9,
            "Reading the grid of a compound signature should not move the notes themselves.");

        // Two parts instead of two staves: the first part is the right hand, the second the left.
        var twoParts = """
<score-partwise version="4.0">
  <part-list><score-part id="P1"><part-name>Right</part-name></score-part><score-part id="P2"><part-name>Left</part-name></score-part></part-list>
  <part id="P1"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration></note></measure></part>
  <part id="P2"><measure number="1"><attributes><divisions>1</divisions></attributes>
    <note><pitch><step>C</step><octave>3</octave></pitch><duration>1</duration></note></measure></part>
</score-partwise>
""";
        var parts = MusicXmlReader.Parse(twoParts);
        Assert(parts.Notes.Count == 2 && parts.Notes.Any(note => note is { Pitch: 72, Track: 0 }) && parts.Notes.Any(note => note is { Pitch: 48, Track: 1 })
                && parts.TrackNames[0] == "Right" && parts.TrackNames[1] == "Left" && parts.HandSplitPitch == 60,
            "Two parts should be simultaneous and named, and the hand split should come from the part each note is in.");

        // Overlapping hands: the file does not state a split, so none is invented.
        var overlapping = twoHands.Replace("<step>C</step><octave>4</octave>", "<step>C</step><octave>3</octave>");
        Assert(MusicXmlReader.Parse(overlapping).HandSplitPitch is null,
            "A score whose hands overlap should leave the split to the user instead of inventing one.");
        // One hand only, and a melody on a single staff: nothing to split either.
        var oneHand = MusicXmlReader.Parse(twoHands.Replace("<staff>2</staff>", "<staff>1</staff>"));
        Assert(oneHand.HandSplitPitch is null, "A score with a single hand should not claim a split.");
        Assert(Throws(() => MusicXmlReader.Parse("<score-partwise><part-list/></score-partwise>")),
            "A score without notes should be refused instead of opening an empty stage.");

        // Compressed: the container points at the score, which is what a real .mxl looks like inside.
        var folder = Path.Combine(Path.GetTempPath(), "keyflow-verify-musicxml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var mxl = Path.Combine(folder, "fixture.mxl");
            using (var archive = System.IO.Compression.ZipFile.Open(mxl, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("META-INF/container.xml").Open()))
                    writer.Write("<container><rootfiles><rootfile full-path=\"score.xml\" media-type=\"application/vnd.recordare.musicxml+xml\"/></rootfiles></container>");
                using (var writer = new StreamWriter(archive.CreateEntry("score.xml").Open())) writer.Write(twoHands);
            }
            var compressed = MusicXmlReader.ReadScore(mxl);
            Assert(compressed.Notes.Count == score.Notes.Count && compressed.HandSplitPitch == 57 && compressed.Title == "Fixture Waltz",
                "A compressed .mxl should be read through its container and arrive as the same score.");
            var bare = Path.Combine(folder, "bare.mxl");
            using (var archive = System.IO.Compression.ZipFile.Open(bare, System.IO.Compression.ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("score.musicxml").Open())) writer.Write(twoHands);
            Assert(MusicXmlReader.ReadScore(bare).Notes.Count == score.Notes.Count,
                "A compressed score without a container should still be found by its extension.");
            var empty = Path.Combine(folder, "empty.mxl");
            using (var archive = System.IO.Compression.ZipFile.Open(empty, System.IO.Compression.ZipArchiveMode.Create))
                archive.CreateEntry("readme.txt");
            Assert(Throws(() => MusicXmlReader.ReadScore(empty)), "A compressed file with no score inside should be refused.");

            var plain = Path.Combine(folder, "plain.musicxml");
            File.WriteAllText(plain, twoHands);
            var fromDisk = MusicXmlReader.ReadScore(plain);
            Assert(fromDisk.Title == "Fixture Waltz" && fromDisk.HandSplitPitch == 57 && MainWindow.IsMusicXml(plain) && !MainWindow.IsMusicXml(Path.ChangeExtension(plain, ".mid")),
                "A plain score should load from disk, and only the score extensions should route to this reader.");

            Assert(Throws(() => MusicXmlReader.Parse("not xml at all <")) && Throws(() => MusicXmlReader.Parse("<foo><bar/></foo>"))
                    && Throws(() => MusicXmlReader.Parse("<score-timewise><part-list/></score-timewise>")),
                "Text that is not XML, a foreign root element and a time-wise score should each be refused with an exception.");
            var untitled = MusicXmlReader.Parse(twoHands.Replace("<work><work-title>Fixture Waltz</work-title></work>", ""), "fallback-name");
            Assert(untitled.Title == "fallback-name", "A score without a work title should fall back to the name it was opened under.");
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
        Results.Add($"PASS MusicXML import: {score.Notes.Count} notes with divisions, chords, backups and a tempo change placed to the second, the staves state the hand split (57) while overlapping hands state none, .mxl reads through its container, and foreign or note-less files are refused.");
    }

    /// <summary>
    /// The PNG sequence exporter: every frame is a 32-bit PNG of the recording size (8-bit RGBA, exactly
    /// the format a compositor needs for an alpha layer), a repeated frame is written as a copy, the folder
    /// gets a manifest that names the pattern and the ffmpeg line back to alpha video, a wrong-sized frame
    /// or an impossible size is refused, and the stage drops its opaque fills only when the export asks for
    /// transparency.
    /// </summary>
    private static void VerifyPngSequenceRecorder(MainWindow window)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyflow-verify-frames-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = new PngSequenceRecorder(directory, 64, 48, 24);
            Assert(recorder.HasAlpha && !recorder.IsNearSizeLimit && recorder.FrameBytes == 64 * 4 * 48 && recorder.OutputPath == directory,
                "The PNG sequence recorder should ask for tightly packed BGRA frames, keep alpha and have no 2 GB limit to stop at.");
            var frame = new byte[64 * 4 * 48];
            for (var y = 0; y < 48; y++)
                for (var x = 0; x < 64; x++)
                {
                    var at = (y * 64 + x) * 4;
                    var inside = x > 16 && y > 24; // premultiplied: an opaque mid grey inside, nothing outside
                    frame[at] = frame[at + 1] = frame[at + 2] = inside ? (byte)60 : (byte)0;
                    frame[at + 3] = inside ? (byte)255 : (byte)0;
                }
            recorder.WriteFrame(frame, 3);
            Assert(recorder.FrameCount == 3 && File.Exists(Path.Combine(directory, "frame-000001.png")) && File.Exists(Path.Combine(directory, "frame-000003.png")),
                "A repeated frame should land in the folder once per copy, numbered from one.");
            var first = Path.Combine(directory, "frame-000001.png");
            var bytes = File.ReadAllBytes(first);
            Assert(bytes.Length > 8 && bytes.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                "Every frame of the sequence should be a PNG.");
            using (var stream = new MemoryStream(bytes))
            {
                var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var pixels = new byte[decoded.PixelWidth * 4 * decoded.PixelHeight];
                var source = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
                source.CopyPixels(pixels, decoded.PixelWidth * 4, 0);
                var corner = pixels[3];
                var middle = pixels[((decoded.PixelHeight - 4) * decoded.PixelWidth + decoded.PixelWidth - 4) * 4 + 3];
                Assert(bytes[24] == 8 && bytes[25] == 6 && decoded.PixelWidth == 64 && decoded.PixelHeight == 48 && corner == 0 && middle > 200,
                    $"A frame should be an 8-bit RGBA PNG carrying the alpha the stage drew (colour type {bytes[25]}, corner alpha {corner}, inside alpha {middle}).");
            }
            Assert(recorder.BytesWritten > 0 && recorder.BytesWritten >= new FileInfo(first).Length,
                "The recorder should count the bytes it wrote.");
            Assert(Throws(() => recorder.WriteFrame(new byte[16])), "A frame of the wrong size should be refused instead of written half-way.");
            recorder.Dispose();
            var manifestPath = Path.Combine(directory, PngSequenceRecorder.ManifestName);
            Assert(File.Exists(manifestPath), "Stopping a sequence should leave a manifest next to the frames.");
            using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var root = document.RootElement;
                Assert(root.GetProperty("format").GetString() == "png32" && root.GetProperty("width").GetInt32() == 64
                        && root.GetProperty("height").GetInt32() == 48 && root.GetProperty("fps").GetInt32() == 24
                        && root.GetProperty("frames").GetInt32() == 3 && root.GetProperty("alpha").GetBoolean()
                        && root.GetProperty("pattern").GetString() == PngSequenceRecorder.Pattern
                        && root.GetProperty("ffmpeg").GetString()!.Contains("libvpx-vp9"),
                    "The manifest should describe the frames and how to turn them back into alpha video.");
            }
            Assert(Throws(() => new PngSequenceRecorder(directory, 0, 48, 24)) && Throws(() => new PngSequenceRecorder(directory, 64, 48, 120)),
                "An impossible size and an impossible frame rate should be refused with an exception.");

            // The stage: transparency is a property of the export, and it is what the alpha frames come from.
            var stage = (PianoStage)Field(window, "Stage")!;
            var hadLook = ((PianoVisualSettings)Field(window, "_visualSettings")!).ToJson();
            var flat = PianoVisualSettings.FromJson(hadLook);
            flat.ShowBackground = false; flat.ShowWatermark = false; flat.ShowCounter = false; flat.ShowFps = false;
            flat.ShowPetals = false; flat.AmbientEnergy = "None"; flat.AmbientNature = "None"; flat.AmbientLight = "None"; flat.AmbientCosmic = "None";
            flat.HorizonGlow = 0; flat.ShowLightBeams = false; flat.ShowHalo = false; flat.ShowNotes = false; flat.ShowImpactFlash = false;
            stage.SetVisualSettings(flat); stage.ClearTransient(); stage.UpdateLayout();
            // Counted over the whole frame rather than sampled at one pixel: the capture fits the stage into
            // the frame with Uniform (so the edges are letterbox bars), and the claim being made is about how
            // much of the picture the background fills — the piano and its effects have to survive.
            const int total = 64 * 48;
            static int OpaquePixels(byte[] frame)
            {
                var count = 0;
                for (var i = 3; i < frame.Length; i += 4) if (frame[i] >= 250) count++;
                return count;
            }
            var opaque = (byte[])InvokeReturn(window, "CaptureStageBgra", 64, 48)!;
            var opaquePixels = OpaquePixels(opaque);
            stage.TransparentBackdrop = true; stage.UpdateLayout();
            var clear = (byte[])InvokeReturn(window, "CaptureStageBgra", 64, 48)!;
            var clearPixels = OpaquePixels(clear);
            stage.TransparentBackdrop = false; stage.UpdateLayout();
            Assert(opaque.Length == total * 4 && opaquePixels > total / 2,
                $"The stage normally paints a full background: {opaquePixels} of {total} pixels were opaque.");
            Assert(clearPixels > total / 40 && clearPixels < opaquePixels - total / 3,
                $"A transparent export should drop the background and keep the piano: {opaquePixels} opaque pixels with the background, {clearPixels} without (of {total}).");
            var restored = PianoVisualSettings.FromJson(hadLook);
            ((PianoVisualSettings)Field(window, "_visualSettings")!).CopyFrom(restored);
            stage.SetVisualSettings(restored); Invoke(window, "RefreshSettingControls");
            Results.Add("PASS PNG sequence: 32-bit frames of the chosen size, repeated frames written once per copy, a manifest with the pattern and the ffmpeg line, bad sizes refused, and a transparent stage that really produces clear pixels.");
        }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }

    /// <summary>
    /// The MP4 take: the format that comes out ready to upload, because the audio is encoded into the same file
    /// while it records. The arithmetic and the frame layout are checked on every machine; the file itself is
    /// really encoded when the media stack has an H.264 encoder, and a machine that has none reports a SKIP
    /// naming what it said instead of failing over a format it cannot write.
    /// </summary>
    private static void VerifyMp4Recorder(MainWindow window)
    {
        // The format is offered by the dock, not only understood by the recorder.
        var choices = (Dictionary<string, ComboBox>)Field(window, "_visualChoices");
        var options = (Dictionary<string, (string Value, string Caption)[]>)Field(window, "_visualChoiceOptions");
        var formats = options[nameof(PianoVisualSettings.RecordingFormat)];
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.RecordingFormat))
                && formats.Select(option => option.Value).SequenceEqual(PianoVisualSettings.RecordingFormats)
                && formats.Length == 3,
            "The recording format picker should offer the MP4 take beside the AVI file and the PNG sequence.");

        // The bitrate asked of the encoder: seven per cent of the frame size times the frame rate, floored so a
        // small stage is not mush and capped so a 4K take is not enormous.
        Assert(Mp4Recorder.BitrateFor(1920, 1080, 60) == 1920 * 1080 * 60 * 7 / 100
                && Mp4Recorder.BitrateFor(320, 180, 15) == 2_000_000 && Mp4Recorder.BitrateFor(3840, 2160, 60) == 24_000_000,
            "The MP4 bitrate should follow the frame size and rate, with a floor that keeps a small stage readable and a ceiling that keeps a 4K take sane.");

        // Every frame and every block of audio is stamped in the 100-nanosecond units a media sample uses.
        Assert(Mp4Recorder.FrameTime(0, 15) == (0L, 666666L) && Mp4Recorder.FrameTime(15, 15) == (10_000_000L, 666666L)
                && Mp4Recorder.FrameTime(59, 60) == (9_833_333L, 166667L) && Mp4Recorder.FrameTime(60, 60) == (10_000_000L, 166666L),
            "A frame should be stamped at its own index on the recording's clock, so sixty frames at sixty a second land exactly a second in.");
        var second = Enumerable.Range(0, 15).Sum(index => Mp4Recorder.FrameTime(index, 15).Duration);
        Assert(second == 10_000_000 && Enumerable.Range(0, 30).All(index => Mp4Recorder.FrameTime(index, 25).Duration > 0),
            $"The lengths of the frames should add up to the time they cover, with no frame of zero length ({second} ticks for fifteen frames at fifteen a second).");
        Assert(Mp4Recorder.AudioTime(0, 512) == (0L, 116099L) && Mp4Recorder.AudioTime(44100, 512) == (10_000_000L, 116099L),
            "An audio block should be stamped where its first sample belongs on the recording's clock, so a second of audio is stamped a second in.");

        // The frame layout the encoder is given: a full plane of brightness and one of colour pairs, BT.601.
        Assert(Nv12Frame.Size(64, 48) == 64 * 48 * 3 / 2 && Nv12Frame.Size(1920, 1080) == 1920 * 1080 * 3 / 2,
            "An NV12 frame should be a plane of brightness plus a quarter as much again of colour pairs.");
        byte[] Converted(byte blue, byte green, byte red)
        {
            var pixels = new byte[2 * 2 * 4];
            for (var pixel = 0; pixel < 4; pixel++)
            {
                pixels[pixel * 4] = blue; pixels[pixel * 4 + 1] = green; pixels[pixel * 4 + 2] = red; pixels[pixel * 4 + 3] = 255;
            }
            var frame = new byte[Nv12Frame.Size(2, 2)];
            Nv12Frame.FromBgra(pixels, 2, 2, frame);
            return frame;
        }
        var black = Converted(0, 0, 0); var white = Converted(255, 255, 255); var grey = Converted(128, 128, 128); var red = Converted(0, 0, 255);
        Assert(black[0] == 16 && black[4] == 128 && black[5] == 128 && grey[4] == 128 && grey[5] == 128 && white[0] == 235 && white[4] == 128 && white[5] == 128,
            $"Black should become brightness 16 with neutral colour samples, white 235 with the same neutrals, and a mid grey should stay neutral (got {black[0]}, {white[0]}, {grey[4]}).");
        Assert(red[0] == 82 && red[4] == 90 && red[5] == 240,
            $"Pure red should land on the brightness and colour pairs a player expects of BT.601 (Y {red[0]}, U {red[4]}, V {red[5]}).");
        Assert(Throws(() => Nv12Frame.FromBgra(new byte[4], 4, 4, new byte[Nv12Frame.Size(4, 4)])),
            "A frame that is not as long as the size it is converted at should be refused, not read past its end.");

        var path = Path.Combine(Path.GetTempPath(), "keyflow-verify-" + Guid.NewGuid().ToString("N") + ".mp4");
        try { VerifyMp4Take(path); }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try { File.Delete(Mp4TakeAttempt.TracePath(path)); } catch { }
        }
    }

    /// <summary>
    /// The last line of a child's own trace file, or null when it wrote none. The children write these because
    /// they are killed when a machine stalls: what they had already written down is then the only account of
    /// where the machine stopped, and a pipe can lose its last lines at exactly that moment.
    /// </summary>
    private static string? LastTraceLine(string tracePath)
    {
        try
        {
            if (!File.Exists(tracePath)) return null;
            var trace = File.ReadAllLines(tracePath);
            return trace.Length > 0 ? trace[^1] : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Writes a real take out of process. The encoders are native code, and a media stack that dies inside one of
    /// them takes the process with it — no exception to catch and no verdict left to write, which is exactly what
    /// a CI run showed. The take is therefore written by a child of this process: whatever happens to it, this run
    /// keeps its own verdict, the child's own lines are carried into the log to say where it stopped, and a machine
    /// that cannot write the take at all is reported as a skipped check rather than as a fault of the app.
    /// </summary>
    private static void VerifyMp4Take(string path)
    {
        const int TakeTimeoutSeconds = 60;
        var lines = new List<string>();
        var exit = 0;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PianoPath.exe"))
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add("--encode-take=" + path);
            using var child = new Process { StartInfo = start };
            child.OutputDataReceived += (_, line) => { if (line.Data is not null) lock (lines) lines.Add(line.Data); };
            child.Start();
            child.BeginOutputReadLine();
            // A take this small is written in well under a second wherever the encoders work at all. A minute
            // is the point past which the media stack is not slow, it is stuck — and a runner whose encoder
            // never comes back is reported as such rather than left to hold the whole run up.
            exit = child.WaitForExit(TakeTimeoutSeconds * 1000) ? child.ExitCode : -1;
            if (exit < 0) { try { child.Kill(entireProcessTree: true); } catch { } }
            child.WaitForExit();
        }
        catch (Exception ex)
        {
            Results.Add("SKIP MP4 encoder: the take could not be attempted in a child process here (" + ex.Message + "), so only the format's arithmetic and its frame layout were checked.");
            return;
        }
        lock (lines) foreach (var line in lines) Results.Add(line);
        // The child's trace file outlives the kill that follows a stall, and its last line is the step the machine
        // really stopped at: a child taken down inside native code can lose the lines still in flight on the pipe,
        // which is how a report can end up naming a call that had in fact already returned.
        var trace = LastTraceLine(Mp4TakeAttempt.TracePath(path));
        Results.Add(trace is null
            ? "NOTE MP4 encoder: the child kept no trace file, so the steps below are only what reached this run through the pipe."
            : $"NOTE MP4 encoder: the child's own trace file was found; its last line is: {trace}");
        // The encoder check runs before the take is opened, so a child that got as far as the take must also have
        // said whether this machine holds an H.264 stream: without that line the run is reading a take with no
        // word on how the machine got there. A child that died inside the check never did — its last line names
        // the step it died at, and that stays a SKIP like every other stop inside native code.
        if (lines.Any(line => line.Contains("opening a", StringComparison.Ordinal)))
        {
            Assert(lines.Any(line => line.Contains("found a way to make H.264", StringComparison.Ordinal)),
                "A take attempt that opened the take should have said the machine's media stack took an H.264 stream, since a take asks for one.");
            Assert(lines.Any(line => line.Contains("built and stamped on their own", StringComparison.Ordinal)
                    || line.Contains("did not come back within three seconds", StringComparison.Ordinal)),
                "A take attempt that opened the take should have said what became of the media buffer and sample the first frame goes into.");
        }
        if (lines.Count == 0)
            Results.Add("SKIP MP4 encoder: the child process that writes the take said nothing at all, so only the format's arithmetic and its frame layout were checked.");
        else if (exit == 0 && !File.Exists(path))
            Results.Add("FAIL MP4 encoder: the child process wrote no take yet reported that it had finished one.");
        else if (exit != 0)
        {
            // The child says what it was doing before each call it makes, so its last line is the step this
            // machine's media stack stopped at — the whole point of writing the take out of this process.
            var last = trace ?? (lines.Count > 0 ? lines[^1] : "<the child reported nothing at all>");
            Results.Add(exit < 0
                ? $"SKIP MP4 encoder: this machine's media stack never came back from the encoders, so the take was stopped after {TakeTimeoutSeconds} seconds; the last step the child reported was: {last}"
                : $"SKIP MP4 encoder: the child process stopped without finishing a take (it ended with code {exit} at: {last}) — this machine's media stack rather than the app, so only the format's arithmetic and its frame layout were checked.");
        }
        else
        {
            // The file is read only now that the child has closed it: a sink writer holds its file while it works.
            var bytes = File.ReadAllBytes(path);
            Assert(!File.Exists(MainWindow.AudioTrackPath(path, false)),
                "An MP4 take should leave no WAV beside it: the samples went into the file itself.");
            Assert(bytes.Length > 1000 && bytes[4] == 'f' && bytes[5] == 't' && bytes[6] == 'y' && bytes[7] == 'p',
                $"An MP4 should be a file of boxes beginning with the type box that makes it an MP4 ({bytes.Length} bytes were written).");
            var sound = lines.Any(line => line.Contains("open with the audio stream", StringComparison.Ordinal))
                ? Loc.T(" with the audio inside")
                : Loc.T("; this machine's media stack has no AAC encoder, so this take carries the video only");
            Results.Add($"PASS MP4 take: frames and audio blocks stamped on the recording's own clock, the stage's pixels converted to the encoder's NV12 layout, and a real 64×48 take written by a child process to a file of {bytes.Length} bytes{sound}.");
        }
        // A run that produced no take gets the second question asked, and only then: what does this machine make
        // of the part of recording that needs no encoder anywhere?
        if (exit != 0 || !File.Exists(path)) VerifyEncodePlumbingProbe();
    }

    /// <summary>
    /// Can this machine write a file through the sample plumbing at all? Three uncompressed pictures into an AVI
    /// need no encoder anywhere, so the answer is what tells a machine whose file handling or buffers are the
    /// trouble apart from one whose encoder would not cooperate — and it is asked out of process, with a short
    /// timeout, because the AVI sink either opens promptly or not at all. It runs after the take, never before
    /// it: a diagnostic that hangs must not stand in front of the thing it exists to explain, which is exactly
    /// what a CI run showed when the probe was tried first.
    /// </summary>
    private static void VerifyEncodePlumbingProbe()
    {
        // The probe runs six guarded media-object cases (three seconds each at the very worst) and then tries the
        // AVI, which is the part that can hang: a minute is room for the cases and for a machine that is merely
        // slow, while still being quick enough that a stuck probe costs the run a line rather than the run.
        const int ProbeTimeoutSeconds = 60;
        var probe = Path.Combine(Path.GetTempPath(), "keyflow-plumbing-probe-" + Guid.NewGuid().ToString("N") + ".avi");
        var lines = new List<string>();
        var exit = 0;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PianoPath.exe"))
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add("--encode-probe=" + probe);
            using var child = new Process { StartInfo = start };
            child.OutputDataReceived += (_, line) => { if (line.Data is not null) lock (lines) lines.Add(line.Data); };
            child.Start();
            child.BeginOutputReadLine();
            exit = child.WaitForExit(ProbeTimeoutSeconds * 1000) ? child.ExitCode : -1;
            if (exit < 0) { try { child.Kill(entireProcessTree: true); } catch { } }
            child.WaitForExit();
        }
        catch (Exception ex)
        {
            Results.Add("SKIP MP4 encoder plumbing: the encoder-free AVI probe could not be attempted in a child process here (" + ex.Message + ").");
            return;
        }
        lock (lines) foreach (var line in lines) Results.Add(line);
        var probeTrace = LastTraceLine(EncodeProbeAttempt.TracePath(probe));
        Results.Add(probeTrace is null
            ? "NOTE MP4 encoder plumbing: the probe kept no trace file, so its steps are only what reached this run through the pipe."
            : $"NOTE MP4 encoder plumbing: the probe's own trace file was found; its last line is: {probeTrace}");
        var last = probeTrace ?? (lines.Count > 0 ? lines[^1] : "<the probe reported nothing at all>");
        Results.Add(exit == 0
            ? "PASS MP4 encoder plumbing: with no take written, a child process wrote three uncompressed pictures into an AVI through the take's own sample step, so this machine's media stack and this side's buffers both work and the take got stuck at the encoder."
            : exit < 0
                ? $"SKIP MP4 encoder plumbing: the encoder-free AVI probe never came back and was stopped after {ProbeTimeoutSeconds} seconds; the last step it reported was: {last}"
                : $"SKIP MP4 encoder plumbing: the encoder-free AVI probe stopped without writing a file (it ended with code {exit} at: {last}).");
        try { File.Delete(probe); } catch { }
    }

    /// <summary>
    /// The sheet layer is geometry first, so most of it is checked as arithmetic: where a pitch is written,
    /// which staff the split gives it, which ledger lines a note outside the staff needs, and where a note in
    /// the visible window lands. The drawing itself is then rendered once, both with a song and without one,
    /// and the stage is asked for the same layer through the toggle the dock exposes — that is what proves the
    /// setting is not dead and that the staves really receive the song's own notes and beat grid.
    /// </summary>
    private static void VerifySheetLayer(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings)
    {
        Assert(SheetLayer.Step(60) == 28 && SheetLayer.Step(72) == 35 && SheetLayer.Step(61) == 28 && SheetLayer.Step(66) == 31 && SheetLayer.Step(43) == 18,
            "Written pitch should follow scientific notation and spell a black key on the line of the letter below it (C4 = 28, B4 = 35, F#4 = 31).");
        Assert(SheetLayer.NeedsSharp(61) && SheetLayer.NeedsSharp(66) && !SheetLayer.NeedsSharp(60) && !SheetLayer.NeedsSharp(64),
            "Only the five black keys of an octave should be written with a sharp sign.");
        var (trebleStaff, trebleStep) = SheetLayer.Place(60, 60);
        var (leftStaff, leftStep) = SheetLayer.Place(43, 60);
        Assert(trebleStaff == 0 && trebleStep == -2 && leftStaff == 1 && leftStep == 0,
            "The hand split should choose the staff: middle C is one ledger line below the treble staff and G2 sits on the bottom line of the bass staff.");
        Assert(SheetLayer.Place(59, 60).Staff == 1 && SheetLayer.Place(60, 60).Staff == 0 && SheetLayer.Place(21, 21).Staff == 0,
            "Every pitch at or above the split belongs to the right hand and everything below it to the left, down to the lowest key.");
        // Every semitone either stays on the same staff and moves the written note up (never down, at most one
        // line or space) or crosses the split onto the upper staff — where the bass leaves off the treble begins.
        var monotone = true; var widestJump = 0;
        for (var pitch = 21; pitch < 108; pitch++)
        {
            var (lowStaff, lowStep) = SheetLayer.Place(pitch, 60);
            var (highStaff, highStep) = SheetLayer.Place(pitch + 1, 60);
            // Staff 0 is the upper (treble) staff and staff 1 the bass one, so a rising pitch may step up from
            // bass to treble but must never fall from treble to bass.
            if (highStaff > lowStaff || (highStaff == lowStaff && highStep < lowStep)) monotone = false;
            if (highStaff == lowStaff) widestJump = Math.Max(widestJump, highStep - lowStep);
        }
        Assert(monotone && widestJump <= 1, $"A higher key must never be written lower on the sheet, and a semitone must move it at most one step (widest was {widestJump}).");

        Assert(SheetLayer.LedgerLines(-1).Count == 0 && SheetLayer.LedgerLines(-2).SequenceEqual([-2]) && SheetLayer.LedgerLines(-4).SequenceEqual([-2, -4])
            && SheetLayer.LedgerLines(8).Count == 0 && SheetLayer.LedgerLines(9).Count == 0 && SheetLayer.LedgerLines(12).SequenceEqual([10, 12]),
            "A note in the space outside a staff needs no ledger line, one on a line outside needs that line, and a note two lines out needs both.");

        // ---- The key: what a song is written in, where the signature goes, and which accidentals a bar needs.
        NoteEvent[] Scale(int tonic, int[] degrees) =>
            [.. degrees.Select((degree, index) => new NoteEvent { Pitch = tonic + degree, Start = index * .5, Duration = .5 })];
        var majorSteps = new[] { 0, 2, 4, 5, 7, 9, 11 };
        var naturalMinor = new[] { 0, 2, 3, 5, 7, 8, 10 };
        var dMajor = MusicKey.Infer(Scale(62, majorSteps));
        Assert(dMajor.Accidentals == 2 && !dMajor.UsesFlats && dMajor.SignedLetters.SequenceEqual([3, 0]),
            $"A D major scale is written with two sharps, F first then C (got {dMajor.Accidentals}, letters [{string.Join(", ", dMajor.SignedLetters)}]).");
        var fMajor = MusicKey.Infer(Scale(65, majorSteps));
        Assert(fMajor.Accidentals == -1 && fMajor.UsesFlats && fMajor.SignedLetters.SequenceEqual([6]),
            $"An F major scale is written with one flat, B (got {fMajor.Accidentals}, letters [{string.Join(", ", fMajor.SignedLetters)}]).");
        var eFlat = MusicKey.Infer(Scale(63, majorSteps));
        Assert(eFlat.Accidentals == -3 && eFlat.SignedLetters.SequenceEqual([6, 2, 5]),
            $"Three flats are written B, E, then A in that order (got {eFlat.Accidentals}, letters [{string.Join(", ", eFlat.SignedLetters)}]).");
        Assert(MusicKey.Infer(Scale(64, naturalMinor)).Accidentals == 1 && MusicKey.Infer(Scale(57, naturalMinor)).Accidentals == 0,
            "A minor-key scale takes the signature of the major key three semitones above its tonic: one sharp for E, none for A.");
        Assert(MusicKey.Infer([]) == MusicKey.CMajor && MusicKey.Infer(Scale(60, [0, 1, 2, 3, 4, 5])).Accidentals == 0,
            "A song that fits no key — an empty one, or a chromatic run — keeps the plain C major spelling instead of inventing a signature.");

        // Where the letters sit: F♯ on the top line of a treble staff, C♯ in its third space, and the bass staff
        // two steps lower, which is what makes B♭ at the head of a bass staff the line below.
        Assert(SheetLayer.SignatureStep(0, 3) == 8 && SheetLayer.SignatureStep(0, 0) == 5 && SheetLayer.SignatureStep(0, 6) == 4
                && SheetLayer.SignatureStep(1, 3) == 6 && SheetLayer.SignatureStep(1, 0) == 3,
            "A signature writes F♯ on the top line of the treble staff and C♯ in its third space, with the bass staff repeating the letters two steps lower.");

        // Spelling follows the key: F major writes B♭ on the B line while C major would have written A♯ on the A line.
        var fSpelling = fMajor.Spell(70);
        var cSpelling = MusicKey.CMajor.Spell(70);
        Assert(fSpelling == (6, 4, -1) && cSpelling == (5, 4, 1) && SheetLayer.Step(70, fMajor) == SheetLayer.Step(71, MusicKey.CMajor)
                && SheetLayer.Step(70, MusicKey.CMajor) != SheetLayer.Step(70, fMajor),
            $"F major should write B♭ on the B line and C major A♯ on the A line (got {fSpelling} and {cSpelling}).");
        Assert(MusicKey.Infer(Scale(65, majorSteps)).Spell(67) == (4, 4, 0) && dMajor.Spell(66) == (3, 4, 1)
                && fMajor.Spell(66) == (4, 4, -1) && MusicKey.CMajor.Spell(66) == (3, 4, 1),
            "A note the key already writes is spelled without a sign — G♮ in F major — while a chromatic note leans the way the signature does: F♯ in D major, G♭ in F major.");

        // What a bar really writes: the signature covers the notes it already alters, an accidental holds to the
        // end of its bar, and a natural takes it back.
        NoteEvent Note(int pitch, double start = 0, double duration = .5) => new() { Pitch = pitch, Start = start, Duration = duration };
        var barPlan = SheetLayer.AccidentalPlan([Note(61), Note(60), Note(60), Note(61), Note(66), Note(65)], dMajor, _ => 0, 60);
        Assert(barPlan[0] == SheetLayer.NoteAccidental.None && barPlan[1] == SheetLayer.NoteAccidental.Natural
                && barPlan[2] == SheetLayer.NoteAccidental.None && barPlan[3] == SheetLayer.NoteAccidental.Sharp,
            "C♯ is in D major so it is bare, the C♮ in the same bar takes a natural, the second C♮ is bare again and the C♯ after it takes the sharp back.");
        Assert(barPlan[4] == SheetLayer.NoteAccidental.None && barPlan[5] == SheetLayer.NoteAccidental.Natural,
            "F♯ comes from the signature while an F♮ in the same bar is written with a natural.");
        var nextBar = SheetLayer.AccidentalPlan([Note(61), Note(60), Note(60)], dMajor, index => index == 0 ? 0 : 1, 60);
        Assert(nextBar[1] == SheetLayer.NoteAccidental.Natural && nextBar[2] == SheetLayer.NoteAccidental.None,
            "A bar starts from the signature again, so the first C♮ of each bar is written with a natural and the ones after it in the same bar are not.");
        var hands = SheetLayer.AccidentalPlan([Note(61), Note(60), Note(49)], dMajor, _ => 0, 60);
        Assert(hands[0] == SheetLayer.NoteAccidental.None && hands[1] == SheetLayer.NoteAccidental.Natural && hands[2] == SheetLayer.NoteAccidental.None,
            "The two staves keep separate bars: the natural C♮ in the treble hand does not change how the bass hand is written.");
        Assert(SheetLayer.BarOf(0, [0, 2, 4]) == 0 && SheetLayer.BarOf(1.9, [0, 2, 4]) == 0 && SheetLayer.BarOf(2, [0, 2, 4]) == 1
                && SheetLayer.BarOf(-1, [0, 2, 4]) == -1 && SheetLayer.BarOf(2.5, []) == -1
                && SheetLayer.Downbeats([0, .5, 1, 1.5, 2], 4).SequenceEqual([0, 2])
                && SheetLayer.Downbeats([0, .5, 1, 1.5, 2, 2.5, 3], 4, [0, 4]).SequenceEqual([0, 2])
                && SheetLayer.Downbeats([0, .5], 0).SequenceEqual([0, .5]),
            "A note belongs to the last downbeat at or before it, with nothing before the first one, and the downbeats of a grid are every fourth beat.");

        // ---- Beams and flags: how long a beat is, which notes are short enough to carry a flag, and which of
        // them are joined by a beam instead. All arithmetic over the note list and the song's own beat grid.
        var eighthGrid = new double[] { 0, .5, 1, 1.5, 2 };
        Assert(Math.Abs(SheetLayer.BeatSeconds(eighthGrid) - .5) < .001
                && Math.Abs(SheetLayer.BeatSeconds([0, 1, 2, 4]) - 1) < .001
                && Math.Abs(SheetLayer.BeatSeconds([0, .5, 1, 4]) - .5) < .001
                && Math.Abs(SheetLayer.BeatSeconds([0]) - .5) < .001 && Math.Abs(SheetLayer.BeatSeconds([]) - .5) < .001,
            "The beat of a song is the middle gap of its own grid, and a grid with no gap to measure falls back to half a second.");
        Assert(SheetLayer.Flags(.5, .5) == 0 && SheetLayer.Flags(.25, .5) == 1 && SheetLayer.Flags(.1, .5) == 2 && SheetLayer.Flags(.05, .5) == 3
                && SheetLayer.Flags(.25, 0) == 0 && SheetLayer.Flags(0, .5) == 0,
            $"A quarter note carries no flag, an eighth one, a sixteenth two and anything shorter three, while a song with no beat or a note with no length carries none (got {SheetLayer.Flags(.5, .5)}, {SheetLayer.Flags(.25, .5)}, {SheetLayer.Flags(.1, .5)}, {SheetLayer.Flags(.05, .5)}).");

        // Two eighths inside one beat are one beam; four eighths over two beats are two beams, because a run
        // never leaves the beat it started in.
        var pair = SheetLayer.Beams([Note(60, 0, .25), Note(62, .25, .25)], eighthGrid, 60, MusicKey.CMajor, .5);
        Assert(pair.Count == 1 && pair[0] == new SheetLayer.Beam(0, 1, true, 1),
            $"Two eighth notes in the same beat should share one beam with their stems up (got {pair.Count} run(s)).");
        var four = SheetLayer.Beams([Note(60, 0, .25), Note(62, .25, .25), Note(64, .5, .25), Note(65, .75, .25)], eighthGrid, 60, MusicKey.CMajor, .5);
        Assert(four.Count == 2 && four[0] == new SheetLayer.Beam(0, 1, true, 1) && four[1] == new SheetLayer.Beam(2, 3, true, 1),
            $"Four eighths over two beats should be two beams, not one run across the bar line of the beat (got {four.Count} run(s)).");
        // The shortest note decides how many beams a run carries, so a sixteenth inside a run of eighths still
        // reads as a sixteenth.
        var mixed = SheetLayer.Beams([Note(60, 0, .25), Note(62, .25, .1)], eighthGrid, 60, MusicKey.CMajor, .5);
        Assert(mixed.Count == 1 && mixed[0] == new SheetLayer.Beam(0, 1, true, 1)
                && SheetLayer.Beams([Note(60, 0, .1), Note(62, .25, .1)], eighthGrid, 60, MusicKey.CMajor, .5) is [{ Beams: 2 }],
            "A beam carries as many lines as its shortest note needs, and a run of sixteenths carries two.");
        // Notes low on the bass staff lean their stems the other way.
        var lowPair = SheetLayer.Beams([Note(55, 0, .25), Note(57, .25, .25)], eighthGrid, 60, MusicKey.CMajor, .5);
        Assert(lowPair.Count == 1 && lowPair[0] == new SheetLayer.Beam(0, 1, false, 1),
            $"A beam written high in the bass staff should point its stems down like the notes' own heads (got up={lowPair.FirstOrDefault().Up}).");

        // What breaks a run: a quarter note written between two eighths, a note of the other hand, a note in the
        // next beat, a chord written at the same moment, and a half note however fast the beat looks.
        var broken = new (string What, NoteEvent[] Notes, double Beat, IReadOnlyList<double> Grid)[]
        {
            ("a quarter between two eighths", [Note(60, 0, .25), Note(62, .25, .5), Note(64, .4, .25)], .5, eighthGrid),
            ("a note of the other hand", [Note(72, 0, .25), Note(48, .25, .25)], .5, eighthGrid),
            ("a note in the next beat", [Note(60, .25, .25), Note(62, .5, .25)], .5, eighthGrid),
            ("a chord written at one moment", [Note(60, 0, .25), Note(62, 0, .25)], .5, eighthGrid),
            ("a half note in a slow song", [Note(60, 0, .25), Note(62, .25, 1.5)], 3, new double[] { 0, 3, 6, 9 }),
        };
        foreach (var (what, group, beat, grid) in broken)
            Assert(SheetLayer.Beams(group, grid, 60, MusicKey.CMajor, beat).Count == 0,
                $"A beam has to be broken by {what}: nothing short enough to beam is next to anything else there.");
        Assert(SheetLayer.Beams([Note(60, 0, .25)], eighthGrid, 60, MusicKey.CMajor, .5).Count == 0,
            "A lone eighth note is not a beam: it keeps its own flag.");

        // ---- Rests: a grand staff keeps a voice per hand, so wherever one hand is silent the other hand's staff
        // is written with a rest — one rest for a silence that carries on across the other hand's notes.
        var quietBass = SheetLayer.Rests([Note(60, 0, .5), Note(64, .5, .5)], 60);
        Assert(quietBass.Count == 1 && quietBass[0] == new SheetLayer.RestGap(1, 0, 1),
            $"A melody in the right hand should rest the left hand for as long as it plays ({string.Join(" · ", quietBass.Select(rest => $"{rest.Staff}@{rest.Start}+{rest.Seconds}"))}).");
        var taking = SheetLayer.Rests([Note(60, 0, .5), Note(60, 1, .5), Note(40, .5, .5), Note(40, 1.5, .5)], 60);
        Assert(taking.Count == 4 && taking[0] == new SheetLayer.RestGap(1, 0, .5) && taking[1] == new SheetLayer.RestGap(0, .5, .5)
                && taking[2] == new SheetLayer.RestGap(1, 1, .5) && taking[3] == new SheetLayer.RestGap(0, 1.5, .5),
            "Two hands taking turns should rest whichever one is quiet, each on its own staff.");
        var soloBass = SheetLayer.Rests([Note(48, 0, .5)], 60);
        Assert(SheetLayer.Rests([], 60).Count == 0 && soloBass.Count == 1 && soloBass[0] == new SheetLayer.RestGap(0, 0, .5),
            "A sheet with nothing played writes no rests, and a note in one hand rests the other one over exactly its own length.");
        var heldChord = SheetLayer.Rests([Note(60, 0, 1), Note(64, 0, 1), Note(48, 0, 1), Note(72, 1, .5)], 60);
        Assert(heldChord.Count == 1 && heldChord[0] == new SheetLayer.RestGap(1, 1, .5),
            $"A hand that plays a chord and then stops should rest only after the chord ends ({string.Join(" · ", heldChord.Select(rest => $"{rest.Staff}@{rest.Start}+{rest.Seconds}"))}).");

        // A long silence is written the way a score writes it: one rest per bar of the hand's quiet, and a bar
        // that is quiet from end to end is a whole rest in any meter.
        var barred = SheetLayer.Bars([new SheetLayer.RestGap(1, 0, 4)], new double[] { 0, 1, 2, 3, 4 });
        Assert(barred.Count == 4 && barred.All(rest => rest.Staff == 1 && rest.Whole && Math.Abs(rest.Seconds - 1) < 1e-9)
                && barred.Select(rest => rest.Start).SequenceEqual(new double[] { 0, 1, 2, 3 }),
            $"A hand quiet for four one-second bars should be written as four whole rests, one a bar ({string.Join(" · ", barred.Select(rest => $"{rest.Start}+{rest.Seconds}{(rest.Whole ? "w" : "")}"))}).");
        var spanning = SheetLayer.Bars([new SheetLayer.RestGap(0, .5, 3)], new double[] { 0, 1, 2, 3 });
        Assert(spanning.Count == 4 && !spanning[0].Whole && spanning[0] == new SheetLayer.RestGap(0, .5, .5)
                && spanning[1].Whole && spanning[1] == new SheetLayer.RestGap(0, 1, 1, Whole: true)
                && spanning[2] == new SheetLayer.RestGap(0, 2, 1, Whole: true) && !spanning[3].Whole && spanning[3] == new SheetLayer.RestGap(0, 3, .5),
            $"A silence that starts and ends in the middle of a bar should keep a partial rest at each end and a whole rest for the bars it fills ({string.Join(" · ", spanning.Select(rest => $"{rest.Start}+{rest.Seconds}{(rest.Whole ? "w" : "")}"))}).");
        Assert(SheetLayer.Bars([new SheetLayer.RestGap(1, .25, .5)], new double[] { 0, 1, 2 }).Count == 1
                && SheetLayer.Bars([], new double[] { 0, 1 }).Count == 0
                && SheetLayer.Bars([new SheetLayer.RestGap(1, 0, 4)], new double[] { 0 }).Count == 1,
            "A silence inside one bar, an empty sheet and a grid with a single bar line should each stay as they are.");

        Assert(SheetLayer.Rest(.1, .5) == SheetLayer.RestShape.Sixteenth && SheetLayer.Rest(.25, .5) == SheetLayer.RestShape.Eighth
                && SheetLayer.Rest(.5, .5) == SheetLayer.RestShape.Quarter && SheetLayer.Rest(1, .5) == SheetLayer.RestShape.Half
                && SheetLayer.Rest(2, .5) == SheetLayer.RestShape.Whole && SheetLayer.Rest(9, .5) == SheetLayer.RestShape.Whole
                && SheetLayer.Rest(.25, 0) == SheetLayer.RestShape.Quarter,
            "A rest is counted in beats of the song's grid — a sixteenth up to a quarter of a beat, an eighth up to half, a quarter up to one, a half up to two and a whole rest beyond that — and a live take with no beat to count reads as a quarter rest.");
        Assert(SheetLayer.RestText(SheetLayer.RestShape.Quarter, true) != SheetLayer.RestText(SheetLayer.RestShape.Quarter, false)
                && char.ConvertToUtf32(SheetLayer.RestText(SheetLayer.RestShape.Quarter, true), 0) == 0x1D13D
                && SheetLayer.RestText(SheetLayer.RestShape.Whole, false) == "W" && SheetLayer.RestText(SheetLayer.RestShape.Eighth, false) == "E",
            "A rest is written with the musical glyph when the font has it and with the shape's own initial when it does not.");
        var plan = SheetLayer.Plan([Note(60, 0, .25), Note(62, .25, .25)], eighthGrid, 4, 60, MusicKey.CMajor);
        Assert(plan.Accidentals.Length == 2 && plan.Beams.Count == 1 && plan.Rests.Count == 1 && plan.Ties.Count == 0 && plan.Slurs.Count == 0,
            "The plan of a song should gather the accidentals, the beams, the rests, the ties and the hand-offs in one working-out a renderer can keep for the whole song.");

        // ---- Ties: a note that starts exactly where the same pitch left off is the same written note carried on,
        // so it is joined by a curve and never signed again.
        var tied = SheetLayer.Ties([Note(60, 0, .5), Note(60, .5, .5)], 60);
        Assert(tied.Count == 1 && tied[0] == new SheetLayer.Tie(0, 1),
            $"A note starting where the same pitch left off should be tied to it (got {tied.Count} tie(s)).");
        var chain = SheetLayer.Ties([Note(60, 0, .5), Note(60, .5, .5), Note(60, 1, .5)], 60);
        Assert(chain.Count == 2 && chain[0] == new SheetLayer.Tie(0, 1) && chain[1] == new SheetLayer.Tie(1, 2),
            $"A note carried on twice should be written as two ties, the middle note ending one and starting the next (got {chain.Count}).");
        var chordTies = SheetLayer.Ties([Note(60, 0, .5), Note(64, 0, .5), Note(60, .5, .5), Note(64, .5, .5)], 60);
        Assert(chordTies.Count == 2 && chordTies[0] == new SheetLayer.Tie(0, 2) && chordTies[1] == new SheetLayer.Tie(1, 3),
            "Every pitch of a chord carried on should get its own tie, so a chord is tied chord-wise and not note-wise.");
        Assert(SheetLayer.Ties([Note(60, 0, .5), Note(60, .52, .5)], 60).Count == 1,
            "A played file that leaves twenty milliseconds between the two halves of a note should still be written as one tied note.");
        var notTies = new (string What, NoteEvent[] Notes)[]
        {
            ("a gap wider than the tie allows", [Note(60, 0, .5), Note(60, .531, .5)]),
            ("a gap of silence", [Note(60, 0, .4), Note(60, .6, .5)]),
            ("an overlap", [Note(60, 0, .5), Note(60, .4, .5)]),
            ("a different pitch", [Note(60, 0, .5), Note(64, .5, .5)]),
            ("the two hands", [Note(72, 0, .5), Note(48, .5, .5)]),
            ("another note of the song between them", [Note(60, 0, .25), Note(64, .25, .25), Note(60, .5, .5)]),
        };
        foreach (var (what, candidates) in notTies)
            Assert(SheetLayer.Ties(candidates, 60).Count == 0, $"Two notes are not tied when {what} separates them.");
        Assert(SheetLayer.Ties([Note(60, 0, .5)], 60).Count == 0 && SheetLayer.Ties([], 60).Count == 0
                && SheetLayer.TieUnder(3) && !SheetLayer.TieUnder(7),
            "A lone note and an empty song have no ties, and a tie leans under the note when its stem points up and over it when the stem points down.");

        // A tie carries the sign it started with, even across a bar line: in D major an F♮ tied into the next bar
        // is written bare, and the F♯ after it still needs its sharp because the tie left F♮ in force.
        var carriedNotes = new[] { Note(65, 0, .5), Note(65, .5, .5), Note(66, 1, .5) };
        var carriedBars = new Func<int, int>(index => index == 0 ? 0 : 1);
        var carriedTies = SheetLayer.Ties(carriedNotes, 60);
        var carriedPlan = SheetLayer.AccidentalPlan(carriedNotes, dMajor, carriedBars, 60, new HashSet<int>(carriedTies.Select(tie => tie.Second)));
        var unsignedPlan = SheetLayer.AccidentalPlan(carriedNotes, dMajor, carriedBars, 60);
        Assert(carriedTies.Count == 1 && carriedPlan.SequenceEqual([SheetLayer.NoteAccidental.Natural, SheetLayer.NoteAccidental.None, SheetLayer.NoteAccidental.Sharp])
                && unsignedPlan.SequenceEqual([SheetLayer.NoteAccidental.Natural, SheetLayer.NoteAccidental.Natural, SheetLayer.NoteAccidental.Sharp]),
            $"A tie should carry its sign into the bar it reaches rather than signing the note again, while the note after it is still signed ({string.Join(", ", carriedPlan)}).");

        // ---- Hand-offs: a line that stops in one hand and continues in the other is one phrase, so the sheet
        // draws a curve over the change of hands instead of leaving two notes that read as separate thoughts.
        var handedOver = SheetLayer.Slurs([Note(60, 0, .5), Note(55, .5, .5)], 60);
        Assert(handedOver.Count == 1 && handedOver[0] == new SheetLayer.Slur(0, 1),
            $"A note that takes the line over exactly where the other hand left it should be a hand-off (got {handedOver.Count}).");
        Assert(SheetLayer.Slurs([Note(60, 0, .5), Note(64, .5, .5)], 60).Count == 0
                && SheetLayer.Slurs([Note(60, 0, .6), Note(55, .5, .5)], 60).Count == 0,
            "The same hand is not a hand-off and neither is a pair that overlaps: a hand-off is a line leaving one hand exactly when the other takes it.");
        Assert(SheetLayer.Slurs([Note(60, 0, .5), Note(55, .52, .5)], 60).Count == 1
                && SheetLayer.Slurs([Note(60, 0, .5), Note(55, .531, .5)], 60).Count == 0,
            "A hand-off may leave the same few milliseconds of air a tie tolerates and no more.");
        Assert(SheetLayer.Slurs([Note(60, 0, .5), Note(48, .5, .5)], 60).Count == 0
                && SheetLayer.Slurs([Note(72, 0, .5), Note(55, .5, .5)], 60).Count == 0,
            "Two registers an octave or more apart are a bass line and a tune rather than one line changing hands.");
        var handOffChain = SheetLayer.Slurs([Note(60, 0, .5), Note(55, .5, .5), Note(57, 1, .5)], 60);
        Assert(handOffChain.Count == 1 && handOffChain[0] == new SheetLayer.Slur(0, 1),
            $"A line handed over once is one hand-off: the hand that took it keeps it while its own notes run on (got {handOffChain.Count}).");
        Assert(SheetLayer.Slurs([Note(60, 0, .5), Note(64, 0, .5), Note(55, .5, .5)], 60).Count == 0
                && SheetLayer.Slurs([Note(60, 0, .5), Note(55, .5, .5), Note(57, .5, .5)], 60).Count == 0,
            "A chord is a block of notes rather than a line, so a chord neither hands one over nor takes one.");
        Assert(SheetLayer.Slurs([Note(60, 0, .5), Note(59, .5, .5)], 60).Count == 1 && SheetLayer.Slurs([Note(60, 0, .5)], 60).Count == 0
                && SheetLayer.Slurs([], 60).Count == 0,
            "The same pitch written in the other hand is carried across the staves by a hand-off, since a tie cannot cross them, and a song with nothing to hand over has none.");
        var handedPlan = SheetLayer.Plan([Note(60, 0, .5), Note(55, .5, .5)], eighthGrid, 4, 60, MusicKey.CMajor);
        Assert(handedPlan.Slurs.Count == 1 && handedPlan.Slurs[0] == new SheetLayer.Slur(0, 1),
            "The plan of a song should carry the hand-offs a caller would work out for itself.");

        // ---- Chords: the notes written at one moment in one hand are one event, so a chord is one column of heads
        // with one stem, and the sheet's own reading of the song is kept in the plan for whoever draws it.
        var chordNotes = new[] { Note(60, 0, .5), Note(64, 0, .5), Note(67, 0, .5), Note(72, .5, .5) };
        var chordGroups = SheetLayer.Chords(chordNotes, 60);
        Assert(chordGroups.Count == 2 && chordGroups[0].SequenceEqual([0, 1, 2]) && chordGroups[1].SequenceEqual([3]),
            $"The three notes of a chord should be one group and the melody note after it its own ({string.Join(" · ", chordGroups.Select(group => "[" + string.Join(",", group) + "]"))}).");
        var splitHands = new[] { Note(72, 0, .5), Note(48, 0, .5) };
        Assert(SheetLayer.Chords(splitHands, 60).Count == 2 && SheetLayer.Chords(splitHands, 60)[0].SequenceEqual([0])
                && SheetLayer.Chords([], 60).Count == 0 && SheetLayer.Chords([Note(60, 0, .25), Note(62, .25, .25)], 60).Count == 2,
            "Two notes written at one moment belong to the same group only when they are in the same hand: a chord is per hand, and single notes are groups of one.");
        var streams = SheetLayer.Streams(chordNotes, 60);
        Assert(streams.Length == 2 && streams[0].Count == 2 && streams[0][0].SequenceEqual([0, 1, 2]) && streams[0][1].SequenceEqual([3])
                && streams[1].Count == 0,
            "A hand's stream should read as one event per written moment, each event carrying the notes of its chord in pitch order.");
        var interleaved = SheetLayer.Streams([Note(60, 0, .5), Note(48, 0, .5), Note(64, 1, .5), Note(52, 1, .5)], 60);
        Assert(interleaved[0].Count == 2 && interleaved[1].Count == 2 && interleaved[0][1].SequenceEqual([2]) && interleaved[1][0].SequenceEqual([1]),
            "Two hands playing at once should be two streams: one event per hand, in the song's own order.");
        var plannedGroups = SheetLayer.Plan(chordNotes, eighthGrid, 4, 60, MusicKey.CMajor).Chords;
        Assert(plannedGroups.Count == chordGroups.Count
                && plannedGroups.Zip(chordGroups).All(matched => matched.First.SequenceEqual(matched.Second)),
            "The plan of a song should carry the same grouping a caller would compute for itself.");

        // A chord is beamed like one note: the run reaches into the chord after it, and a chord on its own is never
        // a beam across itself.
        var chordBeam = SheetLayer.Beams([Note(60, 0, .25), Note(64, 0, .25), Note(62, .25, .25)], eighthGrid, 60, MusicKey.CMajor, .5);
        Assert(chordBeam.Count == 1 && chordBeam[0] == new SheetLayer.Beam(0, 2, true, 1),
            $"A beamed chord should join the next note with a single run reaching the note it ends on (got {chordBeam.Count} run(s), last {chordBeam.FirstOrDefault().Last}).");
        var twoChords = SheetLayer.Beams([Note(60, 0, .25), Note(64, 0, .25), Note(62, .25, .25), Note(65, .25, .25)], eighthGrid, 60, MusicKey.CMajor, .5);
        Assert(twoChords.Count == 1 && twoChords[0] == new SheetLayer.Beam(0, 3, true, 1) && twoChords[0].Beams == 1,
            "Two chords of eighths in one beat should share one beam, and a run of sixteenths should carry two lines.");
        Assert(SheetLayer.Beams([Note(48, 0, .25), Note(52, 0, .25)], eighthGrid, 60, MusicKey.CMajor, .5).Count == 0
                && SheetLayer.Beams([Note(60, 0, .25), Note(64, 0, .25), Note(62, .25, .5)], eighthGrid, 60, MusicKey.CMajor, .5).Count == 0,
            "A chord on its own is one column of heads, not a beam across itself, and a quarter note after a chord still breaks the run.");

        var area = SheetLayer.Band(1280, 480, 34);
        Assert(Math.Abs(area.Height - 163.2) < .01 && area.Width > 1100 && area.X > 0 && area.Bottom < 240,
            $"The staff band should stay in the upper part of the stage and scale with it (got {area}).");
        Assert(Math.Abs(SheetLayer.WindowStart(10, 8) - 8) < .001 && Math.Abs(SheetLayer.NoteX(8, 8, 8, area) - area.X) < .001
            && Math.Abs(SheetLayer.NoteX(12, 8, 8, area) - (area.X + area.Width / 2)) < .001,
            "The playhead should sit a quarter of the window in from the left, so eight seconds of music fill the band from its own start.");
        Assert(SheetLayer.StaffBottom(area, 6, 1) > SheetLayer.StaffBottom(area, 6, 0) + 60, "The bass staff should be drawn below the treble staff, not over it.");
        Assert(SheetLayer.HollowHead(1.5) && !SheetLayer.HollowHead(.5) && SheetLayer.HasStem(.5) && !SheetLayer.HasStem(1.5)
            && SheetLayer.StemUp(3) && !SheetLayer.StemUp(6),
            "Half notes are written hollow without a stem, shorter notes filled with a stem that points away from the middle of the staff.");

        var clefTreble = SheetLayer.ClefText(0); var clefBass = SheetLayer.ClefText(1);
        Assert(clefTreble != clefBass && SheetLayer.MusicGlyphsAvailable == (clefTreble.Length == 2 && char.ConvertToUtf32(clefTreble, 0) == 0x1D11E),
            $"A staff should be labelled with its clef: the musical glyph when the font has it, otherwise the staff's letter (drew “{clefTreble}”/“{clefBass}”).");

        var note = new NoteEvent { Pitch = 60, Start = 1, Duration = .5 };
        var ink = Color.FromRgb(243, 229, 255); var accent = Color.FromRgb(198, 110, 255); var dim = Color.FromRgb(150, 150, 150);
        Assert(SheetLayer.NoteColour(note, .2, dim, ink, accent) == ink && SheetLayer.NoteColour(note, 1.2, dim, ink, accent) == accent,
            "A written note should use the plain ink of the staff until the playhead reaches it, then the accent while it sounds.");
        note.Played = true;
        Assert(SheetLayer.NoteColour(note, 4, dim, ink, accent) == dim, "A note already played should be written in the dimmed ink so the sheet can be read back.");
        note.Missed = true;
        Assert(SheetLayer.NoteColour(note, 4, dim, ink, accent) == Color.FromRgb(255, 118, 130), "A missed note should be marked in the miss colour.");

        // The layer rendered once with a song and once without: what a live performance sees, where there is no
        // beat grid and therefore no bar lines, still has to produce both staves.
        var song = new List<NoteEvent>
        {
            new() { Pitch = 72, Start = .5, Duration = 1.5 }, new() { Pitch = 67, Start = 1, Duration = .5 },
            new() { Pitch = 48, Start = 1, Duration = 1 }, new() { Pitch = 43, Start = 1.5, Duration = .25 }
        };
        var beats = new List<double> { 0, .5, 1, 1.5, 2, 2.5, 3, 3.5 };
        var withSong = new DrawingVisual();
        using (var dc = withSong.RenderOpen())
            SheetLayer.Draw(dc, area, song, 1.2, 60, MusicKey.CMajor, beats, 4, 8, ink, accent, 1, 1);
        var empty = new DrawingVisual();
        using (var dc = empty.RenderOpen())
            SheetLayer.Draw(dc, area, [], 0, 60, MusicKey.CMajor, [], 4, 8, ink, accent, 1, 1);
        Assert(withSong.Drawing.Bounds.Height > 120 && withSong.Drawing.Bounds.Width > area.Width * .8 && empty.Drawing.Bounds.Height > 120,
            $"The sheet should paint both staves with and without a song to read ({withSong.Drawing.Bounds}, {empty.Drawing.Bounds}).");

        // The dock row the user flips, then the same layer through the stage's own song state.
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        Assert(toggles.TryGetValue(nameof(PianoVisualSettings.ShowSheet), out var sheetToggle),
            "The Layers card should expose the sheet as a layer the user can switch on.");
        var wasShowing = visualSettings.ShowSheet;
        sheetToggle!.IsChecked = true;
        Assert(visualSettings.ShowSheet, "Switching the sheet layer on should reach the renderer settings, not only the checkbox.");
        stage.SetSheet(beats, 4);
        stage.SetState(song, 1.2, true, new HashSet<int>());
        // The song's own split decides the staves here, as it does for a MusicXML score.
        var previousSplit = visualSettings.HandSplitPitch;
        visualSettings.HandSplitPitch = 60; stage.SetVisualSettings(visualSettings);
        var stageSheet = new DrawingVisual();
        using (var dc = stageSheet.RenderOpen()) Invoke(stage, "DrawSheet", dc, 1280d, 480d);
        Assert(stageSheet.Drawing.Bounds.Height > 100, $"The stage should draw the sheet from its own notes and beat grid (bounds {stageSheet.Drawing.Bounds}).");
        // The key of the song is worked out once, and a signature really puts ink at the head of both staves.
        var cachedKey = (MusicKey)Field(stage, "_sheetKey");
        var sentinel = MusicKey.Infer(Scale(65, majorSteps));
        SetField(stage, "_sheetKey", sentinel);
        using (var dc = stageSheet.RenderOpen()) Invoke(stage, "DrawSheet", dc, 1280d, 480d);
        Assert(((MusicKey)Field(stage, "_sheetKey")) == sentinel && ReferenceEquals(Field(stage, "_sheetKeyNotes"), Field(stage, "_notes")),
            "The key of a song should be worked out once per song rather than once per frame, so another render keeps the key it already has.");
        SetField(stage, "_sheetKey", cachedKey);
        // The working-out behind the drawing — accidentals, beams and rests — is kept between frames and rebuilt
        // only when the song, its grid, the hand split or the key changes. The key the sentinel replaced has to be
        // rendered once before the plan below is the one being kept.
        using (var dc = stageSheet.RenderOpen()) Invoke(stage, "DrawSheet", dc, 1280d, 480d);
        var cachedPlan = Field(stage, "_sheetCache");
        using (var dc = stageSheet.RenderOpen()) Invoke(stage, "DrawSheet", dc, 1280d, 480d);
        Assert(cachedPlan is not null && ReferenceEquals(Field(stage, "_sheetCache"), cachedPlan),
            "The sheet's working-out should be kept between frames rather than rebuilt for every one of them.");
        visualSettings.HandSplitPitch = 72; stage.SetVisualSettings(visualSettings);
        using (var dc = stageSheet.RenderOpen()) Invoke(stage, "DrawSheet", dc, 1280d, 480d);
        Assert(!ReferenceEquals(Field(stage, "_sheetCache"), cachedPlan),
            "Moving the hand split should rebuild the sheet's working-out, because the beams and the rests follow the hands.");
        visualSettings.HandSplitPitch = 60; stage.SetVisualSettings(visualSettings);
        // The signature really takes ink: draw an empty band in both keys and count the pixels inside the strip
        // between the clef and the first room the music could use, where nothing but the signature can land.
        // The band paints its own translucent background, so "not transparent" would count every pixel of the
        // strip in both keys; the count is of pixels dark opaquely enough to be ink on top of that background.
        var stripLeft = (int)Math.Ceiling(area.X + SheetLayer.ClefSpace(area) + 3);
        var stripRight = (int)Math.Ceiling(area.X + SheetLayer.LeftInset(area, dMajor) - 3);
        int LitPixels(MusicKey key)
        {
            var width = (int)Math.Ceiling(area.Width); var height = (int)Math.Ceiling(area.Height);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) SheetLayer.Draw(dc, area, [], 0, 60, key, [], 4, 8, ink, accent, 1, 1);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            var lit = 0;
            for (var y = 0; y < height; y++)
                for (var x = stripLeft; x < stripRight; x++)
                    if (pixels[(y * width + x) * 4 + 3] > 200) lit++;
            return lit;
        }
        var plainInk = LitPixels(MusicKey.CMajor); var signedInk = LitPixels(dMajor);
        Assert(plainInk == 0 && signedInk > 0,
            $"A key signature should put ink between the clef and the music: C major writes nothing there, D major writes two sharps on each staff (counted {plainInk} and {signedInk} inked pixels).");
        // The beam is really drawn: a pair of eighths inside one beat carries a beam across both stems, while the
        // same pair written either side of a beat carries two flags instead — and the beam is far more ink.
        int Ink(IReadOnlyList<NoteEvent> content, SheetLayer.SheetPlan? painting = null)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) SheetLayer.Draw(dc, area, content, 0, 60, MusicKey.CMajor, beats, 4, 8, ink, accent, 1, 1, painting);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(area.Width), (int)Math.Ceiling(area.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var lit = 0;
            for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] > 200) lit++;
            return lit;
        }
        var beamedInk = Ink([Note(60, 0, .25), Note(62, .25, .25)]);
        var flaggedInk = Ink([Note(60, .25, .25), Note(62, .5, .25)]);
        Assert(beamedInk > flaggedInk,
            $"A beamed pair should draw a beam across its stems rather than two flags ({beamedInk} inked pixels beamed, {flaggedInk} flagged).");

        // The rests are really drawn, and only where they belong: the same drawing with the silences taken out of
        // its plan carries less ink, all of it on the staff of the hand that does not play.
        var soloSong = new NoteEvent[] { Note(72, 1, .5) };
        var soloPlan = SheetLayer.Plan(soloSong, beats, 4, 60, MusicKey.CMajor);
        Assert(soloPlan.Rests.Count == 1 && soloPlan.Rests[0] == new SheetLayer.RestGap(1, 1, .5),
            "The plan of a single right-hand note should rest the left hand for exactly as long as the note sounds.");
        var restInk = Ink(soloSong, soloPlan) - Ink(soloSong, soloPlan with { Rests = Array.Empty<SheetLayer.RestGap>() });
        Assert(restInk > 0, $"A rest should put ink on the staff of the quiet hand ({restInk} inked pixels came from the rest).");

        // The same rule reaches the plan a renderer keeps: a hand quiet for two bars of 2/4 is written as two
        // whole rests a bar apart, and a song whose left hand never plays still writes them for every bar.
        var twoFour = new double[] { 0, .5, 1, 1.5, 2 };
        var quietBars = SheetLayer.Plan(
            [Note(72, 0, .5), Note(72, .5, .5), Note(72, 1, .5), Note(72, 1.5, .5)], twoFour, 2, 60, MusicKey.CMajor);
        Assert(quietBars.Rests.Count == 2
                && quietBars.Rests.All(rest => rest.Staff == 1 && rest.Whole && Math.Abs(rest.Seconds - 1) < 1e-9)
                && Math.Abs(quietBars.Rests[0].Start) < 1e-9 && Math.Abs(quietBars.Rests[1].Start - 1) < 1e-9,
            $"The left hand of a song in 2/4 playing eighths with the right should rest bar by bar, a whole rest each ({string.Join(" · ", quietBars.Rests.Select(rest => $"{rest.Start}+{rest.Seconds}{(rest.Whole ? "w" : "")}"))}).");

        // The tie is really drawn: the same two notes carry the curve's ink when they are tied and none of it when
        // they are not, and the curve is the only difference between the two drawings.
        var tieSong = new NoteEvent[] { Note(60, 0, .5), Note(60, .5, .5) };
        var tiePlan = SheetLayer.Plan(tieSong, beats, 4, 60, MusicKey.CMajor);
        var tieInk = Ink(tieSong, tiePlan) - Ink(tieSong, tiePlan with { Ties = Array.Empty<SheetLayer.Tie>() });
        Assert(tiePlan.Ties.Count == 1 && tieInk > 0,
            $"A tie should draw a curve between the two heads, and nothing else about the drawing should change ({tieInk} inked pixels came from the tie).");

        // The hand-off is really drawn: a line that crosses the split carries the curve's ink, and none of it is
        // drawn when the plan is handed over without the hand-off.
        var handSong = new NoteEvent[] { Note(60, 0, .5), Note(55, .5, .5) };
        var handPlan = SheetLayer.Plan(handSong, beats, 4, 60, MusicKey.CMajor);
        var handInk = Ink(handSong, handPlan) - Ink(handSong, handPlan with { Slurs = Array.Empty<SheetLayer.Slur>() });
        Assert(handPlan.Slurs.Count == 1 && handInk > 0,
            $"A hand-off should draw one curve across the staves, and nothing else about the drawing should change ({handInk} inked pixels came from the hand-off).");

        // The chord is stemmed once: the same three heads carry more ink when the plan hands them over as three
        // separate groups, which is the drawing a sheet makes when it does not know they are a chord.
        var chordSong = new NoteEvent[] { Note(60, 0, .25), Note(64, 0, .25), Note(67, 0, .25), Note(72, .25, .25) };
        var chordPlan = SheetLayer.Plan(chordSong, eighthGrid, 4, 60, MusicKey.CMajor);
        var ungrouped = chordPlan with { Chords = [.. Enumerable.Range(0, chordSong.Length).Select(index => (IReadOnlyList<int>)new[] { index })] };
        var chordInk = Ink(chordSong, chordPlan); var separateInk = Ink(chordSong, ungrouped);
        Assert(chordPlan.Chords.Count == 2 && chordInk < separateInk,
            $"A chord should be drawn with one stem for the whole column of heads ({chordInk} inked pixels) rather than one stem per head ({separateInk}).");

        Assert(SheetLayer.SignatureWidth(MusicKey.CMajor, SheetLayer.StaffGap(area)) == 0
                && Math.Abs(SheetLayer.LeftInset(area, dMajor) - SheetLayer.LeftInset(area, MusicKey.CMajor) - SheetLayer.SignatureWidth(dMajor, SheetLayer.StaffGap(area))) < .001
                && SheetLayer.LeftInset(area, dMajor) > SheetLayer.ClefSpace(area),
            "A song with a signature writes its music after the signs, and a song with no signature takes no room at all in front of it.");
        Assert(ReferenceEquals(Field(stage, "_beats"), beats), "The stage should keep the grid the song was loaded with for the sheet's bar lines.");
        visualSettings.HandSplitPitch = previousSplit; stage.SetVisualSettings(visualSettings);
        sheetToggle.IsChecked = wasShowing;
        stage.SetSheet([], 4);
        stage.ClearTransient();
        Results.Add($"PASS Sheet layer: written pitch and staff placement from the hand split, ledger lines outside the staff, hollow and stemmed heads, the playhead window, the clef by font, the note colours, the key and its signature, beams and flags, the rests of the quiet hand, the ties that carry a note on, the hand-offs between the hands, the chords stemmed once, the streams a hand reads as, the plan kept between frames, and both staves drawn from the stage's own song and grid (glyphs: {(SheetLayer.MusicGlyphsAvailable ? "musical" : "letters")}).");
    }

    /// <summary>
    /// The ghost and the fourteen-day chart. Both are arithmetic over the history file, so the check writes a
    /// history with known days and known graded notes and then reads the picture back: the day the chart ends
    /// on, the runs bucketed per local day, the days with nothing played still present, the geometry of a bar
    /// and of a dot, and the dock page rebuilt from the same model. The last part drives the app path, where a
    /// take's graded notes are collected note by note and written when the transport stops.
    /// </summary>
    private static void VerifyPracticeGhostAndChart(MainWindow window)
    {
        var chart = (Canvas)window.FindName("HistoryChartHost");
        var chartLabel = (TextBlock)window.FindName("HistoryChartLabel");
        var ghostHost = (StackPanel)window.FindName("HistoryGhostHost");
        var ghostLabel = (TextBlock)window.FindName("HistoryGhostLabel");

        // The arithmetic first: a bar is linear in accuracy with a floor, and a dot is the note's place in the
        // song across and its pitch up.
        Assert(Math.Abs(PracticeChart.BarHeight(100, 52, 3) - 52) < .001 && Math.Abs(PracticeChart.BarHeight(50, 52, 3) - 26) < .001
                && PracticeChart.BarHeight(1, 52, 3) == 3 && PracticeChart.BarHeight(0, 52, 3) == 0,
            "A day's bar should be as tall as that day's accuracy and a day that was practised at all should still show a sliver.");
        Assert(PracticeChart.PitchWindow([]) == (48, 72) && PracticeChart.PitchWindow([new PracticePoint(1, 60, true)]) == (53, 67)
                && PracticeChart.PitchWindow([new PracticePoint(1, 40, true), new PracticePoint(2, 90, false)]) == (39, 91),
            "The ghost's pitch axis should be the notes' own range, padded, and at least an octave so a single note has somewhere to sit.");
        var area = new Rect(0, 0, 200, 40);
        var low = PracticeChart.Point(0, 40, 10, 40, 90, area); var high = PracticeChart.Point(10, 90, 10, 40, 90, area);
        var middle = PracticeChart.Point(5, 65, 10, 40, 90, area);
        Assert(Math.Abs(low.X - PracticeChart.DotRadius) < .001 && Math.Abs(high.X - (area.Right - PracticeChart.DotRadius)) < .001
                && high.Y < middle.Y && middle.Y < low.Y && Math.Abs(middle.X - area.Width / 2) < .001,
            $"A graded note should be drawn where it was played and where it sits in pitch ({low}, {middle}, {high}).");
        Assert(PracticeChart.Point(5, 65, 0, 40, 90, area).X == PracticeChart.DotRadius,
            "A take with no length — a live run — should draw every note at the left edge rather than dividing by zero.");

        // A history with known days: two runs today, one yesterday, and one a month ago that the window leaves
        // out of the chart while the file still keeps it.
        PracticeHistory.Clear();
        var today = DateTime.Now.Date;
        static PracticeRun Run(string song, string path, DateTime day, int hits, int misses, params (double At, int Pitch, bool Hit)[] ghost) =>
            new(song, path, day.ToUniversalTime(), hits, misses, 0)
            {
                Ghost = ghost.Length == 0 ? null : ghost.Select(point => new PracticePoint(point.At, point.Pitch, point.Hit)).ToList()
            };
        Directory.CreateDirectory(Path.GetDirectoryName(PracticeHistory.FilePath)!);
        File.WriteAllLines(PracticeHistory.FilePath,
        [
            JsonSerializer.Serialize(Run("Alpha", @"C:\songs\alpha.mid", today.AddDays(-30), 1, 1)),
            JsonSerializer.Serialize(Run("Alpha", @"C:\songs\alpha.mid", today.AddDays(-1), 2, 2,
                (0, 48, true), (1, 50, false), (2, 52, false), (3, 55, true))),
            // Anchored at a safe hour of the day: a run stamped "two hours ago" would fall on yesterday when
            // the check happens to run just after midnight.
            JsonSerializer.Serialize(Run("Alpha", @"C:\songs\alpha.mid", today.AddHours(9), 2, 1, (0.5, 60, true), (1, 64, false), (1.5, 67, true))),
            JsonSerializer.Serialize(Run("Alpha", @"C:\songs\alpha.mid", today.AddHours(12), 1, 1, (0, 60, false), (2, 72, true))),
        ]);
        PracticeHistory.Reload();
        var days = PracticeHistory.Daily(PracticeHistory.ChartDays, DateTime.Now);
        Assert(PracticeChart.RowCaption(2, 1) == Loc.F("{0} hit · {1} missed · {2:0.#}%", 2, 1, 200.0 / 3)
                && PracticeChart.RowCaption(0, 0) == Loc.F("{0} hit · {1} missed · {2:0.#}%", 0, 0, 0.0),
            "A ghost row's caption should print the take's own hits, misses and accuracy.");
        Assert(days.Count == PracticeHistory.ChartDays && days[^1].Day == today && days[0].Day == today.AddDays(1 - PracticeHistory.ChartDays),
            "The chart should cover the requested number of days, oldest first and ending on today.");
        // Today: 2 + 1 hits and 1 + 1 misses across its two runs.
        Assert(days[^1] is { Runs: 2, Hits: 3, Misses: 2 } && Math.Abs(days[^1].Accuracy - 60) < .01,
            $"Today's row should add up every run of the day (got {days[^1]}).");
        Assert(days[^2] is { Runs: 1, Hits: 2, Misses: 2 } && days.Take(days.Count - 2).All(day => day.Runs == 0 && day.Accuracy == 0),
            "Days without a run should still be rows, with zero runs and no accuracy.");
        Assert(PracticeHistory.Runs.Count == 4 && days.Sum(day => day.Runs) == 3,
            "A run older than the window should stay out of the chart while the history file still keeps it.");
        // The window holds 5 hits and 4 misses, so its average counts notes, not days. AverageAccuracy is a
        // percentage, so the expected value is 100 × 5/9 and not the bare fraction.
        Assert(Math.Abs(PracticeHistory.AverageAccuracy(days) - 100.0 * 5 / 9) < .01
                && PracticeHistory.ChartCaption(days) == Loc.F("Accuracy by day: {0} runs · {1:0.#}% average", 3, PracticeHistory.AverageAccuracy(days)),
            $"The window's caption should name its runs and count every note graded in it once ({PracticeHistory.ChartCaption(days)}).");

        // The dock reads the same model.
        Invoke(window, "RefreshPracticeHistory");
        var bars = chart.Children.OfType<Rectangle>().ToList();
        var accent = ((SolidColorBrush)window.FindResource("AccentBrush")).Color; var track = ((SolidColorBrush)window.FindResource("TrackBrush")).Color;
        Assert(bars.Count == PracticeHistory.ChartDays && chartLabel.Text == PracticeHistory.ChartCaption(days),
            $"The History page should draw one bar per day and print the caption of the window ({bars.Count} bars).");
        Assert(Math.Abs(bars[^1].Height - PracticeChart.BarHeight(60, 52, 3)) < .001 && bars[0].Height == 0
                && bars[^1].Fill is SolidColorBrush filled && filled.Color == accent && bars[0].Fill is SolidColorBrush blank && blank.Color == track,
            "Today's bar should be as tall as today's accuracy, and a day with nothing played should be a flat track-coloured sliver.");

        // The ghost of the open song: best on top, latest below, one dot per graded note.
        SetField(window, "_songPath", @"C:\songs\alpha.mid");
        Invoke(window, "RefreshPracticeHistory");
        var canvases = ghostHost.Children.OfType<Canvas>().ToList();
        var missColour = ((SolidColorBrush)window.FindResource("DangerBrush")).Color;
        Assert(canvases.Count == 2 && ghostHost.Children.OfType<TextBlock>().Count() == 2
                && ghostLabel.Text == Loc.T("Each dot is a graded note of the take: its place in the song and its pitch. Cyan landed, red was missed, and the rows share their axes."),
            $"Two takes of the same song should be drawn as two rows with a caption each (drew {canvases.Count}).");
        Assert(canvases[0].Children.Count == 3 && canvases[1].Children.Count == 2
                && canvases[0].Children.OfType<Ellipse>().Count(dot => dot.Fill is SolidColorBrush brush && brush.Color == missColour) == 1,
            "Each row should hold one dot per graded note the run kept, in the colour of whether it landed.");
        Assert(Math.Abs(canvases[0].Width - MainWindow.GhostWidth) < .001 && canvases[0].Height > 0
                && canvases[0].Children.OfType<Ellipse>().All(dot => Canvas.GetLeft(dot) >= 0 && Canvas.GetTop(dot) >= 0),
            "Every dot of the ghost should sit inside its row, on the axes both rows share.");

        // One take is not a comparison, and a song never played has nothing to draw.
        PracticeHistory.Clear();
        PracticeHistory.Record("Alpha", @"C:\songs\alpha.mid", 1, 1, 2, [new PracticePoint(1, 60, true), new PracticePoint(2, 62, false)]);
        Invoke(window, "RefreshPracticeHistory");
        Assert(ghostHost.Children.OfType<Canvas>().Count() == 1
                && ghostHost.Children.OfType<TextBlock>().Single().Text.StartsWith(Loc.T("The only take"), StringComparison.Ordinal),
            "A single take should be drawn on its own, labelled as the only one, instead of pretending to compare it with itself.");
        SetField(window, "_songPath", @"C:\songs\beta.mid");
        Invoke(window, "RefreshPracticeHistory");
        Assert(ghostHost.Children.Count == 0 && ghostLabel.Text == Loc.T("No take of this song has graded notes recorded yet, so there is no ghost to draw. Play it once and every graded note is kept for the next time."),
            "A song that has never been played should print why there is no ghost instead of an empty box.");

        // The app path: notes are collected as they are graded, written with the run, and forgotten when the
        // score restarts.
        PracticeHistory.Clear();
        SetField(window, "_songPath", @"C:\songs\alpha.mid"); SetField(window, "_songLabel", "Alpha");
        SetField(window, "_hits", 3); SetField(window, "_misses", 1); SetField(window, "_bestStreak", 2); SetField(window, "_playing", true);
        Invoke(window, "RecordPracticeNote", true, 60, 1.5);
        Invoke(window, "RecordPracticeNote", false, 64, 2.0);
        Invoke(window, "RecordPracticeNote", true, 67, 2.5);
        static IReadOnlyList<PracticePoint> Collected(MainWindow target) =>
            (IReadOnlyList<PracticePoint>)target.GetType().GetProperty("PracticeGhost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;
        Assert(Collected(window) is [{ At: 1.5, Pitch: 60, Hit: true }, { At: 2, Pitch: 64, Hit: false }, { At: 2.5, Pitch: 67, Hit: true }],
            "A graded note should be collected with its place in the song and its pitch, in the order the notes were scored.");
        Invoke(window, "Stop");
        var recorded = PracticeHistory.Runs[0];
        Assert(recorded is { Song: "Alpha", Hits: 3, Misses: 1 } && recorded.Ghost is { Count: 3 } && recorded.Ghost[1] is { Pitch: 64, Hit: false },
            "Stopping a take should write its graded notes as the ghost of the run.");
        Invoke(window, "ResetScore");
        Assert(Collected(window).Count == 0, "Restarting the score should forget the ghost so the next take starts from zero.");

        PracticeHistory.Record("Long", "", 1, 0, 1, [.. Enumerable.Range(0, PracticeHistory.GhostCapacity + 40).Select(index => new PracticePoint(index, 60, true))]);
        Assert(PracticeHistory.Runs[0].Ghost is { Count: PracticeHistory.GhostCapacity },
            $"A take should keep at most {PracticeHistory.GhostCapacity} graded notes so the history file stays bounded.");
        PracticeHistory.Reload();
        Assert(PracticeHistory.Runs[0].Ghost is { Count: PracticeHistory.GhostCapacity } && PracticeHistory.Runs[0].Ghost![^1].At == PracticeHistory.GhostCapacity - 1,
            "The ghost should survive a reload with the notes in the order they were graded.");
        // A line written before the ghost existed has no such property at all, which is what an older file holds.
        File.AppendAllText(PracticeHistory.FilePath,
            $"{{\"Song\":\"Old\",\"SongPath\":\"C:\\\\songs\\\\old.mid\",\"PlayedUtc\":\"{DateTime.UtcNow:O}\",\"Hits\":2,\"Misses\":0,\"BestStreak\":2}}{Environment.NewLine}");
        PracticeHistory.Reload();
        Assert(PracticeHistory.Runs[0] is { Song: "Old", Ghost: null } && PracticeHistory.Runs.Any(run => run.Ghost is { Count: PracticeHistory.GhostCapacity }),
            "A run written before the ghost existed should load without one while the ghosted runs keep theirs.");
        Invoke(window, "RefreshPracticeHistory");
        PracticeHistory.Clear(); SetField(window, "_songPath", ""); SetField(window, "_songLabel", ""); Invoke(window, "RefreshPracticeHistory");
        Results.Add("PASS Practice ghost and chart: a bar per day for the last two weeks with days that were not practised still on the axis and the window's own average, and the best and latest takes of the open song drawn dot by dot from the notes each run graded, written when the transport stops, bounded at the cap, and readable back with the oldest runs that never had a ghost.");
    }

    /// <summary>
    /// The song library of a folder: a scan that finds every readable file under it, facts read from the
    /// files themselves, a cache that only re-reads what changed, tags that survive a rescan, the search over
    /// titles and tags, and the watcher that makes the list follow the disk. The Play dialog is then asked for
    /// the same list through the controls a user touches.
    /// </summary>
    private static void VerifySongFolderLibrary(MainWindow window)
    {
        var folder = Path.Combine(Path.GetTempPath(), "keyflow-verify-library-" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(folder, "Scales");
        Directory.CreateDirectory(sub);
        try
        {
            var etude = Path.Combine(folder, "Etude.mid");
            File.WriteAllBytes(etude, CreateFormatOneMidi());
            File.WriteAllBytes(Path.Combine(sub, "Scale.midi"), CreateFormatOneMidi());
            File.WriteAllText(Path.Combine(folder, "Nocturne.musicxml"),
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <score-partwise version="4.0">
                  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
                  <part id="P1">
                    <measure number="1">
                      <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
                      <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration><staff>1</staff></note>
                      <backup><duration>2</duration></backup>
                      <note><pitch><step>E</step><octave>3</octave></pitch><duration>2</duration><staff>2</staff></note>
                    </measure>
                  </part>
                </score-partwise>
                """);
            File.WriteAllBytes(Path.Combine(folder, "Broken.mid"), Encoding.ASCII.GetBytes("this is not a MIDI file"));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "a text file next to the songs");
            Assert(SongFolderIndex.IsSong(etude) && SongFolderIndex.IsSong(Path.Combine(folder, "Nocturne.musicxml"))
                    && SongFolderIndex.IsSong("x.MXL") && !SongFolderIndex.IsSong("x.txt") && !SongFolderIndex.IsSong("x.mp3"),
                "The library should only claim the file types the readers actually read.");

            var songs = SongFolderIndex.Scan(folder);
            Assert(songs.Count == 3 && songs.Select(song => song.Title).SequenceEqual(["Etude", "Nocturne", "Scale"]),
                $"A scan should index every readable song below the folder and list them by title (got {string.Join(", ", songs.Select(song => song.Title))}).");
            // The MIDI fixture is a format-1 file whose three notes sit on two tracks, the score is one part.
            Assert(songs[0] is { Format: "mid", Notes: 3, Tracks: 2 } && songs[1] is { Format: "musicxml", Notes: 2, Tracks: 1 }
                    && Math.Abs(songs[1].BeatsPerMinute - 120) < .01 && songs[2].Path.StartsWith(sub, StringComparison.OrdinalIgnoreCase),
                "Facts should come from the files themselves: notes, tracks, tempo and the format of each one.");
            Assert(SongFolderIndex.Folder.EndsWith(Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase) && songs.All(song => song.Tags.Count == 0),
                "A fresh scan should remember the folder it read and start every song without tags.");
            Assert(songs.All(song => !song.Title.Contains("Broken", StringComparison.OrdinalIgnoreCase)) && songs.All(song => !song.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)),
                "A file the reader refuses, and a file that is not a song at all, should stay out of the library.");

            // The cache: an unchanged file is not read again, a changed one is, and the tags survive both.
            // The scan refills the library's own list, so what is compared has to be captured first — an entry
            // read after a later scan would otherwise be compared with itself.
            var etudeBefore = SongFolderIndex.Songs.Single(song => string.Equals(song.Path, etude, StringComparison.OrdinalIgnoreCase));
            var nocturneBefore = SongFolderIndex.Songs.Single(song => song.Title == "Nocturne");
            var cached = SongFolderIndex.Scan(folder);
            Assert(ReferenceEquals(cached.Single(song => song.Title == "Etude"), etudeBefore)
                    && ReferenceEquals(cached.Single(song => song.Title == "Nocturne"), nocturneBefore),
                "Scanning again should reuse the entries of files that did not change instead of re-reading them.");
            SongFolderIndex.Tag(etude, "Chopin");
            File.WriteAllBytes(etude, [.. CreateFormatOneMidi(), .. new byte[16]]);
            File.SetLastWriteTimeUtc(etude, DateTime.UtcNow.AddMinutes(1));
            var rescanned = SongFolderIndex.Scan(folder);
            var etudeEntry = rescanned.Single(song => string.Equals(song.Path, etude, StringComparison.OrdinalIgnoreCase));
            Assert(ReferenceEquals(etudeEntry, etudeBefore) == false && etudeEntry.Size == etudeBefore.Size + 16 && etudeEntry.Tags.SequenceEqual(["chopin"]),
                $"Editing a file should make the next scan re-read it while its tags stay with it (size {etudeBefore.Size} → {etudeEntry.Size}, tags {string.Join("/", etudeEntry.Tags)}).");
            var forced = SongFolderIndex.Scan(folder, force: true);
            Assert(!ReferenceEquals(forced.Single(song => song.Title == "Nocturne"), nocturneBefore),
                "RESCAN should read every file again, changed or not.");

            // Tags and search.
            Assert(SongFolderIndex.Tag(etude, "  Chopin  ") is null && SongFolderIndex.CleanTag("  A  B ") == "a b"
                    && SongFolderIndex.CleanTag(new string('x', 40)).Length == SongFolderIndex.MaxTagLength
                    && SongFolderIndex.Tag(etude, "   ") is null && SongFolderIndex.Tag(Path.Combine(folder, "ghost.mid"), "x") is null,
                "A tag should be trimmed, lower-cased, cut to the limit, never empty and never added to a song that is not indexed.");
            for (var index = 0; index < SongFolderIndex.MaxTags + 2; index++) SongFolderIndex.Tag(Path.Combine(folder, "Nocturne.musicxml"), $"tag{index}");
            var capped = SongFolderIndex.Songs.Single(song => song.Title == "Nocturne");
            Assert(capped.Tags.Count == SongFolderIndex.MaxTags, $"A song should carry at most {SongFolderIndex.MaxTags} tags (has {capped.Tags.Count}).");
            Assert(SongFolderIndex.Untag(Path.Combine(folder, "Nocturne.musicxml"), "TAG0") is { } trimmed && trimmed.Tags.Count == SongFolderIndex.MaxTags - 1
                    && SongFolderIndex.Untag(Path.Combine(folder, "Nocturne.musicxml"), "nope") is null,
                "Removing a tag should ignore case and report when there was nothing to remove.");
            Assert(SongFolderIndex.Search("etu").Count == 1 && SongFolderIndex.Search("CHOPIN").Count == 1
                    && SongFolderIndex.Search("chopin etu").Single().Title == "Etude" && SongFolderIndex.Search("").Count == 3
                    && SongFolderIndex.Search("tag1").Count == 1 && SongFolderIndex.Search("nothing at all").Count == 0,
                "Search should match a title, a file name or a tag, require every word of the query, and match nothing when it should.");
            SongFolderIndex.Reload();
            Assert(SongFolderIndex.Songs.Count == 3 && SongFolderIndex.Search("chopin").Count == 1 && SongFolderIndex.Folder.Length > 0,
                "The folder, the songs and their tags should come back from the index file.");
            File.WriteAllText(SongFolderIndex.FilePath, "{ half a file");
            SongFolderIndex.Reload();
            Assert(SongFolderIndex.Songs.Count == 0 && SongFolderIndex.Folder.Length == 0,
                "A damaged index should be forgotten rather than breaking the library, and the folder can be scanned again.");
            Assert(SongFolderIndex.Scan(Path.Combine(folder, "does-not-exist")).Count == 0 && SongFolderIndex.Tag(etude, "x") is null,
                "Scanning a folder that is not there should yield an empty library instead of throwing.");

            // The Play dialog: the same list, the search box and the tagged chips. The experiments above
            // emptied the index (the damaged-file part is meant to), so the folder is scanned and tagged again.
            SongFolderIndex.Scan(folder);
            SongFolderIndex.Tag(etude, "Chopin");
            Invoke(window, "RefreshLibrarySongs");
            var host = (StackPanel)window.FindName("LibrarySongHost");
            var label = (TextBlock)window.FindName("LibraryFolderLabel");
            var empty = (TextBlock)window.FindName("LibraryEmptyLabel");
            var search = (TextBox)window.FindName("LibrarySearchBox");
            Assert(host.Children.Count == 3 && label.Text.StartsWith(Loc.F("{0} songs in {1}", 3, SongFolderIndex.Folder), StringComparison.Ordinal),
                $"The Play dialog should list the indexed songs and say where they came from ({label.Text}).");
            search.Text = "nocturne";
            Assert(host.Children.Count == 1 && empty.Text == Loc.F("Showing {0} of {1} matching songs", 1, 1),
                $"Typing in the search box should filter the library down to the matches ({empty.Text}).");
            search.Text = "zzz";
            Assert(host.Children.Count == 0 && empty.Text == Loc.T("No song in this folder matches what you typed. Tags and the file name are searched too."),
                "A query that matches nothing should say so instead of leaving an empty list.");
            search.Text = "chopin";
            Assert(host.Children.Count == 1, $"The tag the song carries should be found by the search box (matched {host.Children.Count}).");
            var row = (Grid)host.Children[0];
            var chips = ((StackPanel)((StackPanel)row.Children[0]).Children[2]).Children.OfType<Button>().ToList();
            Assert(chips.Any(chip => (chip.Content as string) == "#chopin"), "A tagged song should show its tag as a chip in the library row.");
            search.Text = "";

            // Bad candidates count against the scan cap too; otherwise a directory full of corrupt files can
            // make RESCAN read arbitrarily far beyond the advertised limit before finding a readable song.
            var crowded = Path.Combine(folder, "Crowded");
            Directory.CreateDirectory(crowded);
            for (var index = 0; index < SongFolderIndex.MaxFiles; index++)
                File.WriteAllBytes(Path.Combine(crowded, $"{index:D4}-broken.mid"), []);
            File.WriteAllBytes(Path.Combine(crowded, "zzzz-readable.mid"), CreateFormatOneMidi());
            Assert(SongFolderIndex.Scan(crowded).Count == 0,
                "The scan should stop after trying the first 500 candidates, even when all 500 are corrupt.");
            Directory.Delete(crowded, true);
            SongFolderIndex.Scan(folder);
            SongFolderIndex.Tag(etude, "Chopin");
            Invoke(window, "RefreshLibrarySongs");

            // The watcher: the list follows the disk, and a file added while it watches is indexed.
            Invoke(window, "StartSongFolderWatch", folder);
            var watcher = (SongFolderWatcher)Field(window, "_songWatcher")!;
            static string Clean(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            Assert(watcher.IsWatching && string.Equals(Clean(watcher.WatchedFolder), Clean(folder), StringComparison.OrdinalIgnoreCase),
                $"Choosing a folder should start watching it for changes (watching “{watcher.WatchedFolder}”).");
            var added = Path.Combine(folder, "Late.mid");
            File.WriteAllBytes(added, CreateFormatOneMidi());
            // The watcher runs on its own thread and only raises the flag; the window's timer would do the
            // rescanning, but the check is holding the UI thread, so it rescan on demand below instead.
            var noticed = false;
            for (var attempt = 0; attempt < 60 && !noticed; attempt++)
            {
                Thread.Sleep(50);
                noticed = (bool)Field(window, "_libraryDirty") || SongFolderIndex.Songs.Count == 4;
            }
            Assert(noticed, "Adding a song to the watched folder should mark the library for a rescan without pressing anything.");
            Invoke(window, "RescanSongFolder_Click", window, new RoutedEventArgs());
            Assert(SongFolderIndex.Songs.Count == 4 && host.Children.Count == 4,
                $"The rescan the watcher asked for should pick the new song up (library has {SongFolderIndex.Songs.Count}).");
            Invoke(window, "StopSongFolderWatch");
            Assert(!watcher.IsWatching, "Closing the window should stop watching the folder.");
            SetField(window, "_libraryDirty", false);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { }
            SongFolderIndex.Forget();
            Invoke(window, "RefreshLibrarySongs");
        }
        Results.Add("PASS Song library: a folder scan that indexes MIDI and MusicXML under it, facts read from the files, a cache that only re-reads what changed and keeps tags, tag limits and search over titles, file names and tags, an index that survives a reload and forgives a damaged file, the Play dialog list and its search box, and a watcher that notices a song added to the folder.");
    }

    /// <summary>
    /// The audio track of a recording: the bytes of the WAV header against the specification, sizes that are
    /// only known at the end, silence and signal, a file that cannot be written, and then the real path — the
    /// engine's tap rendering the loaded SFZ piano into a writer, which is exactly what the recorder attaches
    /// when the REC button starts, plus where the recorder puts the file and what the dock promises it will do.
    /// </summary>
    private static void VerifyRecordingAudioTrack(MainWindow window)
    {
        // The header, byte for byte: 44 bytes, PCM, the channel count and the rate the engine renders.
        var header = WavWriter.Header(44100, 2, 8);
        var expected = new byte[WavWriter.HeaderBytes];
        using (var stream = new MemoryStream(expected))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(44u); writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(16u); writer.Write((short)1); writer.Write((short)2);
            writer.Write(44100u); writer.Write(176400u); writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(8u);
        }
        Assert(header.SequenceEqual(expected), "A recording's WAV header should be the canonical 44-byte PCM header of the engine's own format.");

        var folder = Path.Combine(Path.GetTempPath(), "keyflow-verify-wav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        long frames = 0;
        try
        {
            var path = Path.Combine(folder, "take.wav");
            using (var wav = WavWriter.TryCreate(path))
            {
                Assert(wav is not null && wav.SampleRate == 44100 && wav.Channels == 2 && wav.Frames == 0 && !wav.IsClosed,
                    "Opening a track should leave an empty file with the header already in place.");
                var samples = new short[] { 100, -100, 200, -200, 300, -300, 400, -400 };
                wav!.Append(samples, samples.Length);
                Assert(wav.Frames == 4 && wav.DataBytes == 16 && Math.Abs(wav.Seconds - 4.0 / 44100) < 1e-9,
                    $"Appending interleaved samples should count frames, not values ({wav.Frames} frames, {wav.DataBytes} bytes).");
                wav.Append(samples, 3);          // one and a half frames: the whole frame is kept, the half is not
                Assert(wav.Frames == 5, $"A block that ends inside a frame should keep whole frames only ({wav.Frames}).");
                wav.AppendSilence(6);
                Assert(wav.Frames == 11 && wav.DataBytes == 44, "Silence should advance the track by the frames it writes.");
                wav.Append(new short[8192], 8192);
                Assert(wav.Frames == 11 + 4096, "A block larger than the writer's own buffer should still land whole.");
                frames = wav.Frames;
            }
            var bytes = File.ReadAllBytes(path);
            Assert(bytes.Length == WavWriter.HeaderBytes + frames * 4, $"The file should hold exactly the frames that were appended (wrote {bytes.Length} bytes for {frames} frames).");
            Assert(bytes.Take(4).SequenceEqual("RIFF"u8.ToArray()) && bytes.Skip(8).Take(4).SequenceEqual("WAVE"u8.ToArray())
                    && BitConverter.ToInt32(bytes, 4) == bytes.Length - 8 && BitConverter.ToInt32(bytes, 40) == bytes.Length - WavWriter.HeaderBytes,
                "Closing the file should patch the two sizes that can only be known at the end.");
            Assert(BitConverter.ToInt16(bytes, 22) == 2 && BitConverter.ToInt32(bytes, 24) == 44100 && BitConverter.ToInt16(bytes, 34) == 16,
                "The patched header should still describe the format the samples are in.");
            // Frame 5 is where the silence starts (four frames of signal, then the whole frame of the short block).
            Assert(BitConverter.ToInt16(bytes, WavWriter.HeaderBytes) == 100 && BitConverter.ToInt16(bytes, WavWriter.HeaderBytes + 2) == -100
                    && BitConverter.ToInt16(bytes, WavWriter.HeaderBytes + 4) == 200 && BitConverter.ToInt16(bytes, WavWriter.HeaderBytes + 6) == -200
                    && bytes.Skip(WavWriter.HeaderBytes + 5 * 4).All(value => value == 0),
                "The samples should be written little-endian and in the order they were appended, with the silence left silent.");

            // A path the writer cannot use, and a closed writer that keeps receiving blocks.
            Assert(WavWriter.TryCreate(folder) is null && WavWriter.TryCreate("") is null,
                "A recording that cannot open its audio file should report it instead of throwing on a folder name.");
            var late = WavWriter.TryCreate(Path.Combine(folder, "late.wav"))!;
            late.Dispose();
            late.Append([1, 2, 3, 4], 4);
            late.Dispose();
            Assert(late.IsClosed && File.ReadAllBytes(Path.Combine(folder, "late.wav")).Length == WavWriter.HeaderBytes,
                "A block arriving after the take ended must be ignored rather than throwing on the audio thread.");

            // Where the recorder puts the track, next to the video or inside the frame folder.
            Assert(MainWindow.AudioTrackPath(@"C:\clips\take.avi", false).EndsWith("take.wav", StringComparison.Ordinal)
                    && MainWindow.AudioTrackPath(@"C:\clips\frames", true).EndsWith(Path.Combine("frames", "audio.wav"), StringComparison.Ordinal)
                    && MainWindow.MuxedName(@"C:\clips\take.avi").EndsWith("take.mp4", StringComparison.Ordinal)
                    && MainWindow.MuxedName(null) == "" && MainWindow.MuxedName("") == "",
                "The audio track should land beside an AVI (and inside the PNG folder), and the muxed copy should be named next to it — a take with no name at all names nothing instead of throwing.");

            // The real path: the engine renders an SFZ piano into the writer, block by block.
            var sample = Path.Combine(folder, "recording-source.wav");
            WriteTestWav(sample, channels: 2, bits: 16, rate: 44100, frames: 22050, wave: i => Math.Sin(i * 2 * Math.PI * 440 / 44100) * .33);
            var sfz = Path.Combine(folder, "recording-source.sfz");
            File.WriteAllText(sfz, "<region> sample=recording-source.wav key=69 pitch_keycenter=69 amp_veltrack=0 ampeg_release=0.2");
            using (var engine = new PianoAudioEngine())
            {
                Assert(!engine.PumpTapBlock() && !engine.HasTap, "Nothing should be rendered while no track is attached.");
                engine.LoadSfzInstrument(sfz);
                using var wav = WavWriter.TryCreate(Path.Combine(folder, "engine.wav"))!;
                var blocks = 0;
                engine.SetTap((samples, count) => { wav!.Append(samples, count); blocks++; });
                engine.NoteOn(69, 110); // With no live sink, note commands start queuing once the recording tap is attached.
                Assert(engine.HasTap, "Attaching a tap should be visible on the engine, which is what the recorder asks before it promises audio.");
                for (var i = 0; i < 8; i++) Assert(engine.PumpTapBlock(), "A block should render into the attached tap whether or not the machine has an audio device.");
                Assert(engine.FlushTap(), "The asynchronous recording writer should drain its blocks before verification reads the file.");
                var probe = new float[512 * 2];
                var synthesizer = new SfzSynthesizer(SfzReader.Read(sfz), 44100);
                synthesizer.NoteOn(0, 69, 110); synthesizer.Render(probe, 512);
                var energy = probe.Sum(sampleValue => Math.Abs(sampleValue));
                Assert(blocks == 8 && wav!.Frames == 8 * 512 && energy > 0,
                    $"Eight rendered blocks should become eight blocks of frames in the file ({blocks} blocks, {wav!.Frames} frames).");
                engine.SetTap(null);
                Assert(!engine.HasTap && !engine.PumpTapBlock(), "Detaching the tap should stop the rendering that only existed for the recording.");
                engine.UnloadInstrument();
            }
            var engineBytes = File.ReadAllBytes(Path.Combine(folder, "engine.wav"));
            Assert(engineBytes.Length == WavWriter.HeaderBytes + 8 * 512 * 4 && BitConverter.ToInt32(engineBytes, 40) == 8 * 512 * 4
                    && engineBytes.Skip(WavWriter.HeaderBytes).Any(value => value != 0),
                $"The engine's own blocks should be in the file, with the size patched on close ({engineBytes.Length} bytes).");

            // The dock: the switch, what it promises, and the window's own side of the wiring.
            var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
            Assert(toggles.TryGetValue(nameof(PianoVisualSettings.RecordAudio), out var audioToggle) && audioToggle.IsChecked == true,
                "The Recording page should offer the audio track as a switch, on by default.");
            // The assert above records the failure; without this the run would go on and crash on the very
            // control it just reported missing.
            if (audioToggle is null) return;
            var settings = (PianoVisualSettings)Field(window, "_visualSettings");
            audioToggle.IsChecked = false;
            Assert(!settings.RecordAudio && ReferenceEquals(Field(window, "_audioTrack"), null),
                "Switching the audio track off should reach the settings the recorder reads.");
            var info = (TextBlock)window.FindName("RecordingInfoLabel");
            Assert(info.Text.Contains(Loc.T("audio is not captured"), StringComparison.Ordinal),
                $"With the switch off the recording line should say so ({info.Text}).");
            audioToggle.IsChecked = true;
            var hadPath = Field(window, "_recordingPath");
            SetField(window, "_recordingPath", Path.Combine(folder, "app-take.avi"));
            var started = (bool)window.GetType().GetMethod("BeginAudioTrack", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(window, null)!;
            var audio = (PianoAudioEngine)Field(window, "_audio");
            Assert(!started && !audio.HasTap && Field(window, "_audioTrack") is null,
                "A machine with no Salamander piano loaded should record video only instead of writing an empty WAV.");
            SetField(window, "_recordingPath", hadPath);
            Invoke(window, "UpdateRecordingInfo");
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
        Results.Add("PASS recording audio: the canonical WAV header, frames counted and sizes patched on close, silence and oversized blocks, a file that cannot be opened and a closed writer, the track's path beside the video, the engine tapping its own rendered blocks into the writer, the dock switch and the honest line about a machine without the Salamander piano.");
    }

    /// <summary>True when the action throws, which is how the recorders refuse bad input.</summary>
    private static bool Throws(Action action)
    {
        try { action(); return false; } catch { return true; }
    }

    /// <summary>
    /// Themes the user made: the palette is derived from five seeds and stays readable (dark surfaces, an
    /// accent that stands out, lighter hover and border layers), a stored theme round-trips through the
    /// folder, a hand-edited file is repaired instead of refused, a corrupt file is skipped, an id resolves
    /// through <see cref="ShellThemes.Find"/> once the folder holds it, applying one publishes its colours,
    /// both chip rows show it, and it is refused when named after a built-in theme.
    /// </summary>
    private static void VerifyUserShellThemes(MainWindow window)
    {
        var seeds = new UserShellTheme
        {
            Name = "Sunset Glow", Backdrop = nameof(BackdropStyle.Imperial),
            Accent = "#FF7A59", AccentAlt = "#FFD166", Glow = "#FFB86B", Surface = "#141019", Mote = "#FFE3B0"
        };
        var built = UserShellThemes.Build(seeds);
        Assert(built.Id == "user-sunset-glow" && built.Name == "Sunset Glow" && built.Backdrop == BackdropStyle.Imperial && built.Blurb == UserShellThemes.BlurbKey(BackdropStyle.Imperial),
            "A theme the user made should take its id from its name and describe itself with a translatable key.");
        static int Peak(Color colour) => Math.Max(colour.R, Math.Max(colour.G, colour.B));
        // Lightening raises every channel, so the brightness of the whole colour is what stacks; comparing the
        // single brightest channel would call two colours equal once one of them already sits at 255.
        static int Brightness(Color colour) => colour.R + colour.G + colour.B;
        Assert(Peak(built.Control) <= UserShellThemes.MaxSurface && Peak(built.Control) >= UserShellThemes.MinSurface,
            $"A user theme keeps its base surface inside the dark band ({UserShellThemes.MinSurface:X2}-{UserShellThemes.MaxSurface:X2}) so the light chrome text stays readable.");
        Assert(UserShellThemes.HasContrast(built.Accent, built.Control) && Brightness(built.ControlHover) > Brightness(built.Control)
                && Brightness(built.Border) > Brightness(built.Control) && Brightness(built.ControlBorder) > Brightness(built.Border)
                && Brightness(built.Track) > Brightness(built.Control) && Brightness(built.Window) < Brightness(built.Control)
                && Brightness(built.PanelTop) > Brightness(built.PanelBottom) && Brightness(built.MoteAlt) > Brightness(built.Mote),
            "The derived chrome should keep the accent visible and stack the surfaces: window below control, hover, border and track above it.");
        var clampedLight = UserShellThemes.Build(new UserShellTheme { Name = "Too Light", Surface = "#FFFFFF" });
        var clampedDark = UserShellThemes.Build(new UserShellTheme { Name = "Too Dark", Surface = "#000000" });
        Assert(Peak(clampedLight.Control) == UserShellThemes.MaxSurface && Peak(clampedDark.Control) == UserShellThemes.MinSurface,
            "A surface outside the readable band should be clamped, not accepted as typed.");
        var repaired = UserShellThemes.Build(new UserShellTheme { Name = "Edited by hand", Accent = "not a colour", Surface = "", Mote = null! });
        Assert(Peak(repaired.Control) >= UserShellThemes.MinSurface && UserShellThemes.HasContrast(repaired.Accent, repaired.Control),
            "A theme file edited by hand should be repaired into a readable palette instead of failing to load.");
        Assert(UserShellThemes.Id("Sunset Glow!") == "user-sunset-glow" && UserShellThemes.Id("  ") == "user-custom"
                && UserShellThemes.IsUserTheme("user-sunset-glow") && !UserShellThemes.IsUserTheme(ShellThemes.ConcertNoirId),
            "Theme ids should be stable slugs carrying the user prefix, so the pickers can tell them from the built-in looks.");

        var directory = Path.Combine(Path.GetTempPath(), "keyflow-verify-themes-" + Guid.NewGuid().ToString("N"));
        var store = new UserThemeStore(directory);
        try
        {
            var saved = store.Save(seeds);
            var loaded = store.Load();
            Assert(loaded.Count == 1 && loaded[0].Id == saved.Id && loaded[0].Accent == built.Accent && loaded[0].Control == built.Control
                    && loaded[0].Backdrop == BackdropStyle.Imperial,
                "A theme the user made should round-trip through its file with the same colours and backdrop family.");
            store.Save(new UserShellTheme { Name = "Sunset Glow", Accent = "#59A6FF", AccentAlt = "#9BD0FF", Glow = "#7FC4FF", Surface = "#0E1420", Mote = "#CFE7FF" });
            var replaced = store.Load();
            Assert(replaced.Count == 1 && replaced[0].Accent == Color.FromRgb(0x59, 0xA6, 0xFF),
                "Saving a theme under a name that already exists should update that theme instead of adding a second one.");
            File.WriteAllText(Path.Combine(directory, "Broken.json"), "{ not a theme at all");
            File.WriteAllText(Path.Combine(directory, "Renamed.json"), System.Text.Json.JsonSerializer.Serialize(new UserShellTheme { Name = "Something Else", Accent = "#7CFF6B" }));
            var survivors = store.Load();
            Assert(survivors.Count == 2 && survivors.Any(theme => theme.Name == "Renamed" && theme.Accent == Color.FromRgb(0x7C, 0xFF, 0x6B)),
                "A corrupt theme file should be skipped, and a renamed file should arrive under its new name.");
            Assert(store.Delete("user-renamed") && store.Load().Count == 1 && !store.Delete(ShellThemes.ConcertNoirId),
                "Deleting a theme removes its file, while a built-in theme cannot be deleted.");
            var keep = store.Save(new UserShellTheme { Name = "Keep This Theme" });
            Assert(Throws(() => store.Save(new UserShellTheme { Name = "../library" }, keep.Id))
                    && store.Load().Any(theme => theme.Id == keep.Id),
                "An invalid rename target must be refused without deleting the existing theme or escaping the theme folder.");
            var renamed = store.Save(new UserShellTheme { Name = "Renamed Keep" }, keep.Id);
            Assert(store.Load().Any(theme => theme.Id == renamed.Id) && !store.Load().Any(theme => theme.Id == keep.Id),
                "A successful rename should publish the replacement before removing the old theme file.");
            store.Save(new UserShellTheme { Name = "Warm Theme" });
            Assert(Throws(() => store.Save(new UserShellTheme { Name = "Warm-theme" }))
                    && store.Load().Count(theme => theme.Id == UserShellThemes.Id("Warm Theme")) == 1,
                "Distinct names that normalize to the same theme id must not silently hide one another.");
        }
        finally { try { Directory.Delete(directory, true); } catch { } }

        // The pickers: a theme saved in the settings folder resolves by id, applies, and shows up as a chip.
        var originalDirectory = PianoVisualSettingsStore.SettingsDirectory;
        var originalTheme = ShellThemeManager.Current;
        var hadHighContrast = ShellThemeManager.ForceHighContrast;
        var folder = Path.Combine(Path.GetTempPath(), "keyflow-verify-themes-live-" + Guid.NewGuid().ToString("N"));
        try
        {
            PianoVisualSettingsStore.UseDirectory(folder);
            ShellThemeManager.ForceHighContrast = false;
            UserThemeStore.Default.Save(seeds);
            var resolved = ShellThemes.Find("user-sunset-glow");
            Assert(resolved.Id == "user-sunset-glow" && resolved.Accent == built.Accent && ShellThemes.Everything.Any(theme => theme.Id == resolved.Id),
                "A theme in the settings folder should resolve by id through the same lookup the pickers and the settings file use.");
            ShellThemeManager.Apply(resolved);
            var accentBrush = Application.Current.Resources["AccentBrush"] as SolidColorBrush;
            Assert(ShellThemeManager.Current.Id == resolved.Id && accentBrush is not null && accentBrush.Color == built.Accent,
                "Applying a theme the user made should publish its accent to the chrome.");
            Invoke(window, "RefreshThemeChips");
            var chips = (WrapPanel?)Field(window, "themeChipHost");
            Assert(chips is not null && chips.Children.Count == ShellThemes.Everything.Count()
                    && chips.Children.OfType<Button>().Any(chip => (chip.DataContext as string) == resolved.Id),
                "Both theme chip rows should offer the themes the user made next to the built-in looks.");
            Assert(UserThemeStore.NameConflict("Concert Noir") is not null && UserThemeStore.NameConflict("Sunset Glow") is null
                    && UserThemeStore.NameConflict("sunset-glow") is not null && UserThemeStore.NameConflict("../library") is not null,
                "Built-in names, path-like names and normalized-id collisions should be refused, while the existing theme name stays editable.");
            PianoVisualSettingsStore.UseDirectory(originalDirectory);
            Invoke(window, "RefreshThemeChips");
            var afterRestore = (WrapPanel?)Field(window, "themeChipHost");
            Assert(!ShellThemes.Everything.Any(theme => theme.Id == resolved.Id) && afterRestore is not null
                    && afterRestore.Children.Count == ShellThemes.Everything.Count(),
                "Pointing the settings folder back should drop the themes of the temporary folder from both the lookup and the chips.");
        }
        finally
        {
            ShellThemeManager.ForceHighContrast = hadHighContrast;
            ShellThemeManager.Apply(originalTheme);
            PianoVisualSettingsStore.UseDirectory(originalDirectory);
            Invoke(window, "RefreshThemeChips");
            try { Directory.Delete(folder, true); } catch { }
        }

        // The studio: it refuses to save without a name or with a colour that is not one, and previews the
        // eleven palette surfaces the seeds derive.
        var studio = new ThemeStudioWindow(seeds, "Create theme");
        Assert(studio.PreviewHost is StackPanel { Children.Count: 11 },
            "The studio should preview every surface and colour the seeds derive.");
        Assert(studio.ColourBox(nameof(UserShellTheme.Accent)).Text == seeds.Accent && studio.NameBox.Text == seeds.Name,
            "The studio should open on the theme it was given.");
        studio.NameBox.Text = "   ";
        Assert(!studio.TryBuild(out _, out var nameError) && nameError == "A theme needs a name.",
            "The studio should refuse to save a theme without a name.");
        studio.NameBox.Text = "../library";
        Assert(!studio.TryBuild(out _, out var unsafeName)
                && unsafeName == "Theme names must be 1–32 characters and cannot contain path separators or reserved filename characters.",
            "The studio should refuse a path-like theme name before it reaches the store.");
        studio.NameBox.Text = "Sunset Glow"; studio.ColourBox(nameof(UserShellTheme.Glow)).Text = "purple";
        Assert(!studio.TryBuild(out _, out var hexError) && hexError == "One of the colours is not a hex value like #1A2B3C.",
            "The studio should refuse to save a colour that is not a hex value.");
        studio.ColourBox(nameof(UserShellTheme.Glow)).Text = "#FFB86B"; studio.BackdropBox.SelectedIndex = 1;
        Assert(studio.TryBuild(out var result, out var noError) && noError is null && result is not null
                && result.Backdrop == nameof(BackdropStyle.Obsidian) && UserShellThemes.Build(result).Backdrop == BackdropStyle.Obsidian,
            "A filled-in studio should hand back the theme the fields describe, backdrop family included.");
        Results.Add("PASS user themes: five seeds derive a readable twenty-token chrome, files round-trip and are repaired or skipped when damaged, unsafe names and normalized-id collisions are refused without losing a theme during replacement, an id resolves and applies through the pickers, and the studio refuses a nameless or non-hex theme.");
    }

    /// <summary>
    /// The community shelf: every file embedded in the build is a complete preset (the same keys the
    /// settings class declares, in range, no migration left to run), the shelf names do not collide with a
    /// built-in or with each other, a broken file or a repeated name is skipped instead of emptying the
    /// shelf, the list shows the entries marked COMMUNITY, a shelf look applies like any other, and its
    /// name is refused when saving so the badge keeps meaning something.
    /// </summary>
    private static void VerifyCommunityPresets(MainWindow window)
    {
        var shelf = CommunityPresets.All;
        var sources = CommunityPresets.EmbeddedSources();
        Assert(sources.Count > 0 && sources.Count == shelf.Count,
            $"The build should carry the community shelf (found {sources.Count} embedded file(s) and loaded {shelf.Count}).");
        Assert(shelf.All(preset => preset.Community && !preset.BuiltIn && preset.FilePath is null),
            "A shelf preset is read-only: it is marked as community and it has no file of its own.");
        Assert(shelf.All(preset => !string.IsNullOrWhiteSpace(preset.Description) && preset.Settings.PresetName == preset.Name),
            "Every shelf preset should describe itself and be named after its file.");

        var propertyNames = typeof(PianoVisualSettings).GetProperties().Select(property => property.Name).ToList();
        foreach (var (name, json) in sources)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            // An explicit local: `out var` inside the condition of an && would be "unassigned" on the short path.
            System.Text.Json.JsonElement element = default;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object) root.TryGetProperty("Settings", out element);
            Assert(element.ValueKind == System.Text.Json.JsonValueKind.Object,
                $"presets/{name}.json should be a preset envelope with a Settings object.");
            var keys = element.EnumerateObject().Select(property => property.Name).ToList();
            var missing = propertyNames.Except(keys).ToList();
            var unknown = keys.Except(propertyNames).ToList();
            Assert(missing.Count == 0 && unknown.Count == 0,
                $"presets/{name}.json should name every setting and nothing else (missing: {string.Join(", ", missing)}; unknown: {string.Join(", ", unknown)}).");
            var loaded = PianoVisualSettings.FromJson(json);
            Assert(loaded.ToJson() == PianoVisualSettings.FromJson(loaded.ToJson()).ToJson() && loaded.BackgroundAppearanceVersion >= 2,
                $"presets/{name}.json should already hold final values: loading it twice must not change it again (no migration or clamping left to do).");
        }
        Assert(shelf.All(preset => PianoVisualSettings.BackgroundMotions.Contains(preset.Settings.BackgroundMotion)
                && PianoVisualSettings.HaloPulseStyles.Contains(preset.Settings.HaloPulseStyle))
            && shelf.FirstOrDefault(preset => preset.Name == "Ember Rain")?.Settings.BackgroundMotion == "Ember Haze"
            && shelf.FirstOrDefault(preset => preset.Name == "Lo-Fi Study")?.Settings.BackgroundMotion == "Aurora"
            && shelf.FirstOrDefault(preset => preset.Name == "Sunset Drive")?.Settings.BackgroundMotion == "Retro Grid",
            "Every community preset should ship a validated motion profile that supports its theme.");
        var ember = shelf.First(preset => preset.Name == "Ember Rain").Settings;
        var study = shelf.First(preset => preset.Name == "Lo-Fi Study").Settings;
        var sunset = shelf.First(preset => preset.Name == "Sunset Drive").Settings;
        Assert(ember.NoteStyle == "Fire" && ember.AmbientNature == "Rain" && ember.ImpactWave == "Ripple" && !ember.ShowImpactFlash
                && study.NoteGlow <= 10 && study.ParticleAmount == 0 && !study.ShowImpactFlash
                && study.AmbientEnergy == "None" && study.AmbientNature == "None" && study.AmbientLight == "None" && study.AmbientCosmic == "None"
                && sunset.NoteStyle == "Neon" && sunset.BackgroundMotion == "Retro Grid" && sunset.ImpactBurst == "Fireworks" && !sunset.ShowImpactFlash,
            "The three community looks should opt into deliberate recipes atop a quiet baseline, without inheriting flashy model defaults.");
        Assert(shelf.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == shelf.Count
                && !shelf.Any(preset => VisualPresets.FindBuiltIn(preset.Name) is not null),
            "Shelf names should be unique and should not shadow a built-in preset.");

        // A contribution that is broken, or that repeats a name, must not take the shelf down with it.
        var crowded = CommunityPresets.Load([(sources[0].Name, sources[0].Json), (sources[0].Name.ToLowerInvariant(), sources[0].Json), ("Broken", "{ not json at all")]);
        Assert(crowded.Count == 1 && crowded[0].Name == shelf[0].Name && crowded[0].Description == shelf[0].Description,
            "A shelf with a repeated name and a broken file should still load the one good preset, with its description.");

        // In the window: the list carries them, the badge says where they come from, applying one works.
        Invoke(window, "LoadPresetList", shelf[0].Name);
        var list = (ListBox)window.FindName("PresetList");
        var item = list.Items.OfType<ListBoxItem>().FirstOrDefault(entry => entry.Tag is VisualPreset { Community: true });
        Assert(item?.Content is FrameworkElement content && Texts(content).Contains("COMMUNITY") && Texts(content).Contains(shelf[0].Name),
            "The preset list should offer the community shelf and mark it COMMUNITY.");
        var settings = (PianoVisualSettings)Field(window, "_visualSettings")!;
        var hadLook = settings.ToJson();
        Invoke(window, "ApplyPreset", shelf[0]);
        var wanted = shelf[0].Settings;
        Assert(settings.PresetName == shelf[0].Name && settings.NoteStyle == wanted.NoteStyle && settings.AmbientNature == wanted.AmbientNature
                && settings.ImpactBurst == wanted.ImpactBurst && settings.ShellTheme == wanted.ShellTheme && !settings.PresetModified,
            "Applying a shelf preset should carry its whole look over, theme included, and leave it unmodified.");
        settings.CopyFrom(PianoVisualSettings.FromJson(hadLook));
        Invoke(window, "RefreshSettingControls");

        Assert(CommunityPresets.NameConflict(shelf[0].Name) is not null
                && CommunityPresets.NameConflict(VisualPresets.DefaultPresetName) is not null
                && CommunityPresets.NameConflict("My Very Own Look") is null,
            "Saving over a built-in or community name should be refused, while a fresh name stays free.");
        Assert(!VisualPresetStore.Default.Delete(shelf[0]), "A shelf preset cannot be deleted: it has no file in the user's folder.");
        Results.Add($"PASS community shelf: {shelf.Count} preset(s) ship with the build, every file names all {propertyNames.Count} settings and loads unchanged, the list marks them COMMUNITY, they apply like any look and cannot be overwritten.");
    }

    /// <summary>Every string a piece of preset list content prints, in tree order.</summary>
    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        if (root is TextBlock block) texts.Add(block.Text);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) texts.AddRange(Texts(child));
        return texts;
    }

    /// <summary>
    /// The picture stored inside a preset file: the stage really renders at the fixed size, a saved preset
    /// keeps that render in its file and gets it back on load, an exported preset carries it to another
    /// machine through import, a file written before the envelope existed still loads, and a picture that
    /// is not a PNG of the right size is refused instead of breaking the list.
    /// </summary>
    private static void VerifyPresetThumbnails(MainWindow window)
    {
        var look = VisualPresets.FindBuiltIn("Inferno")!.Settings.Clone();
        var png = PianoPath.PresetThumbnail.RenderPng(look);
        Assert(png.Length > 8 && png.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "A rendered preset picture should be a PNG.");
        static int BigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        Assert(BigEndian(png, 16) == PianoPath.PresetThumbnail.Width && BigEndian(png, 20) == PianoPath.PresetThumbnail.Height && png.Length > 1000,
            $"The rendered picture should be {PianoPath.PresetThumbnail.Width}×{PianoPath.PresetThumbnail.Height} and carry real content (found {BigEndian(png, 16)}×{BigEndian(png, 20)}, {png.Length} bytes).");
        var stored = PianoPath.PresetThumbnail.Decode(PianoPath.PresetThumbnail.Encode(look));
        Assert(stored is { PixelWidth: PianoPath.PresetThumbnail.Width, PixelHeight: PianoPath.PresetThumbnail.Height },
            "The picture a preset stores should decode back to a bitmap of the rendered size.");
        Assert(PianoPath.PresetThumbnail.Decode("") is null && PianoPath.PresetThumbnail.Decode(null) is null
                && PianoPath.PresetThumbnail.Decode("not base64 at all!!") is null
                && PianoPath.PresetThumbnail.Decode(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })) is null
                && PianoPath.PresetThumbnail.Decode(new string('A', PianoPath.PresetThumbnail.MaxBase64Length + 1)) is null,
            "A preset without a picture, and a picture that is not a PNG of the right size, must read as no picture.");

        var directory = Path.Combine(Path.GetTempPath(), "keyflow-verify-thumbs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VisualPresetStore(directory);
            var saved = store.Save("Pictured Look", look, PianoPath.PresetThumbnail.Encode(look));
            Assert(saved.Thumbnail is { Length: > 1000 } && File.ReadAllText(saved.FilePath!).Contains("\"Thumbnail\""),
                "Saving a preset from the app should write the rendered picture into its file.");
            var loaded = store.LoadUserPresets();
            Assert(loaded.Count == 1 && loaded[0].Thumbnail == saved.Thumbnail && loaded[0].Settings.NoteStyle == look.NoteStyle,
                "Loading the preset back should return the same picture together with the same settings.");

            // A file from before the envelope existed: bare settings JSON, no picture, still a preset.
            var plain = Path.Combine(directory, "Old Look.json");
            File.WriteAllText(plain, look.ToJson());
            Assert(VisualPresetStore.ReadPresetFile(File.ReadAllText(plain)) is { Thumbnail: "" } oldFile && oldFile.Settings.NoteStyle == look.NoteStyle,
                "A preset file written before the picture envelope existed should still load as a preset without a picture.");

            var exportPath = Path.Combine(directory, "exported.json");
            VisualPresetStore.Export(look, exportPath, PianoPath.PresetThumbnail.Encode(look));
            var imported = VisualPresetStore.Import(exportPath);
            Assert(imported.Thumbnail is { Length: > 1000 } && PianoPath.PresetThumbnail.Decode(imported.Thumbnail) is not null,
                "An exported preset should carry its picture to the machine that imports it.");

            var noPicture = store.Save("Plain Look", look);
            Assert(noPicture.Thumbnail == "" && VisualPresetStore.ReadPresetFile(File.ReadAllText(noPicture.FilePath!)).Thumbnail == "",
                "Saving without a picture should still write a preset that loads, just without a thumbnail.");
        }
        finally { try { Directory.Delete(directory, true); } catch { } }

        // The list: a preset with a stored picture uses it, one without falls back to the drawn miniature.
        var pictured = new VisualPreset("Pictured", "with a picture", false, look, null, PianoPath.PresetThumbnail.Encode(look));
        var drawn = new VisualPreset("Drawn", "without a picture", false, look);
        if (InvokeReturn(window, "PresetThumbnail", pictured) is not System.Windows.Media.ImageSource picturedImage
            || InvokeReturn(window, "PresetThumbnail", drawn) is not System.Windows.Media.ImageSource drawnImage)
            throw new InvalidOperationException("The preset list should be able to build a picture for both a pictured and a drawn preset.");
        Assert(picturedImage is BitmapSource { PixelWidth: PianoPath.PresetThumbnail.Width, PixelHeight: PianoPath.PresetThumbnail.Height }
                && drawnImage is BitmapSource { PixelWidth: PianoPath.PresetThumbnail.Width, PixelHeight: PianoPath.PresetThumbnail.Height },
            "The preset list should show the stored picture at the same size as the miniature it draws for a preset without one.");
        Results.Add("PASS preset pictures: the stage renders at 192×112, a saved or exported preset carries the picture and gets it back on load, older files load without one, and a picture that is not a PNG of the right size is refused.");
    }

    private static void VerifySettingsProfile(MainWindow window)
    {
        var settings = (PianoVisualSettings)Field(window, "_visualSettings");
        var import = window.GetType().GetMethod("ImportProfile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Import(string path) => (bool)import.Invoke(window, [path])!;
        var folder = Path.Combine(Path.GetTempPath(), "keyflow-verify-profile");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "roundtrip.json");
        var original = settings.ToJson();
        var originalLanguage = Languages.Find(settings.Language).Id;
        var originalTheme = ShellThemeManager.Current.Id;
        var probe = settings.Clone();
        probe.NoteGlow = 141; probe.Language = "vi-VN"; probe.ShellTheme = ShellThemes.ConcertNoirId;
        File.WriteAllText(path, SettingsProfile.Capture(probe, ShellThemes.ConcertNoirId).ToJson());
        var reread = SettingsProfile.FromJson(File.ReadAllText(path));
        Assert(reread is not null && reread.Visual.NoteGlow == 141 && reread.Language == "vi-VN" && reread.ShellTheme == ShellThemes.ConcertNoirId,
            "A profile should carry the stage settings, the language and the shell theme through the file.");
        Assert(SettingsProfile.FromJson("{\"kind\":\"something-else\"}") is null && SettingsProfile.FromJson("not json at all") is null,
            "A JSON file that is not a Keyflow profile must be rejected instead of half-applied.");
        Assert(Import(path), "A profile this build wrote should import through the same path the button calls.");
        Assert(settings.NoteGlow == 141 && ShellThemeManager.Current.Id == ShellThemes.ConcertNoirId && Loc.Current.Id == "vi",
            "Importing a profile should paint the live window: stage settings, shell theme and interface language.");
        File.WriteAllText(path, new SettingsProfile(settings.Clone(), "xx-XX", ShellThemes.VelvetGoldId).ToJson());
        Assert(Import(path) && Loc.Current.Id == "en",
            "A language id this build does not ship should fall back to English rather than to a half-translated window.");
        File.WriteAllText(path, new SettingsProfile(PianoVisualSettings.FromJson(original), originalLanguage, originalTheme).ToJson());
        Assert(Import(path) && settings.NoteGlow == PianoVisualSettings.FromJson(original).NoteGlow && ShellThemeManager.Current.Id == originalTheme,
            "Importing the state the check started from should put the window back exactly as it was.");
        try { Directory.Delete(folder, true); } catch (IOException) { }
        Results.Add("PASS settings profile: the file round-trips the stage settings, the theme and the language, rejects foreign JSON, and falls back to English for an unknown language.");
    }

    private static void Commit(MainWindow window) => Invoke(window, "CommitHistory");

    /// <summary>
    /// Impact phase FX (effects-redesign v1): the Particles page exposes the wave style choice and
    /// the flash switch; hits spawn waves/flashes that paint real geometry and fade over time.
    /// </summary>
    /// <summary>Checks both new materials on the real WPF draw path, including their half-size silhouette and independent depth toggle.</summary>
    private static void VerifyNoteMaterialRendering(PianoStage stage, PianoVisualSettings visualSettings)
    {
        var saved = PianoVisualSettings.FromJson(visualSettings.ToJson());
        try
        {
            visualSettings.NoteGlow = 0;
            visualSettings.NoteHeadGlow = 0;
            visualSettings.NoteRefraction = 0;
            visualSettings.NoteEdge = 88;
            visualSettings.NoteEdgeWidth = 48;
            visualSettings.NoteTint = 78;
            visualSettings.FallingTrail = "None";
            visualSettings.FallingGhost = false;
            visualSettings.FallingPulse = false;
            visualSettings.ShowNoteLabels = false;
            visualSettings.HoldBar = false;
            stage.SetVisualSettings(visualSettings);

            const int width = 128, height = 192;
            byte[] Capture(string style, bool depth3D, bool halfSize = false, double refraction = 0)
            {
                visualSettings.NoteStyle = style;
                visualSettings.Notes3D = depth3D;
                visualSettings.NoteRefraction = refraction;
                stage.SetVisualSettings(visualSettings);
                var visual = new DrawingVisual();
                var bounds = halfSize ? new Rect(53, 55, 22, 76) : new Rect(42, 32, 44, 152);
                using (var dc = visual.RenderOpen())
                    Invoke(stage, "DrawConfiguredNote", dc, bounds, Color.FromRgb(64, 176, 250), 1d, false, 60, false, false);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(pixels, width * 4, 0);
                return pixels;
            }

            (int Count, int MinX, int MinY, int MaxX, int MaxY) Visible(byte[] pixels, int imageWidth, int imageHeight)
            {
                var count = 0; var minX = imageWidth; var minY = imageHeight; var maxX = -1; var maxY = -1;
                for (var y = 0; y < imageHeight; y++)
                    for (var x = 0; x < imageWidth; x++)
                    {
                        if (pixels[(y * imageWidth + x) * 4 + 3] <= 24) continue;
                        count++;
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
                return (count, minX, minY, maxX, maxY);
            }
            byte[] Average2x2(byte[] pixels)
            {
                var halfWidth = width / 2; var halfHeight = height / 2;
                var half = new byte[halfWidth * halfHeight * 4];
                for (var y = 0; y < halfHeight; y++)
                    for (var x = 0; x < halfWidth; x++)
                        for (var channel = 0; channel < 4; channel++)
                        {
                            var source = ((y * 2 * width) + x * 2) * 4 + channel;
                            var sum = pixels[source] + pixels[source + 4] + pixels[source + width * 4] + pixels[source + width * 4 + 4];
                            half[(y * halfWidth + x) * 4 + channel] = (byte)((sum + 2) / 4);
                        }
                return half;
            }
            byte MaxCrystalCoreAlpha(byte[] pixels, int imageWidth, int imageHeight, Rect bounds)
            {
                var y0 = Math.Clamp((int)Math.Floor(bounds.Top + bounds.Height * .4), 0, imageHeight);
                var y1 = Math.Clamp((int)Math.Ceiling(bounds.Top + bounds.Height * .6), 0, imageHeight);
                var ranges = new[]
                {
                    (Start: (int)Math.Floor(bounds.Left + bounds.Width * .34), End: (int)Math.Ceiling(bounds.Left + bounds.Width * .44)),
                    (Start: (int)Math.Floor(bounds.Left + bounds.Width * .56), End: (int)Math.Ceiling(bounds.Left + bounds.Width * .66))
                };
                byte max = 0;
                foreach (var (start, end) in ranges)
                for (var y = y0; y < y1; y++)
                for (var x = Math.Clamp(start, 0, imageWidth); x < Math.Clamp(end, 0, imageWidth); x++)
                    max = Math.Max(max, pixels[(y * imageWidth + x) * 4 + 3]);
                return max;
            }
            int BrightOutlinePixels(byte[] pixels)
            {
                var count = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                    if (pixels[i + 3] > 24 && pixels[i] + pixels[i + 1] + pixels[i + 2] > 125) count++;
                return count;
            }

            var solid = Capture("Solid", false);
            var gem2D = Capture("Gem", false);
            var gem3D = Capture("Gem", true);
            var gemWithRefraction = Capture("Gem", false, refraction: 100);
            var crystal2D = Capture("CrystalOutline", false);
            var crystal3D = Capture("CrystalOutline", true);
            var crystalWithRefraction = Capture("CrystalOutline", false, refraction: 100);
            var gemSmall = Visible(Capture("Gem", true, halfSize: true), width, height);
            var crystalSmallPixels = Capture("CrystalOutline", false, halfSize: true);
            var crystalSmall = Visible(crystalSmallPixels, width, height);
            var crystalAverageHalf = Average2x2(crystal2D);
            var crystalHalf = Visible(crystalAverageHalf, width / 2, height / 2);
            var crystalHalfBright = BrightOutlinePixels(crystalAverageHalf);
            var crystalCoreAlpha = MaxCrystalCoreAlpha(crystal2D, width, height, new Rect(42, 32, 44, 152));
            var crystalHalfCoreAlpha = MaxCrystalCoreAlpha(crystalAverageHalf, width / 2, height / 2, new Rect(21, 16, 22, 76));
            Assert(!gem2D.SequenceEqual(solid) && !crystal2D.SequenceEqual(solid) && !gem2D.SequenceEqual(crystal2D),
                "Gem and Crystal Outline must paint distinct geometry rather than falling through to Solid on the WPF path.");
            Assert(!gem2D.SequenceEqual(gem3D) && crystal2D.SequenceEqual(crystal3D),
                "The 3D toggle must deepen Gem while Crystal Outline remains the deliberately flat material.");
            Assert(!gem2D.SequenceEqual(gemWithRefraction) && !crystal2D.SequenceEqual(crystalWithRefraction),
                "The WPF path should apply NoteRefraction to both Gem and Crystal Outline.");
            Assert(gemSmall.Count > 300 && gemSmall.MaxX - gemSmall.MinX >= 18 && gemSmall.MaxY - gemSmall.MinY >= 68
                    && crystalSmall.Count > 100 && crystalSmall.MaxX - crystalSmall.MinX >= 18 && crystalSmall.MaxY - crystalSmall.MinY >= 68
                    && crystalHalf.Count > 40 && crystalHalfBright > 40
                    && crystalCoreAlpha <= 60 && crystalHalfCoreAlpha <= 60,
                $"Gem and Crystal Outline must remain legible at 50% while Crystal keeps a transparent core (Gem {gemSmall.Count} px; Crystal {crystalSmall.Count}/{crystalHalf.Count} px; bright 2×2 rim {crystalHalfBright}; core alpha full/half {crystalCoreAlpha}/{crystalHalfCoreAlpha}).");
            Results.Add("PASS note materials WPF: Gem and Crystal Outline have distinct output; Gem responds to 3D; both consume NoteRefraction; Crystal stays flat, its rim survives a 2×2 downsample, and its center stays transparent.");
        }
        finally
        {
            visualSettings.CopyFrom(saved, keepBackgroundImage: true);
            stage.SetVisualSettings(visualSettings);
        }
    }

    private static void VerifyImpactFx(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings, Dictionary<string, ComboBox> choices)
    {
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.ImpactWave)) && toggles.ContainsKey(nameof(PianoVisualSettings.ShowImpactFlash)),
            "The Particles page should expose the impact wave style and the impact flash switch.");
        Assert(EffectCatalog.Impact.All.Count(e => e.Status == EffectStatus.Available) >= 6,
            "The impact catalogue should list the implemented burst, ring, shockwave, flash and key-glow effects.");
        var wasWave = visualSettings.ImpactWave; var wasRings = visualSettings.ShowImpactRings;
        var wasWaveIntensity = visualSettings.ImpactWaveIntensity;
        var wasFlash = visualSettings.ShowImpactFlash; var wasFlashIntensity = visualSettings.ImpactFlashIntensity;
        visualSettings.ShowImpactRings = true; visualSettings.ImpactWave = "Shockwave"; visualSettings.ImpactWaveIntensity = 90;
        visualSettings.ShowImpactFlash = true; visualSettings.ImpactFlashIntensity = 80;
        stage.SetVisualSettings(visualSettings);
        stage.Impact(60, 1);
        Assert(stage.RingCount > 0, "A shockwave impact should spawn a wave.");
        Assert(stage.FlashCount > 0, "An impact with flash enabled should spawn a flash.");
        Assert(stage.HasActiveEffects, "Fresh impact waves and flashes should keep the stage animating.");
        var impactVisual = new DrawingVisual();
        using (var dc = impactVisual.RenderOpen())
        {
            Invoke(stage, "DrawRings", dc);
            Invoke(stage, "DrawImpactFlashes", dc);
        }
        Assert(impactVisual.Drawing is not null && impactVisual.Drawing.Bounds.Width > 10 && impactVisual.Drawing.Bounds.Height > 4,
            "Impact waves and flashes should actually paint geometry into the stage.");
        for (var i = 0; i < 40; i++) stage.Advance(.05); // 2 s of physics in frame-size steps (Advance clamps each step to 50 ms).
        Assert(stage.RingCount == 0 && stage.FlashCount == 0, "Impact waves (.55 s) and flashes (.18 s) should fade out over time.");
        visualSettings.ImpactWave = wasWave; visualSettings.ShowImpactRings = wasRings;
        visualSettings.ImpactWaveIntensity = wasWaveIntensity;
        visualSettings.ShowImpactFlash = wasFlash; visualSettings.ImpactFlashIntensity = wasFlashIntensity;
        stage.SetVisualSettings(visualSettings);
        stage.ClearTransient();
    }

    /// <summary>
    /// Falling phase FX (effects-redesign v2): trails/pulse/ghosts on the Notes page; burst styles,
    /// morphs and flash styles on the Particles page. Hits spawn styled particles that paint geometry.
    /// </summary>
    private static void VerifyFallingFx(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings, Dictionary<string, ComboBox> choices)
    {
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.FallingTrail)) && toggles.ContainsKey(nameof(PianoVisualSettings.FallingPulse)) && toggles.ContainsKey(nameof(PianoVisualSettings.FallingGhost)),
            "The Notes page should expose the falling trail choice, the pulse switch and the ghost switch.");
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.ImpactBurst)) && choices.ContainsKey(nameof(PianoVisualSettings.ImpactMorph)) && choices.ContainsKey(nameof(PianoVisualSettings.ImpactFlashStyle)),
            "The Particles page should expose the burst style, the note morph and the flash style.");
        var headTailGradient = EffectCatalog.Falling.All.Single(e => e.Id == "falling.color-gradient");
        Assert(EffectCatalog.AllEffects.Count == 87 && EffectCatalog.AvailableCount == 86
            && headTailGradient.Status == EffectStatus.Planned && headTailGradient.Channel == "falling.body.gradient"
            && EffectCatalog.Falling.All.Where(e => e.Id != headTailGradient.Id).All(e => e.Status == EffectStatus.Available),
            "The effect inventory should keep its missing head-to-tail gradient Planned rather than equating it with the pitch palette.");
        Assert(EffectCatalog.Impact.All.All(e => e.Status == EffectStatus.Available),
            "The whole impact catalogue should be implemented in v2.");
        var wasTrail = visualSettings.FallingTrail; var wasTrailIntensity = visualSettings.FallingTrailIntensity;
        var wasPulse = visualSettings.FallingPulse; var wasGhost = visualSettings.FallingGhost;
        var wasBurst = visualSettings.ImpactBurst; var wasMorph = visualSettings.ImpactMorph;
        var wasFlash = visualSettings.ShowImpactFlash; var wasFlashStyle = visualSettings.ImpactFlashStyle;
        var wasLife = visualSettings.ParticleLife;
        visualSettings.FallingTrail = "Sparkles"; visualSettings.FallingTrailIntensity = 80;
        visualSettings.FallingPulse = true; visualSettings.FallingGhost = true;
        visualSettings.ImpactBurst = "Confetti"; visualSettings.ImpactMorph = "Shatter"; visualSettings.ParticleLife = .3;
        visualSettings.ShowImpactFlash = true; visualSettings.ImpactFlashStyle = "Lightning";
        stage.SetVisualSettings(visualSettings);
        var noteVisual = new DrawingVisual();
        using (var dc = noteVisual.RenderOpen())
        {
            Invoke(stage, "DrawConfiguredNote", dc, new System.Windows.Rect(100, 100, 40, 120), System.Windows.Media.Color.FromRgb(120, 80, 255), 1d, false, 60, false, false);
        }
        Assert(noteVisual.Drawing is not null && noteVisual.Drawing.Bounds.Width > 40 && noteVisual.Drawing.Bounds.Height > 120,
            "A falling note with trail, pulse and ghosts should paint beyond its own body.");
        stage.Impact(60, 1);
        Assert(stage.SparkCount > 0, "A confetti impact with shatter morph should spawn particles.");
        Assert(stage.FlashCount > 0, "A lightning impact should spawn a flash.");
        var bolts = new DrawingVisual();
        using (var dc = bolts.RenderOpen()) { Invoke(stage, "DrawImpactFlashes", dc); }
        Assert(bolts.Drawing is not null && bolts.Drawing.Bounds.Height > 60, "The lightning bolt should strike down from above the key.");
        for (var i = 0; i < 60; i++) stage.Advance(.05);
        Assert(stage.SparkCount == 0 && stage.FlashCount == 0, "Burst particles and flashes should fade out over time.");
        visualSettings.FallingTrail = wasTrail; visualSettings.FallingTrailIntensity = wasTrailIntensity;
        visualSettings.FallingPulse = wasPulse; visualSettings.FallingGhost = wasGhost;
        visualSettings.ImpactBurst = wasBurst; visualSettings.ImpactMorph = wasMorph;
        visualSettings.ShowImpactFlash = wasFlash; visualSettings.ImpactFlashStyle = wasFlashStyle;
        visualSettings.ParticleLife = wasLife;
        stage.SetVisualSettings(visualSettings);
        stage.ClearTransient();
    }

    /// <summary>
    /// Hold phase FX (effects-redesign v3): the Notes page exposes the hold bar, breathing glow,
    /// vibration, color cycle and electric arc; sounding notes and chained keys paint real geometry.
    /// </summary>
    private static void VerifyHoldFx(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings)
    {
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        Assert(toggles.ContainsKey(nameof(PianoVisualSettings.HoldBar)) && toggles.ContainsKey(nameof(PianoVisualSettings.HoldBreath))
            && toggles.ContainsKey(nameof(PianoVisualSettings.HoldVibration)) && toggles.ContainsKey(nameof(PianoVisualSettings.HoldColorCycle))
            && toggles.ContainsKey(nameof(PianoVisualSettings.HoldElectricArc)),
            "The Notes page should expose the hold bar, breathing, vibration, color cycle and arc switches.");
        Assert(EffectCatalog.Hold.All.All(e => e.Status == EffectStatus.Available),
            "The whole hold catalogue should be implemented in v3.");
        var wasBar = visualSettings.HoldBar; var wasBreath = visualSettings.HoldBreath;
        var wasVibration = visualSettings.HoldVibration; var wasCycle = visualSettings.HoldColorCycle;
        var wasArc = visualSettings.HoldElectricArc;
        visualSettings.HoldBar = true; visualSettings.HoldBreath = true; visualSettings.HoldVibration = true;
        visualSettings.HoldColorCycle = true; visualSettings.HoldElectricArc = true; visualSettings.HoldArcIntensity = 80;
        stage.SetVisualSettings(visualSettings);
        var sounding = new DrawingVisual();
        using (var dc = sounding.RenderOpen())
        {
            Invoke(stage, "DrawConfiguredNote", dc, new System.Windows.Rect(100, 100, 40, 120), System.Windows.Media.Color.FromRgb(120, 80, 255), 1d, true, 60, false, false);
        }
        Assert(sounding.Drawing is not null && sounding.Drawing.Bounds.Width > 44,
            "A sounding note with hold FX should paint its highlight beyond the bar.");
        stage.SetState([], 0, false, new HashSet<int> { 60, 64 });
        var arcs = new DrawingVisual();
        using (var dc = arcs.RenderOpen()) { Invoke(stage, "DrawElectricArcs", dc, 1280d, 480d); }
        Assert(arcs.Drawing is not null && arcs.Drawing.Bounds.Width > 10 && arcs.Drawing.Bounds.Height > 4,
            "Two held keys should be chained by a visible electric arc.");
        visualSettings.HoldBar = wasBar; visualSettings.HoldBreath = wasBreath;
        visualSettings.HoldVibration = wasVibration; visualSettings.HoldColorCycle = wasCycle;
        visualSettings.HoldElectricArc = wasArc;
        stage.SetVisualSettings(visualSettings);
        stage.SetState([], 0, false, new HashSet<int>());
        stage.ClearTransient();
    }

    /// <summary>
    /// Release phase FX (effects-redesign v4): live releases and song note-ends emit the release
    /// effect; seeks never burst stale releases.
    /// </summary>
    private static void VerifyReleaseFx(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings, Dictionary<string, ComboBox> choices)
    {
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.ReleaseEffect)),
            "The Notes page should expose the release effect choice.");
        Assert(EffectCatalog.Release.All.All(e => e.Status == EffectStatus.Available),
            "The whole release catalogue should be implemented in v4.");
        var wasEffect = visualSettings.ReleaseEffect; var wasIntensity = visualSettings.ReleaseIntensity;
        visualSettings.ReleaseEffect = "Echo Rings"; visualSettings.ReleaseIntensity = 80;
        stage.SetVisualSettings(visualSettings);
        stage.AddLiveNote(60); stage.ReleaseLiveNote(60);
        Assert(stage.RingCount > 0, "Releasing a live note should emit echo rings.");
        var song = new List<NoteEvent> { new() { Pitch = 64, Start = 0, Duration = .1, Track = 0 } };
        stage.SetState(song, .05, true, new HashSet<int>());
        stage.Advance(.05);
        var beforeEnd = stage.RingCount;
        stage.SetState(song, .15, true, new HashSet<int>());
        stage.Advance(.05);
        Assert(stage.RingCount > beforeEnd, "A song note-end passing the playhead should emit echo rings.");
        stage.SetState(song, 0, true, new HashSet<int>()); // seek back: no stale burst
        stage.Advance(.05);
        var afterSeek = stage.RingCount;
        stage.SetState(song, .04, true, new HashSet<int>());
        stage.Advance(.05);
        Assert(stage.RingCount == afterSeek, "Seeking backwards should not burst stale releases.");
        for (var i = 0; i < 40; i++) stage.Advance(.05);
        Assert(stage.RingCount == 0, "Echo rings should fade out over time.");
        visualSettings.ReleaseEffect = wasEffect; visualSettings.ReleaseIntensity = wasIntensity;
        stage.SetVisualSettings(visualSettings);
        stage.SetState([], 0, false, new HashSet<int>());
        stage.ClearTransient();
    }

    /// <summary>
    /// Ambient layers (effects-redesign v5): the Background page exposes the four layer choices;
    /// each layer paints stage-wide geometry, and the Ripple wave style draws water rings.
    /// </summary>
    private static void VerifyAmbientFx(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings, Dictionary<string, ComboBox> choices)
    {
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.AmbientEnergy)) && choices.ContainsKey(nameof(PianoVisualSettings.AmbientNature))
            && choices.ContainsKey(nameof(PianoVisualSettings.AmbientLight)) && choices.ContainsKey(nameof(PianoVisualSettings.AmbientCosmic)),
            "The Background page should expose the four ambient layer choices.");
        Assert(EffectCatalog.AmbientEnergy.All.All(e => e.Status == EffectStatus.Available)
            && EffectCatalog.AmbientNature.All.All(e => e.Status == EffectStatus.Available)
            && EffectCatalog.AmbientLight.All.All(e => e.Status == EffectStatus.Available)
            && EffectCatalog.AmbientCosmic.All.All(e => e.Status == EffectStatus.Available),
            "All four ambient catalogues should be implemented in v5.");
        var wasEnergy = visualSettings.AmbientEnergy; var wasEnergyAmount = visualSettings.AmbientEnergyAmount;
        var wasNature = visualSettings.AmbientNature; var wasNatureAmount = visualSettings.AmbientNatureAmount;
        var wasLight = visualSettings.AmbientLight; var wasLightAmount = visualSettings.AmbientLightAmount;
        var wasCosmic = visualSettings.AmbientCosmic; var wasCosmicAmount = visualSettings.AmbientCosmicAmount;
        var wasBackgroundMotion = visualSettings.BackgroundMotion;
        var wasBackgroundMotionAmount = visualSettings.BackgroundMotionAmount;
        var wasBackgroundMotionSpeed = visualSettings.BackgroundMotionSpeed;
        var wasBackgroundMotionColor = visualSettings.BackgroundMotionColor;
        var wasBackgroundMode = visualSettings.BackgroundMode;
        var wasShowBackground = visualSettings.ShowBackground;
        var wasBeamIntensity = visualSettings.BeamIntensity;
        var wasShowLightBeams = visualSettings.ShowLightBeams;
        var activeKeys = (bool[])Field(stage, "_activeKey");
        var activeKeyColors = (Color[])Field(stage, "_activeKeyColor");
        var savedActiveKeys = (bool[])activeKeys.Clone();
        var savedActiveKeyColors = (Color[])activeKeyColors.Clone();
        var wasWave = visualSettings.ImpactWave;
        // The curated default intentionally zeros inactive layer intensities, so enable each selected layer
        // explicitly instead of relying on model-default amounts left behind by a different preset.
        visualSettings.AmbientEnergy = "Fireworks"; visualSettings.AmbientEnergyAmount = 60;
        visualSettings.AmbientNature = "Snow"; visualSettings.AmbientNatureAmount = 60;
        visualSettings.AmbientLight = "Prism"; visualSettings.AmbientLightAmount = 60;
        visualSettings.AmbientCosmic = "Galaxy"; visualSettings.AmbientCosmicAmount = 60;
        visualSettings.ImpactWave = "Ripple";
        stage.SetVisualSettings(visualSettings);
        Assert(stage.HasActiveEffects, "Enabled ambient layers should keep the stage animating.");
        foreach (var pass in new[] { "DrawAmbientEnergy", "DrawAmbientNature", "DrawAmbientLight", "DrawAmbientCosmic" })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) { Invoke(stage, pass, dc, 1280d, 480d); }
            Assert(visual.Drawing is not null && visual.Drawing.Bounds.Width > 100 && visual.Drawing.Bounds.Height > 100,
                $"The {pass} layer should paint stage-wide geometry.");
        }
        // Concert Gold selects Spotlights. Keep a WPF/software regression check because the GPU already
        // had this mode while the software stage silently rendered nothing for it.
        visualSettings.AmbientLight = "Spotlights"; visualSettings.AmbientLightAmount = 0;
        stage.SetVisualSettings(visualSettings);
        var disabledSpotlights = new DrawingVisual();
        using (var dc = disabledSpotlights.RenderOpen()) { Invoke(stage, "DrawAmbientLight", dc, 1280d, 480d); }
        Assert(disabledSpotlights.Drawing is null || disabledSpotlights.Drawing.Bounds.IsEmpty,
            "Spotlights intensity zero should disable its WPF drawing pass.");
        visualSettings.AmbientLightAmount = 16; // Concert Gold's shipped intensity; guard the low-level look, not an artificial maximum.
        stage.SetVisualSettings(visualSettings);
        var spotlights = new DrawingVisual();
        using (var dc = spotlights.RenderOpen()) { Invoke(stage, "DrawAmbientLight", dc, 1280d, 480d); }
        Assert(spotlights.Drawing is not null && spotlights.Drawing.Bounds.Width > 500 && spotlights.Drawing.Bounds.Height > 350,
            "The software stage should draw the three Spotlights shafts and their key-side pools.");
        var spotlightRaster = new RenderTargetBitmap(1280, 480, 96, 96, PixelFormats.Pbgra32);
        spotlightRaster.Render(spotlights);
        var spotlightPixels = new byte[1280 * 480 * 4];
        spotlightRaster.CopyPixels(spotlightPixels, 1280 * 4, 0);
        var litPixels = 0;
        byte spotlightPeakAlpha = 0;
        for (var i = 3; i < spotlightPixels.Length; i += 4)
        {
            if (spotlightPixels[i] > 8) litPixels++;
            if (spotlightPixels[i] > spotlightPeakAlpha) spotlightPeakAlpha = spotlightPixels[i];
        }
        Assert(litPixels > 1000 && spotlightPeakAlpha > 80,
            $"Concert Gold Spotlights should rasterize visible, warm WPF light at its shipped intensity (pixels={litPixels}, peak alpha={spotlightPeakAlpha}).");
        var spotlightCoreEnergy = PianoStage.SpotlightEnergyScaleAtOffset(1280, 0);
        var spotlightShoulderEnergy = PianoStage.SpotlightEnergyScaleAtOffset(1280, 1);
        Assert(spotlightCoreEnergy > .90 && spotlightShoulderEnergy < .11,
            $"WPF spotlights should preserve their central energy while keeping the broad penumbra restrained (core {spotlightCoreEnergy:0.000}, one-sigma shoulder {spotlightShoulderEnergy:0.000}).");

        visualSettings.ShowLightBeams = true;
        visualSettings.BeamIntensity = 45;
        stage.SetVisualSettings(visualSettings);
        Array.Clear(activeKeys);
        Array.Clear(activeKeyColors);
        activeKeys[60] = true;
        activeKeyColors[60] = Color.FromRgb(58, 218, 255);
        var keyBeam = new DrawingVisual();
        using (var dc = keyBeam.RenderOpen()) { Invoke(stage, "DrawKeyBeams", dc, 1280d, 480d, 12d); }
        Assert(keyBeam.Drawing is not null && keyBeam.Drawing.Bounds.Height > 300
            && keyBeam.Drawing.Bounds.Width > 50 && keyBeam.Drawing.Bounds.Width < 100,
            $"A sounding key should render a tall, localized shaft with a soft halo (bounds={keyBeam.Drawing?.Bounds}).");
        var keyBeamRaster = new RenderTargetBitmap(1280, 480, 96, 96, PixelFormats.Pbgra32);
        keyBeamRaster.Render(keyBeam);
        var keyBeamPixels = new byte[1280 * 480 * 4];
        keyBeamRaster.CopyPixels(keyBeamPixels, 1280 * 4, 0);
        var keyBeamLitPixels = 0;
        byte keyBeamPeakAlpha = 0;
        for (var i = 3; i < keyBeamPixels.Length; i += 4)
        {
            if (keyBeamPixels[i] > 8) keyBeamLitPixels++;
            if (keyBeamPixels[i] > keyBeamPeakAlpha) keyBeamPeakAlpha = keyBeamPixels[i];
        }
        Assert(keyBeamLitPixels > 1000 && keyBeamPeakAlpha > 80,
            $"WPF key beams should rasterize a bright shaft and bloom halo (pixels={keyBeamLitPixels}, peak alpha={keyBeamPeakAlpha}).");

        visualSettings.BackgroundMode = "Solid";
        visualSettings.ShowBackground = true;
        visualSettings.BackgroundMotionAmount = 60;
        visualSettings.BackgroundMotionSpeed = 38;
        foreach (var motion in PianoVisualSettings.BackgroundMotions.Skip(1))
        {
            visualSettings.BackgroundMotion = motion;
            stage.SetVisualSettings(visualSettings);
            var motionVisual = new DrawingVisual();
            using (var dc = motionVisual.RenderOpen()) { Invoke(stage, "DrawBackgroundMotion", dc, 1280d, 480d); }
            Assert(motionVisual.Drawing is not null && motionVisual.Drawing.Bounds.Width > 100 && motionVisual.Drawing.Bounds.Height > 60,
                $"The WPF fallback should render a visible {motion} background-motion layer.");
        }
        visualSettings.BackgroundMode = "ChromaGreen";
        stage.SetVisualSettings(visualSettings);
        var chromaMotion = new DrawingVisual();
        using (var dc = chromaMotion.RenderOpen()) { Invoke(stage, "DrawBackgroundMotion", dc, 1280d, 480d); }
        Assert(chromaMotion.Drawing is null || chromaMotion.Drawing.Bounds.IsEmpty,
            "Procedural background motion must leave the chroma-key green perfectly clean on the WPF path.");

        stage.Impact(60, 1);
        Assert(stage.RingCount > 0, "A ripple impact should spawn a wave.");
        var ripples = new DrawingVisual();
        using (var dc = ripples.RenderOpen()) { Invoke(stage, "DrawRings", dc); }
        Assert(ripples.Drawing is not null && ripples.Drawing.Bounds.Width > 10, "Water ripples should paint geometry.");
        for (var i = 0; i < 40; i++) stage.Advance(.05);
        Assert(stage.RingCount == 0, "Ripple waves should fade out over time.");
        visualSettings.AmbientEnergy = wasEnergy; visualSettings.AmbientEnergyAmount = wasEnergyAmount;
        visualSettings.AmbientNature = wasNature; visualSettings.AmbientNatureAmount = wasNatureAmount;
        visualSettings.AmbientLight = wasLight; visualSettings.AmbientLightAmount = wasLightAmount;
        visualSettings.AmbientCosmic = wasCosmic; visualSettings.AmbientCosmicAmount = wasCosmicAmount;
        visualSettings.BackgroundMotion = wasBackgroundMotion;
        visualSettings.BackgroundMotionAmount = wasBackgroundMotionAmount;
        visualSettings.BackgroundMotionSpeed = wasBackgroundMotionSpeed;
        visualSettings.BackgroundMotionColor = wasBackgroundMotionColor;
        visualSettings.BackgroundMode = wasBackgroundMode;
        visualSettings.ShowBackground = wasShowBackground;
        visualSettings.BeamIntensity = wasBeamIntensity;
        visualSettings.ShowLightBeams = wasShowLightBeams;
        Array.Copy(savedActiveKeys, activeKeys, activeKeys.Length);
        Array.Copy(savedActiveKeyColors, activeKeyColors, activeKeyColors.Length);
        visualSettings.ImpactWave = wasWave;
        stage.SetVisualSettings(visualSettings);
        stage.ClearTransient();
        Results.Add("PASS WPF lighting: spotlight cores stay bright while broad penumbrae are restrained; six background motions, key-beam bloom, ripple geometry, and clean chroma-key remain intact.");
    }

    /// <summary>
    /// Smart modulators (effects-redesign v6): octave/velocity/zone recolor notes, pedal/beat/onset
    /// boosts run through the halo renderer, and velocity scales the burst amount.
    /// </summary>
    private static void VerifySmartFx(MainWindow window, PianoStage stage, PianoVisualSettings visualSettings)
    {
        var toggles = (Dictionary<string, CheckBox>)Field(window, "_visualToggles");
        Assert(toggles.ContainsKey(nameof(PianoVisualSettings.VelocityColor)) && toggles.ContainsKey(nameof(PianoVisualSettings.OctaveColor))
            && toggles.ContainsKey(nameof(PianoVisualSettings.ZoneSplit)) && toggles.ContainsKey(nameof(PianoVisualSettings.PedalGlow))
            && toggles.ContainsKey(nameof(PianoVisualSettings.TempoSync)) && toggles.ContainsKey(nameof(PianoVisualSettings.AudioReactive)),
            "The Notes page should expose the velocity, octave, zone, pedal, tempo and audio modulators.");
        Assert(EffectCatalog.Smart.All.All(e => e.Status == EffectStatus.Available),
            "The whole smart catalogue should be implemented in v6.");
        var wasColorMode = visualSettings.ColorMode; var wasStart = visualSettings.NoteColorStart; var wasEnd = visualSettings.NoteColorEnd;
        var wasOctave = visualSettings.OctaveColor; var wasBlend = visualSettings.OctaveColorBlend;
        visualSettings.ColorMode = "Gradient"; visualSettings.Palette = "Custom";
        visualSettings.NoteColorStart = "#808080"; visualSettings.NoteColorEnd = "#808080";
        visualSettings.OctaveColor = true; visualSettings.OctaveColorBlend = 100;
        stage.SetVisualSettings(visualSettings);
        var low = stage.NoteColor(36, 0); var high = stage.NoteColor(72, 0);
        Assert(low != high, "With a flat base color, different octaves should resolve to different colors.");
        visualSettings.ColorMode = wasColorMode; visualSettings.NoteColorStart = wasStart; visualSettings.NoteColorEnd = wasEnd;
        visualSettings.OctaveColor = wasOctave; visualSettings.OctaveColorBlend = wasBlend;
        var wasAmount = visualSettings.ParticleAmount; var wasResponse = visualSettings.ParticleResponse;
        var wasBurst = visualSettings.ImpactBurst; var wasZone = visualSettings.ZoneSplit;
        visualSettings.ParticleAmount = 40; visualSettings.ParticleResponse = 55; visualSettings.ImpactBurst = "Embers";
        visualSettings.ZoneSplit = true; visualSettings.ZoneSplitPitch = 60;
        stage.SetVisualSettings(visualSettings);
        stage.ClearTransient();
        stage.Impact(40, .2);
        var softSparks = stage.SparkCount;
        stage.ClearTransient();
        stage.Impact(40, 1);
        Assert(stage.SparkCount > softSparks, "A harder hit should burst more particles than a soft one.");
        var bassKinds = SparkKinds(stage);
        Assert(bassKinds.Count > 0 && bassKinds.All(k => k == 0), "Bass-zone hits should erupt ember bursts.");
        stage.ClearTransient();
        stage.Impact(80, 1);
        var trebleKinds = SparkKinds(stage);
        Assert(trebleKinds.Count > 0 && trebleKinds.All(k => k == 1), "Treble-zone hits should splash droplet bursts.");
        visualSettings.VelocityColor = true; visualSettings.TempoSync = true; visualSettings.AudioReactive = true;
        visualSettings.PedalGlow = true; stage.SetSustainPedal(true); stage.PulseBeat(1);
        stage.Impact(60, 1);
        var halo = new DrawingVisual();
        using (var dc = halo.RenderOpen()) { Invoke(stage, "DrawImpactLine", dc, 1280d, 480d); }
        Assert(halo.Drawing is not null && halo.Drawing.Bounds.Width > 100, "The boosted halo line should paint across the stage.");
        stage.SetSustainPedal(false);
        for (var i = 0; i < 40; i++) stage.Advance(.05);
        visualSettings.ParticleAmount = wasAmount; visualSettings.ParticleResponse = wasResponse;
        visualSettings.ImpactBurst = wasBurst; visualSettings.ZoneSplit = wasZone;
        visualSettings.VelocityColor = false; visualSettings.TempoSync = false; visualSettings.AudioReactive = false;
        visualSettings.PedalGlow = false;
        stage.SetVisualSettings(visualSettings);
        stage.ClearTransient();
    }

    /// <summary>Reads the Kind of every live spark; the zone modulator is asserted through it.</summary>
    private static List<int> SparkKinds(PianoStage stage)
    {
        var sparks = (System.Collections.IList)Field(stage, "_sparks");
        return sparks.Cast<object>().Select(s => (int)s.GetType().GetField("Kind")!.GetValue(s)!).ToList();
    }

    /// <summary>
    /// Combo themes (effects-redesign v7): every theme resolves to a built-in preset graph whose
    /// falling/impact/hold/release/ambient/modulator combination matches the theme.
    /// </summary>
    private static void VerifyThemes()
    {
        Assert(EffectCatalog.Themes.All.All(e => e.Status == EffectStatus.Available),
            "The whole theme catalogue should be implemented in v7.");
        var fire = VisualPresets.FindBuiltIn("Inferno")!.Settings;
        var ice = VisualPresets.FindBuiltIn("Ice Crystal")!.Settings;
        var galaxy = VisualPresets.FindBuiltIn("Galaxy Voyage")!.Settings;
        var sakura = VisualPresets.FindBuiltIn("Sakura Nocturne")!.Settings;
        var electric = VisualPresets.FindBuiltIn("Electric Storm")!.Settings;
        var ocean = VisualPresets.FindBuiltIn("Ocean Depths")!.Settings;
        var retro = VisualPresets.FindBuiltIn("Retro Arcade")!.Settings;
        Assert(fire.ImpactBurst == "Embers" && fire.ImpactWave == "Shockwave" && fire.AmbientEnergy == "Fireworks",
            "The Fire theme (Inferno) should graph embers + shockwave + fireworks.");
        Assert(ice.ImpactBurst == "Splash" && ice.ImpactWave == "Ripple" && ice.AmbientNature == "Snow",
            "The Ice theme (Ice Crystal) should graph splash + ripple + snow.");
        Assert(galaxy.AmbientCosmic == "Galaxy" && galaxy.ImpactFlashStyle == "Plasma" && galaxy.FallingTrail == "Rainbow",
            "The Galaxy theme should graph rainbow trails + plasma + galaxy.");
        Assert(sakura.ShowPetals && sakura.ImpactBurst == "Confetti" && sakura.ReleaseEffect == "Float Up",
            "The Sakura theme should graph petals + petal confetti + floating goodbyes.");
        Assert(electric.ImpactFlashStyle == "Lightning" && electric.HoldElectricArc && electric.AmbientEnergy == "Lightning Storm",
            "The Electric theme should graph lightning + arcs + storm.");
        Assert(ocean.ImpactBurst == "Splash" && ocean.ImpactWave == "Ripple" && ocean.AmbientNature == "Rain",
            "The Ocean theme should graph splash + ripple + rain.");
        Assert(retro.ImpactBurst == "Confetti" && retro.ImpactMorph == "Bounce" && retro.ReleaseEffect == "Snap Back" && retro.NoteRoundness == 0,
            "The Retro theme should graph square pixels + confetti + bounce + snap.");
    }

    /// <summary>
    /// The README previews are committed by CI and compared byte for byte with the next build, so the stage
    /// has to draw the same picture for the same frame count. This asserts the property the capture relies
    /// on: a reset followed by a fixed number of fixed-size steps always lands on the same pixels, while a
    /// different number of steps lands somewhere else — so the frame count, not the wall clock, is what
    /// decides the picture. Before the capture was pinned to a frame count, two builds of the same
    /// interface produced 23 differing PNGs and the drift check could not tell the two cases apart.
    /// </summary>
    private static void VerifyDeterministicPreview(PianoStage stage)
    {
        const double step = 1.0 / 60;
        const int frames = 45;
        var width = Math.Max(160, (int)stage.ActualWidth);
        var height = Math.Max(90, (int)stage.ActualHeight);

        string Shot(int framesToRun)
        {
            stage.ResetAnimation();
            for (var i = 0; i < framesToRun; i++) stage.Advance(step);
            ForceStageRender(stage);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(stage);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
        }

        var first = Shot(frames);
        Assert(Shot(frames) == first,
            "The same frame count must draw the same stage picture, or the README previews cannot be compared byte for byte between builds.");
        Assert(Shot(frames + 30) != first,
            "A different frame count should draw a different picture: if every frame were identical, the assertion above would pass even without the pinned clock.");
        stage.ResetAnimation();
        ForceStageRender(stage);
    }

    /// <summary>Forces the stage to draw now so the shading state can be asserted synchronously.</summary>
    private static void ForceStageRender(PianoStage stage)
    {
        // A live visual reuses its cached drawing, so neither RenderTargetBitmap.Render nor a
        // same-tick InvalidateVisual re-runs OnRender. Invalidate, then pump the dispatcher through
        // the Render priority so the real window performs a fresh render pass we can assert on.
        stage.InvalidateVisual();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>The dock switch must really change what the stage renders, and the bake must be cached.</summary>
    private static void VerifyShadedStage(PianoStage stage, Dictionary<string, ComboBox> choices, PianoVisualSettings windowSettings)
    {
        var shading = choices[nameof(PianoVisualSettings.ShadingQuality)];
        Assert((string?)shading.SelectedValue == "Balanced", "The default look should start on the Balanced shading engine.");
        // Drive the shaded-keyboard pass directly with an explicit DrawingContext so the bake
        // accounting does not depend on WPF render scheduling or window resize churn.
        var off = new PianoStage();
        // The dock writes to the window's settings object; the stage draws a policy-adjusted copy of it,
        // so assertions about dock switches must read the window object, not the stage's copy.
        var settings = windowSettings;
        off.SetVisualSettings(settings);
        Assert(ShadeOnce(off), "The default look should drive the stage with the ray-traced keyboard.");
        Assert(off.IsShadedKeyboardActive && off.ShadedBakeCount >= 1,
            $"The default look should drive the stage with the ray-traced keyboard (bakes={off.ShadedBakeCount}, last bake={off.ShadedBakeMilliseconds:0.0} ms).");
        var bakes = off.ShadedBakeCount;
        ShadeOnce(off); ShadeOnce(off);
        Assert(off.ShadedBakeCount == bakes, "The baked keyboard must be reused between frames; only a settings or size change may re-bake it.");
        settings.ShadingQuality = "Off"; off.SetVisualSettings(settings);
        Assert(!ShadeOnce(off) && !off.IsShadedKeyboardActive, "Turning the shading engine off must fall back to the flat vector keyboard.");
        settings.ShadingQuality = "Balanced"; off.SetVisualSettings(settings);
        Assert(ShadeOnce(off) && off.IsShadedKeyboardActive, "Switching the shading engine back on should restore the ray-traced keyboard.");
        shading.SelectedValue = "Off";
        Assert(settings.ShadingQuality == "Off", "The dock shading switch should drive the live renderer settings.");
        shading.SelectedValue = "Balanced";
    }

    private static bool ShadeOnce(PianoStage stage)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            return (bool)InvokeReturn(stage, "TryDrawShadedKeyboard", dc, 900d, 220d, 280d)!;
    }

    private static object? InvokeReturn(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static void VerifyEmbersShell(MainWindow window, PianoVisualSettings visualSettings)
    {
        var menu = Element<FrameworkElement>(window, "MainMenuOverlay");
        var play = Element<FrameworkElement>(window, "PlayDialogOverlay");
        var quickAdjust = Element<FrameworkElement>(window, "QuickAdjustOverlay");
        var settings = Element<FrameworkElement>(window, "SettingsPanel");
        var tabs = Element<TabControl>(window, "SettingsTabs");
        var quickControlsHost = Element<StackPanel>(window, "QuickAdjustControlsHost");
        var quickLayerHost = Element<WrapPanel>(window, "QuickAdjustLayerHost");
        var quickHandColorsCard = Element<FrameworkElement>(window, "QuickAdjustHandColorsCard");
        // No null test for the eight elements above: Element<T> has already failed the run, naming the
        // missing element, if one was not there — and re-testing them here is what used to make the
        // compiler treat every later `menu.Visibility` in this method as a possible null dereference.
        Assert(menu.Visibility == Visibility.Collapsed && play.Visibility == Visibility.Collapsed, "Automated runs should start on the live stage with the menu closed.");

        window.ShowStartupMenu();
        Assert(menu.Visibility == Visibility.Visible, "The home path should open the main menu over the stage.");

        // The startup-menu quick-adjust entry and Play-dialog settings action share live controls,
        // while Advanced settings remains a one-click route to the full Style dock.
        var quickChoices = (Dictionary<string, ComboBox>)Field(window, "_quickAdjustChoices");
        var visualChoices = (Dictionary<string, ComboBox>)Field(window, "_visualChoices");
        Invoke(window, "MainMenuQuickAdjust_Click", window, new RoutedEventArgs());
        Assert(menu.Visibility == Visibility.Collapsed && quickAdjust.Visibility == Visibility.Visible
                && play.Visibility == Visibility.Collapsed && settings.Visibility == Visibility.Collapsed
                && quickControlsHost.Children.Count > 0 && quickLayerHost.Children.OfType<CheckBox>().Count() == 6
                && KeyboardNavigation.GetTabNavigation((FrameworkElement)window.FindName("QuickAdjustCard")!) == KeyboardNavigationMode.Cycle,
            "The startup menu should open a focused quick-adjust surface with common style and stage-layer controls.");
        var originalQuickStyle = visualSettings.NoteStyle;
        var originalQuickMode = visualSettings.ColorMode;
        var originalQuickBackground = visualSettings.ShowBackground;
        var originalQuickPresetModified = visualSettings.PresetModified;
        var quickStyleChoice = quickChoices[nameof(PianoVisualSettings.NoteStyle)];
        var styleSourceChoice = visualChoices[nameof(PianoVisualSettings.NoteStyle)];
        Assert(quickStyleChoice.Items.Count > 1 && Equals(quickStyleChoice.SelectedValue, styleSourceChoice.SelectedValue),
            "Quick note-style choices should begin synchronized with the Design dock's real choice.");
        if (quickStyleChoice.Items.Count > 1)
        {
            quickStyleChoice.SelectedIndex = (quickStyleChoice.SelectedIndex + 1) % quickStyleChoice.Items.Count;
            Assert(Equals(visualSettings.NoteStyle, quickStyleChoice.SelectedValue)
                    && Equals(styleSourceChoice.SelectedValue, quickStyleChoice.SelectedValue),
                "Changing a quick note-style choice should update the settings model and the Design dock immediately.");
            quickStyleChoice.SelectedValue = originalQuickStyle;
        }
        var originalQuickFallSpeed = visualSettings.NoteFallSpeed;
        var quickFallSlider = ((List<(Slider Slider, TextBlock Value)>)Field(window, "_playInlineSliders"))
            .Single(pair => Equals(pair.Slider.Tag, nameof(PianoVisualSettings.NoteFallSpeed))).Slider;
        var dockFallSlider = ((Dictionary<string, Slider>)Field(window, "_visualSliders"))[nameof(PianoVisualSettings.NoteFallSpeed)];
        var playFallSlider = (Slider)window.FindName("PlaySpeedSlider")!;
        quickFallSlider.Value = originalQuickFallSpeed < (quickFallSlider.Minimum + quickFallSlider.Maximum) / 2
            ? Math.Min(quickFallSlider.Maximum, originalQuickFallSpeed + 20)
            : Math.Max(quickFallSlider.Minimum, originalQuickFallSpeed - 20);
        Assert(Math.Abs(visualSettings.NoteFallSpeed - quickFallSlider.Value) < .01
                && Math.Abs(dockFallSlider.Value - quickFallSlider.Value) < .01
                && Math.Abs(playFallSlider.Value - quickFallSlider.Value) < .01,
            "Quick fall-speed adjustments should update the shared settings model, Design dock and Play speed slider immediately.");
        quickFallSlider.Value = originalQuickFallSpeed;
        var quickColorMode = quickChoices[nameof(PianoVisualSettings.ColorMode)];
        quickColorMode.SelectedValue = "PerHand";
        Assert(visualSettings.ColorMode == "PerHand" && quickHandColorsCard.Visibility == Visibility.Visible,
            "Choosing per-hand colors in Quick Adjust should reveal the direct left/right color controls.");
        quickColorMode.SelectedValue = originalQuickMode;
        var quickBackground = quickLayerHost.Children.OfType<CheckBox>().Single(check => Equals(check.Tag, nameof(PianoVisualSettings.ShowBackground)));
        quickBackground.IsChecked = !originalQuickBackground;
        Assert(visualSettings.ShowBackground != originalQuickBackground,
            "A quick stage-layer switch should update the same live setting as the Design dock.");
        quickBackground.IsChecked = originalQuickBackground;
        visualSettings.PresetModified = originalQuickPresetModified;
        Invoke(window, "RefreshSettingControls");
        Invoke(window, "ApplyVisualSettings", "Visual changes apply live", false, Array.Empty<object>());
        Invoke(window, "QuickAdjustClose_Click", window, new RoutedEventArgs());
        Assert(menu.Visibility == Visibility.Visible && quickAdjust.Visibility == Visibility.Collapsed && settings.Visibility == Visibility.Collapsed,
            "Closing menu-origin Quick Adjust should return to the startup menu.");

        Invoke(window, "MainMenuQuickAdjust_Click", window, new RoutedEventArgs());
        Invoke(window, "QuickAdjustAdvanced_Click", window, new RoutedEventArgs());
        Assert(settings.Visibility == Visibility.Visible && quickAdjust.Visibility == Visibility.Collapsed
                && tabs.SelectedIndex == SettingsPages.IndexOf(SettingsPages.Style),
            "Advanced settings from the startup quick-adjust surface should open the matching full Style dock.");
        Invoke(window, "SettingsBack_Click", window, new RoutedEventArgs());
        Assert(menu.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed,
            "Returning from quick-adjust advanced settings should restore the startup menu.");

        Invoke(window, "MainMenuPlay_Click", window, new RoutedEventArgs());
        Assert(menu.Visibility == Visibility.Collapsed && play.Visibility == Visibility.Visible, "Choosing Play on the main menu should open the pre-flight dialog.");
        Assert(((TextBlock)window.FindName("PlayDialogPrimaryLabel")!).Text == Loc.T("Choose a MIDI file"),
            "With no loaded score, the compact session action should clearly offer to choose a MIDI file.");
        var liveAction = (Button)window.FindName("PlayDialogLiveButton")!;
        var sessionAction = (Button)window.FindName("PlayDialogPrimaryButton")!;
        Assert(Grid.GetRow(liveAction) == 2 && ReferenceEquals(liveAction.Style, window.FindResource("AccentButtonStyle"))
                && Grid.GetColumn(sessionAction) == 2 && Grid.GetRow(sessionAction) == 0
                && ((Button)window.FindName("PlayDialogMidiButton")!).Visibility == Visibility.Collapsed,
            "The large gold action should be Live Play; the former primary action should move to the small session-action position.");

        // The compact action keeps all of its old state-specific behavior: choose a file, start a
        // loaded score, or return to an already-running score. The file-picker button only appears
        // alongside Start once there is a score to replace.
        var originalAllNotes = (List<NoteEvent>)Field(window, "_allNotes");
        var originalNotes = (List<NoteEvent>)Field(window, "_notes");
        var originalSongPath = (string)Field(window, "_songPath");
        var originalSongLabel = (string)Field(window, "_songLabel");
        var originalPlaying = (bool)Field(window, "_playing");
        var sampleNotes = new List<NoteEvent> { new() { Pitch = 60, Start = 0, Duration = 2, Track = 0 } };
        SetField(window, "_allNotes", sampleNotes);
        SetField(window, "_notes", sampleNotes);
        SetField(window, "_songPath", "sample.mid");
        SetField(window, "_songLabel", "Sample");
        Invoke(window, "RefreshPlayDialogState");
        Assert(((TextBlock)window.FindName("PlayDialogPrimaryLabel")!).Text == Loc.T("Start playback")
                && ((Button)window.FindName("PlayDialogMidiButton")!).Visibility == Visibility.Visible,
            "With a score loaded, the compact action should start playback and the MIDI button should remain available to replace it.");
        SetField(window, "_playing", true);
        Invoke(window, "RefreshPlayDialogState");
        Assert(((TextBlock)window.FindName("PlayDialogPrimaryLabel")!).Text == Loc.T("Return to playback"),
            "While the score is already playing, the compact action should return to it without restarting.");
        SetField(window, "_playing", originalPlaying);
        SetField(window, "_allNotes", originalAllNotes);
        SetField(window, "_notes", originalNotes);
        SetField(window, "_songPath", originalSongPath);
        SetField(window, "_songLabel", originalSongLabel);
        Invoke(window, "RefreshPlayDialogState");
        Invoke(window, "PlayDialogLive_Click", liveAction, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed && menu.Visibility == Visibility.Collapsed,
            "The large Live Play button should keep the original live-performance action and return to the stage.");
        Invoke(window, "MainMenuPlay_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Visible,
            "The session setup should remain reachable after choosing Live Play.");

        Assert(KeyboardNavigation.GetTabNavigation((FrameworkElement)window.FindName("PlayDialogCard")!) == KeyboardNavigationMode.Cycle,
            "Tab should remain inside the Play dialog while it is open.");
        Assert(((TextBox)window.FindName("LibrarySearchBox")!).Visibility == Visibility.Collapsed,
            "The empty song library should not leave a dead search field taking up space.");

        var notesToggle = (CheckBox)window.FindName("LayerNotesToggle")!;
        Assert(notesToggle.IsChecked == visualSettings.ShowNotes, "Play-dialog layer switches should mirror the live stage settings.");
        notesToggle.IsChecked = false;
        Assert(!visualSettings.ShowNotes, "Switching the Notes layer off in the play dialog should update the stage settings.");
        notesToggle.IsChecked = true;
        Assert(visualSettings.ShowNotes, "Switching the Notes layer back on should restore the stage settings.");

        // Each hand card opens an in-place color-preset picker. Applying a preset changes only that
        // hand and enables the existing PerHand mode without replacing the rest of the stage look.
        var originalHandMode = visualSettings.ColorMode;
        var originalLeftHandColor = visualSettings.LeftHandColor;
        var originalRightHandColor = visualSettings.RightHandColor;
        var originalPresetModified = visualSettings.PresetModified;
        var handPresetPanel = (FrameworkElement)window.FindName("HandPresetPanel")!;
        var handPresetTitle = (TextBlock)window.FindName("HandPresetTitle")!;
        var handPresetHost = (WrapPanel)window.FindName("HandPresetChoicesHost")!;
        var leftHandCard = (Button)window.FindName("LeftStyleCard")!;
        var rightHandCard = (Button)window.FindName("RightStyleCard")!;
        Invoke(window, "PlayDialogStyleCard_Click", leftHandCard, new RoutedEventArgs());
        Assert(handPresetPanel.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed
                && handPresetTitle.Text == Loc.T("Left hand presets"),
            "The Left Hand card should show its own preset picker in Play instead of navigating into settings.");
        Assert(handPresetHost.Children.OfType<Button>().All(button => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)))
                && AutomationProperties.GetName(handPresetHost.Children.OfType<Button>().Single(button => Equals(button.Tag, "#49E2C2"))) == Loc.T("Aurora Teal"),
            "Every generated hand preset needs a localized screen-reader name.");
        var leftPreset = handPresetHost.Children.OfType<Button>().Single(button => Equals(button.Tag, "#49E2C2"));
        Invoke(window, "PlayDialogHandPreset_Click", leftPreset, new RoutedEventArgs());
        Assert(visualSettings.ColorMode == "PerHand" && visualSettings.LeftHandColor == "#49E2C2"
                && visualSettings.RightHandColor == originalRightHandColor,
            "Choosing a left-hand preset should enable PerHand colors and leave the right hand unchanged.");

        var leftAfterPreset = visualSettings.LeftHandColor;
        Invoke(window, "PlayDialogStyleCard_Click", rightHandCard, new RoutedEventArgs());
        Assert(handPresetPanel.Visibility == Visibility.Visible && handPresetTitle.Text == Loc.T("Right hand presets"),
            "The Right Hand card should switch the in-place picker to right-hand presets.");
        var rightPreset = handPresetHost.Children.OfType<Button>().Single(button => Equals(button.Tag, "#7183FF"));
        Invoke(window, "PlayDialogHandPreset_Click", rightPreset, new RoutedEventArgs());
        Assert(visualSettings.RightHandColor == "#7183FF" && visualSettings.LeftHandColor == leftAfterPreset,
            "Choosing a right-hand preset should leave the left-hand choice intact.");
        var source = PresentationSource.FromVisual(window)!;
        var closeHandPicker = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent };
        Invoke(window, "Window_KeyDown", window, closeHandPicker);
        Assert(handPresetPanel.Visibility == Visibility.Collapsed && play.Visibility == Visibility.Visible && closeHandPicker.Handled,
            "Escape should close the hand preset panel before leaving the Play dialog.");

        Invoke(window, "PlayDialogStyleCard_Click", leftHandCard, new RoutedEventArgs());
        Invoke(window, "PlayDialogHandPresetAdvanced_Click", window, new RoutedEventArgs());
        Assert(settings.Visibility == Visibility.Visible && tabs.SelectedIndex == SettingsPages.IndexOf(SettingsPages.Notes)
                && handPresetPanel.Visibility == Visibility.Collapsed,
            "The hand picker should offer a direct advanced route to the matching Notes settings page.");
        Invoke(window, "SettingsBack_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed,
            "Returning from advanced hand settings should restore the Play dialog.");

        visualSettings.ColorMode = originalHandMode;
        visualSettings.LeftHandColor = originalLeftHandColor;
        visualSettings.RightHandColor = originalRightHandColor;
        visualSettings.PresetModified = originalPresetModified;
        Invoke(window, "RefreshSettingControls");
        Invoke(window, "RefreshHandStyleCards");
        Invoke(window, "ApplyVisualSettings", "Visual changes apply live", false, Array.Empty<object>());

        // The Play-dialog action now opens the shared quick-adjust surface. Its direct advanced route
        // still opens the full dock and preserves the Play dialog as the return destination.
        Invoke(window, "PlayDialogQuickAdjust_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed && quickAdjust.Visibility == Visibility.Visible
                && settings.Visibility == Visibility.Collapsed,
            "Play setup Quick Adjust should open directly without forcing the user through the full dock.");
        Invoke(window, "QuickAdjustAdvanced_Click", window, new RoutedEventArgs());
        Assert(settings.Visibility == Visibility.Visible && quickAdjust.Visibility == Visibility.Collapsed
                && tabs.SelectedIndex == SettingsPages.IndexOf(SettingsPages.Style)
                && ((FrameworkElement)window.FindName("SettingsReturnButton")!).Visibility == Visibility.Visible,
            "Advanced settings from Play Quick Adjust should open the design dock with an explicit way back.");
        Invoke(window, "SettingsBack_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed,
            "The settings dock's return control should restore the Play dialog, not drop the user onto the stage.");
        Invoke(window, "PlayDialogQuickAdjust_Click", window, new RoutedEventArgs());
        Invoke(window, "QuickAdjustClose_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Visible && quickAdjust.Visibility == Visibility.Collapsed,
            "Closing Play-origin Quick Adjust should return to the Play dialog without resetting the session setup.");

        var notesLink = new Button { DataContext = SettingsPages.Notes };
        Invoke(window, "PlayDialogDeepLink_Click", notesLink, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed && settings.Visibility == Visibility.Visible
                && tabs.SelectedIndex == SettingsPages.IndexOf(SettingsPages.Notes),
            "An inline More settings link should open the exact dock page it names.");

        Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert(play.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed,
            "Escape from a deep-linked dock page should restore Play and keep its navigation origin.");

        // Escape must navigate back from the dialog (not open the dock or send a piano note underneath it).
        var pressedBefore = ((HashSet<int>)Field(window, "_pressed")).Count;
        Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.A) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert(((HashSet<int>)Field(window, "_pressed")).Count == pressedBefore,
            "Typing a piano-mapped key while Play is open must not play a hidden stage note.");
        Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert(play.Visibility == Visibility.Collapsed && menu.Visibility == Visibility.Visible,
            "Escape/Back should return to the main menu when Play was opened from there.");

        // Settings opened from the menu also return to their source. The header Play button is the
        // discoverable entry point once the user has moved from the startup menu to the live stage.
        Invoke(window, "MainMenuDesign_Click", window, new RoutedEventArgs());
        Assert(settings.Visibility == Visibility.Visible && menu.Visibility == Visibility.Collapsed,
            "Stage Design from the menu should open the dock.");
        Invoke(window, "SettingsBack_Click", window, new RoutedEventArgs());
        Assert(menu.Visibility == Visibility.Visible && settings.Visibility == Visibility.Collapsed,
            "Back from menu-origin settings should restore the main menu.");
        Invoke(window, "MainMenuPlay_Click", window, new RoutedEventArgs());
        Invoke(window, "PlayDialogClose_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed && menu.Visibility == Visibility.Collapsed,
            "The close control should dismiss Play to the live stage.");
        Assert(window.FindName("PlaySetupButton") is Button, "The live header should expose a button that opens the Play dialog.");
        Invoke(window, "PlayDialogOpen_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Visible && menu.Visibility == Visibility.Collapsed,
            "The live header Play button should open the setup dialog without detouring through the main menu.");
        Invoke(window, "PlayDialogBack_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed && menu.Visibility == Visibility.Collapsed,
            "Back from a stage-origin Play dialog should return to the live stage.");
        var openPlayWithTab = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.KeyDownEvent };
        Invoke(window, "Window_KeyDown", window, openPlayWithTab);
        Assert(play.Visibility == Visibility.Visible && openPlayWithTab.Handled,
            "Tab on the live stage should open the Play dialog as its session-setup shortcut.");
        var tabInsidePlay = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.KeyDownEvent };
        Invoke(window, "Window_KeyDown", window, tabInsidePlay);
        Assert(!tabInsidePlay.Handled,
            "Tab inside the Play dialog should remain ordinary focus navigation, not reopen or dismiss the dialog.");
        Invoke(window, "PlayDialogBack_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed,
            "Closing the Tab-opened Play dialog should return to the stage.");
        Invoke(window, "PlayDialogOpen_Click", window, new RoutedEventArgs());
        Invoke(window, "PlayDialogQuickAdjust_Click", window, new RoutedEventArgs());
        var tabInsideQuickAdjust = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.KeyDownEvent };
        Invoke(window, "Window_KeyDown", window, tabInsideQuickAdjust);
        Assert(!tabInsideQuickAdjust.Handled,
            "Tab in Quick Adjust should remain normal dialog focus navigation, not reopen Play behind it.");
        var escapeQuickAdjust = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent };
        Invoke(window, "Window_KeyDown", window, escapeQuickAdjust);
        Assert(quickAdjust.Visibility == Visibility.Collapsed && play.Visibility == Visibility.Visible && escapeQuickAdjust.Handled,
            "Escape should close Quick Adjust first and return to the Play dialog that opened it.");
        Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert(play.Visibility == Visibility.Collapsed,
            "A second Escape should unwind the Play dialog to the stage.");
        window.ShowStartupMenu();
        Invoke(window, "Window_KeyDown", window, new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert(menu.Visibility == Visibility.Collapsed,
            "Escape from the main menu should return to the live stage and restore the toolbar.");

        // ---- Keyboard & shortcuts help card (F1): the shell hides itself, so the bindings are
        // documented in the app and the card must stay reachable and dismissible.
        var shortcuts = (FrameworkElement)window.FindName("ShortcutOverlay")!;
        Assert(shortcuts.Visibility == Visibility.Collapsed && !window.ShortcutsVisible, "The shortcut card should stay closed until it is asked for.");
        window.ToggleShortcuts();
        Assert(shortcuts.Visibility == Visibility.Visible && window.ShortcutsVisible, "F1 should open the keyboard & shortcuts card.");
        var shortcutGrid = (UniformGrid)window.FindName("ShortcutGrid")!;
        Assert(shortcutGrid.Children.Count == 3 && shortcutGrid.Children.OfType<StackPanel>().All(column => column.Children.OfType<Grid>().Count() >= 4),
            "The card should document the play, navigation and session groups with their bindings.");
        Invoke(window, "ShortcutOverlay_Click", window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
        Assert(shortcuts.Visibility == Visibility.Collapsed && !window.ShortcutsVisible, "Clicking the backdrop should close the shortcut card again.");
        window.ToggleShortcuts(); window.HideShortcuts();
        Assert(shortcuts.Visibility == Visibility.Collapsed, "Toggling the card twice should put it away.");
    }

    private static void VerifyBackgroundImageLoad(MainWindow window, PianoStage stage, PianoVisualSettings settings)
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-background-{Guid.NewGuid():N}.png");
        var originalPath = settings.BackgroundImagePath;
        var originalBackgroundMode = settings.BackgroundMode; var originalShowBackground = settings.ShowBackground;
        try
        {
            var pixels = new byte[] { 20, 80, 240, 255, 40, 120, 220, 255, 60, 160, 200, 255, 80, 200, 180, 255 };
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);

            stage.SetVisualSettings(settings);
            settings.BackgroundImagePath = path; // Deliberately mutate the shared settings object, as the UI does.
            stage.SetVisualSettings(settings);
            var loaded = (BitmapSource?)Field(stage, "_backgroundImage");
            Assert(stage.HasBackgroundImage && loaded?.PixelWidth == 2 && loaded.PixelHeight == 2 && stage.BackgroundLoadError is null,
                $"The stage should load a selected local PNG after in-place settings changes and release the source file handle (has={stage.HasBackgroundImage}, size={loaded?.PixelWidth}x{loaded?.PixelHeight}, error={stage.BackgroundLoadError ?? "none"}).");

            // The documented screenshots go through the same entry point, so pin its contract: applying a
            // picture for one run has to behave like a chosen background for the renderer while leaving the
            // saved profile alone (no dirty flag, no auto-save timer armed), and an unreadable path must
            // surface through the stage instead of a modal dialog that nobody in a headless capture could
            // dismiss. That is what lets `--background-image` render a preview without touching user data.
            var saveTimer = (DispatcherTimer)Field(window, "_settingsSaveTimer");
            var wasModified = settings.PresetModified; var timerWasArmed = saveTimer.IsEnabled;
            window.PreviewBackgroundImage(path);
            Assert(stage.HasBackgroundImage && settings.BackgroundMode == "Image" && settings.ShowBackground
                   && settings.PresetModified == wasModified && saveTimer.IsEnabled == timerWasArmed,
                $"PreviewBackgroundImage should paint one run only (image={stage.HasBackgroundImage}, mode={settings.BackgroundMode}, modified={settings.PresetModified}, auto-save={saveTimer.IsEnabled}).");
            window.PreviewBackgroundImage(path + ".missing");
            Assert(!stage.HasBackgroundImage && !string.IsNullOrWhiteSpace(stage.BackgroundLoadError),
                "A preview background that cannot be read should fall back to the solid colour and report through the stage, without a dialog.");
            saveTimer.Stop();

            settings.BackgroundImagePath = path + ".missing";
            stage.SetVisualSettings(settings);
            Assert(!stage.HasBackgroundImage && !string.IsNullOrWhiteSpace(stage.BackgroundLoadError), "An unreadable or missing background should expose a useful load error without crashing the stage.");
        }
        finally
        {
            // The preview entry point flips the mode as well, and the suite keeps handing this same
            // settings object to the checks after it, so every field the block touched goes back first.
            settings.BackgroundImagePath = originalPath; settings.BackgroundMode = originalBackgroundMode; settings.ShowBackground = originalShowBackground;
            stage.SetVisualSettings(settings, reloadBackground: true);
            if (File.Exists(path)) File.Delete(path);
        }
        Results.Add("PASS background images: changed-path detection, PNG decoding, file release, missing-file diagnostics and the single-run preview path.");
    }

    private static int CountToken(string source, string token)
    {
        var count = 0; var offset = 0;
        while ((offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0) { count++; offset += token.Length; }
        return count;
    }

    private static void VerifySongControls(MainWindow window, PianoStage stage, ComboBox mode, ComboBox tracks)
    {
        mode.SelectedIndex = 2; Assert(((IEnumerable<NoteEvent>)Field(window, "_notes")).All(n => n.Pitch >= 60), "Right hand should filter lower pitches.");
        mode.SelectedIndex = 3; Assert(((IEnumerable<NoteEvent>)Field(window, "_notes")).All(n => n.Pitch < 60), "Left hand should filter upper pitches.");
        mode.SelectedIndex = 0; tracks.SelectedIndex = 1; Assert(((IEnumerable<NoteEvent>)Field(window, "_notes")).All(n => n.Track == 0), "Track selection should isolate a track."); tracks.SelectedIndex = 0;

        SetField(window, "_position", 1.0); Invoke(window, "SetLoopA_Click", window, new RoutedEventArgs());
        SetField(window, "_position", 3.0); Invoke(window, "SetLoopB_Click", window, new RoutedEventArgs());
        Assert(((TextBlock)window.FindName("LoopLabel")).Text == "00:01–00:03", "A/B loop should retain its selected times.");
        SetField(window, "_position", 3.1); Invoke(window, "Tick", .016); Assert((double)Field(window, "_position") < 1.1, "Playback should wrap from B to A.");

        mode.SelectedIndex = 1;
        foreach (var note in ((IEnumerable<NoteEvent>)Field(window, "_notes")).Where(n => n.Start < .99)) note.Played = true;
        SetField(window, "_position", 1.2); ((Stopwatch)Field(window, "_clock")).Restart(); Invoke(window, "StartPlayback"); Invoke(window, "Tick", .016);
        Assert(Math.Abs((double)Field(window, "_position") - 1.0) < .02, "Wait mode should hold at the next note.");
        Invoke(window, "PressNote", 72, 90);
        Assert(((IEnumerable<NoteEvent>)Field(window, "_notes")).Any(n => n.Pitch == 72 && Math.Abs(n.Start - 1) < .01 && n.Played), "The expected note should score and release wait mode.");
        Invoke(window, "Stop"); mode.SelectedIndex = 0;

        var tempo = (Slider)window.FindName("TempoSlider"); tempo.Value = 120;
        Assert(Math.Abs((double)Field(window, "_tempo") - 1.2) < .01, "Tempo control should update playback rate."); tempo.Value = 100;
        SetField(window, "_isSeeking", true); ((Slider)window.FindName("SeekSlider")).Value = 50;
        var duration = ((IEnumerable<NoteEvent>)Field(window, "_allNotes")).Max(n => n.End);
        Assert(Math.Abs((double)Field(window, "_position") - duration * .5) < .02, "Seek slider should move the playhead."); SetField(window, "_isSeeking", false);
        Invoke(window, "Stop");
        Assert(stage.LiveTrailCount == 0 && stage.SparkCount == 0, "Pausing should clear transient note blocks and sparks.");
        window.Width = 1080; window.Height = 700; window.UpdateLayout();
        Assert(stage.ActualWidth > 500 && stage.KeyboardHeight > 100, "Compact window size should keep the keyboard usable.");
        // Animation plumbing: the stage advances on the shared vsync frame clock now, not on a private 16 ms timer.
        Assert(window.GetType().GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic) is null
                && window.GetType().GetField("_stageFrames", BindingFlags.Instance | BindingFlags.NonPublic) is not null && FrameClock.Shared is not null,
            "Stage animation should run on the shared frame clock instead of a private dispatcher timer.");
        Invoke(window, "PressNote", 60, 90);
        Assert(FrameClock.Shared!.IsRunning && (bool)Field(window, "_stageFrames"), "Playing a note should acquire the shared frame clock.");
        Invoke(window, "ReleaseNote", 60);
        Invoke(window, "Stop");
        Results.Add("PASS WPF: idle auto-hide of toolbar and settings, mouse reveal, Escape toggling Stage Design, live AVI frame capture, duration-scaled notes, pedals, MIDI practice controls and the shared frame clock.");
    }

    public static void PressPreviewNote(MainWindow window, int pitch) => Invoke(window, "PressNote", pitch, 90);
    public static void Capture(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window); var width = Math.Max(1, (int)(window.ActualWidth * dpi.DpiScaleX)); var height = Math.Max(1, (int)(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32); bitmap.Render(window);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }

    private static void VerifyPhase1VisualReview(string outputDirectory)
    {
        var captures = Phase1VisualReview.Render(outputDirectory);
        Assert(captures == 48, "Phase 1 visual review must write 48 images across three looks, four checkpoints, two renderers and two sizes.");
        Results.Add($"PASS Phase 1 visual review: {captures} WPF/GPU-WARP images at 1920×1080 and 960×540, with manifest and HTML index in {Path.GetFullPath(outputDirectory)}.");
    }

    private static void Finish(App app, string[] args, Exception? failure)
    {
        var logOption = args.FirstOrDefault(a => a.StartsWith("--verify-log=", StringComparison.Ordinal));
        var path = logOption is null ? Path.Combine(Path.GetTempPath(), "keyflow-verification.log") : logOption[13..];
        if (failure is not null) Results.Add("FAIL: " + failure);
        var skipped = Results.Count(line => line.StartsWith("SKIP ", StringComparison.Ordinal));
        Results.Add(failure is null ? $"Verification passed: {_assertions} assertions, {skipped} skipped check group(s)." : "Verification failed.");
        File.WriteAllLines(path, Results); app.Shutdown(failure is null ? 0 : 1);
    }

    /// <summary>Track 0: low C/G/C octaves held under the melody. Track 1: a high C-major line.</summary>
    private static byte[] CreateTwoHandMidi()
    {
        var bass = BassTrack(); var melody = MelodyTrack();
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(Encoding.ASCII.GetBytes("MThd")); Write32(w, 6); Write16(w, 1); Write16(w, 2); Write16(w, 480);
        WriteTrack(w, bass); WriteTrack(w, melody); return s.ToArray();
    }
    private static byte[] BassTrack()
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        foreach (var pitch in new[] { 36, 43, 48, 36, 43, 48 })
        {
            w.Write((byte)0); w.Write((byte)0x90); w.Write((byte)pitch); w.Write((byte)90);
            w.Write((byte)0x83); w.Write((byte)0x60); w.Write((byte)0x80); w.Write((byte)pitch); w.Write((byte)0);
        }
        w.Write((byte)0); w.Write((byte)0xFF); w.Write((byte)0x2F); w.Write((byte)0);
        return s.ToArray();
    }
    private static byte[] MelodyTrack()
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        foreach (var pitch in new[] { 72, 76, 79, 76, 72, 79 })
        {
            w.Write((byte)0); w.Write((byte)0x90); w.Write((byte)pitch); w.Write((byte)100);
            w.Write((byte)0x83); w.Write((byte)0x60); w.Write((byte)0x80); w.Write((byte)pitch); w.Write((byte)0);
        }
        w.Write((byte)0); w.Write((byte)0xFF); w.Write((byte)0x2F); w.Write((byte)0);
        return s.ToArray();
    }

    /// <summary>
    /// A format 2 file: two independent patterns, each one bar of one quarter note at 120 BPM. Playing them one
    /// after another puts the second note half a second in; playing them together would put both at zero.
    /// </summary>
    /// <summary>
    /// One track, one time signature and one held middle C: enough to read the beat grid a signature produces.
    /// The note lasts a quarter, so the grid is asked about a signature rather than about the music under it.
    /// </summary>
    private static byte[] CreateSignatureMidi(int numerator, int denominator)
    {
        using var track = new MemoryStream(); using var t = new BinaryWriter(track);
        t.Write(new byte[] { 0, 0xFF, 0x58, 4, (byte)numerator, (byte)(Math.Log2(denominator)), 24, 8 });
        t.Write(new byte[] { 0, 0x90, 60, 100 });
        t.Write(new byte[] { 0x83, 0x60, 0x80, 60, 0 });
        t.Write(new byte[] { 0, 0xFF, 0x2F, 0 });
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(Encoding.ASCII.GetBytes("MThd")); Write32(w, 6); Write16(w, 0); Write16(w, 1); Write16(w, 480);
        WriteTrack(w, track.ToArray()); return s.ToArray();
    }

    private static byte[] CreateFormatTwoMidi()
    {
        var patternA = new byte[] { 0, 0xFF, 0x03, 4, 0x42, 0x61, 0x73, 0x73, 0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0, 0, 0xFF, 0x2F, 0 };
        var patternB = new byte[] { 0, 0xFF, 0x03, 4, 0x4C, 0x65, 0x61, 0x64, 0, 0x90, 67, 100, 0x83, 0x60, 0x80, 67, 0, 0, 0xFF, 0x2F, 0 };
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(Encoding.ASCII.GetBytes("MThd")); Write32(w, 6); Write16(w, 2); Write16(w, 2); Write16(w, 480); WriteTrack(w, patternA); WriteTrack(w, patternB); return s.ToArray();
    }

    /// <summary>
    /// An SMPTE file: 25 frames of 40 ticks, so a tick is a millisecond. The tempo map starts at 120 BPM and
    /// changes to 240 BPM one second in, which the note times must ignore and the beat grid must follow.
    /// </summary>
    private static byte[] CreateSmpteMidi()
    {
        var track = new byte[]
        {
            0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20,                                  // 500000 µs per quarter
            0, 0x90, 60, 100, 0x83, 0x74, 0x80, 60, 0,                           // C4 for 500 ticks = half a second
            0x83, 0x74, 0xFF, 0x51, 3, 0x03, 0xD0, 0x90,                         // 250000 µs per quarter, one second in
            0, 0x90, 67, 100, 0x83, 0x74, 0x80, 67, 0, 0, 0xFF, 0x2F, 0          // G4 for half a second of absolute time
        };
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        // The division word is SMPTE: a signed -25 in the high byte and 40 ticks in the low one.
        w.Write(Encoding.ASCII.GetBytes("MThd")); Write32(w, 6); Write16(w, 1); Write16(w, 1); Write16(w, 0xE728); WriteTrack(w, track); return s.ToArray();
    }

    /// <summary>A file whose header declares the given format and division, with one empty track: everything else about it is valid.</summary>
    private static string WriteFormatOneMidi(string path) { File.WriteAllBytes(path, CreateFormatOneMidi()); return path; }

    private static byte[] CreateHeaderMidi(int format, int division)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(Encoding.ASCII.GetBytes("MThd")); Write32(w, 6); Write16(w, format); Write16(w, 1); Write16(w, division);
        WriteTrack(w, [0, 0xFF, 0x2F, 0]); return s.ToArray();
    }

    private static byte[] CreateFormatOneMidi()
    {
        // Track 0: name "Lead", C4 for one beat, then a tempo change to 240 BPM. Track 1: a channel-10 drum hit (must be ignored), E4, G4.
        var a = new byte[] { 0, 0xFF, 0x03, 4, 0x4C, 0x65, 0x61, 0x64, 0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0, 0, 0xFF, 0x51, 3, 3, 0xD0, 0x90, 0, 0xFF, 0x2F, 0 };
        var b = new byte[] { 0, 0x99, 36, 100, 0, 0x89, 36, 0, 0, 0x90, 64, 80, 0x81, 0x70, 0x80, 64, 0, 0x81, 0x70, 0x90, 67, 90, 0x83, 0x60, 0x80, 67, 0, 0, 0xFF, 0x2F, 0 };
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(Encoding.ASCII.GetBytes("MThd")); Write32(w, 6); Write16(w, 1); Write16(w, 2); Write16(w, 480); WriteTrack(w, a); WriteTrack(w, b); return s.ToArray();
    }
    private static void WriteTrack(BinaryWriter w, byte[] data) { w.Write(Encoding.ASCII.GetBytes("MTrk")); Write32(w, data.Length); w.Write(data); }
    private static void Write16(BinaryWriter w, int value) { w.Write((byte)(value >> 8)); w.Write((byte)value); }
    private static void Write32(BinaryWriter w, int value) { w.Write((byte)(value >> 24)); w.Write((byte)(value >> 16)); w.Write((byte)(value >> 8)); w.Write((byte)value); }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    /// <summary>
    /// Looks up a named element of the window under test, and hands it back non-nullable — which is the
    /// whole point. A <c>(T)window.FindName(…)!</c> local followed by a stand-alone
    /// <c>Assert(x is not null, …)</c> leaves the compiler holding x as maybe-null for the rest of the
    /// method (the assert could have been false, and nothing tells it that a false assert throws), so
    /// every later <c>x.Visibility</c> is reported as a possible null dereference: that is what the ten
    /// CS8602 warnings in this file were. Guarding inside the same expression, as
    /// <c>Assert(piano is not null &amp;&amp; piano.Regions.Count &gt; 0, …)</c> does, narrows correctly and
    /// never warned. Failing at the lookup also beats a null test further down: the message names the
    /// element that is missing instead of pointing at the twentieth line that used it.
    /// </summary>
    private static T Element<T>(FrameworkElement root, string name) where T : FrameworkElement
        => (T)(root.FindName(name) ?? throw new InvalidOperationException($"The window under test has no element named '{name}'."));
    /// <summary>
    /// Runs one check and, when it throws, says which check it was: an exception from deep inside a check
    /// (an index out of range in a grid, say) is otherwise reported without the context that names it.
    /// </summary>
    private static void Run(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { throw new InvalidOperationException($"{name}: {ex.Message}", ex); }
    }

    private static void Assert(bool condition, string message) { _assertions++; if (!condition) throw new InvalidOperationException(message); }
}
