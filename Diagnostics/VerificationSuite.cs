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
internal static class VerificationSuite
{
    private static readonly List<string> Results = [];
    private static int _assertions;

    public static void Run(string[] args, App app)
    {
        Results.Clear(); _assertions = 0;
            try { VerifyMidiImport(); VerifyMusicXmlImport(); VerifyHandSplitInference(); VerifyPresetShareCodes(); VerifyVisualSettings(); VerifyShaderPipeline(); VerifyAviVideoRecorder(); VerifySoundFontEngine(); VerifyBundledPiano(); VerifyStereoHallReverb(); VerifyMidiDevicesAndKeyboardMap(); }
        catch (Exception ex) { Finish(app, args, ex); return; }

        var bundledPiano = Path.Combine(AppContext.BaseDirectory, "Assets", "ConcertGrand.sf2");
        var bundledPianoAvailable = File.Exists(bundledPiano) && !SoundFontReader.IsLfsPointer(bundledPiano);
        // A real ~113 MiB bank needs a few seconds to decode; a checkout without Git LFS settles at once.
        var deadline = DateTime.UtcNow.Add(bundledPianoAvailable ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(1.5));
        var startupWindow = new MainWindow { WindowState = WindowState.Normal, Width = 1240, Height = 780, SuppressErrorDialogs = true };
        startupWindow.ContentRendered += (_, _) =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) =>
            {
                var audio = (PianoAudioEngine)Field(startupWindow, "_audio");
                var soundFontLabel = (TextBlock)startupWindow.FindName("SoundFontLabel");
                if (bundledPianoAvailable)
                {
                    if (audio.HasSoundFont && soundFontLabel.Text != Loc.T("NO SOUNDFONT · SILENT"))
                    {
                        timer.Stop();
                        try
                        {
                            Assert(soundFontLabel.Text != Loc.T("NO SOUNDFONT · SILENT"), "A normal app startup should show the bundled piano as its active SoundFont.");
                            Assert(((ComboBox)startupWindow.FindName("PresetCombo")).IsEnabled && ((ComboBox)startupWindow.FindName("PresetCombo")).SelectedIndex == 0, "Startup should select the bundled acoustic-grand preset.");
                            Assert(((ToggleButton)startupWindow.FindName("ReverbToggle")).IsChecked == true && audio.ReverbEnabled, "The bundled piano should start with hall reverb active in the UI and audio engine.");
                            Results.Add($"PASS WPF startup: bundled grand loads automatically, its preset is selected and room reverb starts enabled{(audio.HasAudioOutput ? "" : $"; no audio device here ({audio.PlaybackError}) so playback is silent")}.");
                            startupWindow.Close();
                            VerifyPracticeWindow(args, app);
                        }
                        catch (Exception ex) { Finish(app, args, ex); }
                    }
                    else if (DateTime.UtcNow >= deadline) { timer.Stop(); Finish(app, args, new TimeoutException("The bundled grand did not finish loading during the startup smoke check.")); }
                    return;
                }
                // Git LFS was skipped, so there is no bank to load: the app must degrade to silent mode.
                if (DateTime.UtcNow < deadline) return;
                timer.Stop();
                try
                {
                    Assert(!audio.HasSoundFont && soundFontLabel.Text == Loc.T("NO SOUNDFONT · SILENT"), "Without the bundled SoundFont the app must start in silent mode instead of failing.");
                    Assert(!((ComboBox)startupWindow.FindName("PresetCombo")).IsEnabled, "The instrument picker must stay disabled while no SoundFont is loaded.");
                    Results.Add("SKIP WPF startup bundled-piano checks: Assets/ConcertGrand.sf2 is a Git LFS pointer; verified the silent-mode startup path instead.");
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
        var window = new MainWindow(loadBuiltInSoundFont: false) { WindowState = WindowState.Normal, Width = 1240, Height = 780, SuppressErrorDialogs = true };
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
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("BAD!")); var rejected = false;
        try { MidiReader.Read(path); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Invalid MIDI headers should be rejected cleanly.");
        Results.Add("PASS MIDI: multi-track import, tempo map, beat grid, track names, percussion skip, timing, velocity and malformed input.");
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
        VerifyPresets();
        Assert(ColorPickerWindow.FromHsv(0, 1, 1) == Colors.Red && ColorPickerWindow.FromHsv(120, 1, 1) == Colors.Lime && ColorPickerWindow.FromHsv(240, 1, 1) == Colors.Blue, "The color picker should correctly convert the primary HSV hues.");
        var purple = ColorPickerWindow.ToHsv(Color.FromRgb(128, 0, 128));
        Assert(Math.Abs(purple.Hue - 300) < .01 && Math.Abs(purple.Saturation - 1) < .01 && ColorPickerWindow.ToHex(ColorPickerWindow.FromHsv(purple.Hue, purple.Saturation, purple.Value)) == "#800080", "The color picker should round-trip custom RGB colors through HSV and hex.");
        Results.Add("PASS stage settings: automated runs isolated from the user settings folder, black background defaults/migration, color picker HSV/hex conversion, range limits and JSON round-trip.");
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

    private static void VerifySoundFontEngine()
    {
        var path = Path.Combine(Path.GetTempPath(), "keyflow-test-soundfont.sf2"); File.WriteAllBytes(path, CreateTestSoundFont());
        var font = SoundFontReader.Read(path);
        Assert(font.Name == "Keyflow test piano" && font.Presets.Count == 1 && font.Presets[0].Name == "Test Grand", "SF2 metadata and instrument zones should load.");
        Assert(font.Presets[0].Regions.Count == 1 && font.Presets[0].Regions[0].RootKey == 69, "Preset zones should map a sample to its root note.");
        Assert(font.SampleCount > 1000 && font.ReadSample(1) != 0 && font.Presets[0].Regions[0].Start == 0 && font.Presets[0].Regions[0].End > 1000, "SF2 sample bytes and sample boundaries should be retained.");
        var synth = new SoundFontSynthesizer(font, 22050); synth.NoteOn(0, 69, 100);
        var first = new short[4410 * 2]; synth.Render(first, 4410);
        var testRegion = font.Presets[0].Regions[0];
        Assert(first.Any(sample => sample != 0), $"A loaded SoundFont should render non-silent sampled audio (voices={synth.ActiveVoiceCount}, sample1={font.ReadSample(1)}, sampleCount={font.SampleCount}, range={testRegion.Start}..{testRegion.End}, rate={testRegion.SampleRate}, attack={testRegion.AttackSeconds}, sustain={testRegion.SustainLevel}, gain={testRegion.Attenuation}, pan={testRegion.Pan}).");
        synth.NoteOff(0, 69); var release = new short[4410 * 2]; synth.Render(release, 4410);
        Assert(release.Any(sample => sample != 0), "SoundFont Note Off should preserve the release tail.");
        var tail = new short[22050 * 2]; synth.Render(tail, 22050);
        Assert(tail.All(sample => sample == 0), "A released SoundFont note should eventually stop consuming voices.");

        var sustainSynth = new SoundFontSynthesizer(font, 22050);
        sustainSynth.ProcessMidi(0, 0xB0, 64, 127); sustainSynth.NoteOn(0, 69, 100);
        var heldAudio = new short[2205 * 2]; sustainSynth.Render(heldAudio, 2205); sustainSynth.NoteOff(0, 69);
        var sustainAudio = new short[2205 * 2]; sustainSynth.Render(sustainAudio, 2205);
        Assert(sustainAudio.Any(sample => sample != 0) && sustainSynth.ActiveVoiceCount == 1, "Sustain CC 64 should keep a released key sounding.");
        sustainSynth.ProcessMidi(0, 0xB0, 64, 0); var sustainTail = new short[22050 * 2]; sustainSynth.Render(sustainTail, 22050);
        Assert(sustainSynth.ActiveVoiceCount == 0, "Releasing sustain CC 64 should release held voices.");

        var layeredPath = Path.Combine(Path.GetTempPath(), "keyflow-test-soundfont-layered.sf2"); File.WriteAllBytes(layeredPath, CreateTestSoundFont(layered: true));
        var layered = SoundFontReader.Read(layeredPath).Presets[0].Regions[0];
        Assert(Math.Abs(layered.Attenuation - Math.Pow(10, -90 / 200.0)) < 1e-6, "Instrument-local generators should replace global ones and preset generators should be added on top (SF2 §8.5 / §9.4).");
        Assert(layered.SampleMode == 1 && Math.Abs(layered.ReleaseSeconds - 1) < .01, "Instrument global generators should apply, and a preset offset should add to the generator default.");
        var loopSynth = new SoundFontSynthesizer(SoundFontReader.Read(layeredPath), 22050); loopSynth.NoteOn(0, 69, 100);
        var loopHeld = new short[4410 * 2]; loopSynth.Render(loopHeld, 4410); loopSynth.NoteOff(0, 69);
        var loopRelease = new short[8820 * 2]; loopSynth.Render(loopRelease, 8820);
        Assert(loopRelease.Skip(8820 * 2 - 400).Any(sample => sample != 0), "A continuously looped sample should keep looping through its release tail instead of running off the sample end.");
        var loopTail = new short[22050 * 2]; loopSynth.Render(loopTail, 22050);
        Assert(loopSynth.ActiveVoiceCount == 0, "A looped voice should still be freed once its release envelope finishes.");
        var polySynth = new SoundFontSynthesizer(font, 22050); for (var i = 0; i < 120; i++) polySynth.NoteOn(0, 60 + i % 12, 100);
        Assert(polySynth.ActiveVoiceCount <= 96, "Voice stealing should keep polyphony within the voice limit.");

        var sostenutoSynth = new SoundFontSynthesizer(font, 22050); sostenutoSynth.NoteOn(0, 60, 100);
        var beforeCapture = new short[2205 * 2]; sostenutoSynth.Render(beforeCapture, 2205);
        sostenutoSynth.ProcessMidi(0, 0xB0, 66, 127); sostenutoSynth.NoteOff(0, 60);
        sostenutoSynth.NoteOn(0, 64, 100); var laterVoice = new short[2205 * 2]; sostenutoSynth.Render(laterVoice, 2205); sostenutoSynth.NoteOff(0, 64);
        var sostenutoAudio = new short[4410 * 2]; sostenutoSynth.Render(sostenutoAudio, 4410);
        Assert(sostenutoSynth.ActiveVoiceCount == 1, "Sostenuto CC 66 should hold notes already down, but release notes played afterward.");
        sostenutoSynth.ProcessMidi(0, 0xB0, 66, 0); var sostenutoTail = new short[22050 * 2]; sostenutoSynth.Render(sostenutoTail, 22050);
        Assert(sostenutoSynth.ActiveVoiceCount == 0, "Releasing sostenuto CC 66 should release captured voices.");

        var normalSynth = new SoundFontSynthesizer(font, 22050); var normalAudio = new short[2205 * 2]; normalSynth.NoteOn(0, 69, 100); normalSynth.Render(normalAudio, 2205);
        var softSynth = new SoundFontSynthesizer(font, 22050); softSynth.ProcessMidi(0, 0xB0, 67, 127); var softAudio = new short[2205 * 2]; softSynth.NoteOn(0, 69, 100); softSynth.Render(softAudio, 2205);
        var normalLevel = normalAudio.Select(x => Math.Abs((int)x)).Average(); var softLevel = softAudio.Select(x => Math.Abs((int)x)).Average();
        Assert(softLevel < normalLevel * .82, "Soft CC 67 should reduce volume and mellow the rendered sample.");
        var rejected = false;
        try { _ = SoundFontReader.Read(Path.Combine(Path.GetTempPath(), "missing-soundfont.sf2")); } catch (FileNotFoundException) { rejected = true; }
        Assert(rejected, "A missing SoundFont should be reported rather than replaced by a fallback tone.");
        bool engineOutputAvailable;
        using (var engine = new PianoAudioEngine())
        {
            Assert(!engine.HasSoundFont, "Integrated piano audio must stay silent before a SoundFont is loaded.");
            engine.LoadSoundFont(path); Assert(engine.HasSoundFont && engine.Presets.Count == 1, "Loading an SF2 should activate its presets and audio stream.");
            Assert(engine.HasAudioOutput || engine.PlaybackError is not null, "Without an audio device the engine must say why it is silent instead of pretending to play.");
            engineOutputAvailable = engine.HasAudioOutput;
            if (engineOutputAvailable) { engine.NoteOn(69, 95); Thread.Sleep(110); engine.NoteOff(69); Thread.Sleep(60); }
            else Results.Add($"NOTE audio device unavailable here ({engine.PlaybackError}); the engine stayed loaded and playable in silent mode.");
            engine.UnloadSoundFont(); Assert(!engine.HasSoundFont, "Unloading the SoundFont should return to silent mode.");
        }
        Results.Add($"PASS SoundFont: SF2 playback, generator override/offset semantics, loop-through-release, voice stealing, held-note release, sustain/sostenuto/soft pedal synthesis, {(engineOutputAvailable ? "waveOut output" : "silent-mode fallback")} and silent unload.");
    }

    private static void VerifyBundledPiano()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ConcertGrand.sf2");
        Assert(File.Exists(path), "The bundled Yamaha grand SoundFont should be copied beside the built application.");
        if (SoundFontReader.IsLfsPointer(path))
        {
            // CI checks out with `lfs: false` and a plain clone may skip `git lfs pull`; the 134-byte
            // pointer is not a bank, so report a skip instead of failing the whole suite over it.
            Results.Add("SKIP bundled piano: Assets/ConcertGrand.sf2 is a Git LFS pointer. Run 'git lfs install' then 'git lfs pull' to verify the real Yamaha grand.");
            return;
        }
        var font = SoundFontReader.Read(path);
        Assert(font.Name.Contains("Grand Piano", StringComparison.OrdinalIgnoreCase), "The bundled SoundFont should identify its grand-piano program in the SF2 metadata.");
        var piano = font.Presets.FirstOrDefault(p => p.Bank == 0 && p.Program == 0);
        Assert(piano is not null && piano.Regions.Count > 0, "The bundled SF2 should contain the standard acoustic-grand preset.");
        var synth = new SoundFontSynthesizer(font, 22050); synth.NoteOn(0, 60, 96);
        var attack = new short[4410 * 2]; synth.Render(attack, 4410);
        Assert(attack.Any(sample => sample != 0), "The bundled Yamaha grand preset should produce audio for middle C.");
        synth.NoteOff(0, 60); var release = new short[4410 * 2]; synth.Render(release, 4410);
        Assert(release.Any(sample => sample != 0), "The bundled piano should keep its recorded release after key-up.");
        using var engine = new PianoAudioEngine(); engine.LoadSoundFont(path);
        Assert(engine.HasSoundFont && engine.Presets.Any(p => p.Program == 0), "The real bundled piano SF2 should open in the production waveOut engine.");
        engine.NoteOn(60, 96); Thread.Sleep(80); engine.NoteOff(60); Thread.Sleep(60); engine.UnloadSoundFont();
        Results.Add($"PASS built-in piano: {font.Name}, {font.Presets.Count} preset(s), {font.SampleCount:N0} samples, acoustic-grand audio and live waveOut playback.");
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
        service.NoteChanged += (pitch, velocity, pressed) => messages.Add((pitch, velocity, pressed));
        service.PedalChanged += (pedal, down) => pedals.Add((pedal, down));
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
        Assert(overlay.Visibility == Visibility.Collapsed && rowDefinitions[0].Height.Value == 0 && rowDefinitions[2].Height.Value == 0, "An idle pointer should hide the toolbar, menu and REC so only the stage remains.");
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
        Assert(sliders.Count >= 30 && ReferenceEquals(Field(stage, "_visual"), visualSettings), "The detailed scene, note, particle and camera controls should drive the renderer configuration.");
        var colorInputs = (Dictionary<string, TextBox>)Field(window, "_visualColorInputs"); var colorButtons = (Dictionary<string, Button>)Field(window, "_visualColorButtons");
        Assert(colorInputs.ContainsKey(nameof(PianoVisualSettings.NoteColorStart)) && colorInputs.ContainsKey(nameof(PianoVisualSettings.NoteColorEnd)) && colorInputs.ContainsKey(nameof(PianoVisualSettings.HaloColor)) && colorInputs.ContainsKey(nameof(PianoVisualSettings.LeftHandColor)) && colorButtons.Count >= 6 && colorButtons.Count == colorInputs.Count,
            "Live design settings should provide an interactive color picker for the note gradient, hands, halo, keys and background colors.");
        VerifySettingsDock(window, stage, visualSettings);
        VerifyLanguageSwitching(window);
        VerifyAccessibility(window);
        VerifyDockAccessibility(window);
        VerifySettingsHistory(window);
        VerifySettingsProfile(window);
        VerifyPresetSharing(window);
        VerifyPresetThumbnails(window);
        VerifyCommunityPresets(window);
        VerifyUserShellThemes(window);
        VerifyPngSequenceRecorder(window);
        VerifySheetLayer(window, stage, visualSettings);
        VerifySongFolderLibrary(window);
        VerifyBackgroundImageLoad(window, stage, visualSettings);
        var frameCapture = (byte[])window.GetType().GetMethod("CaptureStageBgr", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [64, 48])!;
        Assert(frameCapture.Length == AviVideoRecorder.BgrStride(64) * 48, "The on-screen piano stage should render into correctly-strided video frames.");
        var glowSlider = sliders[nameof(PianoVisualSettings.NoteGlow)]; var originalGlow = glowSlider.Value; glowSlider.Value = 127;
        Assert(visualSettings.NoteGlow == 127 && ReferenceEquals(Field(stage, "_visual"), visualSettings), "Adjusting note bloom should update the stage renderer immediately.");
        glowSlider.Value = originalGlow; ((DispatcherTimer)Field(window, "_settingsSaveTimer")).Stop();
        var wasShowingEmbers = visualSettings.ShowEmbers;
        // The claim is about bursts this impact would add, so anything still in the air from a check above is
        // cleared first: flames left burning by an earlier preset would otherwise keep emitting on the clock.
        stage.ClearTransient();
        visualSettings.ShowEmbers = false; stage.SetVisualSettings(visualSettings); stage.Impact(60);
        Assert(stage.SparkCount == 0, "Turning off the ember layer should stop new particle bursts.");
        visualSettings.ShowEmbers = wasShowingEmbers; stage.SetVisualSettings(visualSettings);
        VerifyEmbersShell(window, visualSettings);
        var piano = (PianoAudioEngine)Field(window, "_audio"); var silentLabel = (TextBlock)window.FindName("SoundFontLabel");
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
        Assert(!piano.HasSoundFont && !((ComboBox)window.FindName("PresetCombo")).IsEnabled && silentLabel.Text == Loc.T("NO SOUNDFONT · SILENT"), "The initial UI must expose silent mode until a SoundFont is loaded.");
        var reverb = (ToggleButton)window.FindName("ReverbToggle");
        Assert(reverb.IsChecked == true && piano.ReverbEnabled, "The built-in concert room reverb should start enabled.");
        reverb.IsChecked = false; Assert(!piano.ReverbEnabled, "The reverb control should bypass the live audio effect.");
        reverb.IsChecked = true; Assert(piano.ReverbEnabled, "Reverb should be switchable back on while the piano is running.");
        Assert(!(bool)Field(window, "_playing") && ((IEnumerable<NoteEvent>)Field(window, "_notes")).Count() == 0 && !((Button)window.FindName("PlayButton")).IsEnabled, "Startup should be live-play only with no demo MIDI or automatic transport.");
        Invoke(window, "PressNote", 60, 90);
        Assert(!(bool)Field(window, "_playing") && Math.Abs((double)Field(window, "_position")) < .001, "A live piano key must not start or advance MIDI playback.");
        Assert(stage.LiveTrailCount == 1 && stage.SparkCount >= 12, "A keypress should create a short falling note and a neon impact burst.");
        Assert(!piano.HasSoundFont, "Keyboard input should not synthesize a replacement piano voice in silent mode.");
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
        Assert(names.Count >= 6 && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count && names.Contains(VisualPresets.DefaultPresetName), "Built-in presets should have unique names and include the default look.");
        foreach (var preset in VisualPresets.BuiltIn)
        {
            var copy = preset.Settings.Clone(); copy.Clamp();
            Assert(copy.ToJson() == preset.Settings.ToJson(), $"The built-in preset '{preset.Name}' should already be within the allowed ranges.");
        }
        Assert(VisualPresets.FindBuiltIn("inferno")!.Settings.NoteStyle == "Fire" && VisualPresets.FindBuiltIn("Aurora Rainbow")!.Settings.ShowWisps && VisualPresets.FindBuiltIn("Green Screen")!.Settings.BackgroundMode == "ChromaGreen",
            "The reference looks (burning notes, rainbow wisps, chroma key) should map to the matching renderer options.");
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
            Assert(saved.Name == "My- Look-" && loaded.Count == 1 && loaded[0].Name == saved.Name && loaded[0].Settings.ShowWisps && loaded[0].Settings.BackgroundImagePath == "" && !loaded[0].BuiltIn,
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
        Assert(choices.ContainsKey(nameof(PianoVisualSettings.NoteStyle)) && choices.ContainsKey(nameof(PianoVisualSettings.ColorMode)) && choices.ContainsKey(nameof(PianoVisualSettings.KeyboardStyle)) && choices.ContainsKey(nameof(PianoVisualSettings.BackgroundMode)) && toggles.ContainsKey(nameof(PianoVisualSettings.ShowWisps)),
            "Note style, color mode, keyboard style, background mode and wisps should be editable from the dock.");
        var originalStyle = visualSettings.NoteStyle; var originalMode = visualSettings.ColorMode;
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
        VerifyImpactFx(window, stage, visualSettings, choices);
        VerifyFallingFx(window, stage, visualSettings, choices);
        VerifyHoldFx(window, stage, visualSettings);
        VerifyReleaseFx(window, stage, visualSettings, choices);
        VerifyAmbientFx(window, stage, visualSettings, choices);
        VerifySmartFx(window, stage, visualSettings);
        VerifyThemes();
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
        Assert(visualSettings.PresetName == "Classic Roll" && !visualSettings.PresetModified && visualSettings.NoteStyle == "Solid" && !visualSettings.ShowEmbers && ReferenceEquals(Field(stage, "_visual"), visualSettings),
            "Applying a preset should rewrite the live settings in place so the renderer keeps its reference.");
        visualSettings.CopyFrom(PianoVisualSettings.FromJson(beforeJson), keepBackgroundImage: false); visualSettings.PresetName = beforeName;
        Invoke(window, "RefreshSettingControls"); stage.SetVisualSettings(visualSettings);
        VerifyShadedStage(stage, choices);
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
        Loc.Apply("en");
        Assert(firstHeader() == "Style" && Loc.T("Falling notes") == "Falling notes", "Switching back to English must restore every caption.");
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

        // The glyphs that used to be anonymous: the loop buttons show "A" / "B" / "×" and the tooltip a
        // screen reader hears is the localized sentence, not the letter.
        FrameworkElement? ByContent(string content) => controls.FirstOrDefault(element => element is ContentControl { Content: string text } && text == content);
        var loopA = ByContent("A"); var loopB = ByContent("B"); var clearLoop = ByContent("×");
        Assert(loopA is not null && loopB is not null && clearLoop is not null,
            "The transport should expose the A / B / × loop buttons that this check names.");
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
        Assert(PracticeHistory.Runs.Count == PracticeHistory.Capacity && PracticeHistory.Runs[0].Song == $"Take {PracticeHistory.Capacity + 2}",
            "The history should keep the newest runs and stop growing at its cap.");
        File.AppendAllText(PracticeHistory.FilePath, "{ half a line" + Environment.NewLine);
        PracticeHistory.Reload();
        Assert(PracticeHistory.Runs.Count == PracticeHistory.Capacity && PracticeHistory.Summary().Count > 1,
            "A damaged line should be skipped while the rest of the history file still reads.");

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
        Results.Add("PASS practice history: isolated history file with one line per run, newest-first cap, damaged line tolerated, best take per song, UTF-8 HTML report with the per-song summary, and the stop-the-transport recording path.");
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
            SheetLayer.Draw(dc, area, song, 1.2, 60, beats, 4, 8, ink, accent, 1, 1);
        var empty = new DrawingVisual();
        using (var dc = empty.RenderOpen())
            SheetLayer.Draw(dc, area, [], 0, 60, [], 4, 8, ink, accent, 1, 1);
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
        Assert(ReferenceEquals(Field(stage, "_beats"), beats), "The stage should keep the grid the song was loaded with for the sheet's bar lines.");
        visualSettings.HandSplitPitch = previousSplit; stage.SetVisualSettings(visualSettings);
        sheetToggle.IsChecked = wasShowing;
        stage.SetSheet([], 4);
        stage.ClearTransient();
        Results.Add($"PASS Sheet layer: written pitch and staff placement from the hand split, ledger lines outside the staff, hollow and stemmed heads, the playhead window, the clef by font, the note colours, and both staves drawn from the stage's own song and grid (glyphs: {(SheetLayer.MusicGlyphsAvailable ? "musical" : "letters")}).");
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
        Assert(days[^1] is { Runs: 2, Hits: 3, Misses: 3 } && Math.Abs(days[^1].Accuracy - 50) < .01,
            $"Today's row should add up every run of the day (got {days[^1]}).");
        Assert(days[^2] is { Runs: 1, Hits: 2, Misses: 2 } && days.Take(days.Count - 2).All(day => day.Runs == 0 && day.Accuracy == 0),
            "Days without a run should still be rows, with zero runs and no accuracy.");
        Assert(PracticeHistory.Runs.Count == 4 && days.Sum(day => day.Runs) == 3,
            "A run older than the window should stay out of the chart while the history file still keeps it.");
        Assert(Math.Abs(PracticeHistory.AverageAccuracy(days) - 50) < .01
                && PracticeHistory.ChartCaption(days) == Loc.F("Accuracy by day: {0} runs · {1:0.#}% average", 3, PracticeHistory.AverageAccuracy(days)),
            $"The window's caption should name its runs and count every note graded in it once ({PracticeHistory.ChartCaption(days)}).");

        // The dock reads the same model.
        Invoke(window, "RefreshPracticeHistory");
        var bars = chart.Children.OfType<Rectangle>().ToList();
        var accent = ((SolidColorBrush)window.FindResource("AccentBrush")).Color; var track = ((SolidColorBrush)window.FindResource("TrackBrush")).Color;
        Assert(bars.Count == PracticeHistory.ChartDays && chartLabel.Text == PracticeHistory.ChartCaption(days),
            $"The History page should draw one bar per day and print the caption of the window ({bars.Count} bars).");
        Assert(Math.Abs(bars[^1].Height - PracticeChart.BarHeight(50, 52, 3)) < .001 && bars[0].Height == 0
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
            Assert(songs[0] is { Format: "mid", Notes: 3, Tracks: 1 } && songs[1] is { Format: "musicxml", Notes: 2, Tracks: 1 }
                    && Math.Abs(songs[1].BeatsPerMinute - 120) < .01 && songs[2].Path.StartsWith(sub, StringComparison.OrdinalIgnoreCase),
                "Facts should come from the files themselves: notes, tracks, tempo and the format of each one.");
            Assert(SongFolderIndex.Folder.EndsWith(Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase) && songs.All(song => song.Tags.Count == 0),
                "A fresh scan should remember the folder it read and start every song without tags.");
            Assert(songs.All(song => !song.Title.Contains("Broken", StringComparison.OrdinalIgnoreCase)) && songs.All(song => !song.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)),
                "A file the reader refuses, and a file that is not a song at all, should stay out of the library.");

            // The cache: an unchanged file is not read again, a changed one is, and the tags survive both.
            var cached = SongFolderIndex.Scan(folder);
            Assert(ReferenceEquals(cached[0], songs[0]) && ReferenceEquals(cached[1], songs[1]),
                "Scanning again should reuse the entries of files that did not change instead of re-reading them.");
            SongFolderIndex.Tag(etude, "Chopin");
            File.WriteAllBytes(etude, [.. CreateFormatOneMidi(), .. new byte[16]]);
            File.SetLastWriteTimeUtc(etude, DateTime.UtcNow.AddMinutes(1));
            var rescanned = SongFolderIndex.Scan(folder);
            var etudeEntry = rescanned.Single(song => string.Equals(song.Path, etude, StringComparison.OrdinalIgnoreCase));
            Assert(etudeEntry.Size != songs[0].Size && etudeEntry.Tags.SequenceEqual(["chopin"]),
                "Editing a file should make the next scan re-read it while its tags stay with it.");
            Assert(!ReferenceEquals(SongFolderIndex.Scan(folder, force: true)[1], rescanned[1]),
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

            // The Play dialog: the same list, the search box and the tagged chips.
            SongFolderIndex.Scan(folder);
            Invoke(window, "RefreshLibrarySongs");
            var host = (StackPanel)window.FindName("LibrarySongHost");
            var label = (TextBlock)window.FindName("LibraryFolderLabel");
            var empty = (TextBlock)window.FindName("LibraryEmptyLabel");
            var search = (TextBox)window.FindName("LibrarySearchBox");
            Assert(host.Children.Count == 3 && label.Text.StartsWith(Loc.F("{0} songs in {1}", 3, SongFolderIndex.Folder), StringComparison.Ordinal),
                $"The Play dialog should list the indexed songs and say where they came from ({label.Text}).");
            search.Text = "nocturne";
            Assert(host.Children.Count == 1 && empty.Text.Length == 0, "Typing in the search box should filter the library down to the matches.");
            search.Text = "zzz";
            Assert(host.Children.Count == 0 && empty.Text == Loc.T("No song in this folder matches what you typed. Tags and the file name are searched too."),
                "A query that matches nothing should say so instead of leaving an empty list.");
            search.Text = "chopin";
            var row = (Grid)host.Children[0];
            var chips = ((StackPanel)((StackPanel)row.Children[0]).Children[2]).Children.OfType<Button>().ToList();
            Assert(chips.Any(chip => (chip.Content as string) == "#chopin"), "A tagged song should show its tag as a chip in the library row.");
            search.Text = "";

            // The watcher: the list follows the disk, and a file added while it watches is indexed.
            Invoke(window, "StartSongFolderWatch", folder);
            var watcher = (SongFolderWatcher)Field(window, "_songWatcher")!;
            Assert(watcher.IsWatching && watcher.WatchedFolder == folder, "Choosing a folder should start watching it for changes.");
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
            Assert(UserThemeStore.NameConflict("Concert Noir") is not null && UserThemeStore.NameConflict("Sunset Glow") is null,
                "Naming a theme after a built-in look should be refused, while any other name stays free.");
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
        studio.NameBox.Text = "Sunset Glow"; studio.ColourBox(nameof(UserShellTheme.Glow)).Text = "purple";
        Assert(!studio.TryBuild(out _, out var hexError) && hexError == "One of the colours is not a hex value like #1A2B3C.",
            "The studio should refuse to save a colour that is not a hex value.");
        studio.ColourBox(nameof(UserShellTheme.Glow)).Text = "#FFB86B"; studio.BackdropBox.SelectedIndex = 1;
        Assert(studio.TryBuild(out var result, out var noError) && noError is null && result is not null
                && result.Backdrop == nameof(BackdropStyle.Obsidian) && UserShellThemes.Build(result).Backdrop == BackdropStyle.Obsidian,
            "A filled-in studio should hand back the theme the fields describe, backdrop family included.");
        Results.Add("PASS user themes: five seeds derive a readable twenty-token chrome, files round-trip and are repaired or skipped when damaged, an id resolves and applies through the pickers, and the studio refuses a nameless or non-hex theme.");
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
        Assert(EffectCatalog.Falling.All.All(e => e.Status == EffectStatus.Available),
            "The whole falling catalogue should be implemented in v2.");
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
        var wasEnergy = visualSettings.AmbientEnergy; var wasNature = visualSettings.AmbientNature;
        var wasLight = visualSettings.AmbientLight; var wasCosmic = visualSettings.AmbientCosmic;
        var wasWave = visualSettings.ImpactWave;
        visualSettings.AmbientEnergy = "Fireworks"; visualSettings.AmbientNature = "Snow";
        visualSettings.AmbientLight = "Prism"; visualSettings.AmbientCosmic = "Galaxy";
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
        stage.Impact(60, 1);
        Assert(stage.RingCount > 0, "A ripple impact should spawn a wave.");
        var ripples = new DrawingVisual();
        using (var dc = ripples.RenderOpen()) { Invoke(stage, "DrawRings", dc); }
        Assert(ripples.Drawing is not null && ripples.Drawing.Bounds.Width > 10, "Water ripples should paint geometry.");
        for (var i = 0; i < 40; i++) stage.Advance(.05);
        Assert(stage.RingCount == 0, "Ripple waves should fade out over time.");
        visualSettings.AmbientEnergy = wasEnergy; visualSettings.AmbientNature = wasNature;
        visualSettings.AmbientLight = wasLight; visualSettings.AmbientCosmic = wasCosmic;
        visualSettings.ImpactWave = wasWave;
        stage.SetVisualSettings(visualSettings);
        stage.ClearTransient();
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
    private static void VerifyShadedStage(PianoStage stage, Dictionary<string, ComboBox> choices)
    {
        var shading = choices[nameof(PianoVisualSettings.ShadingQuality)];
        Assert((string?)shading.SelectedValue == "Balanced", "The default look should start on the Balanced shading engine.");
        // Drive the shaded-keyboard pass directly with an explicit DrawingContext so the bake
        // accounting does not depend on WPF render scheduling or window resize churn.
        var off = new PianoStage();
        var settings = (PianoVisualSettings)Field(stage, "_visual");
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
        var menu = (FrameworkElement)window.FindName("MainMenuOverlay")!;
        var play = (FrameworkElement)window.FindName("PlayDialogOverlay")!;
        Assert(menu is not null && play is not null, "The Embers-style shell should provide a main menu and a pre-flight play dialog.");
        Assert(menu!.Visibility == Visibility.Collapsed && play!.Visibility == Visibility.Collapsed, "Automated runs should start on the live stage with the menu closed.");
        window.ShowStartupMenu();
        Assert(menu!.Visibility == Visibility.Visible, "The home path should open the main menu over the stage.");
        Invoke(window, "MainMenuPlay_Click", window, new RoutedEventArgs());
        Assert(menu!.Visibility == Visibility.Collapsed && play!.Visibility == Visibility.Visible, "Choosing Play on the main menu should open the pre-flight dialog.");
        var notesToggle = (CheckBox)window.FindName("LayerNotesToggle")!;
        Assert(notesToggle.IsChecked == visualSettings.ShowNotes, "Play-dialog layer switches should mirror the live stage settings.");
        notesToggle.IsChecked = false;
        Assert(!visualSettings.ShowNotes, "Switching the Notes layer off in the play dialog should update the stage settings.");
        notesToggle.IsChecked = true;
        Assert(visualSettings.ShowNotes, "Switching the Notes layer back on should restore the stage settings.");
        Invoke(window, "PlayDialogClose_Click", window, new RoutedEventArgs());
        Assert(play!.Visibility == Visibility.Collapsed, "The play dialog close button should return to the stage.");

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

    private static byte[] CreateTestSoundFont(bool layered = false)
    {
        const int sampleCount = 11025; var pcm = new byte[(sampleCount + 46) * 2];
        for (var i = 0; i < sampleCount; i++) { var value = (short)(Math.Sin(i * 2 * Math.PI * 440 / 22050) * 11000); BitConverter.GetBytes(value).CopyTo(pcm, i * 2); }
        var info = List("INFO", list => { Chunk(list, "ifil", Bytes(w => { U16(w, 2); U16(w, 1); })); Chunk(list, "isng", Encoding.ASCII.GetBytes("EMU8000\0")); Chunk(list, "INAM", Encoding.ASCII.GetBytes("Keyflow test piano\0")); });
        var sdta = List("sdta", list => Chunk(list, "smpl", pcm));
        var pdta = List("pdta", list =>
        {
            Chunk(list, "phdr", Bytes(w => { PresetHeader(w, "Test Grand", 0, 0, 0); PresetHeader(w, "EOP", 0, 0, 1); }));
            if (layered)
            {
                // Preset zone: +50 cB attenuation and +12000 timecents release (relative to the -12000 default → 1 s).
                Chunk(list, "pbag", Bytes(w => { U16(w, 0); U16(w, 0); U16(w, 3); U16(w, 0); }));
                Chunk(list, "pmod", new byte[10]);
                Chunk(list, "pgen", Bytes(w => { Generator(w, 48, 50); Generator(w, 38, 12000); Generator(w, 41, 0); }));
                Chunk(list, "inst", Bytes(w => { InstrumentHeader(w, "Piano", 0); InstrumentHeader(w, "EOI", 2); }));
                // Instrument global zone: 100 cB attenuation + continuous loop. Local zone overrides attenuation to 40 cB.
                Chunk(list, "ibag", Bytes(w => { U16(w, 0); U16(w, 0); U16(w, 2); U16(w, 0); U16(w, 4); U16(w, 0); }));
                Chunk(list, "imod", new byte[10]);
                Chunk(list, "igen", Bytes(w => { Generator(w, 48, 100); Generator(w, 54, 1); Generator(w, 48, 40); Generator(w, 53, 0); }));
            }
            else
            {
                Chunk(list, "pbag", Bytes(w => { U16(w, 0); U16(w, 0); U16(w, 1); U16(w, 0); }));
                Chunk(list, "pmod", new byte[10]);
                Chunk(list, "pgen", Bytes(w => { Generator(w, 41, 0); Generator(w, 0, 0); }));
                Chunk(list, "inst", Bytes(w => { InstrumentHeader(w, "Piano", 0); InstrumentHeader(w, "EOI", 1); }));
                Chunk(list, "ibag", Bytes(w => { U16(w, 0); U16(w, 0); U16(w, 1); U16(w, 0); }));
                Chunk(list, "imod", new byte[10]);
                Chunk(list, "igen", Bytes(w => { Generator(w, 53, 0); Generator(w, 0, 0); }));
            }
            Chunk(list, "shdr", Bytes(w => { SampleHeader(w, "Test sine", 0, sampleCount, 512, sampleCount - 512, 22050, 69); SampleHeader(w, "EOS", sampleCount, sampleCount, sampleCount, sampleCount, 0, 0); }));
        });
        using var body = new MemoryStream(); using (var writer = new BinaryWriter(body, Encoding.ASCII, true)) { writer.Write(Encoding.ASCII.GetBytes("sfbk")); WriteChunk(writer, "LIST", info); WriteChunk(writer, "LIST", sdta); WriteChunk(writer, "LIST", pdta); }
        using var result = new MemoryStream(); using (var writer = new BinaryWriter(result, Encoding.ASCII, true)) { writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)body.Length); body.Position = 0; body.CopyTo(result); }
        return result.ToArray();
    }
    private static byte[] List(string type, Action<BinaryWriter> children)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.ASCII, true); writer.Write(Encoding.ASCII.GetBytes(type)); children(writer); return stream.ToArray();
    }
    private static void Chunk(BinaryWriter writer, string id, byte[] data) => WriteChunk(writer, id, data);
    private static void WriteChunk(BinaryWriter writer, string id, byte[] data) { writer.Write(Encoding.ASCII.GetBytes(id)); writer.Write((uint)data.Length); writer.Write(data); if ((data.Length & 1) != 0) writer.Write((byte)0); }
    private static byte[] Bytes(Action<BinaryWriter> action) { using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.ASCII, true)) action(writer); return stream.ToArray(); }
    private static void PresetHeader(BinaryWriter w, string name, int program, int bank, int bag) { FixedName(w, name, 20); U16(w, program); U16(w, bank); U16(w, bag); w.Write(0u); w.Write(0u); w.Write(0u); }
    private static void InstrumentHeader(BinaryWriter w, string name, int bag) { FixedName(w, name, 20); U16(w, bag); }
    private static void SampleHeader(BinaryWriter w, string name, int start, int end, int loopStart, int loopEnd, int rate, int root)
    {
        FixedName(w, name, 20); w.Write((uint)start); w.Write((uint)end); w.Write((uint)loopStart); w.Write((uint)loopEnd); w.Write((uint)rate); w.Write((byte)root); w.Write((sbyte)0); U16(w, 0); U16(w, 1);
    }
    private static void Generator(BinaryWriter w, int op, int amount) { U16(w, op); U16(w, amount); }
    private static void FixedName(BinaryWriter w, string value, int length) { var bytes = new byte[length]; Encoding.ASCII.GetBytes(value).AsSpan(0, Math.Min(value.Length, length - 1)).CopyTo(bytes); w.Write(bytes); }
    private static void U16(BinaryWriter w, int value) => w.Write((ushort)value);

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
    private static void Assert(bool condition, string message) { _assertions++; if (!condition) throw new InvalidOperationException(message); }
}
