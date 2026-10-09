using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// Pins AR support to the actual composite contract: six ambient layers are verified over the camera;
    /// stage-anchored note/key effects are suppressed, and note materials keep their explicit flat-quad fallback.
    /// Pixel-level evidence for the visible and suppressed groups is exercised by VerifyArOverlayCapturedFrames.
    /// </summary>
    private static void VerifyArOverlayExclusions()
    {
        var nativeInAr = new HashSet<string>(StringComparer.Ordinal)
        {
            "ambient.energy", "ambient.nature", "ambient.light", "ambient.cosmic",
            "atmosphere.shooting-stars", "atmosphere.petals",
        };
        var families = EffectContractCatalog.AllFamilies;
        Assert(families.Count == 24, "AR classification should cover all 24 typed effect families.");
        Assert(nativeInAr.SetEquals(families.Where(family => family.Capabilities.ArOverlay.State == EffectBackendState.Native).Select(family => family.Id)),
            "Only the four ambient layers, shooting stars and petals are verified as native over the camera.");
        foreach (var family in families)
        {
            var expected = nativeInAr.Contains(family.Id) ? EffectBackendState.Native : EffectBackendState.Unsupported;
            Assert(family.Capabilities.ArOverlay.State == expected,
                $"{family.Id} must match the captured AR compositor evidence ({expected}).");
            var expectedBinding = expected == EffectBackendState.Native ? EffectParameterBindingState.Applied : EffectParameterBindingState.Ignored;
            Assert(family.Parameters.All(parameter => parameter.Bindings.ArOverlay == expectedBinding),
                $"{family.Id} parameter bindings should match its AR output support state.");
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
            Assert(material.Capabilities.ArOverlay.State == EffectBackendState.Fallback
                    && material.Capabilities.ArOverlay.FallbackId == EffectContractCatalog.ArFlatQuadFallbackId,
                $"{material.Id} must keep the tested flat-quad AR fallback, not claim native Gem/Crystal facets.");
        }
        Results.Add("PASS AR contract classification: six camera-composited ambient families are Native, 18 stage-anchored families are Unsupported, note materials retain the ar.note.flat-quad fallback; all 24 family-level Recording claims have isolated capture fixtures, while parameter bindings remain Unverified.");
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

        var flatRing = new CameraFrameRing(); PublishGrey(flatRing, 1280, 720);
        var flatInput = new GpuCameraInput(flatRing, PictureFit.Fit, false, 1f);
        var arInput = new GpuArInput(calibration!, ArNoteStyle.Default, 1f);
        var expected = new List<ArNoteShape>();
        ArNotes.Build(calibration!, arView, arNotes, 10, 1, ArNoteStyle.Default, expected);
        Assert(expected.Count == 4, $"Four of the five notes are in reach or sounding ({expected.Count}).");
        var arFrame = Render(MakeArFeed(flatInput, arInput));
        // the sounding G4 also lights its key, so the keys are counted apart from the notes
        Assert(renderer.ArQuadCount - renderer.ArKeyQuadCount == 4 && renderer.ArKeyQuadCount >= 1, $"The renderer should draw one quad for each note in reach ({renderer.ArQuadCount} quads, {renderer.ArKeyQuadCount} of them keys).");
        var gemArFrame = Render(MakeArFeed(flatInput, arInput, noteStyle: "Gem"));
        var crystalArFrame = Render(MakeArFeed(flatInput, arInput, noteStyle: "CrystalOutline"));
        Assert(arFrame.AsSpan().SequenceEqual(gemArFrame) && arFrame.AsSpan().SequenceEqual(crystalArFrame),
            "AR should retain its projected flat-quad material fallback for Gem and Crystal Outline, not claim their stage facets.");
        var plain = Look(arFrame, width, height, (20, 20))!.Value;
        Assert(plain.Off <= 3 && plain.Spread <= 3, $"Away from every note the picture is the camera's grey (off by {plain.Off}).");
        foreach (var shape in expected)
        {
            var insideChecked = 0;
            foreach (var (s, t) in new[] { (.5, .5), (.2, .5), (.8, .5), (.5, .15), (.5, .85), (.5, .95) })
            {
                if (Look(arFrame, width, height, Blend(shape.Patch, s, t)) is not { } inside) continue;
                insideChecked++;
                Assert(inside.Spread > 30 && inside.Off > 40, $"Note {shape.Pitch} should be coloured at ({s:0.00},{t:0.00}) of its quad, spread {inside.Spread}, off grey by {inside.Off}.");
            }
            Assert(insideChecked >= 2, $"At least two sample points of note {shape.Pitch} should be on the output ({insideChecked}).");
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
        Assert(renderer.ArQuadCount - renderer.ArKeyQuadCount == 0, "A paused song draws no notes, like the stage.");
        Render(MakeArFeed(flatInput, new GpuArInput(calibration!, ArNoteStyle.Default, 0f)));
        Assert(renderer.ArQuadCount == 0, "An AR layer at zero opacity draws nothing.");
        Render(MakeArFeed(flatInput, new GpuArInput(calibration!, ArNoteStyle.Default, 1f, KeyLight: .6f, ShowNotes: false)));
        Assert(renderer.ArQuadCount == renderer.ArKeyQuadCount && renderer.ArKeyQuadCount >= 1,
            "Perform mode draws no notes to come but still lights the key of the note that sounds.");
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
        var unlit = RenderWarm(MakeKeyFeed(null));
        Assert(litKeys == 5 && renderer.ArQuadCount == 0, $"Three held keys (C4 and E4 white, C#4 black) are five rectangles ({litKeys}).");
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
            ("D4, a white key not held", KeyPoint(layout.CenterX(62), .85)),
            ("D4's stem between two black keys", KeyPoint((layout.Span(61).Right + layout.Span(63).Left) / 2, .3)),
            ("D#4, a black key beside two held keys", KeyPoint(layout.CenterX(63), .3)),
            ("F#4, a black key not held", KeyPoint(layout.CenterX(66), .3)),
            ("G5, far from any held key", KeyPoint(layout.CenterX(79), .85)),
            ("the air above C4", KeyPoint(layout.CenterX(60), -.3)),
        ];
        foreach (var (what, at) in shouldStay)
            Assert(Change(at) <= 2, $"Only held keys light up: {what} changed by {Change(at)}.");
        RenderWarm(MakeKeyFeed(new GpuArInput(calibration, ArNoteStyle.Default, 1f, 0f)));
        Assert(renderer.ArQuadCount == 0, "A key light strength of zero draws no key.");

        // ---- the keys under the player's own hands: a bar leaves the key ---------------------------------------
        // No song at all: nothing plays and no notes are loaded, which is what playing freely looks like. The key
        // still lights up, and a bar leaves it along the keyboard's own direction — the direction the song's notes
        // come from — stretching as the key is held. Perform mode keeps the bar: it leaves out the notes of the song
        // to come, not the mark of what the player just played.
        GpuStageFeed ArFeed(int[] pitches, bool press)
        {
            var feed = MakeFeed(flatInput);
            feed.SetState([], 0, false, new HashSet<int>(pitches));
            feed.Ar = new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: .6f, ShowNotes: false);   // Perform: the song's notes are left out
            if (press) foreach (var pitch in pitches) feed.LiveNote(pitch, true, .9f);
            return feed;
        }
        (byte[] Pixels, int Bars, int Keys, int Quads) Draw(GpuStageFeed feed, int frames, Action<int>? beforeFrame = null)
        {
            var pixels = GpuRenderLoop.RenderFrames(renderer, feed, width, height, frames, 1 / 60.0, false, out _, beforeFrame, 20260930);
            return (pixels, renderer.ArLiveBarCount, renderer.ArKeyQuadCount, renderer.ArQuadCount);
        }
        int Probe(byte[] withBar, byte[] withoutBar, (double X, double Y) at)
        {
            var x = (int)Math.Floor(at.X); var y = (int)Math.Floor(at.Y);
            Assert(x >= 0 && y >= 0 && x < width && y < height, $"The bar probe ({at.X:0.0},{at.Y:0.0}) must be on the output.");
            var i = (y * width + x) * 4;
            return Math.Max(Math.Abs(withBar[i] - withoutBar[i]), Math.Max(Math.Abs(withBar[i + 1] - withoutBar[i + 1]), Math.Abs(withBar[i + 2] - withoutBar[i + 2])));
        }
        var withoutBars = Draw(ArFeed([60, 61], press: false), 30);        // the same keys held, with no press behind them
        var withBars = Draw(ArFeed([60, 61], press: true), 30);
        Assert(withBars.Bars == 2 && withBars.Keys == 3 && withBars.Quads == 5,
            $"Two keys pressed should light their three key rectangles and draw one bar each ({withBars.Quads} quads: {withBars.Keys} key, {withBars.Bars} bar).");
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
        var withoutBarsLong = Draw(ArFeed([60, 61], press: false), 60);
        Assert(stretchedBars.Bars == 2 && Probe(stretchedBars.Pixels, withoutBarsLong.Pixels, farProbe) > 25,
            $"The bar stretches as the key is held: past where half a second reached ({Probe(stretchedBars.Pixels, withoutBarsLong.Pixels, farProbe)} at -1.2).");
        // let go: the bar ends a moment later, while the key stays lit — the light is what the key is, the bar is what the hand did
        var letGoFeed = ArFeed([60, 61], press: true);
        var afterRelease = Draw(letGoFeed, 80, frame =>
        {
            if (frame != 20) return;
            letGoFeed.LiveNote(60, false, 0); letGoFeed.LiveNote(61, false, 0);
        });
        Assert(afterRelease.Bars == 0 && afterRelease.Keys == 3 && afterRelease.Quads == 3,
            $"Letting go ends the bars while the keys stay lit ({afterRelease.Quads} quads: {afterRelease.Keys} key, {afterRelease.Bars} bar).");
        // a fifth of a second after the release the bars are still there, dimming; the key light was never part of the deal either way
        var fadingFeed = ArFeed([60, 61], press: true);
        var fadingAfterRelease = Draw(fadingFeed, 25, frame =>
        {
            if (frame != 20) return;
            fadingFeed.LiveNote(60, false, 0); fadingFeed.LiveNote(61, false, 0);
        });
        Assert(fadingAfterRelease.Bars == 2 && fadingAfterRelease.Quads == 5,
            $"A key just let go keeps its bar for a moment ({fadingAfterRelease.Quads} quads: {fadingAfterRelease.Keys} key, {fadingAfterRelease.Bars} bar five frames after the release).");
        Results.Add("PASS ar live keys: the keys the player presses draw their own bars behind them, white or black, stretching along the keyboard as they are held and stopping at the look-ahead, gone a moment after the key is let go while the lit key stays; nothing is drawn over the key, behind a key that was not pressed, or beyond the reach — and Perform mode keeps them.");

        Render(MakeArFeed(flatInput, null));
        Assert(renderer.ArQuadCount == 0, "Without an AR layer there are no AR quads.");
        var smallRing = new CameraFrameRing(); PublishGradient(smallRing, 160, 90);
        Render(MakeArFeed(new GpuCameraInput(smallRing, PictureFit.Fit, false, 1f), arInput));
        Assert(renderer.ArQuadCount == 0, "A camera that is not the size the calibration was made at must hide the AR layer, not draw notes in the wrong place.");
        Render(MakeArFeed(null, arInput));
        Assert(renderer.ArQuadCount == 0, "The AR layer lies on the camera picture and is not drawn without it.");

        VerifyArOverlayCapturedFrames(renderer);
        Results.Add($"PASS ar keys: three held keys are five rectangles, each tinted where it is in the picture and nothing around it, black keys beside a held white key left alone; read back against the same frame without the layer.");
        Results.Add($"PASS ar notes: 4 of 5 notes of a song projected onto the sample keyboard and read back, inside and just outside their quads; Gem/Crystal selections keep the flat-quad fallback; paused, zero opacity, no layer and a camera of another size draw nothing.");
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
        GpuStageFeed MakeFeed(PianoVisualSettings settings, bool overCamera, bool sounding = true)
        {
            var feed = new GpuStageFeed();
            feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(settings, (pitch, track) => Color.FromRgb(255, 80, 220), .205, overCamera: overCamera));
            IReadOnlyList<NoteEvent> stateNotes = sounding ? notes : Array.Empty<NoteEvent>();
            IReadOnlySet<int> pressed = sounding ? held : new HashSet<int>();
            feed.SetDeterministicState(stateNotes, sounding ? 1 : 0, sounding, pressed);
            feed.Camera = camera;
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
                && !overCameraLook.ShowEmbers && !overCameraLook.ShowWisps && !overCameraLook.ShowFlame
                && !overCameraLook.ShowImpactRings && !overCameraLook.ShowImpactFlash && !overCameraLook.ShowLightBeams
                && !overCameraLook.HoldElectricArc && overCameraLook.ImpactWave == "None" && overCameraLook.ImpactMorph == "None"
                && overCameraLook.ReleaseEffect == "Fade" && overCameraLook.ReleaseIntensity == 0,
            "The AR look must disable note/key effects before simulation and mark the camera composition for transient suppression.");
        GpuStageFeed BusyFeed()
        {
            var feed = MakeFeed(busy, overCamera: false);
            feed.Impact(60, 1);
            feed.LiveNote(60, true, 1);
            feed.LiveNote(60, false, 1);
            return feed;
        }
        var stageImage = Capture(BusyFeed());
        Assert(ChangedPixels(background, stageImage, threshold: 8) > 30,
            "The busy fixture must visibly contain stage notes, keys and transients before its camera overlay is enabled.");
        var transitioning = BusyFeed();
        var hidden = Capture(transitioning, frame =>
        {
            if (frame == 1) transitioning.SetLook(overCameraLook);
        });
        Assert(background.AsSpan().SequenceEqual(hidden),
            "The AR camera composite must hide active stage notes, keys and transients that were already in flight when the overlay turned on.");
        Results.Add("PASS AR over-camera captured frames: WARP readback shows all six camera-composited ambient families, omits backdrop stars and key beams, and hides notes/keys plus in-flight impact, particle, hold and release effects when AR turns on.");
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
    /// camera; keeps Done shut on corners that cannot be used; saves nothing when cancelled or when the camera changed behind
    /// it; and draws the picture, the corners and the grid of keys where the wizard says they are, read back from a rendering.
    /// </summary>
    private static void VerifyCalibrationWindow(MainWindow window, PianoVisualSettings visualSettings)
    {
        const int W = 1280, H = 720;
        var ring = (CameraFrameRing)Field(window, "_cameraFrames");
        var mirrorWas = visualSettings.CameraMirror;
        var viewWas = visualSettings.ArViewProfileId;
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
                w.OrientationBox.SelectedValue = upright;
                Assert(w.Wizard.Orientation == upright && w.DoneButton.IsEnabled && w.Wizard.Step == WizardStep.Adjust, "Choosing the first orientation again should bring the first reading back.");
                w.Accept();
                return w.Result is not null;
            });
            Assert(opened == before + 1, "A camera with a picture of the right size should open the window once.");
            Assert(window.ArCalibration.State == ArCalibrationState.Ready && SameCorners(window.ArCalibration.Current?.Corners, truth),
                $"Four clicks and Done should calibrate this camera with the corners that were marked on the picture as delivered (mirror {mirror}).");
            Assert(SameCorners(window.ArCalibration.Store.Get("verification camera", view, W, H, keyCount), truth),
                "The calibration should be stored for this camera, view, picture size and keyboard size.");
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
        Invoke(window, "UpdateArFeed");
        Results.Add("PASS calibration window: refuses without a camera, picture or the right size; the picture as the stage shows it; four clicks to a saved calibration, mirrored or not; Done shut on bad corners; New picture; nothing saved when cancelled, when the camera changed behind it, or when the view of the camera did; each view keeps its own key area and an unmarked view asks to be marked.");
    }

    /// <summary>
    /// AR-S4: the scene→image map the stage's own note and sprite passes are to be drawn through
    /// (docs/AR-OVERLAY-ROADMAP.md §12, §14.10). The unit tests hold the map to <c>ArProjection</c>; this holds it to
    /// the scene layout the renderer really builds — <c>GpuSceneLayout</c> lives on the far side of the WPF line and
    /// cannot be linked into the test project, so the one thing that could silently break the map (a scene whose key
    /// positions are not the keyboard's normalised ones, or a hit line that is not the back edge of the keys) is
    /// checked here, on the layout the GPU stage is handed.
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
        Results.Add("PASS AR scene projection (AR-S4): the scene-to-image map agrees with ArProjection on the scene layout the renderer builds, at three output sizes, mirrored and not.");
    }

    /// <summary>
    /// AR-S4b, the scene→image map on the GPU (docs/AR-OVERLAY-ROADMAP.md §14.10): the stage's own note pass draws
    /// through it over the camera picture, instead of the AR layer keeping flat note quads of its own. Read back on
    /// WARP and checked twice over. Switched off — the default — the frame must not change by a single pixel even
    /// with the whole map sitting in the frame constants beside the ones the stage already reads. Switched on, every
    /// note must land inside the patch <see cref="ArProjection.TryProject"/> reports for its own rectangle, with the
    /// sample keyboard and with the picture mirrored, and the min-scale floor the flags float4 carries must reach
    /// the shader without moving the note.
    /// </summary>
    private static void VerifyArNoteProjection()
    {
        const int width = 1280, height = 720;
        // Solid notes with halo, bloom and every side effect off: what a frame can hold is the note body and the
        // camera's grey, so the patch either holds note pixels or it does not and there is no glow to argue about.
        var settings = new PianoVisualSettings
        {
            ShowNotes = true, NoteStyle = "Solid", NoteWidth = 100, NoteGlow = 0, NoteHeadGlow = 0, NoteRoundness = 0,
            FallingPulse = false, FallingTrail = "None", FallingGhost = false, NoteShimmer = false, NoteLandingGlow = false,
            HoldBar = false, HoldBreath = false, HoldVibration = false, HoldColorCycle = false, HoldElectricArc = false,
            ShowEmbers = false, ShowWisps = false, ShowFlame = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowKeys = false, ShowHalo = false, ShowBackground = false, ShowStars = false, ShowLightBeams = false, ShowPetals = false,
            AmbientEnergy = "None", AmbientNature = "None", AmbientLight = "None", AmbientCosmic = "None",
            BloomIntensity = 0, HorizonGlow = 0, Vignette = 0, ShootingStars = false
        };
        var notes = new List<NoteEvent>
        {
            new() { Pitch = 60, Start = 10.08, Duration = .18 },   // white key, hanging just above the keys
            new() { Pitch = 64, Start = 10.2, Duration = .2 },     // two white keys over: bodies may not touch
            new() { Pitch = 67, Start = 10.32, Duration = .22 }    // white key, highest of the three
        };
        // The starts sit the notes in the air the projection really magnifies (SongFallSpeed puts a note
        // 0.6 s out at v ~ -1.7 and 1.35 s out past the top of the frame): each body must lie wholly on the
        // output, or its patch samples cannot be asked to show anything. The pitches stay two white keys
        // apart because the bodies are a lane wide and a black key sits only ~10 px from its white
        // neighbours - touching bodies would put a neighbour under the outside samples of the patch.
        // the very layout the render loop builds from this feed, so the rectangles below are the drawn ones
        var scene = new GpuSceneLayout(width, height, .205f);
        var frame = new ArSceneFrame(scene.Width, scene.HitY, scene.KeyboardHeight);
        var corners = new (double X, double Y)[] { (70, 292), (1214, 268), (1236, 421), (52, 449) };
        var calibration = ArCalibration.Create(scene.Keys, corners, 1280, 720, out var problem)
            ?? throw new InvalidOperationException($"the sample corners were refused: {problem}");
        // a grey camera picture: the note is the only colour a frame can hold
        var ring = new CameraFrameRing();
        var grey = ring.BeginWrite(1280 * 720 * 4, out var slot);
        Array.Fill(grey, (byte)90, 0, 1280 * 720 * 4);
        for (var i = 3; i < grey.Length; i += 4) grey[i] = 255;
        ring.Publish(slot, 1280, 720, Stopwatch.GetTimestamp());
        GpuStageFeed MakeFeed(bool mirror, GpuArInput? ar)
        {
            var feed = new GpuStageFeed();
            feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(settings, (_, _) => Color.FromRgb(255, 80, 220), .205));
            feed.SetDeterministicState(notes, 10, true, new HashSet<int>());
            feed.SetDeterministicBeatPulse(0);
            feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, mirror, 1f);
            feed.Ar = ar;
            return feed;
        }
        // where (s, t) of a patch lands on the output, blended in perspective the way the rasteriser blends the quad
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
        // what a pixel says: colour spread and distance from the camera's grey, or null for a point off the output
        static (int Spread, int Off)? Look(byte[] pixels, int w, int h, (double X, double Y) at)
        {
            if (!(at.X >= 0 && at.Y >= 0 && at.X < w && at.Y < h)) return null;
            var i = ((int)Math.Floor(at.Y) * w + (int)Math.Floor(at.X)) * 4;
            int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
            return (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)),
                Math.Max(Math.Abs(red - 90), Math.Max(Math.Abs(green - 90), Math.Abs(blue - 90))));
        }
        static int NotePixels(byte[] pixels)
        {
            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (Math.Max(Math.Abs(pixels[i] - 90), Math.Max(Math.Abs(pixels[i + 1] - 90), Math.Abs(pixels[i + 2] - 90))) > 40) count++;
            return count;
        }

        using var renderer = GpuStageRenderer.Create(forceWarp: true);
        byte[] Render(GpuStageFeed feed) => GpuRenderLoop.RenderFrames(renderer, feed, width, height, 0, 1 / 60.0, false, out _);

        // ---- default off: the map in the constants must not move one pixel ------------------------------
        var withoutAr = Render(MakeFeed(false, null));
        var mapInConstants = Render(MakeFeed(false, new GpuArInput(calibration, ArNoteStyle.Default, 0f)));
        Assert(withoutAr.AsSpan().SequenceEqual(mapInConstants),
            "AR-S4b default off: with the whole scene→image map in the frame constants (the AR layer at zero opacity, the note branch's flag down) the frame must be byte-identical to one with no AR input at all — not one pixel may change.");
        var defaultNotes = NotePixels(withoutAr);
        Assert(defaultNotes > 500, $"The default frame must still show its notes over the picture before any of this proves anything ({defaultNotes} note pixels).");

        // ---- on: every note lands inside the patch ArProjection.TryProject reports for its own rectangle ----
        foreach (var mirror in new[] { false, true })
        {
            var feed = MakeFeed(mirror, new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0, ShowNotes: true, ProjectedNotes: true));
            // the rectangles the note pass is handed, rebuilt on the CPU from the same deterministic input the frame is drawn from
            var replica = new GpuStageSimulation(20260930);
            var input = new GpuFrameInput();
            feed.Capture(input, GpuStageFeed.Now);
            replica.Step(1 / 60.0, input, feed, scene.Width);
            var rects = new GpuInstanceList<GpuNoteInstance>(32);
            replica.BuildNotes(input, scene, rects);
            Assert(rects.Count == notes.Count, $"Every note of the fixture should reach the note pass ({rects.Count} of {notes.Count}).");
            var drawn = Render(feed);
            Assert(renderer.ArQuadCount == 0,
                "With the note pass drawing through the map, the AR layer keeps no flat note or bar quad of its own.");
            Assert(NotePixels(drawn) > 500, $"The projected notes must be drawn ({NotePixels(drawn)} note pixels, mirrored {mirror}).");
            var view = new ArView(1280, 720, width, height, PictureFit.Fit, mirror);
            foreach (var rect in rects.Span)
            {
                var x0 = rect.Rect.X; var x1 = rect.Rect.X + rect.Rect.Z;
                var y0 = rect.Rect.Y; var y1 = rect.Rect.Y + rect.Rect.W;
                Assert(ArSceneProjection.TryProjectRect(calibration, view, frame, x0, x1, y0, y1, out var patch, out _, out _),
                    $"The note rectangle [{x0:0},{y0:0}]–[{x1:0},{y1:0}] must project to a patch.");
                var inside = 0;
                foreach (var (s, t) in new[] { (.5, .5), (.35, .5), (.65, .5), (.5, .3), (.5, .7), (.5, .2), (.5, .8) })
                {
                    if (Look(drawn, width, height, Blend(patch, s, t)) is not { } pixel) continue;
                    inside++;
                    Assert(pixel.Spread > 30 && pixel.Off > 40,
                        $"A note drawn through the map must fill the patch ArProjection.TryProject reports for its rectangle [{x0:0},{y0:0}]–[{x1:0},{y1:0}]: at ({s:0.00},{t:0.00}) of the patch the frame should hold the note colour (spread {pixel.Spread}, off grey {pixel.Off}, mirrored {mirror}).");
                }
                Assert(inside >= 4, $"At least four sample points of the patch of [{x0:0},{y0:0}]–[{x1:0},{y1:0}] should be on the output ({inside}, mirrored {mirror}).");
                var outside = 0;
                foreach (var (s, t) in new[] { (-.35, .5), (1.35, .5), (.5, -.35), (-.35, .9), (1.35, .9) })
                {
                    if (Look(drawn, width, height, Blend(patch, s, t)) is not { } pixel) continue;
                    outside++;
                    Assert(pixel.Off <= 4 && pixel.Spread <= 4,
                        $"Just outside the patch of [{x0:0},{y0:0}]–[{x1:0},{y1:0}] ({s:0.00},{t:0.00}) the camera's grey must show — the note may not spill past the projection of its own rectangle (off by {pixel.Off}, mirrored {mirror}).");
                }
                Assert(outside >= 1, $"At least one outside sample should be on the output ({outside}, mirrored {mirror}).");
            }
        }

        // ---- the min-scale floor the flags float4 carries: it reaches the shader and moves nothing ------
        var plain = Render(MakeFeed(false, new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0, ShowNotes: true, ProjectedNotes: true)));
        var floored = Render(MakeFeed(false, new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0, ShowNotes: true, ProjectedNotes: true, MinScalePx: 24)));
        Assert(!plain.AsSpan().SequenceEqual(floored),
            "A non-zero min-scale floor must reach the note pass and change how the SDF measures (a floor the shader ignored would leave the frame byte-identical).");
        // the body may not move under the floor: the same patches must still hold the note, inside and out
        var viewPlain = new ArView(1280, 720, width, height, PictureFit.Fit, false);
        var replicaFloored = new GpuStageSimulation(20260930);
        var inputFloored = new GpuFrameInput();
        var feedFloored = MakeFeed(false, new GpuArInput(calibration, ArNoteStyle.Default, 1f, KeyLight: 0, ShowNotes: true, ProjectedNotes: true, MinScalePx: 24));
        feedFloored.Capture(inputFloored, GpuStageFeed.Now);
        replicaFloored.Step(1 / 60.0, inputFloored, feedFloored, scene.Width);
        var rectsFloored = new GpuInstanceList<GpuNoteInstance>(32);
        replicaFloored.BuildNotes(inputFloored, scene, rectsFloored);
        foreach (var rect in rectsFloored.Span)
        {
            Assert(ArSceneProjection.TryProjectRect(calibration, viewPlain, frame, rect.Rect.X, rect.Rect.X + rect.Rect.Z, rect.Rect.Y, rect.Rect.Y + rect.Rect.W, out var patch, out _, out _),
                "The floored frame draws the same rectangles, which must project to the same patches.");
            if (Look(floored, width, height, Blend(patch, .5, .5)) is { } pixel)
                Assert(pixel.Spread > 30 && pixel.Off > 40,
                    $"The min-scale floor may change how the SDF measures but not where the note is: its patch centre must still hold the note colour (spread {pixel.Spread}, off grey {pixel.Off}).");
            if (Look(floored, width, height, Blend(patch, 1.35, .5)) is { } outside)
                Assert(outside.Off <= 4 && outside.Spread <= 4,
                    $"Under the min-scale floor the note may not spill past its patch either (off by {outside.Off}).");
        }

        Results.Add($"PASS AR note projection (AR-S4b): default off the {width}×{height} frame is byte-identical with the whole map in the constants and without any AR input ({defaultNotes} note pixels unchanged); switched on, 3 notes on the sample keyboard land inside the ArProjection patches of their own rectangles, mirrored and not, with no flat AR note quads of their own; min-scale floor reaches the shader without moving them.");
    }
}
