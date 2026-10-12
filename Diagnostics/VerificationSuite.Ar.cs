using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// Pins AR support to its evidence grade: six screen-space ambient families are Native, 14 S5 families
    /// remain Unverified despite sampled calibrated-3D WARP checks (the full choice/value matrix is incomplete),
    /// four unprojected families stay Unsupported, and note-material capability/parameter bindings remain
    /// Unverified although the shared shader renders on camera-facing quads. The WARP checks sample styles,
    /// not a full parameter matrix. VerifyArOverlayCapturedFrames checks ambient/suppression; VerifyArNoteProjection
    /// exercises selected S5 and note-material paths.
    /// </summary>
    private static void VerifyArOverlayExclusions()
    {
        var nativeInAr = new HashSet<string>(StringComparer.Ordinal)
        {
            "ambient.energy", "ambient.nature", "ambient.light", "ambient.cosmic",
            "atmosphere.shooting-stars", "atmosphere.petals",
        };
        var unverifiedInAr = new HashSet<string>(StringComparer.Ordinal)
        {
            "impact.wave", "impact.flash", "impact.burst", "impact.morph",
            "hold.bar", "hold.breath", "hold.vibration", "hold.color-cycle", "hold.arc",
            "atmosphere.beams", "particles.sparks", "particles.wisps", "particles.flames", "release.fx",
        };
        var families = EffectContractCatalog.AllFamilies;
        Assert(families.Count == 24, "AR classification should cover all 24 typed effect families.");
        Assert(families.Count(family => family.Capabilities.ArOverlay.State == EffectBackendState.Native) == 6
                && families.Count(family => family.Capabilities.ArOverlay.State == EffectBackendState.Unverified) == 14
                && families.Count(family => family.Capabilities.ArOverlay.State == EffectBackendState.Unsupported) == 4,
            "AR evidence must distinguish six verified screen-space families, 14 unverified S5 projections and four unsupported unprojected families.");
        Assert(nativeInAr.SetEquals(families.Where(family => family.Capabilities.ArOverlay.State == EffectBackendState.Native).Select(family => family.Id)),
            "Only the four ambient layers, shooting stars and petals are verified Native over the camera.");
        Assert(unverifiedInAr.SetEquals(families.Where(family => family.Capabilities.ArOverlay.State == EffectBackendState.Unverified).Select(family => family.Id)),
            "Sampled S5 WARP checks do not justify Native family support or Applied bindings for every option/value.");
        foreach (var family in families)
        {
            var expected = nativeInAr.Contains(family.Id) ? EffectBackendState.Native
                : unverifiedInAr.Contains(family.Id) ? EffectBackendState.Unverified
                : EffectBackendState.Unsupported;
            Assert(family.Capabilities.ArOverlay.State == expected,
                $"{family.Id} must match its current AR evidence grade ({expected}).");
            var expectedBinding = expected switch
            {
                EffectBackendState.Native => EffectParameterBindingState.Applied,
                EffectBackendState.Unverified => EffectParameterBindingState.Unverified,
                _ => EffectParameterBindingState.Ignored,
            };
            Assert(family.Parameters.All(parameter => parameter.Bindings.ArOverlay == expectedBinding),
                $"{family.Id} parameter bindings should match its AR evidence state ({expectedBinding}).");
            Assert(family.Capabilities.Recording.State == EffectBackendState.Native,
                $"{family.Id} may be Native for Recording only because its own family capture fixture reads back a real GPU take frame.");
            Assert(family.Parameters.All(parameter => parameter.Bindings.Recording == EffectParameterBindingState.Unverified),
                $"{family.Id} family-level capture must not claim that every individual setting is applied by Recording.");
            Assert(new[] { family.Cost.WpfStage, family.Cost.GpuStage, family.Cost.Recording, family.Cost.ArOverlay }
                    .All(cost => cost.State == EffectCostState.Unmeasured && cost.P95Milliseconds is null),
                $"{family.Id} pixel capture evidence must not invent a renderer or recording cost.");
        }
        foreach (var material in EffectContractCatalog.NoteMaterialPilot)
        {
            Assert(material.Capabilities.ArOverlay.State == EffectBackendState.Unverified
                    && material.Capabilities.ArOverlay.FallbackId is null,
                $"{material.Id} uses the shared material shader on AR quads, but its AR support remains Unverified.");
            Assert(material.Parameters.All(parameter => parameter.Bindings.ArOverlay == EffectParameterBindingState.Unverified),
                $"{material.Id} must keep per-setting AR bindings Unverified until their values are covered.");
        }
        Results.Add("PASS AR contract classification: six screen-space ambient families are Native; 14 calibrated S5 projection families remain Unverified after sampled WARP checks because the full style/value/parameter matrix is incomplete; four unprojected families stay Unsupported; note materials use the shared shader on camera-facing quads but their support and parameter bindings remain Unverified.");
    }

    /// <summary>
    /// AR-1.1, the camera picture as a GPU layer: frames taken from the ring, the texture kept and only re-created when the
    /// camera changes size, bad frames refused without harm, and — on one WARP device, read back — the picture landing
    /// where <see cref="ArView"/> says it does: fit with bars, fill with a crop, mirrored, dimmed, gone when the layer is
    /// removed, never in a chroma-key frame.
    /// </summary>
    private static void VerifyArCameraLayer()
    {
        const int width = 640, height = 360;
        var quiet = new PianoVisualSettings
        {
            BackgroundGradient = false, ShowStars = false, ShowHalo = false, ShowKeys = false, ShowNotes = false,
            ShowEmbers = false, ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowLightBeams = false, ShowPetals = false, HorizonGlow = 0, Vignette = 0, BloomIntensity = 0
        };

        // a frame whose blue grows to the right and green grows downward: any shift, flip or stretch changes the numbers
        static void Gradient(byte[] pixels, int w, int h)
        {
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = (y * w + x) * 4;
                    pixels[i] = (byte)Math.Round(x * 255.0 / (w - 1)); pixels[i + 1] = (byte)Math.Round(y * 255.0 / (h - 1)); pixels[i + 2] = 40; pixels[i + 3] = 255;
                }
        }
        static void PublishGradient(CameraFrameRing ring, int w, int h)
        {
            var buffer = ring.BeginWrite(w * h * 4, out var slot);
            Gradient(buffer, w, h);
            ring.Publish(slot, w, h, Stopwatch.GetTimestamp());
        }
        GpuStageFeed MakeFeed(GpuCameraInput? camera, bool chroma = false, string noteStyle = "Neon")
        {
            var settings = quiet.Clone(); settings.NoteStyle = noteStyle;
            if (chroma) settings.BackgroundMode = "ChromaGreen";
            var feed = new GpuStageFeed(); feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(settings, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
            feed.SetState([], 0, false, new HashSet<int>());
            feed.Camera = camera;
            return feed;
        }
        // what the picture holds under an output pixel: blue and green of the gradient at that place, sampled the way a bilinear fetch does
        static (double Blue, double Green) Expect(double ox, double oy, double left, double top, double scale, int fw, int fh, bool mirror)
        {
            var u = (ox + .5 - left) / scale; var v = (oy + .5 - top) / scale;
            if (mirror) u = fw - u;
            return (Math.Clamp(u - .5, 0, fw - 1) * 255.0 / (fw - 1), Math.Clamp(v - .5, 0, fh - 1) * 255.0 / (fh - 1));
        }

        var started = Stopwatch.StartNew();
        using var renderer = GpuStageRenderer.Create(forceWarp: true);
        string Check(byte[] pixels, int x, int y, double left, double top, double scale, int fw, int fh, bool mirror, double gain, string what)
        {
            var (blue, green) = Expect(x, y, left, top, scale, fw, fh, mirror);
            var i = (y * width + x) * 4;
            Assert(Math.Abs(pixels[i] - blue * gain) <= 4 && Math.Abs(pixels[i + 1] - green * gain) <= 4 && Math.Abs(pixels[i + 2] - 40 * gain) <= 4,
                $"{what}: at ({x},{y}) the camera picture should show B {blue * gain:0} G {green * gain:0} R {40 * gain:0}, got B {pixels[i]} G {pixels[i + 1]} R {pixels[i + 2]}.");
            return "";
        }
        byte[] Render(GpuStageFeed feed) => GpuRenderLoop.RenderFrames(renderer, feed, width, height, 0, 1 / 60.0, false, out _);
        (int X, int Y)[] points = [(40, 30), (320, 180), (600, 100), (100, 250), (610, 20)];

        // ---- the texture: made once, rewritten for each frame, remade for a new size --------------------------
        Assert(renderer.CameraTexture is null, "A renderer starts without a camera texture.");
        var ring = new CameraFrameRing();
        var input = new GpuCameraInput(ring, PictureFit.Fit, false, 1f);
        renderer.UpdateCamera(input);
        Assert(renderer.CameraTexture is null, "A camera layer with no frame yet must not create a texture or draw anything.");
        PublishGradient(ring, 160, 90);
        renderer.UpdateCamera(input);
        var texture = renderer.CameraTexture;
        Assert(texture is { Width: 160, Height: 90, Creations: 1, Uploads: 1, Sequence: 1 }, "The first frame should create a 160×90 texture and upload once.");
        renderer.UpdateCamera(input); renderer.UpdateCamera(input);
        Assert(texture!.Uploads == 1, "With no new frame the texture must not be written again (the camera may be slower than the screen).");
        PublishGradient(ring, 160, 90); PublishGradient(ring, 160, 90);
        renderer.UpdateCamera(input);
        Assert(texture.Creations == 1 && texture.Uploads == 2 && texture.Sequence == 3, "Frames at the same size reuse the texture, and when two arrive between draws the newest wins.");
        PublishGradient(ring, 320, 180);
        renderer.UpdateCamera(input);
        Assert(renderer.CameraTexture is { Width: 320, Height: 180, Creations: 2 }, "A camera that changes size gets a texture of the new size, created once.");
        var badRing = new CameraFrameRing();
        var badInput = new GpuCameraInput(badRing, PictureFit.Fit, false, 1f);
        var shortBuffer = badRing.BeginWrite(16, out var shortSlot); Assert(shortBuffer.Length < 320 * 180 * 4, "A fresh ring hands out the small buffer asked for.");
        badRing.Publish(shortSlot, 320, 180, 0);
        var beforeBad = renderer.CameraTexture!.Uploads;
        renderer.UpdateCamera(badInput);
        Assert(renderer.CameraTexture!.Uploads == beforeBad, "A frame whose buffer is too short for its size must be refused, not read past its end.");
        foreach (var (w, h) in new[] { (0, 90), (160, 0), (-4, 90), (GpuCameraTexture.MaxSide + 1, 90) })
        {
            var slotBuffer = badRing.BeginWrite(64, out var slot); _ = slotBuffer;
            badRing.Publish(slot, w, h, 0);
            renderer.UpdateCamera(badInput);
            Assert(renderer.CameraTexture!.Uploads == beforeBad, $"A frame of {w}×{h} must be refused.");
        }
        renderer.UpdateCamera(null);
        Assert(renderer.CameraTexture is null, "Removing the layer must free the camera texture.");

        // ---- pixels: Fit, same shape -----------------------------------------------------------------------
        var fitRing = new CameraFrameRing(); PublishGradient(fitRing, 160, 90);
        var baseline = Render(MakeFeed(null));
        var fit = Render(MakeFeed(new GpuCameraInput(fitRing, PictureFit.Fit, false, 1f)));
        foreach (var (x, y) in points) Check(fit, x, y, 0, 0, 4, 160, 90, false, 1, "Fit, same shape");
        var differs = 0;
        foreach (var (x, y) in points) { var i = (y * width + x) * 4; if (Math.Abs(fit[i] - baseline[i]) + Math.Abs(fit[i + 1] - baseline[i + 1]) > 30) differs++; }
        Assert(differs >= 3, $"The camera picture must replace the stage's own background ({differs} of {points.Length} sample points differ from the frame without a camera).");

        // ---- Mirror ---------------------------------------------------------------------------------------
        var mirrorFeed = MakeFeed(new GpuCameraInput(fitRing, PictureFit.Fit, true, 1f));
        var mirrored = Render(mirrorFeed);
        foreach (var (x, y) in points) Check(mirrored, x, y, 0, 0, 4, 160, 90, true, 1, "Mirrored");
        { var i = (180 * width + 40) * 4; var j = (180 * width + (width - 1 - 40)) * 4; Assert(Math.Abs(mirrored[i] - fit[j]) <= 4 && Math.Abs(mirrored[i + 1] - fit[j + 1]) <= 4, "A mirrored frame is the plain frame flipped left to right."); }

        // ---- Opacity --------------------------------------------------------------------------------------
        var dim = Render(MakeFeed(new GpuCameraInput(fitRing, PictureFit.Fit, false, .5f)));
        foreach (var (x, y) in points) Check(dim, x, y, 0, 0, 4, 160, 90, false, .5, "Half opacity");

        // ---- a square picture on a wide stage: bars with Fit, a crop with Fill ------------------------------
        var squareRing = new CameraFrameRing(); PublishGradient(squareRing, 90, 90);
        var barred = Render(MakeFeed(new GpuCameraInput(squareRing, PictureFit.Fit, false, 1f)));
        foreach (var (x, y) in new[] { (20, 40), (100, 250), (139, 180), (500, 40), (620, 240) })
        {
            var i = (y * width + x) * 4;
            Assert(barred[i] <= 3 && barred[i + 1] <= 3 && barred[i + 2] <= 3, $"Fit must leave the bars empty at ({x},{y}) (got {barred[i]},{barred[i + 1]},{barred[i + 2]}).");
        }
        foreach (var (x, y) in new[] { (200, 40), (320, 180), (450, 100), (260, 250) }) Check(barred, x, y, 140, 0, 4, 90, 90, false, 1, "Fit, square picture");
        var filled = Render(MakeFeed(new GpuCameraInput(squareRing, PictureFit.Fill, false, 1f)));
        var fillScale = Math.Max(width / 90.0, height / 90.0);
        foreach (var (x, y) in new[] { (20, 40), (320, 180), (600, 100), (100, 250) }) Check(filled, x, y, 0, (height - 90 * fillScale) / 2, fillScale, 90, 90, false, 1, "Fill, square picture");

        // ---- the layer removed, and chroma key -------------------------------------------------------------
        var gone = Render(MakeFeed(null));
        var same = true;
        foreach (var (x, y) in points) { var i = (y * width + x) * 4; if (Math.Abs(gone[i] - baseline[i]) > 3 || Math.Abs(gone[i + 1] - baseline[i + 1]) > 3) same = false; }
        Assert(same, "With the layer removed the stage's own background must come back.");
        PublishGradient(fitRing, 160, 90); // the picture is fresh again, so only the chroma rule can keep it off the frame
        var chroma = Render(MakeFeed(new GpuCameraInput(fitRing, PictureFit.Fit, false, 1f), chroma: true));
        var centre = ((height / 2) * width + width / 2) * 4;
        Assert(chroma[centre + 1] > 240 && chroma[centre] < 10 && chroma[centre + 2] < 10, "A chroma-key frame must stay pure green; the camera layer is not drawn into it.");

        // ---- AR-1.3: notes projected onto the keyboard in the picture ----------------------------------------
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], 1280, 720, out var calibrationProblem);
        Assert(calibration is not null, $"The sample keyboard's corners must make a calibration ({calibrationProblem}).");
        var arView = new ArView(1280, 720, width, height, PictureFit.Fit, false);
        static void PublishGrey(CameraFrameRing ring, int w, int h)
        {
            var buffer = ring.BeginWrite(w * h * 4, out var slot);
            Array.Fill(buffer, (byte)90, 0, w * h * 4);
            for (var i = 3; i < w * h * 4; i += 4) buffer[i] = 255;
            ring.Publish(slot, w, h, Stopwatch.GetTimestamp());
        }
        var arNotes = new List<NoteEvent>
        {
            new() { Pitch = 66, Start = 11.2, Duration = .5 },    // black, on its way
            new() { Pitch = 72, Start = 10.5, Duration = .5 },
            new() { Pitch = 67, Start = 9.8, Duration = 1 },      // already sounding
            new() { Pitch = 60, Start = 11, Duration = .5 },
            new() { Pitch = 48, Start = 12.9, Duration = .5 },    // not in reach yet
        };
        arNotes.Sort((a, b) => a.Start.CompareTo(b.Start));
        GpuStageFeed MakeArFeed(GpuCameraInput? camera, GpuArInput? ar, bool playing = true, string noteStyle = "Neon")
        {
            var feed = MakeFeed(camera, noteStyle: noteStyle);
            feed.SetState(arNotes, 10, playing, new HashSet<int>());
            feed.Ar = ar;
            return feed;
        }
        static (double X, double Y) Blend(ArPatch patch, double s, double t)
        {
            double weight = 0, x = 0, y = 0;
            for (var i = 0; i < 4; i++)
            {
                var lambda = i switch { 0 => (1 - s) * (1 - t), 1 => s * (1 - t), 2 => s * t, _ => (1 - s) * t };
                weight += lambda * patch[i].W; x += lambda * patch[i].W * patch[i].X; y += lambda * patch[i].W * patch[i].Y;
            }
            return (x / weight, y / weight);
        }
        // null for a point that is not on the output: a note may reach above the top edge, and a point there says nothing
        static (int Spread, int Off)? Look(byte[] pixels, int w, int h, (double X, double Y) at)
        {
            if (!(at.X >= 0 && at.Y >= 0 && at.X < w && at.Y < h)) return null;
            var px = (int)Math.Floor(at.X); var py = (int)Math.Floor(at.Y);
            var i = (py * w + px) * 4;
            int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
            return (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)), Math.Max(Math.Abs(red - 90), Math.Max(Math.Abs(green - 90), Math.Abs(blue - 90))));
        }
        static int ChangedPixels(byte[] first, byte[] second, int threshold = 4)
        {
            var count = 0;
            for (var i = 0; i < Math.Min(first.Length, second.Length); i += 4)
                if (Math.Max(Math.Abs(first[i] - second[i]), Math.Max(Math.Abs(first[i + 1] - second[i + 1]), Math.Abs(first[i + 2] - second[i + 2]))) > threshold)
                    count++;
            return count;
        }

        var flatRing = new CameraFrameRing(); PublishGrey(flatRing, 1280, 720);
        var flatInput = new GpuCameraInput(flatRing, PictureFit.Fit, false, 1f);
        var arInput = new GpuArInput(calibration!, ArNoteStyle.Default, 1f);
        var expected = new List<ArNoteShape>();
        ArNotes.Build(calibration!, arView, arNotes, 10, 1, ArNoteStyle.Default, expected);
        Assert(expected.Count == 4, $"Four of the five notes are in reach or sounding ({expected.Count}).");
        var arFrame = Render(MakeArFeed(flatInput, arInput));
        // The sounding G4 also lights its key; standard quality may add neighboring-key light spill, so count note shapes separately.
        Assert(renderer.ArShapeCount == expected.Count && renderer.ArKeyQuadCount >= 1,
            $"The renderer should draw one note quad for each note in reach ({renderer.ArShapeCount} note quads, {renderer.ArKeyQuadCount} key quads, {renderer.ArNoteLightCount} spill quads, {renderer.ArObjectQuadCount} object quads plus {renderer.ArDamperStripCount} damper strips).");
        var gemArFrame = Render(MakeArFeed(flatInput, arInput, noteStyle: "Gem"));
        var crystalArFrame = Render(MakeArFeed(flatInput, arInput, noteStyle: "CrystalOutline"));
        var neonGemPixels = ChangedPixels(arFrame, gemArFrame);
        var neonCrystalPixels = ChangedPixels(arFrame, crystalArFrame);
        var gemCrystalPixels = ChangedPixels(gemArFrame, crystalArFrame);
        Assert(neonGemPixels > 100 && neonCrystalPixels > 100 && gemCrystalPixels > 100,
            $"The AR note quad shader should render visibly distinct Gem and Crystal Outline branches (Neon/Gem {neonGemPixels}, Neon/Crystal {neonCrystalPixels}, Gem/Crystal {gemCrystalPixels} changed pixels); full AR parameter parity remains unverified.");
        var plain = Look(arFrame, width, height, (20, 20))!.Value;
        Assert(plain.Off <= 3 && plain.Spread <= 3, $"Away from every note the picture is the camera's grey (off by {plain.Off}).");
        foreach (var shape in expected)
        {
            var insideChecked = 0;
            var colouredSamples = 0;
            foreach (var (s, t) in new[] { (.5, .5), (.2, .5), (.8, .5), (.5, .15), (.5, .85), (.5, .95) })
            {
                if (Look(arFrame, width, height, Blend(shape.Patch, s, t)) is not { } inside) continue;
                insideChecked++;
                Assert(inside.Off > 40, $"Note {shape.Pitch} should visibly change the camera at ({s:0.00},{t:0.00}) of its quad (off grey by {inside.Off}).");
                if (inside.Spread > 30) colouredSamples++;
            }
            Assert(insideChecked >= 2, $"At least two sample points of note {shape.Pitch} should be on the output ({insideChecked}).");
            Assert(colouredSamples > 0, $"At least one sample point of note {shape.Pitch} should preserve its saturated note colour beyond Neon's achromatic white core ({colouredSamples}).");
            foreach (var (s, t) in new[] { (-.3, .5), (1.3, .5), (.5, -.35), (-.3, .9), (1.3, .9) })
            {
                if (Look(arFrame, width, height, Blend(shape.Patch, s, t)) is not { } outside) continue;
                Assert(outside.Off <= 3 && outside.Spread <= 3, $"Just outside note {shape.Pitch}'s quad at ({s:0.00},{t:0.00}) the camera's grey must show (off by {outside.Off}).");
            }
        }
        // past a coming note's leading edge there is only the picture: the notes are not lit before they arrive
        var coming = expected.First(shape => shape.Pitch == 60);
        var overKeys = Look(arFrame, width, height, Blend(coming.Patch, .5, 1.2));
        Assert(overKeys is { } over && over.Off <= 3, "Just past the leading edge of a coming note the camera's grey must show.");

        Render(MakeArFeed(flatInput, arInput, playing: false));
        Assert(renderer.ArShapeCount == 0 && renderer.ArKeyQuadCount == 0 && renderer.ArNoteLightCount == 0,
            "A paused song draws no notes or key-light spill, like the stage.");
        Render(MakeArFeed(flatInput, new GpuArInput(calibration!, ArNoteStyle.Default, 0f)));
        Assert(renderer.ArObjectQuadCount == 0, "An AR layer at zero opacity draws nothing.");
        Render(MakeArFeed(flatInput, new GpuArInput(calibration!, ArNoteStyle.Default, 1f, KeyLight: .6f, ShowNotes: false)));
        Assert(renderer.ArShapeCount == 0 && renderer.ArKeyQuadCount >= 1,
            "Perform mode draws no notes to come but still lights the key that sounds; standard-quality spill is a separate pass.");
        // ---- AR-1.4: held keys lit where they are in the picture ---------------------------------------------
        var held = new HashSet<int> { 60, 61, 64 };
        GpuStageFeed MakeKeyFeed(GpuArInput? ar)
        {
            var feed = MakeFeed(flatInput);
            feed.SetState([], 0, false, held);
            feed.Ar = ar;
            return feed;
        }
        byte[] RenderWarm(GpuStageFeed feed) => GpuRenderLoop.RenderFrames(renderer, feed, width, height, 10, 1 / 60.0, false, out _);
        var layout = calibration!.Layout;
        (double X, double Y) KeyPoint(double u, double v) { var p = calibration.ToPicture(u, v); return arView.ToOutput(p.X, p.Y); }
        var lit = RenderWarm(MakeKeyFeed(arInput));
        var litKeys = renderer.ArKeyQuadCount;
        var litNoteLights = renderer.ArNoteLightCount;
        var unlit = RenderWarm(MakeKeyFeed(null));
        Assert(litKeys == 5 && litNoteLights > 0 && renderer.ArObjectQuadCount == 0,
            $"Three held keys (C4 and E4 white, C#4 black) are five rectangles plus neighboring-key spill ({litKeys} keys, {litNoteLights} spill quads).");
        int Change((double X, double Y) at)
        {
            var x = (int)Math.Floor(at.X); var y = (int)Math.Floor(at.Y);
            Assert(x >= 0 && y >= 0 && x < width && y < height, $"The probe ({at.X:0.0},{at.Y:0.0}) must be on the output.");
            var i = (y * width + x) * 4;
            return Math.Max(Math.Abs(lit[i] - unlit[i]), Math.Max(Math.Abs(lit[i + 1] - unlit[i + 1]), Math.Abs(lit[i + 2] - unlit[i + 2])));
        }
        (string What, (double X, double Y) At)[] shouldLight =
        [
            ("C4, front of the white key", KeyPoint(layout.CenterX(60), .85)),
            ("C4, stem between the black keys", KeyPoint((layout.Span(60).Left + layout.Span(61).Left) / 2, .3)),
            ("C#4, the black key", KeyPoint(layout.CenterX(61), .3)),
            ("E4, front of the white key", KeyPoint(layout.CenterX(64), .85)),
            ("E4, stem beside D#4", KeyPoint((layout.Span(63).Right + layout.Span(64).Right) / 2, .3)),
        ];
        foreach (var (what, at) in shouldLight)
            Assert(Change(at) > 25, $"A held key must be tinted: {what} changed by {Change(at)}.");
        (string What, (double X, double Y) At)[] shouldStay =
        [
            ("G5, far from any held key", KeyPoint(layout.CenterX(79), .85)),
            ("the air above C4", KeyPoint(layout.CenterX(60), -.3)),
        ];
        foreach (var (what, at) in shouldStay)
            Assert(Change(at) <= 2, $"Only held keys light up: {what} changed by {Change(at)}.");
        RenderWarm(MakeKeyFeed(new GpuArInput(calibration, ArNoteStyle.Default, 1f, 0f)));
        Assert(renderer.ArObjectQuadCount == 0, "A key light strength of zero draws no key.");

        // ---- the keys under the player's own hands: a bar leaves the key ---------------------------------------
        // No song at all: nothing plays and no notes are loaded, which is what playing freely looks like. The key
        // still lights up, and a bar leaves it along the keyboard's own direction — the direction the song's notes
        // come from — stretching as the key is held. Perform mode keeps the bar: it leaves out the notes of the song
        // to come, not the mark of what the player just played.
        GpuStageFeed ArFeed(int[] pitches, bool press, bool showTrails = true, float keyLight = .6f)
        {
            var feed = MakeFeed(flatInput);
            feed.SetState([], 0, false, new HashSet<int>(pitches));
            feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: keyLight, ShowNotes: false,
                ShowLiveTrails: showTrails);   // Perform leaves out song notes; impact lights and optional press trails stay independent.
            // A physical note-on arrives as a pair on the same wall-clock frame — the held note and its strike
            // impact (Ui/MainWindow.xaml.cs); the strike lights come from that explicit hit, exactly as they do
            // for a real hand, not from the falling trail's touchdown (which stays quiet by design).
            if (press) foreach (var pitch in pitches) { feed.LiveNote(pitch, true, .9f); feed.Impact(pitch, .9); }
            return feed;
        }
        (byte[] Pixels, int Bars, int Keys, int Impacts, int NoteLights, int Quads) Draw(GpuStageFeed feed, int frames, Action<int>? beforeFrame = null)
        {
            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, width, height, frames, 1 / 60.0, false, out _, beforeFrame, 20260930);
            return (pixels, renderer.ArLiveBarCount, renderer.ArKeyQuadCount, renderer.ArImpactLightCount, renderer.ArNoteLightCount, renderer.ArObjectQuadCount);
        }
        int Probe(byte[] withBar, byte[] withoutBar, (double X, double Y) at)
        {
            var x = (int)Math.Floor(at.X); var y = (int)Math.Floor(at.Y);
            Assert(x >= 0 && y >= 0 && x < width && y < height, $"The bar probe ({at.X:0.0},{at.Y:0.0}) must be on the output.");
            var i = (y * width + x) * 4;
            return Math.Max(Math.Abs(withBar[i] - withoutBar[i]), Math.Max(Math.Abs(withBar[i + 1] - withoutBar[i + 1]), Math.Abs(withBar[i + 2] - withoutBar[i + 2])));
        }
        // Both renders receive the same MIDI strikes and key-light setting. Turning trails off is the control so
        // transient impact lights remain identical and only the two independently optional bars differ.
        var withoutBars = Draw(ArFeed([60, 61], press: true, showTrails: false), 30);
        var withBars = Draw(ArFeed([60, 61], press: true), 30);
        Assert(withBars.Bars == 2 && withBars.Keys == 3 && withBars.Impacts == 2 && withBars.NoteLights > 0
                && withBars.Quads == withBars.Keys + withBars.Bars + withBars.Impacts + withBars.NoteLights,
            $"Two keys pressed should light three key rectangles, draw two bars, retain two strike lights and add neighboring spill ({withBars.Quads} quads: {withBars.Keys} keys, {withBars.Bars} bars, {withBars.Impacts} impacts, {withBars.NoteLights} spill).");
        Assert(withoutBars.Bars == 0 && withoutBars.Keys == 3 && withoutBars.Impacts == 2 && withoutBars.NoteLights == withBars.NoteLights
                && withoutBars.Quads == withoutBars.Keys + withoutBars.Impacts + withoutBars.NoteLights,
            $"Turning live press trails off must remove only the bars, leaving key tint, neighboring spill and independent strike lights ({withoutBars.Quads} quads: {withoutBars.Keys} keys, {withoutBars.Impacts} impacts, {withoutBars.NoteLights} spill).");
        var trailsOffNoTint = Draw(ArFeed([60, 61], press: true, showTrails: false, keyLight: 0), 30);
        var tintedKey = KeyPoint(layout.CenterX(60), .85);
        Assert(Probe(withoutBars.Pixels, trailsOffNoTint.Pixels, tintedKey) > 25,
            "The key tint must still be visible when press trails are disabled.");
        int Bar((double X, double Y) at) => Probe(withBars.Pixels, withoutBars.Pixels, at);
        var behindC4 = KeyPoint(layout.CenterX(60), -.05); var behindCs4 = KeyPoint(layout.CenterX(61), -.05);
        Assert(Bar(behindC4) > 25 && Bar(behindCs4) > 25,
            $"A bar should be drawn just behind each key that was pressed (white {Bar(behindC4)}, black {Bar(behindCs4)}).");
        Assert(Bar(KeyPoint(layout.CenterX(62), -.05)) <= 2, "The bar belongs to the key that was pressed and to no other key.");
        Assert(Bar(KeyPoint(layout.CenterX(60), .5)) <= 2, "The bar must not be drawn over the key itself: the lit key is the mark there.");
        var farProbe = KeyPoint(layout.CenterX(60), -1.2);
        Assert(Bar(farProbe) <= 2, $"A key held half a second has a short bar, well short of the look-ahead ({Bar(farProbe)} at -1.2).");
        // held twice as long: the bar has stretched past where it reached at half a second, in the keyboard's own direction
        var stretchedBars = Draw(ArFeed([60, 61], press: true), 60);
        var withoutBarsLong = Draw(ArFeed([60, 61], press: true, showTrails: false), 60);
        Assert(stretchedBars.Bars == 2 && Probe(stretchedBars.Pixels, withoutBarsLong.Pixels, farProbe) > 25,
            $"The bar stretches as the key is held: past where half a second reached ({Probe(stretchedBars.Pixels, withoutBarsLong.Pixels, farProbe)} at -1.2).");
        // Note Off freezes the live bar's hold length, but it continues travelling through its fixed AR reach;
        // key tint remains the key's state, and strike lights have their own shorter lifetime.
        var letGoFeed = ArFeed([60, 61], press: true);
        var afterRelease = Draw(letGoFeed, 80, frame =>
        {
            if (frame != 20) return;
            letGoFeed.LiveNote(60, false, 0); letGoFeed.LiveNote(61, false, 0);
        });
        Assert(afterRelease.Bars == 2 && afterRelease.Keys == 3 && afterRelease.Impacts == 0 && afterRelease.NoteLights > 0
                && afterRelease.Quads == afterRelease.Keys + afterRelease.Bars + afterRelease.NoteLights,
            $"After Note Off the live bars keep travelling while strike lights fade and key tint plus spill remain ({afterRelease.Quads} quads: {afterRelease.Keys} keys, {afterRelease.Bars} bars, {afterRelease.NoteLights} spill).");
        // a moment after the release — past the strike lights' .85 s lifetime — the bars are still there, dimming;
        // the key light was never part of the deal either way
        var fadingFeed = ArFeed([60, 61], press: true);
        var fadingAfterRelease = Draw(fadingFeed, 60, frame =>
        {
            if (frame != 55) return;
            fadingFeed.LiveNote(60, false, 0); fadingFeed.LiveNote(61, false, 0);
        });
        Assert(fadingAfterRelease.Bars == 2 && fadingAfterRelease.Keys == 3 && fadingAfterRelease.Impacts == 0
                && fadingAfterRelease.NoteLights > 0
                && fadingAfterRelease.Quads >= fadingAfterRelease.Keys + fadingAfterRelease.Bars + fadingAfterRelease.NoteLights,
            $"A key just let go keeps its bar for a moment while key tint and spill remain; unrelated transients may add quads ({fadingAfterRelease.Quads} quads: {fadingAfterRelease.Keys} keys, {fadingAfterRelease.Bars} bars, {fadingAfterRelease.NoteLights} spill).");
        Results.Add("PASS ar live keys: player press bars stretch behind pressed white and black keys and fade after release; disabling the bars leaves the separately verified key tint visible in flat AR mode, while Perform can opt in to trails.");

        Render(MakeArFeed(flatInput, null));
        Assert(renderer.ArObjectQuadCount == 0, "Without an AR layer there are no AR quads.");
        var smallRing = new CameraFrameRing(); PublishGradient(smallRing, 160, 90);
        Render(MakeArFeed(new GpuCameraInput(smallRing, PictureFit.Fit, false, 1f), arInput));
        Assert(renderer.ArObjectQuadCount == 0, "A camera that is not the size the calibration was made at must hide the AR layer, not draw notes in the wrong place.");
        Render(MakeArFeed(null, arInput));
        Assert(renderer.ArObjectQuadCount == 0, "The AR layer lies on the camera picture and is not drawn without it.");

        VerifyArOverlayCapturedFrames(renderer);
        Results.Add($"PASS ar keys: three held keys produce five tinted rectangles and separate neighboring-key spill at standard quality; distant keys and the air above the keyboard stay clear, read back against the same frame without the layer.");
        Results.Add($"PASS ar notes: 4 of 5 notes of a song projected onto the sample keyboard and read back, inside and just outside their quads; Gem/Crystal use distinct material branches on AR quads, with the full parameter matrix still Unverified; paused, zero opacity, no layer and a camera of another size draw nothing.");
        Results.Add($"PASS ar camera layer: one texture reused across frames and re-created once for a new size, bad frames refused; fit, mirror, half opacity, bars and crop read back from a {width}×{height} frame on {renderer.AdapterName} in {started.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>Read-back check of what is and is not composited over a camera in the AR stage look.</summary>
    private static void VerifyArOverlayCapturedFrames(GpuStageRenderer renderer)
    {
        const int width = 320, height = 180;
        var cameraRing = new CameraFrameRing();
        var image = cameraRing.BeginWrite(width * height * 4, out var slot);
        for (var i = 0; i < image.Length; i += 4)
        {
            image[i] = image[i + 1] = image[i + 2] = 90;
            image[i + 3] = 255;
        }
        cameraRing.Publish(slot, width, height, Stopwatch.GetTimestamp());
        var camera = new GpuCameraInput(cameraRing, PictureFit.Fit, false, 1f);
        var notes = new List<NoteEvent>
        {
            new() { Pitch = 60, Start = 1.2, Duration = .7 },
            new() { Pitch = 61, Start = 1.25, Duration = .5 },
        };
        var held = new HashSet<int> { 60, 61 };
        var quiet = new PianoVisualSettings
        {
            ShowBackground = true, BackgroundMode = "Solid", BackgroundGradient = false, BackgroundMotion = "None",
            ShowNotes = false, ShowKeys = false, ShowHalo = false, ShowStars = false,
            ShowEmbers = false, ShowWisps = false, ShowFlame = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowLightBeams = false, ShowPetals = false, ShootingStars = false,
            AmbientEnergy = "None", AmbientNature = "None", AmbientLight = "None", AmbientCosmic = "None",
            HorizonGlow = 0, BloomIntensity = 0, Vignette = 0,
        };
        GpuStageFeed MakeFeed(PianoVisualSettings settings, bool overCamera, bool sounding = true, bool withCamera = true)
        {
            var feed = new GpuStageFeed();
            feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(settings, (pitch, track) => Color.FromRgb(255, 80, 220), .205, overCamera: overCamera));
            IReadOnlyList<NoteEvent> stateNotes = sounding ? notes : Array.Empty<NoteEvent>();
            IReadOnlySet<int> pressed = sounding ? held : new HashSet<int>();
            feed.SetDeterministicState(stateNotes, sounding ? 1 : 0, sounding, pressed);
            if (withCamera) feed.Camera = camera;
            return feed;
        }
        byte[] Capture(GpuStageFeed feed, Action<int>? beforeFrame = null) =>
            GpuRenderLoop.RenderFrames(renderer, feed, width, height, 4, .02, false, out _, beforeFrame, 20261008);
        static int ChangedPixels(byte[] a, byte[] b, int threshold = 4)
        {
            var changed = 0;
            for (var i = 0; i < a.Length; i += 4)
                if (Math.Abs(a[i] - b[i]) >= threshold || Math.Abs(a[i + 1] - b[i + 1]) >= threshold || Math.Abs(a[i + 2] - b[i + 2]) >= threshold)
                    changed++;
            return changed;
        }

        var background = Capture(MakeFeed(quiet, overCamera: true));
        var visibleLayers = new (string Id, Action<PianoVisualSettings> Enable)[]
        {
            ("ambient.energy", s => { s.AmbientEnergy = "Lightning Storm"; s.AmbientEnergyAmount = 100; }),
            ("ambient.nature", s => { s.AmbientNature = "Rain"; s.AmbientNatureAmount = 100; }),
            ("ambient.light", s => { s.AmbientLight = "Prism"; s.AmbientLightAmount = 100; }),
            ("ambient.cosmic", s => { s.AmbientCosmic = "Geometric"; s.AmbientCosmicAmount = 100; }),
            ("atmosphere.shooting-stars", s => { s.ShootingStars = true; s.ShootingStarsAmount = 100; }),
            ("atmosphere.petals", s => { s.ShowPetals = true; s.PetalAmount = 150; }),
        };
        foreach (var (id, enable) in visibleLayers)
        {
            var settings = quiet.Clone(); enable(settings);
            var captured = Capture(MakeFeed(settings, overCamera: true, sounding: false));
            var changed = ChangedPixels(background, captured);
            Assert(changed > 0, $"The {id} layer should appear in the captured camera composite (changed {changed} pixels).");
        }
        var stars = quiet.Clone(); stars.ShowStars = true;
        Assert(background.AsSpan().SequenceEqual(Capture(MakeFeed(stars, overCamera: true))),
            "The star field belongs to the replaced backdrop, so it must not be drawn over the camera picture.");
        var beams = quiet.Clone(); beams.ShowLightBeams = true;
        Assert(background.AsSpan().SequenceEqual(Capture(MakeFeed(beams, overCamera: true))),
            "Key-anchored beams must stay out of the camera composite.");

        var busy = quiet.Clone();
        busy.ShowNotes = true; busy.ShowKeys = true; busy.ShowHalo = true; busy.ShowStars = true;
        busy.NoteShimmer = true; busy.FallingTrail = "Ribbon"; busy.FallingPulse = true;
        busy.HoldBar = true; busy.HoldBreath = true; busy.HoldVibration = true; busy.HoldColorCycle = true; busy.HoldElectricArc = true;
        busy.ShowEmbers = true; busy.ParticleAmount = 120; busy.ShowWisps = true; busy.WispAmount = 150;
        busy.ShowFlame = true; busy.FlameIntensity = 100;
        busy.ShowImpactRings = true; busy.ImpactWave = "Shockwave"; busy.ImpactWaveIntensity = 100;
        busy.ShowImpactFlash = true; busy.ImpactFlashIntensity = 100; busy.ImpactFlashStyle = "Lightning";
        busy.ImpactMorph = "Shatter"; busy.ImpactMorphIntensity = 100;
        busy.ReleaseEffect = "Dissolve"; busy.ReleaseIntensity = 100;
        busy.ShowLightBeams = true; busy.BeamIntensity = 100;
        var overCameraLook = GpuLook.From(busy, (pitch, track) => Color.FromRgb(255, 80, 220), .205, overCamera: true);
        Assert(overCameraLook.OverCamera && !overCameraLook.ShowNotes && !overCameraLook.ShowKeys
                && overCameraLook.ShowEmbers && overCameraLook.ShowWisps && overCameraLook.ShowFlame
                && overCameraLook.ShowImpactRings && overCameraLook.ShowImpactFlash && overCameraLook.ShowLightBeams
                && overCameraLook.HoldElectricArc && overCameraLook.ShowHalo && overCameraLook.ImpactWave == "Shockwave" && overCameraLook.ImpactMorph == "Shatter"
                && overCameraLook.ReleaseEffect == "Dissolve" && overCameraLook.ReleaseIntensity > 0,
            "The camera-mode look must suppress stage-space note/key drawing but preserve Live Stage effect settings for the calibrated 3D AR pass.");
        GpuStageFeed BusyFeed(bool withCamera = true)
        {
            var feed = MakeFeed(busy, overCamera: false, withCamera: withCamera);
            feed.Impact(60, 1);
            feed.LiveNote(60, true, 1);
            feed.LiveNote(60, false, 1);
            return feed;
        }
        var stageImage = Capture(BusyFeed(withCamera: false));
        Assert(ChangedPixels(background, stageImage, threshold: 8) > 30,
            "The busy fixture must visibly contain stage notes, keys and transients before its camera overlay is enabled.");
        var transitioning = BusyFeed(withCamera: false);
        var hidden = Capture(transitioning, frame =>
        {
            if (frame != 1) return;
            var resumedImage = cameraRing.BeginWrite(width * height * 4, out var resumedSlot);
            for (var i = 0; i < resumedImage.Length; i += 4)
            {
                resumedImage[i] = resumedImage[i + 1] = resumedImage[i + 2] = 90;
                resumedImage[i + 3] = 255;
            }
            cameraRing.Publish(resumedSlot, width, height, Stopwatch.GetTimestamp());
            transitioning.Camera = camera;
            transitioning.SetLook(overCameraLook);
        });
        Assert(background.AsSpan().SequenceEqual(hidden),
            "The AR camera composite must hide active stage notes, keys and transients that were already in flight when the overlay turned on.");
        Results.Add("PASS AR over-camera captured frames: WARP readback shows all six screen-space ambient families, omits backdrop stars and uncalibrated key beams, preserves Live Stage effect settings for the AR projection, and suppresses their stage-space pixels when camera mode turns on.");
    }

    /// <summary>
    /// AR-1.7, a take made from the GPU stage holds what the screen shows: the camera's picture and the AR layer over it, drawn by the
    /// render thread alone at the take's own size. Three takes (the stage's shape, a square one with bars, and the picture mirrored), each
    /// read back and set against the same take without the AR layer: the picture is there, the lit keys are tinted where they are
    /// in the picture of that take, and nothing else changes.
    /// </summary>
    private static void VerifyArRecording()
    {
        const int cw = 1280, ch = 720;
        var quiet = new PianoVisualSettings
        {
            BackgroundGradient = false, ShowStars = false, ShowHalo = false, ShowKeys = false, ShowNotes = false,
            ShowEmbers = false, ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowLightBeams = false, ShowPetals = false, HorizonGlow = 0, Vignette = 0, BloomIntensity = 0
        };
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], cw, ch, out var problem);
        Assert(calibration is not null, $"The sample keyboard's corners must make a calibration ({problem}).");
        var ring = new CameraFrameRing();
        void PublishGrey(long captureTicks)
        {
            var buffer = ring.BeginWrite(cw * ch * 4, out var slot);
            Array.Fill(buffer, (byte)90, 0, cw * ch * 4);
            for (var i = 3; i < cw * ch * 4; i += 4) buffer[i] = 255;
            ring.Publish(slot, cw, ch, captureTicks);
        }

        var held = new HashSet<int> { 60, 61, 64 };
        var feed = new GpuStageFeed(); feed.SetStageHeight(360);
        feed.SetLook(GpuLook.From(quiet, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
        feed.SetState([], 0, false, held);
        var started = Stopwatch.StartNew();
        var takes = 0;
        var maxAge = 0.0;
        using var loop = new GpuRenderLoop(feed, forceWarp: true);
        // one take: the layers as asked, recorded by the render thread at the size asked, the newest frame copied out
        byte[] Take(bool mirror, bool withAr, int w, int h)
        {
            feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, mirror, 1f);
            feed.Ar = withAr ? new GpuArInput(calibration!, ArNoteStyle.Default, 1f) : null;
            var tap = new GpuRecordingTap(w, h, 30);
            feed.Recording = tap;
            // a picture the camera handed over 80 ms ago: the take must count it once, and at least that old
            PublishGrey(Stopwatch.GetTimestamp() - Stopwatch.Frequency * 80 / 1000);
            var wait = Stopwatch.StartNew();
            while (tap.Serial < 3 && loop.Error is null && wait.Elapsed.TotalSeconds < 30) Thread.Sleep(20);
            Assert(loop.Error is null && tap.Serial >= 3, $"The render loop should record {w}×{h} frames with the camera layer on ({loop.Error}, {tap.Serial} frames).");
            var age = tap.CameraAge.Snapshot;
            Assert(age.Frames == 1 && age.LastMilliseconds >= 80 && age.LastMilliseconds < 80 + 1500 && age.WorstMilliseconds == age.LastMilliseconds,
                $"A take should count the 80 ms old camera picture once, at least that old ({age.Frames} frames, last {age.LastMilliseconds:0} ms).");
            maxAge = Math.Max(maxAge, age.LastMilliseconds - 80);
            byte[]? copy = null;
            tap.TryRead(frame => copy = (byte[])frame.Clone());
            feed.Recording = null;
            takes++;
            return copy!;
        }
        foreach (var (w, h, mirror) in new[] { (320, 180, false), (240, 240, false), (320, 180, true) })
        {
            var lit = Take(mirror, true, w, h);
            var plain = Take(mirror, false, w, h);
            var view = new ArView(cw, ch, w, h, PictureFit.Fit, mirror);
            var layout = calibration!.Layout;
            (int X, int Y) At(double u, double v) { var p = calibration.ToPicture(u, v); var o = view.ToOutput(p.X, p.Y); return ((int)Math.Floor(o.X), (int)Math.Floor(o.Y)); }
            int Change((int X, int Y) at)
            {
                Assert(at.X >= 0 && at.Y >= 0 && at.X < w && at.Y < h, $"The probe ({at.X},{at.Y}) must be on the {w}×{h} take.");
                var i = (at.Y * w + at.X) * 4;
                return Math.Max(Math.Abs(lit[i] - plain[i]), Math.Max(Math.Abs(lit[i + 1] - plain[i + 1]), Math.Abs(lit[i + 2] - plain[i + 2])));
            }
            var what = $"{w}×{h}{(mirror ? " mirrored" : "")}";
            // the camera's picture is in the take (grey 90), and where the picture does not reach the stage's own backdrop shows
            var sky = view.ToOutput(cw / 2.0, 100);
            var skyAt = ((int)sky.Y * w + (int)sky.X) * 4;
            Assert(Math.Abs(plain[skyAt] - 90) <= 4 && Math.Abs(plain[skyAt + 1] - 90) <= 4 && Math.Abs(plain[skyAt + 2] - 90) <= 4,
                $"A take of {what} should hold the camera's picture (got {plain[skyAt]},{plain[skyAt + 1]},{plain[skyAt + 2]}).");
            if (view.Top > 10)
            {
                var bar = (3 * w + w / 2) * 4;
                Assert(plain[bar] < 40 && plain[bar + 1] < 40 && plain[bar + 2] < 40, $"The bar above the picture of a {what} take should show the dark backdrop.");
            }
            foreach (var (name, at) in new[] { ("C4 front", At(layout.CenterX(60), .85)), ("C#4 black key", At(layout.CenterX(61), .3)), ("E4 front", At(layout.CenterX(64), .85)) })
                Assert(Change(at) > 25, $"A held key must be tinted in a take of {what}: {name} changed by {Change(at)}.");
            foreach (var (name, at) in new[] { ("D4 front", At(layout.CenterX(62), .85)), ("G5 front", At(layout.CenterX(79), .85)), ("the air above C4", At(layout.CenterX(60), -.3)), ("the sky", ((int)sky.X, (int)sky.Y)) })
                Assert(Change(at) <= 4, $"Only held keys light up in a take of {what}: {name} changed by {Change(at)}.");
        }
        Assert(loop.CameraAge.Frames >= 3 && loop.CameraAge.WorstMilliseconds >= 80, $"The render loop should have counted the pictures it drew ({loop.CameraAge.Frames} frames, worst {loop.CameraAge.WorstMilliseconds:0} ms).");
        Results.Add($"PASS ar recording: {takes} takes (stage shape, square with bars, mirrored) hold the camera picture and the lit keys where they are in each take, read back from the render thread's own frames in {started.Elapsed.TotalSeconds:0.0} s; each counted its 80 ms old picture once (the app added at most {maxAge:0} ms before it was drawn).");
    }

    /// <summary>
    /// AR-3.1, the camera delay compensation on the real GPU path: with a delay set, the notes are drawn where the song's position
    /// of that long ago puts them, and a key the player presses lights up that much later on the render thread's clock; with no
    /// delay nothing moves. Read back from the rendered frames on WARP.
    /// </summary>
    /// <summary>
    /// The delay measurement on the real clock and real threads: a camera thread publishing frames into a ring whose tap feeds the
    /// measurement, a thread playing twenty notes and a picture that flashes 80 ms after each, the way a camera that is 80 ms
    /// behind would show a hand coming down. It must find about 80 ms, add the app's share, and leave the ring working.
    /// </summary>
    private static void VerifyArLatency()
    {
        const int w = 160, h = 90;
        var ring = new CameraFrameRing();
        var measurement = new LatencyMeasurement(Stopwatch.Frequency);
        var region = new PixelRegion(30, 20, 130, 75);
        var random = new Random(1234);
        var notes = new List<double>();
        for (var t = 1.0; notes.Count < LatencyMeasurement.TargetPresses;) { t += .4 + random.NextDouble() * .5; notes.Add(t); }
        const double Delay = .08;
        var started = Stopwatch.GetTimestamp();
        double Now() => (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;

        measurement.Start(region, started);
        ring.FrameTap = measurement.AddFrame;
        var quit = false;
        var next = 0;
        var camera = new Thread(() =>
        {
            var seen = 0L;
            while (!Volatile.Read(ref quit))
            {
                var now = Now();
                var buffer = ring.BeginWrite(w * h * 4, out var slot);
                var flash = notes.Any(note => now >= note + Delay && now < note + Delay + .02);
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var i = (y * w + x) * 4;
                        var value = (byte)(60 + (flash && x is >= 50 and < 110 && y is >= 30 and < 60 ? 150 : 0));
                        buffer[i] = buffer[i + 1] = buffer[i + 2] = value; buffer[i + 3] = 255;
                    }
                ring.Publish(slot, w, h, Stopwatch.GetTimestamp());
                if (ring.TryTake(out var taken)) seen = taken.Sequence;                      // the drawing thread, as far as the ring is concerned
                Thread.Sleep(10);
            }
            Volatile.Write(ref quit, true);
            _ = seen;
        }) { IsBackground = true };
        camera.Start();

        LatencyReport? report = null;
        var age = new CameraAge(100, 10, 14, 30);
        while (report is null && Now() < 40)
        {
            var now = Now();
            while (next < notes.Count && notes[next] <= now) { measurement.NoteOn(started + (long)(notes[next] * Stopwatch.Frequency)); next++; }
            report = measurement.Poll(Stopwatch.GetTimestamp(), age);
            Thread.Sleep(5);
        }
        Volatile.Write(ref quit, true);
        camera.Join(5000);
        ring.FrameTap = null;

        Assert(report is not null, $"The measurement should have finished within 40 s ({measurement.Presses} presses, {ring.Published} frames).");
        var estimate = report!.Estimate;
        Assert(estimate.IsMeasured, $"The delay should have been measured: {estimate} ({ring.Published} frames).");
        // the camera thread sleeps in steps of about 15 ms here, so a frame can be that late in showing what it saw
        Assert(Math.Abs(estimate.DelaySeconds - Delay) <= .05, $"The delay found ({estimate.DelaySeconds * 1000:0} ms) should be within 50 ms of the 80 the picture was given.");
        Assert(report.AppAgeMilliseconds == 14 && report.SuggestedMilliseconds == (int)Math.Round(estimate.DelaySeconds * 1000 + 14), $"The suggestion ({report.SuggestedMilliseconds} ms) should be the delay plus the app's 14 ms.");
        Assert(measurement.Presses == LatencyMeasurement.TargetPresses && measurement.State == MeasurementState.Finished, "The run should be over with twenty presses.");
        Results.Add($"NOTE VerifyArLatency: found {estimate.DelaySeconds * 1000:0} ms of 80 ({estimate.Confidence:0.00} sure, {ring.Published} frames), suggested {report.SuggestedMilliseconds} ms.");
        var before = ring.Published;
        var buffer2 = ring.BeginWrite(w * h * 4, out var slot2); ring.Publish(slot2, w, h, 1);
        Assert(ring.Published == before + 1 && ring.TryTake(out _), "The ring should keep working after the tap is taken off.");
    }

    private static void VerifyArDelay()
    {
        const int cw = 1280, ch = 720, width = 640, height = 360;
        var quiet = new PianoVisualSettings
        {
            BackgroundGradient = false, ShowStars = false, ShowHalo = false, ShowKeys = false, ShowNotes = false,
            ShowEmbers = false, ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowLightBeams = false, ShowPetals = false, HorizonGlow = 0, Vignette = 0, BloomIntensity = 0
        };
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], cw, ch, out var problem);
        Assert(calibration is not null, $"The sample keyboard's corners must make a calibration ({problem}).");
        var ring = new CameraFrameRing();
        void PublishGrey()
        {
            var buffer = ring.BeginWrite(cw * ch * 4, out var slot);
            Array.Fill(buffer, (byte)90, 0, cw * ch * 4);
            for (var i = 3; i < cw * ch * 4; i += 4) buffer[i] = 255;
            ring.Publish(slot, cw, ch, Stopwatch.GetTimestamp());
        }
        var started = Stopwatch.StartNew();

        // ---- notes: drawn for the song's position of one delay ago ------------------------------------------
        var song = new List<NoteEvent> { new() { Pitch = 60, Start = 10.5, Duration = .2 } };
        var view = new ArView(cw, ch, width, height, PictureFit.Fit, false);
        (int X, int Y) CentreAt(double position)
        {
            var shapes = new List<ArNoteShape>();
            ArNotes.Build(calibration!, view, song, position, 1, ArNoteStyle.Default, shapes);
            Assert(shapes.Count == 1, $"The one note should be in reach at position {position:0.00} ({shapes.Count} shapes).");
            var patch = shapes[0].Patch;
            double x = 0, y = 0;
            for (var i = 0; i < 4; i++) { x += patch[i].X / 4; y += patch[i].Y / 4; }
            return ((int)Math.Floor(x), (int)Math.Floor(y));
        }
        var (nowX, nowY) = CentreAt(10);
        var (laterX, laterY) = CentreAt(9.7);
        Assert(Math.Abs(nowY - laterY) > 8, $"A 0.3 s delay should move the note by more than its own length ({nowY} against {laterY}).");
        using var renderer = GpuStageRenderer.Create(forceWarp: true);
        PublishGrey();
        byte[] Render(float delay)
        {
            var feed = new GpuStageFeed(); feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(quiet, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
            feed.SetState(song, 10, true, new HashSet<int>());
            feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1f);
            feed.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, ShowNotes: true, DelaySeconds: delay);
            PublishGrey();
            return GpuRenderLoop.RenderFrames(renderer, feed, width, height, 0, 1 / 60.0, false, out _);
        }
        int Spread(byte[] pixels, (int X, int Y) at)
        {
            Assert(at.X >= 0 && at.Y >= 0 && at.X < width && at.Y < height, $"The probe ({at.X},{at.Y}) must be on the output.");
            var i = (at.Y * width + at.X) * 4;
            return Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) - Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
        }
        var straight = Render(0);
        Assert(Spread(straight, (nowX, nowY)) > 30 && Spread(straight, (laterX, laterY)) <= 3, "With no delay the note is drawn at the song's position, and nowhere else.");
        var delayed = Render(.3f);
        Assert(Spread(delayed, (laterX, laterY)) > 30 && Spread(delayed, (nowX, nowY)) <= 3, "With a 0.3 s delay the note is drawn where it was 0.3 s ago, and not where it is.");

        // ---- keys: a pressed key lights up one delay later on the render thread's clock ----------------------
        var (kx, ky) = view.ToOutput(calibration!.ToPicture(calibration.Layout.CenterX(60), .85).X, calibration.ToPicture(calibration.Layout.CenterX(60), .85).Y);
        var feedKeys = new GpuStageFeed(); feedKeys.SetStageHeight(height);
        feedKeys.SetLook(GpuLook.From(quiet, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
        feedKeys.SetState([], 0, false, new HashSet<int>());
        feedKeys.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1f);
        using var loop = new GpuRenderLoop(feedKeys, forceWarp: true);
        double SecondsUntilLit(float delay)
        {
            var tap = new GpuRecordingTap(width, height, 60);
            int Grey() { var v = 0; tap.TryRead(frame => { var i = ((int)ky * width + (int)kx) * 4; v = Math.Max(frame[i], Math.Max(frame[i + 1], frame[i + 2])) - Math.Min(frame[i], Math.Min(frame[i + 1], frame[i + 2])); }); return v; }
            // first with no delay and the key up until the last glow of an earlier press has died away, so the history starts dark
            feedKeys.SetState([], 0, false, new HashSet<int>());
            feedKeys.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, ShowNotes: true, DelaySeconds: 0f);
            PublishGrey();
            feedKeys.Recording = tap;
            var wait = Stopwatch.StartNew();
            while ((tap.Serial < 8 || Grey() > 3) && loop.Error is null && wait.Elapsed.TotalSeconds < 30) Thread.Sleep(10);
            Assert(loop.Error is null && tap.Serial >= 8 && Grey() <= 3, $"Before the key is pressed its place in the picture is the camera's grey ({loop.Error}).");
            feedKeys.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, ShowNotes: true, DelaySeconds: delay);
            Thread.Sleep((int)(delay * 1000) + 250);                               // the delayed history now holds only dark
            Assert(Grey() <= 3, "Switching the delay on must not light anything by itself.");
            var pressed = Stopwatch.StartNew();
            feedKeys.SetState([], 0, false, new HashSet<int> { 60 });
            double found = -1;
            while (pressed.Elapsed.TotalSeconds < 6)
            {
                if (Grey() > 25) { found = pressed.Elapsed.TotalSeconds; break; }
                Thread.Sleep(2);
            }
            feedKeys.Recording = null;
            Assert(found >= 0, $"A key pressed with a delay of {delay:0.0} s should light up in the end ({loop.Error}).");
            return found;
        }
        var straightSeconds = SecondsUntilLit(0);
        var delayedSeconds = SecondsUntilLit(.3f);
        Assert(delayedSeconds >= .27 && delayedSeconds - straightSeconds >= .2,
            $"With a 0.3 s delay the key must light up 0.3 s later (no delay: {straightSeconds:0.000} s, delay: {delayedSeconds:0.000} s).");
        Results.Add($"PASS ar delay: notes drawn {nowY - laterY} px apart for a 0.3 s delay (and only there), and a pressed key lit after {straightSeconds * 1000:0} ms with no delay and {delayedSeconds * 1000:0} ms with a 0.3 s delay, in {started.Elapsed.TotalSeconds:0.0} s on {renderer.AdapterName}.");
    }

    /// <summary>
    /// AR-1.5, the calibration window end to end on the real window: it refuses to open without a camera, a picture or the right
    /// size; shows the camera's picture the way the stage shows it; takes four clicks to a calibration that is saved for this
    /// camera; exercises the full-object Move/Rotate/Scale gizmo and numeric controls, the independent 3D note-direction arrow,
    /// screen-up/key-depth aiming and FOV persistence; keeps Done shut on corners that cannot be used; saves nothing when cancelled
    /// or when the camera changed behind it; and draws the picture, the corners and the key grid where the wizard says they are.
    /// </summary>
    private static void VerifyCalibrationWindow(MainWindow window, PianoVisualSettings visualSettings)
    {
        const int W = 1280, H = 720;
        var ring = (CameraFrameRing)Field(window, "_cameraFrames");
        var mirrorWas = visualSettings.CameraMirror;
        var viewWas = visualSettings.ArViewProfileId;
        var noteTiltWas = visualSettings.ArNoteDirectionTilt;
        var noteAzimuthWas = visualSettings.ArNoteDirectionAzimuth;
        var view = ArCalibrationProfiles.Normalize(viewWas).Id;
        var otherView = ArCalibrationProfiles.All.First(profile => profile.Id != view);
        var keyCount = visualSettings.KeyboardLayoutChoice.KeyCount;
        (double X, double Y)[] truth = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
        (double X, double Y)[] flat = [(100, 300), (400, 300), (700, 300), (1000, 300)];
        (double X, double Y)[] moved = [(94, 274), (1238, 250), (1260, 403), (76, 431)];      // a key area of its own, to tell a save that landed from one that did not

        // the left half of the picture is red, the right half green: a picture turned round shows at once
        var halves = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                halves[i + 1] = (byte)(x >= W / 2 ? 200 : 0); halves[i + 2] = (byte)(x < W / 2 ? 200 : 0); halves[i + 3] = 255;
            }

        var opened = 0;
        void Open(Func<CalibrationWindow, bool?>? script) => window.CalibrateAr(w => { opened++; return script?.Invoke(w) ?? false; });
        static void Click(CalibrationWindow w, (double X, double Y)[] points)
        {
            foreach (var point in points)
            {
                var (x, y) = w.Wizard.View.ToOutput(point.X, point.Y);
                Assert(w.Surface.HandlePress(new Point(x, y)), "A press on the picture should place or pick up a corner.");
                w.Surface.HandleRelease();
            }
        }
        static void Arrange(CalibrationWindow w)
        {
            w.Surface.Measure(new Size(1000, 600)); w.Surface.Arrange(new Rect(0, 0, 1000, 600)); w.Surface.UpdateLayout();
        }
        static void ClickButton(System.Windows.Controls.Button button) =>
            button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        static byte[] Shot(CalibrationWindow w)
        {
            // the surface is not in a shown window, so its own drawing is asked for directly, as the stage's overlay is
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) Invoke(w.Surface, "OnRender", dc);
            var bitmap = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[1000 * 600 * 4];
            bitmap.CopyPixels(pixels, 4000, 0);
            return pixels;
        }
        static int Differs(byte[] shot, int x, int y, byte b, byte g, byte r) => Math.Max(Math.Abs(shot[(y * 1000 + x) * 4] - b), Math.Max(Math.Abs(shot[(y * 1000 + x) * 4 + 1] - g), Math.Abs(shot[(y * 1000 + x) * 4 + 2] - r)));
        static bool SameCorners(IReadOnlyList<(double X, double Y)>? a, (double X, double Y)[] b) =>
            a is not null && a.Count == b.Length && a.Zip(b).All(pair => Math.Abs(pair.First.X - pair.Second.X) < 1e-6 && Math.Abs(pair.First.Y - pair.Second.Y) < 1e-6);
        static bool ScreenPoint(ArCameraPose pose, ArVec3 local, ArView view, out Point point)
        {
            point = default;
            if (!pose.TryProject(local, view, out var projected)) return false;
            point = new Point(projected.X, projected.Y);
            return true;
        }
        static bool ScreenAxis(ArCameraPose pose, ArCameraPose axisPose, ArVec3 centre, ArVec3 axis, ArView view, out System.Windows.Vector derivative)
        {
            derivative = default;
            if (!pose.TryProjectDelta(pose.ToCamera(centre), axisPose.DirectionToCamera(axis), view, out var projected)) return false;
            derivative = new System.Windows.Vector(projected.X, projected.Y);
            return derivative.Length > 1e-6 && double.IsFinite(derivative.X) && double.IsFinite(derivative.Y);
        }

        // ---- it opens only when there is something to mark corners on ----
        Invoke(window, "ApplyArSource", null!, 0, 0); ring.Clear();
        Open(null);
        Assert(opened == 0, "Without a camera the calibration window must not open.");
        window.MeasureArDelay();
        Assert(!window.MeasuringArDelay, "Without the overlay on marked keys, a live camera and a MIDI keyboard no measurement may start.");
        Invoke(window, "ApplyArSource", "verification camera", W, H);
        Open(null);
        Assert(opened == 0, "A camera that has delivered no picture yet must not open the calibration window.");
        Invoke(window, "InjectCameraFrame", new byte[640 * 360 * 4], 640, 360);
        Open(null);
        Assert(opened == 0, "A picture of another size than the overlay reads must not open the calibration window.");
        Assert(window.ArCalibration.State == ArCalibrationState.NotCalibrated, "Refusing to open must leave the calibration alone.");

        foreach (var mirror in new[] { false, true })
        {
            window.ArCalibration.Reset();
            visualSettings.CameraMirror = mirror;
            Invoke(window, "InjectCameraFrame", halves, W, H);
            var framesMirrored = mirror && !(bool)Field(window, "_arRawFrames");
            var before = opened;
            var savedNoteDirection = ArNoteDirection.Default;
            Open(w =>
            {
                Arrange(w);
                Assert(w.Wizard.View.Mirror == mirror && w.Wizard.ImageWidth == W && w.Wizard.ImageHeight == H, "The window should show the picture as the stage does and know its size.");
                // the picture handed over is the one the camera delivered, whichever way the thread that read it had turned it
                Assert(w.Surface.Picture.Bgra[1] == (framesMirrored ? 200 : 0) && w.Surface.Picture.Bgra[2] == (framesMirrored ? 0 : 200),
                    "The window should hold the picture as the camera delivered it, not turned round.");
                Assert(!w.DoneButton.IsEnabled && w.HintText == Loc.T(CalibrationHints.ForStep(w.Wizard)), "Before any corner is placed Done is shut and the line says what to click.");
                var first = w.HintText;
                var shot = Shot(w);
                foreach (var screenX in new[] { 250, 750 })
                {
                    var (ix, iy) = w.Wizard.View.ToImage(screenX, 80);
                    var at = ((int)iy * W + (int)ix) * 4;
                    var got = (shot[(80 * 1000 + screenX) * 4], shot[(80 * 1000 + screenX) * 4 + 1], shot[(80 * 1000 + screenX) * 4 + 2], shot[(80 * 1000 + screenX) * 4 + 3]);
                    Assert(Differs(shot, screenX, 80, w.Surface.Picture.Bgra[at], w.Surface.Picture.Bgra[at + 1], w.Surface.Picture.Bgra[at + 2]) <= 3,
                        $"The picture should be drawn where the view says, turned round when the stage turns it round (mirror {mirror}, x {screenX}: drew BGRA {got}, picture has {w.Surface.Picture.Bgra[at]},{w.Surface.Picture.Bgra[at + 1]},{w.Surface.Picture.Bgra[at + 2]}; surface {w.Surface.ActualWidth}x{w.Surface.ActualHeight}).");
                }
                Assert(Differs(shot, 500, 5, 12, 7, 8) <= 3, "The bars around the picture should be the dark backdrop.");

                Click(w, truth[..3]);
                Assert(!w.DoneButton.IsEnabled && w.HintText != first, "With three corners placed Done stays shut and the line moves to the next corner.");
                Click(w, truth[3..]);
                Assert(w.DoneButton.IsEnabled && w.Wizard.Step == WizardStep.Adjust, "Four good corners should open Done.");
                var drawn = Shot(w);
                var keys = w.Wizard.PreviewKeys();
                Assert(keys.Count == keyCount, $"The grid should have a key for every key of the keyboard (found {keys.Count}).");
                var c4 = keys.First(key => key.Pitch == 60);
                var mx = (int)(c4.Corners.Average(c => c.X)); var my = (int)(c4.Corners.Average(c => c.Y));
                var (jx, jy) = w.Wizard.View.ToImage(mx, my);
                var under = ((int)jy * W + (int)jx) * 4;
                Assert(Differs(drawn, mx, my, w.Surface.Picture.Bgra[under], w.Surface.Picture.Bgra[under + 1], w.Surface.Picture.Bgra[under + 2]) >= 15,
                    "The grid of keys should be drawn over the picture once the four corners can be used.");
                var (cx, cy) = w.Wizard.PointOnScreen(w.Wizard.Selected);
                Assert(Differs(drawn, (int)Math.Round(cx), (int)Math.Round(cy), 255, 230, 67) <= 8, "The corner that was placed last should be drawn, filled, where it was clicked.");
                // the choice of how the keyboard looks in the picture: starts as upright in the picture as delivered, and changes how the same corners are read
                var upright = KeyboardOrientations.Default(mirror);
                Assert(w.OrientationBox.Items.Count == 4 && w.Wizard.Orientation == upright && Equals(w.OrientationBox.SelectedValue, upright),
                    "The window should start with the keyboard upright in the picture as delivered, and offer the four ways it can look.");
                var turned = upright == KeyboardOrientation.LowRightFarBottom ? KeyboardOrientation.Upright : KeyboardOrientation.LowRightFarBottom;
                w.OrientationBox.SelectedValue = turned;
                Assert(w.Wizard.Orientation == turned && w.Wizard.Points.Count == 4 && w.Wizard.PreviewKeys().Count == keyCount,
                    "Choosing another orientation should read the same four corners another way and move none of them.");
                w.ResetOrientationButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(w.Wizard.Orientation == upright && w.DoneButton.IsEnabled && w.Wizard.Step == WizardStep.Adjust,
                    "The circular reset control should restore the orientation that was active when calibration opened.");
                Assert(w.ToolBox.Items.Count == 5 && w.PositionXSlider.IsEnabled && w.UniformScaleSlider.IsEnabled,
                    "A usable calibration should enable the five gizmo modes and the object translation/uniform-scale controls.");
                var directionBeforeObjectTransform = w.NoteDirection;
                var fovBeforeObjectTransform = w.Wizard.CameraFovDegrees;
                w.PositionXSlider.Value = .08; w.PositionYSlider.Value = -.04; w.UniformScaleSlider.Value = 1.1;
                Assert(Math.Abs(w.Wizard.PoseTransform.PositionX - .08) < .001 && Math.Abs(w.Wizard.PoseTransform.PositionY + .04) < .001
                        && Math.Abs(w.Wizard.PoseTransform.UniformScale - 1.1) < .001 && w.NoteDirection == directionBeforeObjectTransform
                        && Math.Abs(w.Wizard.CameraFovDegrees - fovBeforeObjectTransform) < .001,
                    "Numeric controls should apply object translation and one uniform scale without changing independent note travel or camera FOV.");
                w.PositionXSlider.Value = 0; w.PositionYSlider.Value = 0; w.UniformScaleSlider.Value = 1;
                w.ToolBox.SelectedValue = CalibrationTool.Move;
                var moveCalibration = w.Wizard.CurrentCalibration!;
                var movePose = moveCalibration.CameraPose!.Value;
                var baseCalibration = ArCalibration.Create(w.Wizard.Layout, moveCalibration.PoseBaseCorners, W, H, out _, w.Wizard.CameraFovDegrees);
                Assert(baseCalibration?.CameraPose is not null, "The original key-area baseline should supply the gizmo's world axes.");
                var basePose = baseCalibration!.CameraPose!.Value;
                var gizmoCentre = new ArVec3(movePose.KeyboardWidthDepthRatio * .5, .5, 0);
                Assert(ScreenPoint(movePose, gizmoCentre, w.Wizard.View, out var gizmoOrigin),
                    "The Move gizmo origin should project through the keyboard centre.");
                Assert(ScreenAxis(movePose, basePose, gizmoCentre, new ArVec3(1, 0, 0), w.Wizard.View, out var moveAxis),
                    "The Move gizmo should project a screen-space axis through the keyboard centre.");
                moveAxis.Normalize();
                var moveStart = gizmoOrigin + moveAxis * 50;
                Assert(w.Surface.HandlePress(moveStart), "Pressing a Move gizmo axis should start a transform drag.");
                w.Surface.HandleMove(moveStart + moveAxis * 8);
                w.Surface.HandleRelease();
                Assert(Math.Abs(w.Wizard.PoseTransform.PositionX) + Math.Abs(w.Wizard.PoseTransform.PositionY)
                        + Math.Abs(w.Wizard.PoseTransform.PositionZ) > .001 && w.NoteDirection == directionBeforeObjectTransform
                        && Math.Abs(w.Wizard.CameraFovDegrees - fovBeforeObjectTransform) < .001,
                    "Dragging the Move gizmo should translate the keyboard without changing note direction or camera FOV.");
                Assert(w.Wizard.SetObjectTransform(ArPoseTransform.Identity, w.Wizard.CameraFovDegrees),
                    "After testing this gizmo mode, the object transform should return to its original baseline.");
                Invoke(w, "SyncPoseControls"); w.Surface.Refresh();

                w.ToolBox.SelectedValue = CalibrationTool.Rotate;
                var compoundPose = new ArPoseTransform(.02, -.01, .01, 8, -5, 3, 1);
                Assert(w.Wizard.SetObjectTransform(compoundPose, w.Wizard.CameraFovDegrees),
                    "The Rotate gizmo should accept a compound arbitrary 3D keyboard orientation.");
                Invoke(w, "SyncPoseControls"); w.Surface.Refresh();
                foreach (var axis in new[] { 'X', 'Y', 'Z' })
                {
                    var hasRingStart = w.Surface.TryRotationRingPoint(axis, 37, out var ringStart);
                    var hasRingTarget = w.Surface.TryRotationRingPoint(axis, 49, out var ringTarget);
                    Assert(hasRingStart && hasRingTarget, $"The {axis} rotation ring should project through the arbitrary 3D pose.");
                    var before = w.Wizard.PoseTransform;
                    Assert(w.Surface.HandlePress(ringStart), $"Pressing the projected {axis} ring should start its 3D rotation drag.");
                    w.Surface.HandleMove(ringTarget);
                    w.Surface.HandleRelease();
                    var after = w.Wizard.PoseTransform;
                    double Component(char selected) => selected switch
                    {
                        'X' => after.PitchDegrees - before.PitchDegrees,
                        'Y' => after.YawDegrees - before.YawDegrees,
                        _ => after.RollDegrees - before.RollDegrees,
                    };
                    Assert(Math.Abs(Component(axis) - 12) < .3,
                        $"Dragging the {axis} ring should change its matching Euler component by the 12-degree projected arc.");
                    Assert(axis != 'X' || (Math.Abs(after.YawDegrees - before.YawDegrees) < .001 && Math.Abs(after.RollDegrees - before.RollDegrees) < .001),
                        "The X rotation ring should not modify Y or Z rotation.");
                    Assert(axis != 'Y' || (Math.Abs(after.PitchDegrees - before.PitchDegrees) < .001 && Math.Abs(after.RollDegrees - before.RollDegrees) < .001),
                        "The Y rotation ring should not modify X or Z rotation.");
                    Assert(axis != 'Z' || (Math.Abs(after.PitchDegrees - before.PitchDegrees) < .001 && Math.Abs(after.YawDegrees - before.YawDegrees) < .001),
                        "The Z rotation ring should not modify X or Y rotation.");
                    Assert(w.Surface.TryRotationRingPoint(axis, 37, out var ringAfter)
                            && (ringAfter - ringStart).Length < .001,
                        $"The {axis} ring should stay anchored while its own angle changes, rather than drifting around the screen centre.");
                }
                Assert(w.NoteDirection == directionBeforeObjectTransform && Math.Abs(w.Wizard.CameraFovDegrees - fovBeforeObjectTransform) < .001,
                    "Arbitrary ring rotation should preserve independent note direction and camera FOV.");
                Assert(w.Wizard.SetObjectTransform(ArPoseTransform.Identity, w.Wizard.CameraFovDegrees),
                    "After testing this gizmo mode, the object transform should return to its original baseline.");
                Invoke(w, "SyncPoseControls"); w.Surface.Refresh();

                w.ToolBox.SelectedValue = CalibrationTool.Scale;
                var scaleCalibration = w.Wizard.CurrentCalibration!;
                var scalePose = scaleCalibration.CameraPose!.Value;
                var scaleCentre = new ArVec3(scalePose.KeyboardWidthDepthRatio * .5, .5, 0);
                Assert(ScreenPoint(scalePose, scaleCentre, w.Wizard.View, out var scaleOrigin),
                    "The Scale gizmo centre should project through the keyboard centre.");
                Assert(ScreenAxis(scalePose, scalePose, scaleCentre, new ArVec3(1, 1, 0).Normalized, w.Wizard.View, out var scaleAxis),
                    "The Scale gizmo should project its uniform-scale handle.");
                scaleAxis.Normalize();
                var scaleEnd = scaleOrigin + scaleAxis * 78;
                Assert(w.Surface.HandlePress(scaleEnd), "Pressing the Scale gizmo handle should start a uniform-scale drag.");
                w.Surface.HandleMove(scaleEnd + scaleAxis * 12);
                w.Surface.HandleRelease();
                Assert(w.Wizard.PoseTransform.UniformScale > 1.01,
                    "Dragging Scale should update one uniform scale while keeping all keyboard proportions.");
                Assert(w.Wizard.SetObjectTransform(ArPoseTransform.Identity, w.Wizard.CameraFovDegrees),
                    "After testing this gizmo mode, the object transform should return to its original baseline.");
                Invoke(w, "SyncPoseControls"); w.Surface.Refresh();

                w.ToolBox.SelectedValue = CalibrationTool.NoteDirection;
                w.NoteTiltSlider.Value = 90; w.NoteAzimuthSlider.Value = 90;
                Assert(w.NoteDirection.Vector.X > .999 && Math.Abs(w.NoteDirection.Vector.Y) < .001
                        && Math.Abs(w.NoteDirection.Vector.Z) < .001,
                    "The independent tilt and azimuth sliders should aim notes toward across-key motion in 3D.");
                w.KeyDepthButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(w.NoteDirection == ArNoteDirection.Default, "The key-depth control should aim perpendicular to the key row.");
                w.ScreenUpButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var directionCalibration = w.Wizard.CurrentCalibration!;
                var directionPose = directionCalibration.CameraPose!.Value;
                var directionCentre = new ArVec3(directionPose.KeyboardWidthDepthRatio * .5, .5, 0);
                Assert(directionPose.TryProjectDelta(directionPose.ToCamera(directionCentre), directionPose.DirectionToCamera(w.NoteDirection.Vector),
                    w.Wizard.View, out var screenDirection), "The chosen note direction should project at its keyboard centre.");
                var screenLength = Math.Sqrt(screenDirection.X * screenDirection.X + screenDirection.Y * screenDirection.Y);
                Assert(screenLength > 1e-6 && Math.Abs(screenDirection.X / screenLength) < 1e-3 && screenDirection.Y / screenLength < -.999
                        && Math.Abs(w.NoteTiltSlider.Value - w.NoteDirection.TiltDegrees) < .001,
                    "The preview arrow and direction sliders should agree on a 3D keyboard-plane direction projecting straight up.");
                Assert(ScreenPoint(directionPose, directionCentre, w.Wizard.View, out var arrowOrigin),
                    "The note-direction arrow should have a projectable centre for free aiming.");
                var up = new System.Windows.Vector(screenDirection.X / screenLength, screenDirection.Y / screenLength);
                var dragStart = arrowOrigin + up * 56;
                const double aimDragRadians = 35 * Math.PI / 180;
                var aimed = new System.Windows.Vector(up.X * Math.Cos(aimDragRadians) - up.Y * Math.Sin(aimDragRadians),
                    up.X * Math.Sin(aimDragRadians) + up.Y * Math.Cos(aimDragRadians));
                var dragTarget = arrowOrigin + aimed * 56;
                Assert(w.Surface.HandlePress(dragStart), "Pressing the note-direction arrow should begin free 3D aiming.");
                w.Surface.HandleMove(dragTarget);
                w.Surface.HandleRelease();
                Assert(directionPose.TryProjectDelta(directionPose.ToCamera(directionCentre),
                    directionPose.DirectionToCamera(w.NoteDirection.Vector), w.Wizard.View, out var aimedScreenDirection),
                    "The dragged note direction should remain projectable in the current keyboard pose.");
                var aimedLength = Math.Sqrt(aimedScreenDirection.X * aimedScreenDirection.X + aimedScreenDirection.Y * aimedScreenDirection.Y);
                Assert(aimedLength > 1e-6 && Math.Abs(aimedScreenDirection.X / aimedLength - aimed.X) < 1e-3
                        && Math.Abs(aimedScreenDirection.Y / aimedLength - aimed.Y) < 1e-3
                        && Math.Abs(w.NoteTiltSlider.Value - w.NoteDirection.TiltDegrees) < .001,
                    "Dragging the preview arrow should choose an arbitrary screen heading and keep its numeric 3D controls synchronized.");

                ClickButton(w.ResetToolButton);
                Assert(Equals(w.ToolBox.SelectedValue, CalibrationTool.Corners), "The tool reset should return to corner editing.");
                w.PitchSlider.Value = 12; w.YawSlider.Value = -11; w.RollSlider.Value = 8;
                ClickButton(w.ResetYawButton);
                Assert(Math.Abs(w.Wizard.PitchDegrees - 12) < .001 && Math.Abs(w.Wizard.YawDegrees) < .001 && Math.Abs(w.Wizard.RollDegrees - 8) < .001,
                    "Resetting one Euler control should restore only that control to its default.");
                ClickButton(w.ResetPitchButton); ClickButton(w.ResetRollButton);
                Assert(w.Wizard.PoseTransform.Rotation == default, "The three per-axis rotation resets should restore the identity orientation.");
                w.CameraFovSlider.Value = 89; ClickButton(w.ResetFovButton);
                Assert(Math.Abs(w.Wizard.CameraFovDegrees - ArCameraPose.DefaultFovDegrees) < .001,
                    "The FOV reset should restore its standard 70-degree default.");
                w.PositionXSlider.Value = .2; w.PositionYSlider.Value = -.15; w.PositionZSlider.Value = .1; w.UniformScaleSlider.Value = 1.25;
                ClickButton(w.ResetPositionXButton);
                Assert(Math.Abs(w.Wizard.PoseTransform.PositionX) < .001 && Math.Abs(w.Wizard.PoseTransform.PositionY + .15) < .001,
                    "Resetting one translation should leave the other object controls untouched.");
                ClickButton(w.ResetPositionYButton); ClickButton(w.ResetPositionZButton); ClickButton(w.ResetScaleButton);
                Assert(w.Wizard.PoseTransform == ArPoseTransform.Identity, "The per-axis translation and scale resets should restore an identity object transform.");
                w.NoteTiltSlider.Value = 48; w.NoteAzimuthSlider.Value = -72;
                ClickButton(w.ResetNoteTiltButton);
                Assert(Math.Abs(w.NoteDirection.TiltDegrees) < .001 && Math.Abs(w.NoteDirection.AzimuthDegrees + 72) < .001,
                    "Resetting note tilt should preserve independently adjustable note azimuth.");
                ClickButton(w.ResetNoteAzimuthButton);
                Assert(w.NoteDirection == ArNoteDirection.Default, "The note-direction reset controls should restore each default component independently.");
                savedNoteDirection = w.NoteDirection;
                w.ToolBox.SelectedValue = CalibrationTool.Corners;
                var unrotatedPreview = w.Wizard.PreviewKeys().First(key => key.Pitch == 60).Corners;
                w.PitchSlider.Value = 12;
                var rotatedPreview = w.Wizard.PreviewKeys().First(key => key.Pitch == 60).Corners;
                Assert(w.PitchSlider.IsEnabled && rotatedPreview.Zip(unrotatedPreview).Any(pair => Math.Abs(pair.First.X - pair.Second.X) > 1 || Math.Abs(pair.First.Y - pair.Second.Y) > 1)
                        && w.DoneButton.IsEnabled && w.Wizard.Points.Count == 4,
                    "The pitch control should rotate the calibrated keyboard in 3D, keeping all four corners usable and Done enabled.");
                w.PitchSlider.Value = 0;
                Assert(SameCorners(w.Wizard.UsableCorners, truth), "Returning the 3D pitch control to zero should restore the same ordered corners.");
                w.CameraFovSlider.Value = 83;
                Assert(Math.Abs(w.Wizard.CameraFovDegrees - 83) < .01,
                    "The camera FOV control should update the pose estimate independently of pitch, yaw and roll.");
                w.ScreenUpButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                savedNoteDirection = w.NoteDirection;
                var finalDirectionPose = w.Wizard.CurrentCalibration!.CameraPose!.Value;
                var finalDirectionCentre = new ArVec3(finalDirectionPose.KeyboardWidthDepthRatio * .5, .5, 0);
                Assert(finalDirectionPose.TryProjectDelta(finalDirectionPose.ToCamera(finalDirectionCentre),
                    finalDirectionPose.DirectionToCamera(savedNoteDirection.Vector), w.Wizard.View, out var finalScreenDirection),
                    "The final screen-up direction should still project through the final FOV pose.");
                var finalScreenLength = Math.Sqrt(finalScreenDirection.X * finalScreenDirection.X + finalScreenDirection.Y * finalScreenDirection.Y);
                Assert(finalScreenLength > 1e-6 && Math.Abs(finalScreenDirection.X / finalScreenLength) < 1e-3
                        && finalScreenDirection.Y / finalScreenLength < -.999,
                    "Screen-up aiming should be based on the currently saved FOV and keyboard pose.");
                w.Accept();
                return w.Result is not null;
            });
            Assert(opened == before + 1, "A camera with a picture of the right size should open the window once.");
            Assert(window.ArCalibration.State == ArCalibrationState.Ready && SameCorners(window.ArCalibration.Current?.Corners, truth)
                    && Math.Abs((window.ArCalibration.Current?.CameraFovDegrees ?? 0) - 83) < .01,
                $"Four clicks and Done should calibrate this camera with the marked corners and chosen FOV (mirror {mirror}).");
            Assert(SameCorners(window.ArCalibration.Store.Get("verification camera", view, W, H, keyCount), truth)
                    && Math.Abs(window.ArCalibration.Store.GetCameraFov("verification camera", view, W, H, keyCount) - 83) < .01,
                "Corners and camera FOV should be stored together for this camera, view, picture size and keyboard size.");
            Assert(Math.Abs(visualSettings.ArNoteDirectionTilt - savedNoteDirection.TiltDegrees) < .001
                    && Math.Abs(visualSettings.ArNoteDirectionAzimuth - savedNoteDirection.AzimuthDegrees) < .001,
                "Accepting Calibrate should save the independent note direction into the user's visual settings.");
            Open(w =>
            {
                Arrange(w);
                w.PitchSlider.Value = 1; w.YawSlider.Value = -1; w.RollSlider.Value = 1;
                w.CameraFovSlider.Value = 97;
                w.StartOverButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(w.Wizard.Points.Count == 0 && w.Wizard.PitchDegrees == 0 && w.Wizard.YawDegrees == 0 && w.Wizard.RollDegrees == 0
                        && w.PitchSlider.Value == 0 && w.YawSlider.Value == 0 && w.RollSlider.Value == 0
                        && Math.Abs(w.Wizard.CameraFovDegrees - 97) < .01 && Math.Abs(w.CameraFovSlider.Value - 97) < .01,
                    "Start over should sync the reset pitch/yaw/roll controls while preserving the chosen camera FOV.");
                return false;
            });
            Open(w =>
            {
                Arrange(w);
                Assert(SameCorners(w.Wizard.Points, truth) && SameCorners(w.Wizard.UsableCorners, truth) && w.RotationSlider.IsEnabled,
                    "Opening the already marked camera and view should load the saved, ordered corners for adjustment.");
                var savedOrientation = w.Wizard.Orientation;
                var alternateOrientation = savedOrientation == KeyboardOrientation.LowRightFarBottom ? KeyboardOrientation.Upright : KeyboardOrientation.LowRightFarBottom;
                w.OrientationBox.SelectedValue = alternateOrientation;
                Assert(!SameCorners(w.Wizard.UsableCorners, truth), "Changing orientation on a loaded, ordered calibration should relabel its corners rather than silently ignoring the choice.");
                w.OrientationBox.SelectedValue = savedOrientation;
                Assert(SameCorners(w.Wizard.UsableCorners, truth), "Restoring the saved orientation should restore the ordered corner mapping.");
                w.RotationSlider.Value = -6;
                Assert(!SameCorners(w.Wizard.Points, truth), "A loaded calibration should rotate as one ordered key grid.");
                w.RotationSlider.Value = 0;
                w.Accept();
                return w.Result is not null;
            });
            Assert(SameCorners(window.ArCalibration.Current?.Corners, truth),
                "Saving the restored zero-angle adjustment should keep the saved calibration's corner order and positions.");
        }

        // ---- corners that cannot be used keep Done shut ----
        window.ArCalibration.Reset();
        Open(w =>
        {
            Arrange(w);
            Click(w, flat);
            Assert(w.Wizard.Step == WizardStep.Fix && !w.DoneButton.IsEnabled && w.HintText == Loc.T(CalibrationHints.ForProblem(w.Wizard.Problem)),
                "Four corners on one line should shut Done and say why.");
            w.Accept();
            Assert(w.Result is null, "Done must do nothing while the corners cannot be used.");
            w.Surface.HandleKey(System.Windows.Input.Key.Back, System.Windows.Input.ModifierKeys.None);
            Assert(w.Wizard.Points.Count == 3, "Backspace should take the last corner back.");
            return false;
        });
        Assert(window.ArCalibration.State == ArCalibrationState.NotCalibrated, "A window closed without Done must save nothing.");

        // ---- New picture keeps the corners and refuses a picture of another size ----
        Open(w =>
        {
            Arrange(w);
            Click(w, truth[..2]);
            var blue = new byte[W * H * 4];
            for (var i = 0; i < blue.Length; i += 4) { blue[i] = 200; blue[i + 3] = 255; }
            Invoke(window, "InjectCameraFrame", blue, W, H);
            w.Retake();
            Assert(w.Surface.Picture.Bgra[0] == 200 && w.Wizard.Points.Count == 2 && w.NoteText.Length == 0, "New picture should show the newest frame and keep the corners placed.");
            Invoke(window, "InjectCameraFrame", new byte[320 * 180 * 4], 320, 180);
            w.Retake();
            Assert(w.Surface.Picture.Width == W && w.NoteText.Length > 0, "A new picture of another size must be refused with a word, keeping the one on show.");
            ring.Clear();
            w.Retake();
            Assert(w.NoteText.Length > 0, "With no frame to take New picture should say so.");
            return false;
        });

        // ---- the camera changing behind the open window ----
        Invoke(window, "InjectCameraFrame", halves, W, H);
        Open(w =>
        {
            Arrange(w);
            Click(w, truth);
            Invoke(window, "ApplyArSource", "another camera", W, H);
            w.Accept();
            return w.Result is not null;
        });
        Assert(window.ArCalibration.Snapshot.Source == "another camera" && window.ArCalibration.Current is null
                && window.ArCalibration.Store.Get("verification camera", view, W, H, keyCount) is null && window.ArCalibration.Store.Get("another camera", view, W, H, keyCount) is null,
            "Corners marked for one camera must not be saved for another that replaced it while the window was open.");

        // ---- a view of the camera whose keys were never marked ----
        Invoke(window, "InjectCameraFrame", halves, W, H);
        visualSettings.ArViewProfileId = view;
        Invoke(window, "ApplyArSource", "verification camera", W, H);
        Open(w => { Arrange(w); Click(w, truth); w.Accept(); return w.Result is not null; });
        Assert(window.ArCalibration.State == ArCalibrationState.Ready && window.ArCalibration.Snapshot.Profile.Id == view
                && SameCorners(window.ArCalibration.Current?.Corners, truth),
            "The view the camera stands at should be the one marked, and should be in force.");
        // Turning the camera to another angle: the other view asks to be marked, and no note may be drawn with corners
        // that were marked for the first angle.
        visualSettings.ArViewProfileId = otherView.Id;
        Invoke(window, "ApplyArSource", "verification camera", W, H);
        Assert(window.ArCalibration.State == ArCalibrationState.NotCalibrated && window.ArCalibration.Current is null
                && window.ArCalibration.Snapshot.Profile.Id == otherView.Id,
            $"Choosing the {otherView.Id} view of a camera marked at {view} must ask for its keys to be marked, not draw with the corners of the other angle.");
        var arWasOn = visualSettings.ArEnabled;
        visualSettings.ArEnabled = true;             // so the card speaks about the calibration rather than about the switch
        Invoke(window, "UpdateArFeed");
        Assert(window.ArPlanNow.Status != ArStatus.Active,
            $"Turning the camera to the {otherView.Id} view must not keep drawing with the corners marked at {view}.");
        if (window.ArPlanNow.Status == ArStatus.NeedsCalibration)     // with the engine running: the card names the view whose keys are to be marked
            Assert(Field(window, "_arStatus") is System.Windows.Controls.TextBlock line && line.Text.Contains(Loc.T(otherView.Name), StringComparison.Ordinal),
                $"The AR card should name the view whose keys have to be marked ({otherView.Name}).");
        visualSettings.ArEnabled = arWasOn;
        Invoke(window, "UpdateArFeed");
        Open(w =>
        {
            Arrange(w);
            Assert(w.Title == Loc.F("Mark the keys for the {0} view", Loc.T(otherView.Name)),
                $"The window should say which view of the camera is being marked (found “{w.Title}”).");
            Click(w, truth);
            w.Accept();
            return w.Result is not null;
        });
        Assert(window.ArCalibration.State == ArCalibrationState.Ready && window.ArCalibration.Snapshot.Profile.Id == otherView.Id
                && SameCorners(window.ArCalibration.Store.Get("verification camera", otherView.Id, W, H, keyCount), truth)
                && SameCorners(window.ArCalibration.Store.Get("verification camera", view, W, H, keyCount), truth),
            "Each view keeps its own key area: marking the second one must leave the first exactly as it was.");
        // The view changing behind the open window is not a save either: those corners were marked for the angle the camera
        // was at when the window opened, and both views must be left exactly as they were.
        visualSettings.ArViewProfileId = view;
        Invoke(window, "ApplyArSource", "verification camera", W, H);          // the camera is back at the first view, with the window shut
        Open(w =>
        {
            Arrange(w);
            Assert(w.Title == Loc.F("Mark the keys for the {0} view", Loc.T(ArCalibrationProfiles.Normalize(view).Name)),
                $"The window that opens for the {view} view should say so (found “{w.Title}”).");
            // Reopen now loads the saved corners; start over before this case marks a deliberately different set.
            w.Wizard.Clear();
            w.Surface.Refresh();
            Click(w, moved);
            visualSettings.ArViewProfileId = otherView.Id;
            Invoke(window, "ApplyArSource", "verification camera", W, H);
            w.Accept();
            return w.Result is not null;
        });
        Assert(window.ArCalibration.Snapshot.Profile.Id == otherView.Id
                && SameCorners(window.ArCalibration.Store.Get("verification camera", otherView.Id, W, H, keyCount), truth)
                && SameCorners(window.ArCalibration.Store.Get("verification camera", view, W, H, keyCount), truth),
            "A view chosen while the window is open must not be given the corners marked for the view the window was opened on.");

        // ---- leave everything as it was ----
        foreach (var profile in new[] { view, otherView.Id })
        {
            visualSettings.ArViewProfileId = profile;
            Invoke(window, "ApplyArSource", "verification camera", W, H);
            window.ArCalibration.Reset();
        }
        visualSettings.ArViewProfileId = viewWas;
        Invoke(window, "ApplyArSource", "another camera", W, H);
        window.ArCalibration.Reset();
        Invoke(window, "ApplyArSource", null!, 0, 0);
        ring.Clear();
        visualSettings.CameraMirror = mirrorWas;
        visualSettings.ArNoteDirectionTilt = noteTiltWas;
        visualSettings.ArNoteDirectionAzimuth = noteAzimuthWas;
        Invoke(window, "RefreshSettingControls");
        Invoke(window, "UpdateArFeed");
        Assert(Math.Abs(visualSettings.ArNoteDirectionTilt - noteTiltWas) < .001
                && Math.Abs(visualSettings.ArNoteDirectionAzimuth - noteAzimuthWas) < .001,
            "The calibration diagnostic should restore the user's note direction after testing the aiming controls.");
        Results.Add("PASS calibration window: refuses without a camera, picture or the right size; the picture as the stage shows it; four clicks to a saved calibration, mirrored or not; Move/Rotate/Scale gizmo and numeric pose controls; independent note-direction sliders, screen-up and key-depth alignment; FOV and pose persistence; Done shut on bad corners; New picture; nothing saved when cancelled, when the camera changed behind it, or when the view of the camera did; each view keeps its own key area and an unmarked view asks to be marked.");
    }

    /// <summary>
    /// Regression for the legacy flat scene→image map helpers: the pure projection tests hold the map to <c>ArProjection</c>;
    /// this holds it to the scene layout the renderer builds. The active AR note path uses <c>ArCameraPose</c> and
    /// <c>Ar3DNoteProjection</c>, and must not use this map as a note-visibility gate.
    /// </summary>
    private static void VerifyArSceneProjection()
    {
        var corners = new (double X, double Y)[] { (70, 292), (1214, 268), (1236, 421), (52, 449) };
        foreach (var (width, height, fraction) in new[] { (1280f, 720f, .28f), (1920f, 1080f, .24f), (900f, 900f, .32f) })
        {
            var scene = new GpuSceneLayout(width, height, fraction);
            var frame = new ArSceneFrame(scene.Width, scene.HitY, scene.KeyboardHeight);
            Assert(frame.IsUsable && Math.Abs(scene.HitY + scene.KeyboardHeight - scene.Height) < 1e-3,
                "The scene layout's hit line and keyboard depth should add up to the height of the scene.");
            var calibration = ArCalibration.Create(scene.Keys, corners, 1280, 720, out var problem)
                ?? throw new InvalidOperationException($"the sample corners were refused: {problem}");
            foreach (var mirror in new[] { false, true })
            {
                var view = new ArView(1280, 720, width, height, PictureFit.Fit, mirror);
                Assert(ArSceneProjection.TryBuild(calibration, view, frame, out var map),
                    "A calibration, a view and a scene layout should always carry a map.");
                Assert(Math.Abs(map.WeightAt(0, scene.HitY) - 1) < 1e-12,
                    "The weight should be 1 on the back-left corner of the keys, which is what ArProjection.MinW assumes.");
                foreach (var pitch in new[] { scene.Keys.FirstPitch, 60, 61, scene.Keys.FirstPitch + scene.Keys.KeyCount - 1 })
                {
                    if (!scene.Keys.Contains(pitch)) continue;
                    Assert(Math.Abs(scene.X(pitch) - scene.Keys.CenterX(pitch) * scene.Width) < 1e-3,
                        "The scene's key positions should be the keyboard's normalised ones scaled by the scene width, or x/Width is not u.");
                    double left, right, depth;
                    if (KeyboardLayout.IsBlack(pitch)) { (left, right) = scene.Keys.Span(pitch); depth = ArCalibration.BlackLengthShare; }
                    else { var centre = scene.Keys.CenterX(pitch); left = centre - scene.Keys.WhiteWidth / 2; right = centre + scene.Keys.WhiteWidth / 2; depth = 1; }
                    Assert(ArProjection.TryProject(calibration, view, left, right, 0, depth, out var expected, out _, out _),
                        "The sample keyboard should project every one of its keys.");
                    var (x0, y0) = frame.Scene(left, 0); var (x1, y1) = frame.Scene(right, depth);
                    var got = new[] { map.Project(x0, y0), map.Project(x1, y0), map.Project(x1, y1), map.Project(x0, y1) };
                    for (var i = 0; i < 4; i++)
                        Assert(Math.Abs(expected[i].X - got[i].X) < 1e-6 && Math.Abs(expected[i].Y - got[i].Y) < 1e-6 && Math.Abs(expected[i].W - got[i].W) < 1e-9,
                            $"The scene map should put key {pitch} exactly where the projection the overlay already uses puts it.");
                    var scale = map.ScaleAt(scene.X(pitch), scene.HitY);
                    Assert(scale > 0 && double.IsFinite(scale), "The magnification should be a real number wherever a key can be drawn.");
                }
            }
        }
        Results.Add("PASS legacy AR scene projection helpers: the dormant scene-to-image map agrees with ArProjection on synthetic layouts at three output sizes, mirrored and not; active notes use the separate 3D camera-pose route.");
    }

    /// <summary>
    /// WARP regression for the live AR render route: the calibrated 3D pose owns song-note quads, the stage-space
    /// note/trail pass stays empty, and direction/FOV are not coupled to the old scene-to-image homography.
    /// </summary>
    private static void VerifyArNoteProjection()
    {
        const int width = 1280, height = 720;
        var settings = new PianoVisualSettings
        {
            ShowNotes = true, NoteStyle = "Solid", NoteWidth = 100, NoteMinLength = 4, NoteGap = 1,
            NoteGlow = 0, NoteHeadGlow = 0, NoteRoundness = 0,
            FallingPulse = false, FallingTrail = "None", FallingGhost = false, NoteShimmer = false, NoteLandingGlow = false,
            HoldBar = false, HoldBreath = false, HoldVibration = false, HoldColorCycle = false, HoldElectricArc = false,
            ShowEmbers = false, ShowWisps = false, ShowFlame = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowKeys = false, ShowHalo = false, ShowBackground = false, ShowStars = false, ShowLightBeams = false, ShowPetals = false,
            AmbientEnergy = "None", AmbientNature = "None", AmbientLight = "None", AmbientCosmic = "None",
            BloomIntensity = 0, HorizonGlow = 0, Vignette = 0, ShootingStars = false,
            ArNotesInPerspective = true, ArLivePressTrails = false
        };
        var notes = new List<NoteEvent>
        {
            new() { Pitch = 60, Start = 10.08, Duration = .18 },
            new() { Pitch = 64, Start = 10.20, Duration = .20 },
            new() { Pitch = 67, Start = 10.32, Duration = .22 }
        };
        var shortNotes = new List<NoteEvent>
        {
            new() { Pitch = 60, Start = 10.08, Duration = .02 },
            new() { Pitch = 64, Start = 10.20, Duration = .02 },
            new() { Pitch = 67, Start = 10.32, Duration = .02 }
        };
        var corners = new (double X, double Y)[] { (70, 292), (1214, 268), (1236, 421), (52, 449) };
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, corners, 1280, 720, out var problem, 70)
            ?? throw new InvalidOperationException($"the sample corners were refused: {problem}");
        var savedCorners = calibration.Corners.ToArray();
        var ring = new CameraFrameRing();
        var grey = ring.BeginWrite(1280 * 720 * 4, out var slot);
        for (var i = 0; i < grey.Length; i += 4)
        {
            grey[i] = 90; grey[i + 1] = 90; grey[i + 2] = 90; grey[i + 3] = 255;
        }
        ring.Publish(slot, 1280, 720, Stopwatch.GetTimestamp());
        var view = new ArView(1280, 720, width, height, PictureFit.Fit, false);

        GpuStageFeed MakeFeed(GpuArInput? ar, bool mirror = false, IReadOnlyList<NoteEvent>? song = null,
            double position = 10, bool playing = true, IReadOnlySet<int>? pressed = null, bool overCamera = true)
        {
            var feed = new GpuStageFeed { LabelAtlas = PianoStage.GpuGlyphAtlas };
            feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(settings, (_, _) => Color.FromRgb(255, 80, 220), .205, overCamera: overCamera));
            feed.SetDeterministicState(song ?? notes, position, playing, pressed ?? new HashSet<int>());
            feed.SetDeterministicBeatPulse(0);
            feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, mirror, 1f);
            feed.Ar = ar;
            return feed;
        }

        using var renderer = GpuStageRenderer.Create(forceWarp: true);
        byte[] Render(GpuStageFeed feed, bool alphaOutput = false) =>
            GpuRenderLoop.RenderFrames(renderer, feed, width, height, 0, 1 / 60.0, false, out _, null, 20260930, alphaOutput);
        byte[] RenderFrames(GpuStageFeed feed, int frames, bool alphaOutput = false) =>
            GpuRenderLoop.RenderFrames(renderer, feed, width, height, frames, 1 / 60.0, false, out _, null, 20260930, alphaOutput);

        static (double X, double Y) Centre(ArPatch patch) =>
            ((patch.BackLeft.X + patch.BackRight.X + patch.FrontRight.X + patch.FrontLeft.X) * .25,
             (patch.BackLeft.Y + patch.BackRight.Y + patch.FrontRight.Y + patch.FrontLeft.Y) * .25);

        static (int Spread, int Off)? Look(byte[] pixels, int w, int h, (double X, double Y) at)
        {
            if (!(at.X >= 0 && at.Y >= 0 && at.X < w && at.Y < h)) return null;
            var i = ((int)Math.Floor(at.Y) * w + (int)Math.Floor(at.X)) * 4;
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            return (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)),
                Math.Max(Math.Abs(r - 90), Math.Max(Math.Abs(g - 90), Math.Abs(b - 90))));
        }

        static int ColouredPixels(byte[] pixels, byte greyValue = 90)
        {
            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (Math.Max(Math.Abs(pixels[i] - greyValue), Math.Max(Math.Abs(pixels[i + 1] - greyValue), Math.Abs(pixels[i + 2] - greyValue))) > 40) count++;
            return count;
        }

        static int ChangedPixels(byte[] first, byte[] second, int threshold = 8)
        {
            var count = 0;
            for (var i = 0; i < Math.Min(first.Length, second.Length); i += 4)
                if (Math.Max(Math.Abs(first[i] - second[i]), Math.Max(Math.Abs(first[i + 1] - second[i + 1]), Math.Abs(first[i + 2] - second[i + 2]))) > threshold) count++;
            return count;
        }
        // BGRA readback: [i] blue, [i+1] green, [i+2] red.
        static (int R, int G, int B) Sample(byte[] pixels, int w, (double X, double Y) at)
        {
            var i = ((int)Math.Floor(at.Y) * w + (int)Math.Floor(at.X)) * 4;
            return (pixels[i + 2], pixels[i + 1], pixels[i]);
        }

        var defaultInput = new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0,
            ShowNotes: true, ProjectedNotes: true);

        // The old flat scene→image homography may reject a scene frame at its horizon. That is not a reason to reject
        // a calibrated 3D camera pose; the active renderer no longer uses the map as an AR note-presence gate.
        Assert(!ArSceneProjection.TryBuild(calibration, view, new ArSceneFrame(0, 0, 0), out _),
            "The regression fixture must make the legacy scene map unavailable.");
        var expected = new List<Ar3DNoteShape>();
        Ar3DNoteProjection.Build(calibration, view, notes, 10, .22, defaultInput.Notes,
            defaultInput.NoteDirection, defaultInput.MinAspect, expected);
        Assert(expected.Count == notes.Count,
            $"The 3D note projection should build one patch per input note ({expected.Count} patches for {notes.Count} notes).");
        var projected = Render(MakeFeed(defaultInput));
        var projectedPixels = ColouredPixels(projected);
        Assert(renderer.ArObjectQuadCount == expected.Count && renderer.ArLiveBarCount == 0,
            $"The AR quad pass should own one 3D patch per note ({renderer.ArObjectQuadCount} quads for {expected.Count} notes), not the legacy scene-note pass.");
        Assert(renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0,
            $"The camera must suppress stage-space note bodies and falling trails (notes {renderer.StageNoteCount}, trails {renderer.StageNoteTrailCount}).");
        Assert(projectedPixels > 500,
            $"Projected AR note quads must change the camera frame; otherwise notes disappeared between input and the overlay pass ({projectedPixels} coloured pixels).");
        foreach (var shape in expected)
        {
            var at = Centre(shape.Patch);
            Assert(Look(projected, width, height, at) is { } pixel && pixel.Off > 40,
                $"The rendered note for pitch {shape.Pitch} must occupy the camera-projected 3D patch centre ({at.X:0.0},{at.Y:0.0}).");
        }

        // AR note quads share the stage's material and sRGB-to-linear path, not the old flat cyan quad approximation.
        var colorAt = Centre(expected[0].Patch);
        var colorIndex = ((int)Math.Floor(colorAt.Y) * width + (int)Math.Floor(colorAt.X)) * 4;
        var noteRed = projected[colorIndex + 2];
        var noteGreen = projected[colorIndex + 1];
        Assert(noteRed > noteGreen * 1.8,
            $"A magenta sRGB note must be linearized before the HDR shader (expected red to stay well above green, got R={noteRed}, G={noteGreen}).");
        var materialChanges = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var style in new[] { "Neon", "Glass", "Fire", "Gem", "CrystalOutline" })
        {
            settings.NoteStyle = style;
            var styledNotes = Render(MakeFeed(defaultInput));
            materialChanges[style] = ChangedPixels(projected, styledNotes, threshold: 4);
            Assert(renderer.ArObjectQuadCount == expected.Count,
                $"Changing the AR note material to {style} must preserve one calibrated quad per song note.");
        }
        settings.NoteStyle = "Solid";
        var materialChangeSummary = string.Join(", ", materialChanges.Select(pair => $"{pair.Key} {pair.Value}"));
        Assert(materialChanges.Values.All(changed => changed > 100),
            $"Each sampled known material branch should change the calibrated AR note pixels relative to Solid ({materialChangeSummary}); full parameter parity remains unverified.");

        settings.NoteShimmer = true;
        settings.NoteShimmerAmount = 100;
        var shimmerNotes = Render(MakeFeed(defaultInput));
        var shimmerPixels = ChangedPixels(projected, shimmerNotes, threshold: 4);
        var shimmerQuads = renderer.ArObjectQuadCount;
        settings.NoteShimmer = false;
        settings.NoteShimmerAmount = 45;
        Assert(shimmerQuads == expected.Count * 2 && shimmerPixels > 20,
            $"Note shimmer should draw a visible sheen inside each calibrated note quad ({shimmerQuads} quads, {shimmerPixels} changed pixels).");

        settings.FallingTrail = "Glow";
        settings.FallingTrailIntensity = 100;
        settings.FallingTrailLength = 60;
        var glowTrailNotes = Render(MakeFeed(defaultInput));
        var glowTrailQuads = renderer.ArNoteTrailQuadCount;
        var glowTrailPixels = ChangedPixels(projected, glowTrailNotes, threshold: 4);
        settings.FallingTrail = "Stream";
        var streamTrailNotes = Render(MakeFeed(defaultInput));
        var streamTrailQuads = renderer.ArNoteTrailQuadCount;
        Assert(glowTrailQuads == expected.Count && streamTrailQuads == expected.Count
                && glowTrailPixels > 20 && ChangedPixels(glowTrailNotes, streamTrailNotes, threshold: 4) > 10,
            $"Configured Glow and Stream note trails should stay on each calibrated 3D path and produce distinct pixels ({glowTrailQuads}/{streamTrailQuads} quads, {glowTrailPixels} changed pixels).");
        settings.FallingTrail = "None";
        settings.FallingGhost = true;
        settings.FallingGhostAmount = 100;
        var ghostNotes = Render(MakeFeed(defaultInput));
        var ghostQuads = renderer.ArGhostNoteQuadCount;
        Assert(ghostQuads >= expected.Count && ChangedPixels(projected, ghostNotes, threshold: 4) > 20,
            $"Falling Ghost should add calibrated, pitch-bound echoes ahead of future notes ({ghostQuads} projected copies).");
        settings.FallingGhost = false;
        settings.NoteLandingGlow = true;
        settings.NoteLandingGlowAmount = 100;
        var landingNotes = Render(MakeFeed(defaultInput));
        var landingGlowCount = renderer.ArLandingGlowCount;
        Assert(landingGlowCount >= expected.Count && ChangedPixels(projected, landingNotes, threshold: 4) > 20,
            $"Note Landing Glow should project an approach cue onto each upcoming real key ({landingGlowCount} calibrated glow quads).");
        settings.NoteLandingGlow = false;
        settings.NoteLandingGlowAmount = 18;
        settings.ShowNoteLabels = true;
        var labelledNotes = Render(MakeFeed(defaultInput));
        var noteLabelCount = renderer.ArNoteLabelCount;
        var noteLabelPixels = ChangedPixels(projected, labelledNotes, threshold: 4);
        settings.ShowNoteLabels = false;
        Assert(noteLabelCount > 0 && noteLabelPixels > 10,
            $"Show note labels should sample the atlas on calibrated note quads ({noteLabelCount} projected labels, {noteLabelPixels} changed pixels).");

        // The actual WARP AR pass must follow saved translation and uniform scale as well as the plane rotation.
        var objectTransform = new ArPoseTransform(.06, -.03, .01, 8, -5, 3, 1.05);
        Assert(ArCameraPose.TryTransformCorners(KeyboardLayout.Standard88, corners, 1280, 720, 70, objectTransform, out var transformedCorners),
            "A modest full object transform should keep the keyboard plane projectable.");
        var transformedCalibration = ArCalibration.Create(KeyboardLayout.Standard88, transformedCorners, 1280, 720,
            out var transformProblem, 70, objectTransform, corners)
            ?? throw new InvalidOperationException($"the transformed sample corners were refused: {transformProblem}");
        var transformedInput = defaultInput with { Calibration = transformedCalibration };
        var transformedShapes = new List<Ar3DNoteShape>();
        Ar3DNoteProjection.Build(transformedCalibration, view, notes, 10, .22, transformedInput.Notes,
            transformedInput.NoteDirection, transformedInput.MinAspect, transformedShapes);
        var transformedPixels = Render(MakeFeed(transformedInput));
        Assert(renderer.ArObjectQuadCount == notes.Count && ColouredPixels(transformedPixels) > 500
                && transformedShapes.Count == notes.Count && !transformedCorners.SequenceEqual(corners),
            "A moved, uniformly scaled and rotated calibration should still reach the live camera AR-quad pass.");
        foreach (var shape in transformedShapes)
        {
            var at = Centre(shape.Patch);
            Assert(Look(transformedPixels, width, height, at) is { } pixel && pixel.Off > 40,
                $"The transformed pitch-{shape.Pitch} note should land at its 3D projected centre ({at.X:0.0},{at.Y:0.0}).");
        }

        // Mirror changes only the output view, and the predicted 3D note corners must still match the visible quads.
        var mirroredView = new ArView(1280, 720, width, height, PictureFit.Fit, true);
        var mirroredExpected = new List<Ar3DNoteShape>();
        Ar3DNoteProjection.Build(calibration, mirroredView, notes, 10, .22, defaultInput.Notes,
            defaultInput.NoteDirection, defaultInput.MinAspect, mirroredExpected);
        var mirrored = Render(MakeFeed(defaultInput, mirror: true));
        Assert(renderer.ArObjectQuadCount == mirroredExpected.Count && renderer.StageNoteCount == 0,
            "Mirroring the camera must not send projected notes back through the flat stage pass.");
        foreach (var shape in mirroredExpected)
        {
            var at = Centre(shape.Patch);
            Assert(Look(mirrored, width, height, at) is { } pixel && pixel.Off > 40,
                $"The mirrored pitch-{shape.Pitch} note must land in its 3D projected patch ({at.X:0.0},{at.Y:0.0}).");
        }

        // The note's travel vector is 3D and independent of the saved keyboard pose; changing it changes the render
        // without changing the four calibration anchors.
        var diagonalInput = defaultInput with { NoteDirection = new ArNoteDirection(72, 38) };
        var diagonal = Render(MakeFeed(diagonalInput));
        Assert(ChangedPixels(projected, diagonal) > 100 && renderer.ArObjectQuadCount == notes.Count,
            "A different 3D note direction must move/rotate the note billboards, not silently reuse the keyboard's flat travel axis.");
        Assert(savedCorners.SequenceEqual(calibration.Corners),
            "Changing note travel direction must never mutate the calibrated keyboard pose.");

        // Perform is a separate mode: it drops the song notes while keeping key tint available.
        var performInput = defaultInput with { ShowNotes = false };
        var perform = Render(MakeFeed(performInput));
        Assert(renderer.ArObjectQuadCount == 0 && renderer.ArDamperStripCount == 1,
            "Perform must omit future song-note quads instead of routing them through either note renderer; the damper strip, a scene element tied to the pedal and not the song, stays.");
        Assert(renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0,
            "Perform over the camera must keep the stage-space note and trail pass empty as well.");

        // Minimum length is measured in each note's own projected key width. It extends staccato notes but does not edit MIDI.
        var shortNaturalInput = defaultInput;
        var shortMinimumInput = defaultInput with { MinAspect = 2f };
        var shortNatural = Render(MakeFeed(shortNaturalInput, song: shortNotes));
        var naturalPixels = ColouredPixels(shortNatural);
        var shortMinimum = Render(MakeFeed(shortMinimumInput, song: shortNotes));
        var minimumPixels = ColouredPixels(shortMinimum);
        Assert(renderer.ArObjectQuadCount == shortNotes.Count && minimumPixels > naturalPixels,
            $"A 200% minimum length must visibly lengthen short projected notes ({naturalPixels} → {minimumPixels} pixels).");

        // Live press bars are independently switched from key tint and use the same 3D direction as song notes.
        // The effect fixture deliberately enables several Live Stage families so S5 verifies the actual simulation output.
        var liveFxLook = settings.Clone();
        liveFxLook.ShowEmbers = true; liveFxLook.ParticleAmount = 90; liveFxLook.ParticleResponse = 100; liveFxLook.ParticleVelocity = 300;
        liveFxLook.ShowWisps = true; liveFxLook.WispAmount = 55; liveFxLook.ShowFlame = true; liveFxLook.FlameIntensity = 85;
        liveFxLook.ImpactBurst = "Confetti"; liveFxLook.ShowImpactRings = true; liveFxLook.ImpactWave = "Ripple";
        liveFxLook.ImpactWaveIntensity = 100; liveFxLook.RingSize = 62; liveFxLook.ShowImpactFlash = true;
        liveFxLook.ImpactFlashIntensity = 100; liveFxLook.ImpactFlashStyle = "Lightning";
        liveFxLook.ImpactMorph = "Shatter"; liveFxLook.ImpactMorphIntensity = 100;
        liveFxLook.ReleaseEffect = "Dissolve"; liveFxLook.ReleaseIntensity = 100;
        liveFxLook.HoldElectricArc = true; liveFxLook.HoldArcIntensity = 100;
        liveFxLook.ShowLightBeams = true; liveFxLook.BeamIntensity = 100;
        GpuStageFeed PressFeed(bool showBars, float keyLight, ArQualityTier quality = ArQualityTier.Standard, bool addImpact = false,
            PianoVisualSettings? visualSettings = null, ArCalibration? selectedCalibration = null, int[]? activePitches = null,
            bool sustain = false, bool alignment = false, bool showStrip = true, Vector3 guideColor = default, string? viewProfileId = null)
        {
            var pressed = new HashSet<int>(activePitches ?? new[] { 60 });
            var feed = MakeFeed(new GpuArInput(selectedCalibration ?? calibration, ArNoteStyle.Default, 1f, KeyLight: keyLight, Quality: quality,
                    ShowNotes: false, ProjectedNotes: true, ShowLiveTrails: showBars, AlignmentGuides: alignment,
                    ShowDamperStrip: showStrip, AlignmentGuideColor: guideColor, ViewProfileId: viewProfileId),
                song: Array.Empty<NoteEvent>(), position: 0, playing: false, pressed: pressed);
            if (visualSettings is not null)
                feed.SetLook(GpuLook.From(visualSettings, (_, _) => Color.FromRgb(255, 80, 220), .205, overCamera: true));
            // Give each WARP case an empty simulation history so an earlier effect cannot leak into the no-impact control.
            feed.ClearTransient();
            feed.SetDeterministicState(Array.Empty<NoteEvent>(), 0, false, pressed);
            feed.SetDeterministicBeatPulse(0);
            feed.SetSustainLevel(sustain, 1);
            foreach (var pitch in pressed) feed.LiveNote(pitch, true, .9f);
            if (addImpact) feed.Impact(60, .9);
            return feed;
        }
        GpuStageFeed UncalibratedFeed(PianoVisualSettings visual, bool triggerEffects)
        {
            var pressed = triggerEffects ? new HashSet<int> { 60 } : new HashSet<int>();
            var feed = MakeFeed(null, song: Array.Empty<NoteEvent>(), position: 0, playing: false,
                pressed: pressed, overCamera: false);
            feed.SetLook(GpuLook.From(visual, (_, _) => Color.FromRgb(255, 80, 220), .205, overCamera: false));
            feed.ClearTransient();
            feed.SetDeterministicState(Array.Empty<NoteEvent>(), 0, false, pressed);
            feed.SetDeterministicBeatPulse(0);
            if (triggerEffects)
            {
                feed.LiveNote(60, true, .9f);
                feed.Impact(60, .9);
            }
            return feed;
        }
        var quietUncalibratedLook = settings.Clone();
        var busyUncalibratedLook = liveFxLook.Clone();
        busyUncalibratedLook.ShowKeys = true;
        busyUncalibratedLook.ShowBackground = true; busyUncalibratedLook.BackgroundGuide = true;
        var uncalibratedIdleFrame = RenderFrames(UncalibratedFeed(quietUncalibratedLook, triggerEffects: false), 4);
        var uncalibratedMidiFrame = RenderFrames(UncalibratedFeed(busyUncalibratedLook, triggerEffects: true), 4);
        Assert(uncalibratedIdleFrame.AsSpan().SequenceEqual(uncalibratedMidiFrame)
                && renderer.StageKeyCount == 0 && renderer.StageSpriteCount == 0
                && renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0 && renderer.ArObjectQuadCount == 0,
            "Before calibration, the camera must show physical keys only: no static virtual keyboard, Live Stage sprites, guide lanes, notes or falling trails may leak into the uncalibrated camera overlay.");

        // BuildAmbient also guards its scene-only lane/trail geometry itself; it must not rely on BuildNotes
        // having cleared the trail list earlier in this particular renderer order.
        var sceneTrailSettings = settings.Clone();
        sceneTrailSettings.ShowBackground = true; sceneTrailSettings.BackgroundGuide = true;
        sceneTrailSettings.FallingTrail = "Glow"; sceneTrailSettings.FallingTrailIntensity = 100;
        var sceneTrailLook = GpuLook.From(sceneTrailSettings, (_, _) => Color.FromRgb(255, 80, 220), .205, overCamera: false);
        var sceneTrailFeed = new GpuStageFeed();
        sceneTrailFeed.SetStageHeight(height);
        sceneTrailFeed.SetLook(sceneTrailLook);
        sceneTrailFeed.SetDeterministicState(Array.Empty<NoteEvent>(), 0, false, new HashSet<int> { 60 });
        sceneTrailFeed.LiveNote(60, true, .9f);
        var sceneTrailInput = new GpuFrameInput();
        sceneTrailFeed.Capture(sceneTrailInput, GpuStageFeed.Now);
        var sceneTrailSimulation = new GpuStageSimulation(20261010);
        sceneTrailSimulation.Step(1 / 60.0, sceneTrailInput, sceneTrailFeed, width);
        var sceneTrailLayout = new GpuSceneLayout(width, (float)sceneTrailInput.StageHeightDip, sceneTrailLook.KeyboardFraction);
        sceneTrailSimulation.BuildNotes(sceneTrailInput, sceneTrailLayout, new GpuInstanceList<GpuNoteInstance>(16),
            songNotes: false, onStage: true, liveTrails: true);
        var sceneSpritesOverCamera = new GpuInstanceList<GpuSpriteInstance>(32);
        var sceneSpritesOnStage = new GpuInstanceList<GpuSpriteInstance>(32);
        sceneTrailSimulation.BuildAmbient(sceneTrailLook, sceneTrailLayout, sceneSpritesOverCamera, cameraOverlayActive: true);
        sceneTrailSimulation.BuildAmbient(sceneTrailLook, sceneTrailLayout, sceneSpritesOnStage, cameraOverlayActive: false);
        Assert(sceneTrailSimulation.NoteTrailCount > 0 && sceneSpritesOverCamera.Count == 0 && sceneSpritesOnStage.Count > 0,
            "BuildAmbient must suppress scene-space guide lanes and falling trails over a camera even when the flat look itself is not marked OverCamera.");

        var barsOn = RenderFrames(PressFeed(showBars: true, keyLight: .6f), 30);
        var liveBarCount = renderer.ArLiveBarCount;
        var barsOff = RenderFrames(PressFeed(showBars: false, keyLight: .6f), 30);
        var noTint = RenderFrames(PressFeed(showBars: false, keyLight: 0), 30);
        var barPixels = ChangedPixels(barsOn, barsOff);
        var tintPixels = ChangedPixels(barsOff, noTint, threshold: 4);
        Assert(liveBarCount > 0 && barPixels > 100 && renderer.ArLiveBarCount == 0,
            $"The live-bar option should add camera-projected bars only when on ({liveBarCount} built, {barPixels} changed pixels; off built {renderer.ArLiveBarCount}).");
        Assert(tintPixels > 20,
            $"Turning bars off must not turn the independent held-key tint off ({tintPixels} pixels still respond to key-light strength).");

        // Perform-mode Note On objects are independent of the optional press-bar channel and remain available in Lite.
        var risingMidiInput = new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0,
            ShowNotes: false, Quality: ArQualityTier.Lite, NoteDirection: new ArNoteDirection(68, 37), ShowRisingMidiNotes: true);
        var risingMidiFeed = MakeFeed(risingMidiInput, song: Array.Empty<NoteEvent>(), position: 0, playing: false, pressed: new HashSet<int>());
        risingMidiFeed.LiveNote(60, true, .45f);
        var risingMidiFrame = GpuRenderLoop.RenderFrames(renderer, risingMidiFeed, width, height, 22, 1 / 60.0, false,
            out _, frame =>
            {
                if (frame == 4 || frame == 10) risingMidiFeed.LiveNote(60, false, 0);
                if (frame == 7) risingMidiFeed.LiveNote(60, true, .95f);
            }, 20260930);
        var risingMidiCount = renderer.ArRisingMidiNoteCount;
        var risingMidiPixels = ColouredPixels(risingMidiFrame);
        var noRisingMidiFeed = MakeFeed(risingMidiInput with { ShowRisingMidiNotes = false },
            song: Array.Empty<NoteEvent>(), position: 0, playing: false, pressed: new HashSet<int>());
        noRisingMidiFeed.LiveNote(60, true, .45f);
        var noRisingMidiFrame = GpuRenderLoop.RenderFrames(renderer, noRisingMidiFeed, width, height, 22, 1 / 60.0, false,
            out _, frame =>
            {
                if (frame == 4 || frame == 10) noRisingMidiFeed.LiveNote(60, false, 0);
                if (frame == 7) noRisingMidiFeed.LiveNote(60, true, .95f);
            }, 20260930);
        Assert(risingMidiCount == 2 && risingMidiPixels > 30 && renderer.ArRisingMidiNoteCount == 0
                && renderer.ArLiveBarCount == 0 && renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0
                && ChangedPixels(risingMidiFrame, noRisingMidiFrame, threshold: 4) > 25,
            $"Each same-pitch MIDI Note On must create a distinct calibrated rising note that survives its Note Off, independently of press bars and Lite's transient gate ({risingMidiCount} notes, {risingMidiPixels} pixels; disabled count {renderer.ArRisingMidiNoteCount}).");

        // These four Live Stage hold modifiers belong to the optional live-bar channel and must survive 3D projection.
        (byte[] Pixels, int Bars) RenderHoldModifier(PianoVisualSettings holdSettings)
        {
            var pixels = RenderFrames(PressFeed(showBars: true, keyLight: .6f, quality: ArQualityTier.Standard,
                visualSettings: holdSettings, activePitches: new[] { 60 }), 12);
            return (pixels, renderer.ArLiveBarCount);
        }
        var holdBaseline = RenderHoldModifier(settings);
        var holdBarZeroLook = settings.Clone(); holdBarZeroLook.HoldBar = true; holdBarZeroLook.HoldBarIntensity = 0;
        var holdBarFullLook = settings.Clone(); holdBarFullLook.HoldBar = true; holdBarFullLook.HoldBarIntensity = 100;
        var holdBarZero = RenderHoldModifier(holdBarZeroLook);
        var holdBarFull = RenderHoldModifier(holdBarFullLook);
        var holdBreathSlowLook = settings.Clone(); holdBreathSlowLook.HoldBreath = true; holdBreathSlowLook.HoldBreathRate = 0;
        var holdBreathFastLook = settings.Clone(); holdBreathFastLook.HoldBreath = true; holdBreathFastLook.HoldBreathRate = 100;
        var holdBreathSlow = RenderHoldModifier(holdBreathSlowLook);
        var holdBreathFast = RenderHoldModifier(holdBreathFastLook);
        var holdVibrationOffLook = settings.Clone(); holdVibrationOffLook.HoldVibration = true; holdVibrationOffLook.HoldVibrationAmount = 0;
        var holdVibrationFullLook = settings.Clone(); holdVibrationFullLook.HoldVibration = true; holdVibrationFullLook.HoldVibrationAmount = 100;
        var holdVibrationOff = RenderHoldModifier(holdVibrationOffLook);
        var holdVibrationFull = RenderHoldModifier(holdVibrationFullLook);
        var holdColorCycleOffLook = settings.Clone(); holdColorCycleOffLook.HoldColorCycle = true; holdColorCycleOffLook.HoldColorCycleSpeed = 0;
        var holdColorCycleFullLook = settings.Clone(); holdColorCycleFullLook.HoldColorCycle = true; holdColorCycleFullLook.HoldColorCycleSpeed = 100;
        var holdColorCycleOff = RenderHoldModifier(holdColorCycleOffLook);
        var holdColorCycleFull = RenderHoldModifier(holdColorCycleFullLook);
        var holdBarTogglePixels = ChangedPixels(holdBaseline.Pixels, holdBarFull.Pixels, threshold: 4);
        var holdBarIntensityPixels = ChangedPixels(holdBarZero.Pixels, holdBarFull.Pixels, threshold: 4);
        var holdBreathTogglePixels = ChangedPixels(holdBaseline.Pixels, holdBreathSlow.Pixels, threshold: 4);
        var holdBreathRatePixels = ChangedPixels(holdBreathSlow.Pixels, holdBreathFast.Pixels, threshold: 4);
        var holdVibrationTogglePixels = ChangedPixels(holdBaseline.Pixels, holdVibrationFull.Pixels, threshold: 4);
        var holdVibrationAmountPixels = ChangedPixels(holdVibrationOff.Pixels, holdVibrationFull.Pixels, threshold: 4);
        var holdColorCycleTogglePixels = ChangedPixels(holdBaseline.Pixels, holdColorCycleFull.Pixels, threshold: 4);
        var holdColorCycleSpeedPixels = ChangedPixels(holdColorCycleOff.Pixels, holdColorCycleFull.Pixels, threshold: 4);
        Assert(holdBaseline.Bars == 1 && holdBarZero.Bars == 1 && holdBarFull.Bars == 1
                && holdBreathSlow.Bars == 1 && holdBreathFast.Bars == 1
                && holdVibrationOff.Bars == 1 && holdVibrationFull.Bars == 1
                && holdColorCycleOff.Bars == 1 && holdColorCycleFull.Bars == 1
                && holdBarTogglePixels > 20 && holdBarIntensityPixels > 20
                && holdBreathTogglePixels > 20 && holdBreathRatePixels > 20
                && holdVibrationTogglePixels > 20 && holdVibrationAmountPixels > 20
                && holdColorCycleTogglePixels > 20 && holdColorCycleSpeedPixels > 20,
            $"Projected Live Stage bars must retain all four hold toggles and their representative parameter changes (bar {holdBarTogglePixels}/{holdBarIntensityPixels}, breath {holdBreathTogglePixels}/{holdBreathRatePixels}, vibration {holdVibrationTogglePixels}/{holdVibrationAmountPixels}, color-cycle {holdColorCycleTogglePixels}/{holdColorCycleSpeedPixels} changed pixels; bars {holdBaseline.Bars}/{holdBarZero.Bars}/{holdBarFull.Bars}/{holdBreathSlow.Bars}/{holdBreathFast.Bars}/{holdVibrationOff.Bars}/{holdVibrationFull.Bars}/{holdColorCycleOff.Bars}/{holdColorCycleFull.Bars}).");

        // Velocity colour (Keysight row 6): the live bar's own strength retints it through the look's velocity tint,
        // on the same wire notes use. This shipped with 2.0.0; here it is simply measured like the other modifiers.
        var velocityTintLook = settings.Clone(); velocityTintLook.VelocityColor = true; velocityTintLook.VelocityColorAmount = 100;
        var velocityTinted = RenderHoldModifier(velocityTintLook);
        var velocityTintPixels = ChangedPixels(holdBaseline.Pixels, velocityTinted.Pixels, threshold: 4);
        Assert(velocityTinted.Bars == 1 && velocityTintPixels > 20,
            $"A live bar must retint with the pressing strength when the look enables velocity colour ({velocityTintPixels} changed pixels, bars {velocityTinted.Bars}).");

        // Catalogue Dampers (roadmap §14.3): the strip hugs the key back edge from Standard up, lifts with the
        // sustain level 0..1 and takes the look's halo colour; Lite must never draw it.
        var damperDown = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, sustain: true), 8);
        var damperStripCount = renderer.ArDamperStripCount;
        var damperUp = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, sustain: false), 8);
        var damperLiftPixels = ChangedPixels(damperUp, damperDown, threshold: 4);
        RenderFrames(PressFeed(false, .6f, ArQualityTier.Lite, sustain: true), 4);
        Assert(damperStripCount == 1 && damperLiftPixels > 20 && renderer.ArDamperStripCount == 0,
            $"Sustain must lift the damper strip over the key back edge at Standard and the strip must vanish at Lite ({damperLiftPixels} changed pixels at Standard, {renderer.ArDamperStripCount} strips at Lite).");

        // The strip follows the player's choice too (2026-10-12): the toggle removes its quad entirely, and a Custom
        // colour mode repaints the same strip away from the look's halo family.
        var stripOff = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, sustain: true, showStrip: false), 8);
        Assert(renderer.ArDamperStripCount == 0, "The damper strip toggle must remove the strip quad.");
        var damperColorLook = settings.Clone();
        damperColorLook.ArDamperStripColorMode = "Custom";
        damperColorLook.ArDamperStripColor = "#00C8FF";
        var customStrip = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, sustain: true, visualSettings: damperColorLook), 8);
        var customStripPixels = ChangedPixels(damperDown, customStrip, threshold: 4);
        Assert(renderer.ArDamperStripCount == 1 && customStripPixels > 20 && ChangedPixels(stripOff, customStrip, threshold: 4) > 20,
            $"A custom strip colour must repaint the damper strip ({customStripPixels} changed pixels against the halo look).");
        Assert(ArDampers.TryPatch(calibration, view, .75, out var stripPatch), "The strip patch must project for colour sampling.");
        var stripCentre = Centre(stripPatch);
        var haloPixel = Sample(damperDown, width, stripCentre);
        var customPixel = Sample(customStrip, width, stripCentre);
        Assert(customPixel.G - customPixel.R > haloPixel.G - haloPixel.R + 10,
            $"A cyan strip pick must lean the strip pixel green where the halo purple leaned red (halo {haloPixel}, custom {customPixel}).");

        // The 90° overhead view (the odyssey-style top-down look): a fronto-parallel quad is this homography's best
        // case, and every in-plane element — bars, velocity tint, coverage, the damper strip — must project whole.
        var overhead = ArCalibration.Create(KeyboardLayout.Standard88,
            [(40, 150), (1240, 150), (1240, 270), (40, 270)], width, height, out var overheadProblem);
        Assert(overhead is not null, $"A fronto-parallel keyboard rectangle must calibrate for a 90° overhead camera ({overheadProblem}).");
        var overheadBars = RenderFrames(PressFeed(showBars: true, keyLight: .6f, selectedCalibration: overhead!), 12);
        var overheadBarCount = renderer.ArLiveBarCount;
        var overheadTinted = RenderFrames(PressFeed(showBars: true, keyLight: .6f,
            visualSettings: velocityTintLook, selectedCalibration: overhead!), 12);
        var overheadTintPixels = ChangedPixels(overheadBars, overheadTinted, threshold: 4);
        var overheadDamperUp = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, sustain: false,
            selectedCalibration: overhead!), 8);
        Assert(renderer.ArDamperStripCount == 1, "A drawn damper strip must be counted at Standard, even pedal-up.");
        var overheadDamper = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, sustain: true,
            selectedCalibration: overhead!), 8);
        Assert(renderer.ArDamperStripCount == 1, "The sustained frame must keep drawing the damper strip.");
        var overheadDamperPixels = ChangedPixels(overheadDamperUp, overheadDamper, threshold: 4);
        Assert(overheadBarCount > 0 && ColouredPixels(overheadBars) > 200 && overheadTintPixels > 20
                && overheadDamperPixels > 20,
            $"The 90° overhead view must project live bars, the look's velocity tint and the pedal-lifted damper strip whole (bars {overheadBarCount}, tint {overheadTintPixels} pixels, damper lift {overheadDamperPixels} pixels).");

        // View presets (roadmap §14.7 "Thêm 5"): the same overhead camera feeds the same ultra effect set, but the
        // stand-up quads stay home while the flat-born lights keep drawing — the opposite of the leaned camera above.
        var overheadUltraFx = PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true, visualSettings: liveFxLook,
            activePitches: new[] { 60, 64 }, selectedCalibration: overhead!, viewProfileId: ArCalibrationProfiles.Overhead90.Id);
        RenderFrames(overheadUltraFx, 4);
        Assert(renderer.ArBeamCount == 0 && renderer.ArHoldArcCount == 0
                && renderer.ArImpactLightCount > 0 && renderer.ArPulseCount > 0 && renderer.ArParticleFxCount > 0,
            $"A 90° overhead view must hide beams and hold arcs while keeping impact lights, pulses and particles (beams {renderer.ArBeamCount}, arcs {renderer.ArHoldArcCount}, lights {renderer.ArImpactLightCount}, pulses {renderer.ArPulseCount}, particles {renderer.ArParticleFxCount}).");

        // roadmap §14.8: an AR frame over budget drops families in roadmap's sentence order — same style, same look,
        // just the deepest adaptive tier — while the things that ARE the overlay (keys, notes, keypresses) stay put.
        var budgetCutFeed = PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true, visualSettings: liveFxLook,
            activePitches: new[] { 60, 64 });
        renderer.ArBudgetTierOverride = GpuAdaptiveQualityTier.AmbientMinimal;
        RenderFrames(budgetCutFeed, 4);
        var cutKeyQuads = renderer.ArKeyQuadCount;
        var cutStrip = renderer.ArDamperStripCount;
        Assert(renderer.ArBeamCount == 0 && renderer.ArPulseCount == 0 && renderer.ArImpactLightCount == 0
                && renderer.ArParticleFxCount == 0 && renderer.ArNoteLightCount == 0
                && cutKeyQuads > 0 && cutStrip == 1,
            $"A deeply over-budget AR frame must cut particles, pulses, note lights, beams and the impact glow while keys and the damper strip stay (keys {cutKeyQuads}, strip {cutStrip}, beams {renderer.ArBeamCount}, pulses {renderer.ArPulseCount}, impacts {renderer.ArImpactLightCount}, particles {renderer.ArParticleFxCount}, note lights {renderer.ArNoteLightCount}).");
        renderer.ArBudgetTierOverride = null;
        // A rebuilt feed: the same look and calibration, budget healthy — a fresh strike triggers the whole
        // Ultra set again, because nothing in the look itself was touched by the cut.
        RenderFrames(PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true, visualSettings: liveFxLook,
            activePitches: new[] { 60, 64 }), 4);
        Assert(renderer.ArImpactLightCount > 0 && renderer.ArPulseCount > 0 && renderer.ArBeamCount > 0,
            "The cut is strictly transient: with the budget healthy again the lights come back without touching any setting.");

        // Alignment check (roadmap §14.7 "Thêm 1"): the saved key area redraws as a frame with octave lines, packed
        // after every effect so a bumped camera reads at a glance, on any tier, only while calibrated.
        var guidesOn = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, alignment: true), 8);
        var guideCount = renderer.ArAlignmentGuideCount;
        RenderFrames(PressFeed(false, .6f, ArQualityTier.Lite, alignment: true), 4);
        var liteGuideCount = renderer.ArAlignmentGuideCount;
        var guidesOff = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, alignment: false), 8);
        var guidePixels = ChangedPixels(guidesOff, guidesOn, threshold: 4);
        Assert(guideCount > 4 && liteGuideCount > 4 && renderer.ArAlignmentGuideCount == 0 && guidePixels > 100,
            $"The alignment check must redraw the saved key area over the live frame on any tier and switch fully off ({guideCount} guides at Standard, {liteGuideCount} at Lite, {guidePixels} changed pixels).");

        // The guide colour is one of five high-contrast picks; recolouring must repaint the lines, not rebuild them.
        var cyanGuides = RenderFrames(PressFeed(false, .6f, ArQualityTier.Standard, alignment: true,
            guideColor: new Vector3(.35f, .95f, 1f)), 8);
        Assert(renderer.ArAlignmentGuideCount == guideCount && ChangedPixels(guidesOn, cyanGuides, threshold: 4) > 20,
            "A custom guide colour must recolour the alignment frame without changing its lines.");

        // AR-S5: project the same simulated impact, hold and release effects as Live Stage, not a parallel key-tint-only approximation.
        var standardFxFeed = PressFeed(false, .6f, ArQualityTier.Standard, addImpact: true, visualSettings: liveFxLook, activePitches: new[] { 60, 64 });
        RenderFrames(standardFxFeed, 4);
        var standardImpactLights = renderer.ArImpactLightCount;
        var standardNoteLights = renderer.ArNoteLightCount;
        var standardPulses = renderer.ArPulseCount;
        var standardParticles = renderer.ArParticleFxCount;
        var ultraFxFeed = PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true, visualSettings: liveFxLook, activePitches: new[] { 60, 64 });
        var ultraImpactFrame = RenderFrames(ultraFxFeed, 4);
        var ultraImpactLights = renderer.ArImpactLightCount;
        var ultraNoteLights = renderer.ArNoteLightCount;
        var ultraPulses = renderer.ArPulseCount;
        var ultraParticles = renderer.ArParticleFxCount;
        var ultraFlashes = renderer.ArFlashCount;
        var ultraHoldArcs = renderer.ArHoldArcCount;
        var ultraBeams = renderer.ArBeamCount;
        var noImpactUltra = RenderFrames(PressFeed(false, .6f, ArQualityTier.Ultra, visualSettings: liveFxLook,
            activePitches: new[] { 60, 64 }), 4);
        var effectPixels = ChangedPixels(ultraImpactFrame, noImpactUltra, threshold: 4);
        Assert(standardImpactLights > 0 && standardNoteLights > 0 && standardPulses == 0 && standardParticles == 0
                && ultraImpactLights > 0 && ultraNoteLights > 0 && ultraPulses > 0 && ultraParticles > 0
                && ultraFlashes > 0 && ultraHoldArcs > 0 && ultraBeams > 0 && effectPixels > 100
                && renderer.ArLiveBarCount == 0,
            $"S5 must retain the configured Live Stage hit/hold channels in calibrated 3D while respecting AR quality and the separate bar toggle (Standard lights {standardImpactLights}/{standardNoteLights}, Ultra {ultraImpactLights}/{ultraNoteLights}/{ultraPulses}/{ultraParticles}/{ultraFlashes}/{ultraHoldArcs}/{ultraBeams}, {effectPixels} impact pixels).");

        (byte[] Pixels, int Pulses, int Flashes) RenderFxVariant(PianoVisualSettings fxSettings)
        {
            var pixels = RenderFrames(PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true,
                visualSettings: fxSettings, activePitches: new[] { 60, 64 }), 4);
            return (pixels, renderer.ArPulseCount, renderer.ArFlashCount);
        }
        var plasmaLook = liveFxLook.Clone(); plasmaLook.ImpactFlashStyle = "Plasma";
        var plasmaVariant = RenderFxVariant(plasmaLook);
        var morphLook = liveFxLook.Clone(); morphLook.ImpactFlashStyle = "Flash"; morphLook.ImpactMorph = "Morph";
        var morphVariant = RenderFxVariant(morphLook);
        var shockwaveLook = liveFxLook.Clone(); shockwaveLook.ImpactWave = "Shockwave"; shockwaveLook.ImpactMorph = "None";
        var shockwaveVariant = RenderFxVariant(shockwaveLook);
        var absorbLook = liveFxLook.Clone(); absorbLook.ImpactWave = "None"; absorbLook.ImpactMorph = "Absorb";
        var absorbVariant = RenderFxVariant(absorbLook);
        var plasmaPixels = ChangedPixels(ultraImpactFrame, plasmaVariant.Pixels, threshold: 4);
        var morphPixels = ChangedPixels(ultraImpactFrame, morphVariant.Pixels, threshold: 4);
        var shockwavePixels = ChangedPixels(ultraImpactFrame, shockwaveVariant.Pixels, threshold: 4);
        var absorbPixels = ChangedPixels(ultraImpactFrame, absorbVariant.Pixels, threshold: 4);
        Assert(plasmaVariant.Flashes > 0 && morphVariant.Flashes > 0 && shockwaveVariant.Pulses > 0 && absorbVariant.Pulses > 0
                && plasmaPixels > 30 && morphPixels > 30 && shockwavePixels > 30 && absorbPixels > 30,
            $"The projected style branches must retain Plasma/Morph flashes and Shockwave/Absorb rings ({plasmaVariant.Flashes}/{morphVariant.Flashes} flashes, {shockwaveVariant.Pulses}/{absorbVariant.Pulses} pulses; pixel deltas {plasmaPixels}/{morphPixels}/{shockwavePixels}/{absorbPixels}).");

        // Live bars use the saved 3D pose even when the optional song-note perspective toggle is off.
        var flatNotesLiveBars = MakeFeed(new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: .6f,
                Quality: ArQualityTier.Standard, ShowNotes: false, ProjectedNotes: false, ShowLiveTrails: true),
            song: Array.Empty<NoteEvent>(), position: 0, playing: false, pressed: new HashSet<int> { 60 });
        flatNotesLiveBars.LiveNote(60, true, .9f);
        RenderFrames(flatNotesLiveBars, 8);
        Assert(renderer.ArLiveBarCount > 0 && renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0,
            "The live press bar remains 3D and stage-space notes/trails stay suppressed when Notes in perspective is toggled off.");

        // Stage-space bars used to be culled as soon as their 2D tail left the canvas. A calibrated AR bar must
        // stay alive beyond that edge, keep travelling in its 3D direction, then disappear exactly at its AR reach.
        var releasePathFeed = MakeFeed(new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0,
                ShowNotes: false, ProjectedNotes: false, ShowLiveTrails: true),
            song: Array.Empty<NoteEvent>(), position: 0, playing: false, pressed: new HashSet<int>());
        releasePathFeed.LiveNote(60, true, .9f);
        var releasePathInput = new GpuFrameInput();
        var releasePathSimulation = new GpuStageSimulation(20261010);
        var barsPastStageEdge = 0;
        for (var frame = 0; frame <= 190; frame++)
        {
            if (frame == 4) releasePathFeed.LiveNote(60, false, 0);
            releasePathFeed.Capture(releasePathInput, GpuStageFeed.Now);
            releasePathSimulation.Step(1 / 60.0, releasePathInput, releasePathFeed, width);
            if (frame == 70) barsPastStageEdge = releasePathSimulation.LivePresses.Count;
        }
        Assert(barsPastStageEdge == 1 && releasePathSimulation.LivePresses.Count == 0,
            $"A released calibrated bar should outlive the old ~1.1 s stage-edge cutoff, then expire at its {ArOptions.LivePressReachSeconds:0.00} s fixed AR reach ({barsPastStageEdge} past the old edge, {releasePathSimulation.LivePresses.Count} at the limit).");

        // A release is a separate effect phase and must be anchored to the source pitch, not flattened over camera.
        var releaseFeed = PressFeed(false, .6f, ArQualityTier.Ultra, visualSettings: liveFxLook, activePitches: new[] { 60 });
        var releaseFrame = GpuRenderLoop.RenderFrames(renderer, releaseFeed, width, height, 8, 1 / 60.0, false, out _, frame =>
        {
            if (frame != 4) return;
            releaseFeed.SetDeterministicState(Array.Empty<NoteEvent>(), 0, false, new HashSet<int>());
            releaseFeed.LiveNote(60, false, 0);
        }, 20260930);
        var releaseEffects = renderer.ArReleaseEffectCount;
        Assert(releaseEffects > 0 && renderer.ArParticleFxCount > 0 && ColouredPixels(releaseFrame) > 100,
            $"The configured Dissolve release should emit a visible 3D release effect at its calibrated key ({releaseEffects} active releases, {renderer.ArParticleFxCount} particles).");

        // Effects follow the keyboard transform and piano axes, independently of the note's editable travel direction.
        var movedImpactFeed = PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true, visualSettings: liveFxLook,
            selectedCalibration: transformedCalibration, activePitches: new[] { 60, 64 });
        var movedImpactFrame = RenderFrames(movedImpactFeed, 4);
        var poseEffectPixels = ChangedPixels(ultraImpactFrame, movedImpactFrame, threshold: 4);
        Assert(poseEffectPixels > 100,
            $"Rotating/translating the calibrated piano must move the complete impact/hold effect group with it ({poseEffectPixels} changed pixels).");
        var directionOnlyFeed = PressFeed(false, .6f, ArQualityTier.Ultra, addImpact: true, visualSettings: liveFxLook, activePitches: new[] { 60, 64 });
        directionOnlyFeed.Ar = directionOnlyFeed.Ar! with { NoteDirection = new ArNoteDirection(74, 39) };
        var directionOnlyFrame = RenderFrames(directionOnlyFeed, 4);
        Assert(ultraImpactFrame.AsSpan().SequenceEqual(directionOnlyFrame),
            "Changing note travel direction alone must not rotate key-anchored Live Stage effects away from the calibrated piano.");

        // The flat stage-space trail stays suppressed; the selected note trail is emitted only as a calibrated AR quad.
        settings.FallingTrail = "None";
        var noTrail = Render(MakeFeed(defaultInput));
        settings.FallingTrail = "Glow";
        var glowSetting = Render(MakeFeed(defaultInput));
        var projectedGlowTrails = renderer.ArNoteTrailQuadCount;
        Assert(!noTrail.AsSpan().SequenceEqual(glowSetting) && renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0
                && projectedGlowTrails == notes.Count && renderer.ArObjectQuadCount == notes.Count + projectedGlowTrails
                && ChangedPixels(noTrail, glowSetting, threshold: 4) > 20,
            $"Glow trails should add calibrated AR quads while the misaligned stage-space streak stays suppressed ({projectedGlowTrails} projected trails, stage {renderer.StageNoteTrailCount}).");
        settings.FallingTrail = "None";
        var uncalibratedCamera = Render(MakeFeed(null, overCamera: false));
        settings.FallingTrail = "Glow";
        var uncalibratedCameraWithTrailSetting = Render(MakeFeed(null, overCamera: false));
        Assert(uncalibratedCamera.AsSpan().SequenceEqual(uncalibratedCameraWithTrailSetting)
                && renderer.StageNoteCount == 0 && renderer.StageNoteTrailCount == 0 && renderer.ArObjectQuadCount == 0,
            "Even before calibration, a full-screen camera background must not receive flat scene notes or falling-note trails.");
        settings.FallingTrail = "None";

        // ---- alpha-effects output: preserve the 3D quads, omit camera pixels, and return premultiplied BGRA ---------
        var cameraComposite = Render(MakeFeed(defaultInput));
        var transparent = Render(MakeFeed(defaultInput), alphaOutput: true);
        const int backgroundX = 16, backgroundY = 16;
        var backgroundAt = (backgroundY * width + backgroundX) * 4;
        Assert(Math.Abs(cameraComposite[backgroundAt] - 90) <= 4 && Math.Abs(cameraComposite[backgroundAt + 1] - 90) <= 4
                && Math.Abs(cameraComposite[backgroundAt + 2] - 90) <= 4,
            "The ordinary AR composite should keep the camera picture visible behind the notes.");
        Assert(transparent[backgroundAt] == 0 && transparent[backgroundAt + 1] == 0 && transparent[backgroundAt + 2] == 0 && transparent[backgroundAt + 3] == 0,
            "The alpha-effects output must omit the camera and leave the empty background fully transparent.");
        var alphaPixels = 0; var premultiplied = true;
        for (var i = 0; i < transparent.Length; i += 4)
        {
            if (transparent[i + 3] > 0) alphaPixels++;
            if (transparent[i] > transparent[i + 3] + 1 || transparent[i + 1] > transparent[i + 3] + 1 || transparent[i + 2] > transparent[i + 3] + 1) premultiplied = false;
        }
        Assert(alphaPixels > 500 && premultiplied,
            $"Transparent AR should keep projected note pixels in premultiplied BGRA ({alphaPixels} pixels, premultiplied {premultiplied}).");
        var noteCentre = Centre(expected[0].Patch);
        var noteCentreIndex = ((int)Math.Floor(noteCentre.Y) * width + (int)Math.Floor(noteCentre.X)) * 4;
        Assert(transparent[noteCentreIndex + 3] >= 240,
            "A projected note body should retain opaque alpha in the camera-free output.");

        // S6.2: the geometry layer keeps the solid projected note/direct hot core, while a deliberately bright
        // additive sprite produces a softer bloom contribution in the independent glow layer. Both are read back once.
        settings.BloomIntensity = 65;
        const int glowX = 1100, glowY = 80;
        renderer.ExtraSprites =
        [
            new GpuSpriteInstance
            {
                PosSize = new Vector4(glowX, glowY, 96, 9),
                Color = new Vector4(6, 4, 3, .9f),
                Dir = new Vector4(0, -1, 1, .2f)
            }
        ];
        var activeExpected = new List<Ar3DNoteShape>();
        Ar3DNoteProjection.Build(calibration, view, notes, 10.14, .22, defaultInput.Notes,
            defaultInput.NoteDirection, defaultInput.MinAspect, activeExpected);
        var activeCentre = Centre(activeExpected[0].Patch);
        var activeCentreIndex = ((int)Math.Floor(activeCentre.Y) * width + (int)Math.Floor(activeCentre.X)) * 4;
        var (geometryLayer, glowLayer) = GpuRenderLoop.RenderAlphaLayersOnce(renderer, MakeFeed(defaultInput, position: 10.14), width, height);
        renderer.ExtraSprites = null;
        settings.BloomIntensity = 0;
        var glowProbe = (glowY * width + glowX) * 4;
        var geometryPixels = 0; var glowPixels = 0; var splitPremultiplied = true;
        for (var i = 0; i < geometryLayer.Length; i += 4)
        {
            if (geometryLayer[i + 3] > 0) geometryPixels++;
            if (glowLayer[i + 3] > 0) glowPixels++;
            if (geometryLayer[i] > geometryLayer[i + 3] + 1 || geometryLayer[i + 1] > geometryLayer[i + 3] + 1
                || geometryLayer[i + 2] > geometryLayer[i + 3] + 1 || glowLayer[i] > glowLayer[i + 3] + 1
                || glowLayer[i + 1] > glowLayer[i + 3] + 1 || glowLayer[i + 2] > glowLayer[i + 3] + 1)
                splitPremultiplied = false;
        }
        var splitLayerDifference = ChangedPixels(geometryLayer, glowLayer, threshold: 4);
        Assert(geometryPixels > 500 && glowPixels > 0 && splitLayerDifference > 100 && splitPremultiplied
                && geometryLayer[activeCentreIndex + 3] >= 240 && geometryLayer[glowProbe + 3] > 0
                && glowLayer[glowProbe + 3] > 0
                && geometryLayer[backgroundAt + 3] == 0 && glowLayer[backgroundAt + 3] == 0,
            $"S6.2 should return distinct synchronized premultiplied geometry/glow images: opaque note/direct hot core in Geometry, bloom in Glow, empty transparent corners ({geometryPixels}/{glowPixels} pixels, {splitLayerDifference} different; premultiplied {splitPremultiplied}).");

        // Ambient petals and meteors are deliberately rendered in screen-space: changing only note perspective must
        // leave their WARP pixels byte-for-byte identical.
        settings.ShowNotes = false;
        settings.ShowBackground = true;
        settings.ShowPetals = false;
        settings.PetalAmount = 28;
        settings.ShootingStars = false;
        var ambientFlatInput = new GpuArInput(calibration, ArNoteStyle.Default, 0f, KeyLight: 0,
            ShowNotes: false, ProjectedNotes: false);
        var noPetals = Render(MakeFeed(ambientFlatInput), alphaOutput: true);
        settings.ShowPetals = true;
        var petalsFlat = Render(MakeFeed(ambientFlatInput), alphaOutput: true);
        var petalPixels = 0;
        for (var i = 3; i < petalsFlat.Length; i += 4) if (petalsFlat[i] > 0) petalPixels++;
        Assert(renderer.AmbientCount > 0 && petalPixels > 0 && !noPetals.AsSpan().SequenceEqual(petalsFlat),
            "The fixture must render visible screen-space petals before checking their projection behavior.");
        settings.ArNotesInPerspective = true;
        var petalsWithPerspective = Render(MakeFeed(ambientFlatInput with { ProjectedNotes = true }), alphaOutput: true);
        Assert(petalsFlat.AsSpan().SequenceEqual(petalsWithPerspective),
            "Enabling 3D note projection must leave the ambient-petal pass flat and byte-identical.");

        settings.ShowPetals = false;
        settings.ShootingStars = true;
        settings.ShootingStarsAmount = 100;
        byte[] RenderStars(bool projectedNotes) => RenderFrames(
            MakeFeed(ambientFlatInput with { ProjectedNotes = projectedNotes }), 60, alphaOutput: true);
        var starsFlat = RenderStars(projectedNotes: false);
        var starPixels = 0;
        for (var i = 3; i < starsFlat.Length; i += 4) if (starsFlat[i] > 0) starPixels++;
        Assert(renderer.AmbientCount > 0 && starPixels > 0,
            "The shooting-star fixture must contain visible alpha pixels, not only invisible additive RGB.");
        var starsWithPerspective = RenderStars(projectedNotes: true);
        Assert(starsFlat.AsSpan().SequenceEqual(starsWithPerspective),
            "Enabling 3D note projection must leave shooting stars screen-space and byte-identical.");

        Results.Add($"PASS AR 3D note render (AR-S4): WARP drew {projectedPixels} projected note pixels into {expected.Count} AR-owned quads with no stage notes/trails; the flat-map failure did not hide them, mirroring and independent 3D direction worked, Perform omitted notes, and minimum length grew short notes.");
        Results.Add($"PASS AR note color/material/effects: magenta read back R/G {noteRed}/{noteGreen}; sampled known material branches changed [{materialChangeSummary}] pixels versus Solid; shimmer changed {shimmerPixels} pixels in {shimmerQuads} quads; Glow/Stream trails used {glowTrailQuads}/{streamTrailQuads} 3D quads; ghosts {ghostQuads}; landing glow {landingGlowCount} quads; atlas labels {noteLabelCount}/{noteLabelPixels}. Full parameter parity remains Unverified.");
        Results.Add("PASS AR uncalibrated-camera gate: synthetic camera frames remain pixel-identical with Live Stage hit/hold effects and ShowKeys enabled; virtual keys, stage sprites, guide lanes, song notes and falling trails stay suppressed until calibrated projection is available, while screen-space ambient remains independent.");
        Results.Add($"PASS AR live bars and trails: {liveBarCount} projected live bars added {barPixels} pixels only when enabled; key tint still changed {tintPixels} pixels with bars off; a released bar survived the old stage-edge cull ({barsPastStageEdge}) and retired at 3D reach ({releasePathSimulation.LivePresses.Count}); flat falling-note trails added no pixels over the camera.");
        Results.Add($"PASS AR-S5 Live Stage effects: Standard projected {standardImpactLights} impact and {standardNoteLights} note lights; Ultra projected impact/note lights, {ultraPulses} configured pulses, {ultraParticles} particles, {ultraFlashes} flashes, {ultraHoldArcs} hold-arc segments and {ultraBeams} keyed beams; release emitted {releaseEffects} 3D effects; Plasma/Morph flashes and Shockwave/Absorb rings were pixel-checked; independent HoldBar/Breath/Vibration/ColorCycle live-bar toggles and representative parameter changes changed {holdBarTogglePixels}/{holdBarIntensityPixels}, {holdBreathTogglePixels}/{holdBreathRatePixels}, {holdVibrationTogglePixels}/{holdVibrationAmountPixels}, {holdColorCycleTogglePixels}/{holdColorCycleSpeedPixels} pixels; the look's velocity tint recoloured a bar in {velocityTintPixels} pixels at the leaned view and {overheadTintPixels} at 90° overhead; sustain lifted the damper strip in {damperLiftPixels} pixels leaned / {overheadDamperPixels} overhead (Lite drew none, overhead projected {overheadBarCount} bars); impact changed {effectPixels} pixels with bars disabled, pose motion changed {poseEffectPixels}, and note-direction-only motion stayed image-identical.");
        Results.Add($"PASS AR alpha/ambient projection: {alphaPixels} projected premultiplied note pixels; S6.2 separated {geometryPixels} geometry and {glowPixels} glow pixels ({splitLayerDifference} differed); {petalPixels} flat petal pixels and {starPixels} flat shooting-star pixels were unchanged by 3D note projection.");
    }

    private static void VerifyArDriftWatch()
    {
        // roadmap §14.7 "Thêm 2 — đo trôi": the pure picture-shift measurement behind the watch, checked without a camera
        // on the same striped trapezoid stand-in the xUnit suite locks (a moved picture must measure to within a cell,
        // lighting and hands must never raise the alarm).
        const int w = 640, h = 360;
        (double X, double Y)[] corners = [(35, 146), (607, 134), (618, 211), (26, 224)];
        byte[] Paint(double shiftX, double shiftY, double brightness = 1)
        {
            var px = new byte[w * h * 4];
            var rng = new Random(9);
            for (var y = 0; y < h; y++)
            {
                double tBack = corners[0].Y, tFront = corners[3].Y;
                if (y < tBack + shiftY - 1 || y > tFront + shiftY + 1) continue;
                double tv = Math.Clamp((y - (tBack + shiftY)) / (tFront - tBack), 0, 1);
                var xa = corners[0].X + (corners[3].X - corners[0].X) * tv + shiftX;
                var xb = corners[1].X + (corners[2].X - corners[1].X) * tv + shiftX;
                for (var x = (int)Math.Ceiling(xa); x < Math.Min(w, xb); x++)
                {
                    var u = (x - xa) / (xb - xa);
                    var lum = (int)Math.Floor(u * 26) % 2 == 0 ? 232 : 56;   // 45 px bands: unique across the search window, so a move cannot alias onto the next stripe
                    if (u > .3 && u < .72 && tv < .6) lum = Math.Min(lum, 44);
                    lum = (int)Math.Clamp(lum * brightness + 6 + (rng.NextDouble() - .5) * 6, 0, 255);
                    var i = (y * w + x) * 4;
                    px[i] = px[i + 1] = px[i + 2] = (byte)lum; px[i + 3] = 255;
                }
            }
            return px;
        }
        var region = MotionEnergy.KeysRegion(corners, w, h);
        var margin = ArDriftMeter.MaxShiftPixels(w);
        var expanded = ArDriftMeter.Expand(region, margin, w, h);
        var reference = new byte[MotionEnergy.SampleCount(region)];
        Assert(ArDriftMeter.SampleGrid(Paint(0, 0), w, h, region, reference), "The reference grid samples for the marked key area.");
        ArDriftEstimate Measure(double shiftX, double shiftY, double brightness = 1)
        {
            var grid = new byte[MotionEnergy.SampleCount(expanded)];
            Assert(ArDriftMeter.SampleGrid(Paint(shiftX, shiftY, brightness), w, h, expanded, grid), "Each measured frame samples the expanded area.");
            return ArDriftMeter.Estimate(reference, region, grid, expanded, w);
        }
        var threshold = ArDriftMeter.ThresholdPixels(w);
        var aligned = Measure(0, 0);
        var moved = Measure(9, 3);
        var bright = Measure(0, 0, 1.18);
        Assert(aligned.Usable && aligned.Pixels <= 1.5 && !aligned.IsDrift(threshold)
                && moved.IsDrift(threshold) && Math.Abs(moved.OffsetX - 9) <= 3 && Math.Abs(moved.OffsetY - 3) <= 3
                && bright.Usable && !bright.IsDrift(threshold),
            $"The drift meter must measure a real move to within a cell (got {moved.OffsetX:0.#}, {moved.OffsetY:0.#}) and never mistake a still or brighter picture for drift (aligned {aligned.Pixels:0.#} px, bright residual {bright.Residual:0.###}).");
        // the watch's alarm rule itself: nothing before the streak, one sentence after three readings in a row
        Assert(!moved.IsDrift(ArDriftMeter.ThresholdPixels(w * 4)) && moved.IsDrift(ArDriftMeter.ThresholdPixels(w)),
            "The pixel threshold scales with the picture width, so the same move only flags at the matching resolution.");
        Results.Add($"PASS AR drift watch: a 9-pixel move of the key picture was read as {moved.OffsetX:0.#}, {moved.OffsetY:0.#} pixels (threshold {threshold:0.#} px at {w} px wide) while the still picture read {aligned.Pixels:0.#} px and an 18% brightness change stayed silent; the watch nags after {ArDriftMeter.FlagHoldReadings} readings in a row and never moves the calibration itself.");
    }

    private static void VerifyCalibrationAdvice()
    {
        // roadmap §14.7 "Thêm 4 — hướng dẫn ngay trong cửa sổ": the calibration window's live lookout, checked on the
        // same synthetic frames the xUnit suite locks — a palm in frame fires, quiet keys/felt/wood stay silent, and
        // with all four corners down only what is INSIDE the quad is watched.
        const int w = 320, h = 180;
        var quiet = new byte[w * h * 4];
        var rng = new Random(4);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var lum = y > 120 ? (200 + rng.Next(30)) : (34 + rng.Next(18));
                if (y > 120 && x % 40 > 32) lum = 24;
                quiet[i] = quiet[i + 1] = quiet[i + 2] = (byte)lum; quiet[i + 3] = 255;
            }
        void PaintHand(byte[] px, int cx, int cy, int radius)
        {
            for (var y = cy - radius; y <= cy + radius; y++)
                for (var x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h || (x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
                    var i = (y * w + x) * 4;
                    px[i] = 140; px[i + 1] = 174; px[i + 2] = 226; px[i + 3] = 255;
                }
        }
        var (warnQuiet, shareQuiet) = CalibrationAdvice.CheckPicture(quiet, w, h, null);
        Assert(!warnQuiet && shareQuiet < CalibrationAdvice.SkinWarningFraction, "White and black keys over dark wood never read as skin.");
        var palm = (byte[])quiet.Clone();
        PaintHand(palm, w / 2, h / 2, 42);
        var (warnPalm, sharePalm) = CalibrationAdvice.CheckPicture(palm, w, h, null);
        Assert(warnPalm, "A palm-sized skin blob in frame fires the hands advice before the corners are down.");
        (double X, double Y)[] quad = [(40, 100), (280, 100), (290, 170), (30, 170)];
        var inside = (byte[])quiet.Clone(); PaintHand(inside, 160, 135, 30);
        var outside = (byte[])quiet.Clone(); PaintHand(outside, 30, 30, 30);
        Assert(CalibrationAdvice.CheckPicture(inside, w, h, quad).HandsInTheWay, "Skin inside the marked quad fires.");
        Assert(!CalibrationAdvice.CheckPicture(outside, w, h, quad).HandsInTheWay, "A waving arm beside the keys is nobody's business.");
        Results.Add($"PASS Calibration framing advice: a quiet keyboard read {shareQuiet:0.000} skin (silent), a palm fired at {sharePalm:0.000}, and with corners down the lookout watches only the inside of the quad (inside fired, outside stayed silent; threshold {CalibrationAdvice.SkinWarningFraction:0%}).");
    }

    private static void VerifyArQualityBudget()
    {
        // roadmap §14.8: the drop rule in its sentence order (particles → pulses → note lights → light bars → note
        // style → impact glow), each adaptive tier cutting a deterministic count, and the overlay's three essences
        // — keys, notes, keypresses — untouched no matter how deep the pressure goes; the WARP half lives inside
        // VerifyArNoteProjection, which plays one over-budget frame on the same look and counts what survives.
        Assert(ArQualityBudget.CutOrder.Length == 6
                && ArQualityBudget.CutOrder[0] == ArBudgetCut.Particles
                && ArQualityBudget.CutOrder[^1] == ArBudgetCut.ImpactLights,
            "The cut table must open with particles and close with the impact glow, as the roadmap's sentence says.");
        Assert(ArQualityBudget.CutCountFor(GpuAdaptiveQualityTier.Full) == 0
                && ArQualityBudget.CutCountFor(GpuAdaptiveQualityTier.BloomReduced) == 1
                && ArQualityBudget.CutCountFor(GpuAdaptiveQualityTier.AmbientMinimal) == ArQualityBudget.CutOrder.Length
                && ArQualityBudget.IsCut(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.NoteLights)
                && !ArQualityBudget.IsCut(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.LightBars),
            "Each adaptive step cuts exactly one more family down the order, never deeper than the table.");
        foreach (var tier in System.Enum.GetValues<GpuAdaptiveQualityTier>())
            Assert(ArQualityBudget.CanDraw(ArQualityTier.Lite, tier, ArElement.PianoKeys)
                    && ArQualityBudget.CanDraw(ArQualityTier.Lite, tier, ArElement.NoteObjects)
                    && ArQualityBudget.CanDraw(ArQualityTier.Lite, tier, ArElement.Keypresses),
                "Keys, notes and keypresses stay drawable at any tier under any adaptive pressure.");
        Results.Add($"PASS AR quality budget: {ArQualityBudget.CutOrder.Length} deterministic cuts in the roadmap's order across {(int)GpuAdaptiveQualityTier.AmbientMinimal + 1} adaptive steps (deeper tier ⇒ deeper cut), with keys/notes/keypresses proved outside every cut; the transient WARP half renders inside VerifyArNoteProjection.");
    }

    private static void VerifyArCalibrationArchive()
    {
        // roadmap §14.7 "Thêm 3 — xuất/nhập bản sao căn chỉnh": the portable backup must round-trip every field of
        // every saved calibration of one camera, refuse what is not this archive in plain words, and put rows back
        // only after they pass the same checks a fresh calibration passes — checked without a camera or a window,
        // in a throwaway folder like the xUnit suite does.
        (double X, double Y)[] cornerSet = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
        ArCalibrationArchiveRow Row(string profile, int keyCount = 88) => new(
            profile, 1280, 720, keyCount, 71.5,
            PositionX: .5, PositionY: -.25, PositionZ: 1.2,
            PitchDegrees: 3, YawDegrees: -2, RollDegrees: 1, UniformScale: 1.04,
            Corners: ArCalibrationArchive.Flat(cornerSet),
            PoseBaseCorners: [68, 290, 1212, 266, 1234, 419, 50, 447]);
        var json = ArCalibrationArchive.ToJson(new ArCalibrationArchiveBundle(
            "\\\\?\\USB#VID_0ECA", "Studio Cam", AppInfo.Version, "2026-10-12T10:30:00Z",
            true, 1280, 720, 0, [Row("tilt45"), Row("overhead90")]));
        Assert(ArCalibrationArchive.TryRead(json, out var bundle, out var refused) && bundle!.Rows.Count == 2 && bundle.SkippedRows == 0,
            "A backup of two calibrations reads back with both rows and nothing skipped.");
        var overhead = bundle!.Rows.Single(row => row.Profile == "overhead90");
        Assert(Math.Abs(overhead.CameraFovDegrees - 71.5) < 1e-6 && Math.Abs(overhead.UniformScale - 1.04) < 1e-9 && overhead.Corners.SequenceEqual(ArCalibrationArchive.Flat(cornerSet)),
            "FOV, pose, scale and corners survive the trip exactly.");
        Assert(!ArCalibrationArchive.TryRead("{\"preset\": 1}", out _, out var wrongKind) && wrongKind.Length > 0,
            "A file that is not this archive is refused with a reason, never guessed at.");
        var crossed = Row("tilt45") with { Corners = [100, 100, 900, 100, 100, 600, 900, 600] };
        Assert(ArCalibrationArchive.TryRead(ArCalibrationArchive.ToJson(new ArCalibrationArchiveBundle("cam", "", "", "", false, 0, 0, 0, [crossed, Row("tilt45")])), out var weeded, out _)
            && weeded!.Rows.Count == 1 && weeded.SkippedRows == 1,
            "A row whose corners would never draw is skipped, and the good row still comes back.");
        var directory = Path.Combine(Path.GetTempPath(), "keyflow-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var store = new ArCalibrationStore(directory);
            Assert(store.Set("\\\\?\\USB#VID_0ECA", "tilt45", 1280, 720, 88, cornerSet, 71.5), "The live store keeps the calibration to export.");
            var exported = store.ExportRows("\\\\?\\USB#VID_0ECA");
            Assert(exported.Count == 1 && store.ExportRows("\\\\?\\USB#VID_OTHER").Count == 0, "The backup holds exactly the camera asked for.");
            var (restored, skipped) = new ArCalibrationStore(directory).ImportRows("\\\\?\\USB#VID_0ECA", exported);
            Assert(restored == 1 && skipped == 0, "Every exported row goes back through the store's own checks.");
        }
        finally { try { Directory.Delete(directory, true); } catch { } }
        Results.Add($"PASS AR calibration backup: 2 rows round-tripped every field (FOV 71.5°, scale 1.04, corners exact) through {ArCalibrationArchive.FormatId} v{ArCalibrationArchive.FormatVersion}, a wrong file was refused in words, 1 unusable row skipped instead of drawn, and the store exported 1 camera and restored 1/1 rows.");
    }

    /// <summary>
    /// AR render-to-video (roadmap §14.10 AR‑S7, frame half): the profile plans the geometry and rate, the
    /// offline sweep draws the scripted ticks at a fixed time step over the camera picture and writes them
    /// as a real AVI and as a PNG frame folder, and a live-shaped render of the same script gives the same
    /// pixels. Audio and the watermark sit in the S7 voice half and are not claimed here.
    /// </summary>
    private static void VerifyArRenderToVideo()
    {
        const int cw = 640, ch = 360;
        var quiet = new PianoVisualSettings
        {
            BackgroundGradient = false, ShowStars = false, ShowHalo = false, ShowKeys = false, ShowNotes = false,
            ShowEmbers = false, ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowLightBeams = false, ShowPetals = false, HorizonGlow = 0, Vignette = 0, BloomIntensity = 0
        };
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, [(40, 170), (606, 156), (620, 248), (30, 262)], cw, ch, out var problem);
        Assert(calibration is not null, $"The take script's corners must calibrate ({problem}).");
        var ring = new CameraFrameRing();
        void PublishTickCamera(int tick)
        {
            // The camera itself steps too: a bright left stripe plus a grey that brightens with every tick.
            var grey = 90 + tick * 8;
            var buffer = ring.BeginWrite(cw * ch * 4, out var slot);
            for (var pixel = 0; pixel < cw * ch; pixel++)
            {
                var level = pixel % cw < cw / 12 ? grey + 60 : grey;
                var i = pixel * 4;
                buffer[i] = (byte)level; buffer[i + 1] = (byte)level; buffer[i + 2] = (byte)level; buffer[i + 3] = 255;
            }
            ring.Publish(slot, cw, ch, Stopwatch.GetTimestamp());
        }

        // The script: quiet before and after; keys held through ticks 1–3; an impact flash on tick 2.
        GpuStageFeed TickFeed(int tick)
        {
            PublishTickCamera(tick);
            var held = tick is >= 1 and <= 3 ? new HashSet<int> { 60, 64 } : new HashSet<int>();
            var feed = new GpuStageFeed(); feed.SetStageHeight(360);
            feed.SetLook(GpuLook.From(quiet, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
            feed.ClearTransient();
            feed.SetDeterministicState([], 0, false, held);
            feed.SetDeterministicBeatPulse(0);
            if (tick == 2) { feed.LiveNote(60, true, .8f); feed.Impact(60, .8); }
            if (tick == 3) feed.LiveNote(64, true, .7f);
            feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1f);
            feed.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, KeyLight: .8f);
            return feed;
        }

        using var renderer = GpuStageRenderer.Create(forceWarp: true);
        var directory = Path.Combine(Path.GetTempPath(), "keyflow-arvideo-" + Guid.NewGuid().ToString("N"));
        var view = new ArView(cw, ch, 200, 112, PictureFit.Fit, false);
        (int X, int Y) At(int pitch, double v) { var p = calibration!.ToPicture(calibration.Layout.CenterX(pitch), v); var o = view.ToOutput(p.X, p.Y); return ((int)Math.Floor(o.X), (int)Math.Floor(o.Y)); }
        try
        {
            Directory.CreateDirectory(directory);

            // AVI: the sweep writes every planned frame at the planned geometry, and the file reads back as video.
            var aviPlan = ArVideoRenderProfile.Plan(cw, ch, lengthSeconds: 0.5, frameRate: 12, downsample: 1, maxSide: 200);
            Assert(aviPlan.RenderWidth == 200 && aviPlan.RenderHeight == 112 && aviPlan.Width == 200 && aviPlan.Frames == 6,
                "The take planner should cap the long side to 200 px and count half a second at 12 fps as six frames.");
            var aviPath = Path.Combine(directory, "take.avi");
            ArVideoTake aviTake;
            using (var recorder = new AviVideoRecorder(aviPath, aviPlan.Width, aviPlan.Height, aviPlan.FrameRate))
                aviTake = ArRenderToVideo.Run(renderer, TickFeed, aviPlan, recorder);
            Assert(aviTake.FramesWritten == 6 && aviTake.Seconds is > 0.45 and <= 0.6 && File.Exists(aviPath) && new FileInfo(aviPath).Length > 2000,
                $"The engine should write every planned frame into the AVI (frames {aviTake.FramesWritten}, seconds {aviTake.Seconds:0.##}).");
            var reader = CameraFrameReader.OpenFile(aviPath, out var readError);
            Assert(reader is not null, $"The take must read back as a video ({readError}); the media stack of the CI box decodes the app's own AVI as part of the recording checks.");
            var lumaLeftStripe0 = 0; var keyLevels = new List<int>();
            using (reader)
            {
                Assert(reader!.Width == 200 && reader.Height == 112, $"The AVI must come back at the plan's size (got {reader.Width}×{reader.Height}).");
                var frame = new byte[200 * 112 * 4];
                var frameIndex = 0;
                while (reader.TryReadFrame(frame, mirror: false, out var gotWidth, out var gotHeight))
                {
                    Assert(gotWidth == 200 && gotHeight == 112 && frame[3] == 255, "Every take frame is opaque and at the plan's size.");
                    if (frameIndex == 0) lumaLeftStripe0 = frame[0];
                    var key = At(60, .85);
                    keyLevels.Add(frame[(key.Y * 200 + key.X) * 4]);
                    frameIndex++;
                }
                Assert(frameIndex == 6, $"All six ticks must be in the file (read {frameIndex}).");
            }
            Assert(lumaLeftStripe0 > 120, $"The left stripe of the tick-0 camera picture must survive recording ({lumaLeftStripe0}).");
            Assert(keyLevels.Count == 6, $"The take must carry six probe readings (got {keyLevels.Count}).");
            // Keys light the moment they are pressed (ticks 1–3) and go quiet when the take goes quiet.
            Assert(keyLevels[0] < 160 && keyLevels[5] < 170 && keyLevels[1] > keyLevels[0] + 40 && keyLevels[2] > keyLevels[0] + 40
                    && keyLevels[3] > keyLevels[0] + 40 && keyLevels[5] < keyLevels[2] - 40,
                $"The C4 light must be quiet at the take's ends and lit through the pressed ticks ({string.Join("/", keyLevels)}).");

            // PNG: downsample 2 boxes the 320×180 sweep into 160×90 frames — and the sweep itself replays bit-identically.
            var pngPlan = ArVideoRenderProfile.Plan(cw, ch, 0.5, 12, downsample: 2, maxSide: 320);
            Assert(pngPlan.RenderWidth == 320 && pngPlan.RenderHeight == 180 && pngPlan.Width == 160 && pngPlan.Height == 90,
                "Downsample 2 should box a 320×180 render into a 160×90 output frame.");
            var firstBuffers = new List<byte[]>();
            var pngDir = Path.Combine(directory, "frames");
            using (var recorder = new PngSequenceRecorder(pngDir, pngPlan.Width, pngPlan.Height, pngPlan.FrameRate))
            {
                var pngTake = ArRenderToVideo.Run(renderer, TickFeed, pngPlan, recorder, (i, bgra) => firstBuffers.Add(bgra));
                Assert(pngTake.FramesWritten == 6 && pngTake.Downsample == 2, "The PNG take writes the full sweep.");
            }
            Assert(Directory.GetFiles(pngDir, "frame-*.png").Length == 6 && File.Exists(Path.Combine(pngDir, PngSequenceRecorder.ManifestName))
                    && firstBuffers.All(b => b.Length == 160 * 4 * 90),
                "The take writes one PNG per tick plus the manifest, from 160×90 BGRA readbacks.");
            // So khung live ↔ render: tick 2 by hand, the exact call the engine made, is the same picture bit for bit.
            var liveTick = GpuRenderLoop.RenderFrames(renderer, TickFeed(2), 320, 180, 0, pngPlan.FrameSeconds, false, out _, null, ArVideoRenderProfile.Seed);
            Assert(BgraPack.Box2(liveTick, 320, 180).AsSpan().SequenceEqual(firstBuffers[2]),
                "A live-shaped render of the same tick, same fixed time step and same seed must land on the take's third frame bit for bit.");
            // And the whole take seconds as an export: a second sweep of the same script writes the same readbacks.
            var secondBuffers = new List<byte[]>();
            using (var recorder = new PngSequenceRecorder(pngDir, pngPlan.Width, pngPlan.Height, pngPlan.FrameRate))
                ArRenderToVideo.Run(renderer, TickFeed, pngPlan, recorder, (i, bgra) => secondBuffers.Add(bgra));
            Assert(secondBuffers.Count == firstBuffers.Count && firstBuffers.Zip(secondBuffers).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)),
                "Two offline sweeps of one script must be byte-identical — render-to-video owns no clock of its own.");
            Results.Add("PASS AR render-to-video (frame half): the plan caps a 640×360 camera to 200×112/12 fps and counts 6 frames, the engine's AVI take reads back with the camera stripe and the pressed-key light on the right ticks, the PNG take at downsample 2 boxes 320×180→160×90 with one file per tick plus manifest, the impact tick equals a live-shaped render bit for bit, and two sweeps of one script are byte-identical.");
        }
        finally { try { Directory.Delete(directory, true); } catch { } }
    }
}
