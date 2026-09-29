using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            try { VerifyMidiImport(); VerifyVisualSettings(); VerifyShaderPipeline(); VerifyAviVideoRecorder(); VerifySoundFontEngine(); VerifyBundledPiano(); VerifyStereoHallReverb(); VerifyMidiDevicesAndKeyboardMap(); }
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
                    if (audio.HasSoundFont && soundFontLabel.Text.Contains("BUILT-IN", StringComparison.Ordinal))
                    {
                        timer.Stop();
                        try
                        {
                            Assert(soundFontLabel.Text.Contains("BUILT-IN", StringComparison.Ordinal), "A normal app startup should show the bundled piano as its active SoundFont.");
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
                    Assert(!audio.HasSoundFont && soundFontLabel.Text.Contains("SILENT", StringComparison.Ordinal), "Without the bundled SoundFont the app must start in silent mode instead of failing.");
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

    private static void VerifyVisualSettings()
    {
        var defaults = new PianoVisualSettings();
        Assert(!defaults.BackgroundGradient && !defaults.BackgroundGuide && !defaults.ShowStars && defaults.BackgroundImagePath == "", "A fresh visual profile should open on a black stage with no image or decorative background layers.");
        var migrated = PianoVisualSettings.FromJson("{\"BackgroundGradient\":true,\"BackgroundGuide\":true,\"ShowStars\":true,\"BackgroundImagePath\":\"C:\\\\piano.png\"}");
        Assert(!migrated.BackgroundGradient && !migrated.BackgroundGuide && !migrated.ShowStars && migrated.BackgroundAppearanceVersion == 2 && migrated.BackgroundImagePath == "C:\\piano.png" && migrated.BackgroundMode == "Image",
            "Legacy settings should switch to the black stage while preserving an optional selected image path and enabling image mode for it.");
        var settings = new PianoVisualSettings { NoteFallSpeed = 5000, ParticleAmount = 500, Palette = "not-a-palette", NoteGlow = 126.5, NoteStyle = "Plasma", ColorMode = "??", KeyboardStyle = "", BackgroundMode = "Blue", ShadingQuality = "Ultra", TrackColors = ["#FFFFFF"] };
        settings.Clamp();
        Assert(settings.NoteFallSpeed == 1000 && settings.ParticleAmount == 120 && settings.Palette == "Spectrum" && settings.NoteStyle == "Neon" && settings.ColorMode == "Gradient" && settings.KeyboardStyle == "Studio" && settings.BackgroundMode == "Solid" && settings.ShadingQuality == "Balanced" && settings.TrackColors.Count == 8 && settings.TrackColors[0] == "#FFFFFF",
            "Visual settings should clamp unsafe ranges, reject unknown palettes, styles, shading levels and modes, and pad the track palette.");
        var restored = PianoVisualSettings.FromJson(settings.ToJson());
        Assert(restored.NoteGlow == 126.5 && restored.ParticleAmount == 120 && restored.Palette == "Spectrum" && restored.TrackColors.SequenceEqual(settings.TrackColors), "Visual settings should round-trip through the persisted JSON format.");
        VerifyPresets();
        Assert(ColorPickerWindow.FromHsv(0, 1, 1) == Colors.Red && ColorPickerWindow.FromHsv(120, 1, 1) == Colors.Lime && ColorPickerWindow.FromHsv(240, 1, 1) == Colors.Blue, "The color picker should correctly convert the primary HSV hues.");
        var purple = ColorPickerWindow.ToHsv(Color.FromRgb(128, 0, 128));
        Assert(Math.Abs(purple.Hue - 300) < .01 && Math.Abs(purple.Saturation - 1) < .01 && ColorPickerWindow.ToHex(ColorPickerWindow.FromHsv(purple.Hue, purple.Saturation, purple.Value)) == "#800080", "The color picker should round-trip custom RGB colors through HSV and hex.");
        Results.Add("PASS stage settings: black background defaults/migration, color picker HSV/hex conversion, range limits and JSON round-trip.");
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
        VerifyBackgroundImageLoad(stage, visualSettings);
        var frameCapture = (byte[])window.GetType().GetMethod("CaptureStageBgr", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [64, 48])!;
        Assert(frameCapture.Length == AviVideoRecorder.BgrStride(64) * 48, "The on-screen piano stage should render into correctly-strided video frames.");
        var glowSlider = sliders[nameof(PianoVisualSettings.NoteGlow)]; var originalGlow = glowSlider.Value; glowSlider.Value = 127;
        Assert(visualSettings.NoteGlow == 127 && ReferenceEquals(Field(stage, "_visual"), visualSettings), "Adjusting note bloom should update the stage renderer immediately.");
        glowSlider.Value = originalGlow; ((DispatcherTimer)Field(window, "_settingsSaveTimer")).Stop();
        var wasShowingEmbers = visualSettings.ShowEmbers;
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
        Assert(!piano.HasSoundFont && !((ComboBox)window.FindName("PresetCombo")).IsEnabled && silentLabel.Text.Contains("SILENT"), "The initial UI must expose silent mode until a SoundFont is loaded.");
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
                Invoke(window, "PopulateTracks"); Invoke(window, "UpdateSongUi"); Invoke(window, "UpdatePlaybackLabel");
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
        Assert(tabs.Items.Count == 10 && ((TabItem)tabs.Items[0]).Header.ToString() == "Style" && ((TabItem)tabs.Items[9]).Header.ToString() == "Recording", "The settings dock should expose ten categorized pages from Style to Recording.");
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
        var search = (TextBox)window.FindName("SettingsSearchBox");
        search.Text = "wisp";
        var rows = colorRows.Cast<object>().Select(r => (FrameworkElement)r.GetType().GetField("Element")!.GetValue(r)!).ToList();
        Assert(rows.Count > 40 && rows.Count(r => r.Visibility == Visibility.Visible) < rows.Count / 2, "Searching should hide the rows that do not match.");
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
        Results.Add("PASS settings dock: ten pages, style/color-mode controls, per-hand and per-track colors, search filter, preset application and the ray-traced keyboard switch.");
    }

    /// <summary>Forces the stage to draw now so the shading state can be asserted synchronously.</summary>
    private static void ForceStageRender(PianoStage stage)
    {
        var width = Math.Max(1, (int)stage.ActualWidth); var height = Math.Max(1, (int)stage.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
    }

    /// <summary>The dock switch must really change what the stage renders, and the bake must be cached.</summary>
    private static void VerifyShadedStage(PianoStage stage, Dictionary<string, ComboBox> choices)
    {
        var shading = choices[nameof(PianoVisualSettings.ShadingQuality)];
        Assert((string?)shading.SelectedValue == "Balanced", "The default look should start on the Balanced shading engine.");
        ForceStageRender(stage);
        Assert(stage.IsShadedKeyboardActive && stage.ShadedBakeCount >= 1,
            $"The default look should drive the stage with the ray-traced keyboard (bakes={stage.ShadedBakeCount}, last bake={stage.ShadedBakeMilliseconds:0.0} ms).");
        var bakes = stage.ShadedBakeCount;
        ForceStageRender(stage); ForceStageRender(stage);
        Assert(stage.ShadedBakeCount == bakes, "The baked keyboard must be reused between frames; only a settings or size change may re-bake it.");
        shading.SelectedValue = "Off";
        ForceStageRender(stage);
        var liveVisual = (PianoVisualSettings)Field(stage, "_visual");
        Assert(!stage.IsShadedKeyboardActive,
            $"Turning the shading engine off must fall back to the flat vector keyboard (quality={liveVisual.ShadingQuality}, selected={shading.SelectedValue}, items={shading.Items.Count}).");
        shading.SelectedValue = "Balanced";
        ForceStageRender(stage);
        Assert(stage.IsShadedKeyboardActive && stage.ShadedBakeCount > bakes, "Switching the shading engine back on should re-bake and restore the ray-traced keyboard.");
    }

    private static void VerifyEmbersShell(MainWindow window, PianoVisualSettings visualSettings)
    {
        var menu = (FrameworkElement)window.FindName("MainMenuOverlay")!;
        var play = (FrameworkElement)window.FindName("PlayDialogOverlay")!;
        Assert(menu is not null && play is not null, "The Embers-style shell should provide a main menu and a pre-flight play dialog.");
        Assert(menu.Visibility == Visibility.Collapsed && play.Visibility == Visibility.Collapsed, "Automated runs should start on the live stage with the menu closed.");
        window.ShowStartupMenu();
        Assert(menu.Visibility == Visibility.Visible, "The home path should open the main menu over the stage.");
        Invoke(window, "MainMenuPlay_Click", window, new RoutedEventArgs());
        Assert(menu.Visibility == Visibility.Collapsed && play.Visibility == Visibility.Visible, "Choosing Play on the main menu should open the pre-flight dialog.");
        var notesToggle = (CheckBox)window.FindName("LayerNotesToggle")!;
        Assert(notesToggle.IsChecked == visualSettings.ShowNotes, "Play-dialog layer switches should mirror the live stage settings.");
        notesToggle.IsChecked = false;
        Assert(!visualSettings.ShowNotes, "Switching the Notes layer off in the play dialog should update the stage settings.");
        notesToggle.IsChecked = true;
        Assert(visualSettings.ShowNotes, "Switching the Notes layer back on should restore the stage settings.");
        Invoke(window, "PlayDialogClose_Click", window, new RoutedEventArgs());
        Assert(play.Visibility == Visibility.Collapsed, "The play dialog close button should return to the stage.");
    }

    private static void VerifyBackgroundImageLoad(PianoStage stage, PianoVisualSettings settings)
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-background-{Guid.NewGuid():N}.png");
        var originalPath = settings.BackgroundImagePath;
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

            settings.BackgroundImagePath = path + ".missing";
            stage.SetVisualSettings(settings);
            Assert(!stage.HasBackgroundImage && !string.IsNullOrWhiteSpace(stage.BackgroundLoadError), "An unreadable or missing background should expose a useful load error without crashing the stage.");
        }
        finally
        {
            settings.BackgroundImagePath = originalPath;
            stage.SetVisualSettings(settings, reloadBackground: true);
            if (File.Exists(path)) File.Delete(path);
        }
        Results.Add("PASS background images: changed-path detection, PNG decoding, file release and missing-file diagnostics.");
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
        SetField(window, "_position", 3.1); Invoke(window, "Tick"); Assert((double)Field(window, "_position") < 1.1, "Playback should wrap from B to A.");

        mode.SelectedIndex = 1;
        foreach (var note in ((IEnumerable<NoteEvent>)Field(window, "_notes")).Where(n => n.Start < .99)) note.Played = true;
        SetField(window, "_position", 1.2); ((Stopwatch)Field(window, "_clock")).Restart(); Invoke(window, "StartPlayback"); Invoke(window, "Tick");
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
        Results.Add("PASS WPF: idle auto-hide of toolbar and settings, mouse reveal, Escape toggling Stage Design, live AVI frame capture, duration-scaled notes, pedals and MIDI practice controls.");
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
