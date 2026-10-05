using System.Diagnostics;
using System.Windows.Media;

namespace PianoPath;

internal static partial class VerificationSuite
{
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
        GpuStageFeed MakeFeed(GpuCameraInput? camera, bool chroma = false)
        {
            var settings = quiet.Clone(); if (chroma) settings.BackgroundMode = "ChromaGreen";
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
        GpuStageFeed MakeArFeed(GpuCameraInput? camera, GpuArInput? ar, bool playing = true)
        {
            var feed = MakeFeed(camera);
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
        Render(MakeArFeed(flatInput, new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, ShowNotes: false)));
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

        Render(MakeArFeed(flatInput, null));
        Assert(renderer.ArQuadCount == 0, "Without an AR layer there are no AR quads.");
        var smallRing = new CameraFrameRing(); PublishGradient(smallRing, 160, 90);
        Render(MakeArFeed(new GpuCameraInput(smallRing, PictureFit.Fit, false, 1f), arInput));
        Assert(renderer.ArQuadCount == 0, "A camera that is not the size the calibration was made at must hide the AR layer, not draw notes in the wrong place.");
        Render(MakeArFeed(null, arInput));
        Assert(renderer.ArQuadCount == 0, "The AR layer lies on the camera picture and is not drawn without it.");

        Results.Add($"PASS ar keys: three held keys are five rectangles, each tinted where it is in the picture and nothing around it, black keys beside a held white key left alone; read back against the same frame without the layer.");
        Results.Add($"PASS ar notes: 4 of 5 notes of a song projected onto the sample keyboard and read back, inside and just outside their quads; paused, zero opacity, no layer and a camera of another size draw nothing.");
        Results.Add($"PASS ar camera layer: one texture reused across frames and re-created once for a new size, bad frames refused; fit, mirror, half opacity, bars and crop read back from a {width}×{height} frame on {renderer.AdapterName} in {started.Elapsed.TotalSeconds:0.0} s.");
    }
}
