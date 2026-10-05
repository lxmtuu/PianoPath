using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            feed.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, true, delay);
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
            feedKeys.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, true, 0f);
            PublishGrey();
            feedKeys.Recording = tap;
            var wait = Stopwatch.StartNew();
            while ((tap.Serial < 8 || Grey() > 3) && loop.Error is null && wait.Elapsed.TotalSeconds < 30) Thread.Sleep(10);
            Assert(loop.Error is null && tap.Serial >= 8 && Grey() <= 3, $"Before the key is pressed its place in the picture is the camera's grey ({loop.Error}).");
            feedKeys.Ar = new GpuArInput(calibration!, ArNoteStyle.Default, 1f, .6f, true, delay);
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
    /// AR-6.2b, the stage's own notes drawn through the scene-to-picture map: with the overlay on it is the stage's note pass
    /// that draws, so a note lands where <see cref="ArSceneMap"/> puts it and nowhere else, as wide as the map makes its own
    /// scene width, with its NoteStyle and its glow reaching it because it is the very shader that drew them before a camera was
    /// ever involved, and a note that runs past the map's horizon is cut there instead of smeared over the picture. Read back
    /// from frames rendered on WARP.
    /// <para>
    /// One thing these pixels cannot say is whether <c>PsNote</c> anti-aliases the note's edge against the map's own local scale
    /// (<see cref="ArSceneMap.Scale"/>) rather than the stage's <c>SceneSize.z</c>. The composite lays the camera picture under
    /// the light and the sRGB encode is steep near black, so a one-pixel ramp and a five-pixel one land on nearly the same byte
    /// values and any threshold between them would be a guess dressed up as a check. That the vertex shader works the scale out
    /// and hands it to the pixel shader is held to <see cref="ArSceneMap"/> arithmetically instead, in ArSceneConstantsTests.
    /// The corner radius is left out for a plain reason: at any width a note is drawn here, roundness 0 and roundness 100 differ
    /// by less than a pixel of this output, and it travels to the same shader in the same constant buffer as the NoteStyle the
    /// check below does read back.
    /// </para>
    /// </summary>
    private static void VerifyArSceneNotes()
    {
        const int cw = 1280, ch = 720;         // the camera picture
        const int width = 640, height = 360;   // the output the notes are read at
        var started = Stopwatch.StartNew();
        Assert(ReferenceEquals(KeyboardLayout.Active, KeyboardLayout.Standard88), "The scene notes are drawn over the full keyboard.");
        var problem = CalibrationProblem.None;
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], cw, ch, out problem)
            ?? throw new InvalidOperationException($"The sample corners were refused: {problem}");

        // Two notes sounding at the position below, so they hang over the keys, and one still falling, so it lies above them.
        // Every setting that would put a second shape on the same note is off, so one pixel says one thing.
        var song = new List<NoteEvent>
        {
            new() { Pitch = 60, Start = 10, Duration = .3 },
            new() { Pitch = 64, Start = 10, Duration = .3 },
            new() { Pitch = 67, Start = 10.3, Duration = .3 },
        };
        const double position = 10.1;
        var quiet = new PianoVisualSettings
        {
            BackgroundGradient = false, ShowStars = false, ShowHalo = false, ShowKeys = false, ShowNotes = true, ShowEmbers = false,
            ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false, ShowLightBeams = false,
            ShowPetals = false, HorizonGlow = 0, Vignette = 0, BloomIntensity = 0, BloomSize = 0, Notes3D = false,
            ShowNoteLabels = false, NoteLandingGlow = false, FallingGhost = false, FallingTrail = "None", NoteShimmer = false,
            FallingPulse = false, HoldBar = false, HoldBreath = false, HoldVibration = false, HoldElectricArc = false,
            VelocityColor = false, HaloPulse = false, ShootingStars = false, NoteStyle = "Solid", NoteWidth = 100, NoteHeadGlow = 0,
        };

        // A flat picture, so "the picture, untouched" is a level and not a guess. Grey says it for the places a note must not
        // be; black is used where the note's own light has to be the only thing in the frame, because the picture showing
        // through a note's edge compresses the very differences those checks are about. One ring, published again for each
        // frame: two rings would both be on sequence 1 and the GPU would keep the first picture for the second.
        var ring = new CameraFrameRing();
        void Publish(byte level)
        {
            var buffer = ring.BeginWrite(cw * ch * 4, out var slot);
            Array.Fill(buffer, level, 0, cw * ch * 4);
            for (var i = 3; i < cw * ch * 4; i += 4) buffer[i] = 255;
            ring.Publish(slot, cw, ch, Stopwatch.GetTimestamp());
        }

        GpuLook LookOf(PianoVisualSettings settings) => GpuLook.From(settings, (pitch, track) => Color.FromRgb(255, 80, 220), .205);
        // The scene the render loop lays out for this output (its height is the scene height, so the width is the output's own)
        // and the notes the renderer's own call builds in it, so the checks hold the pixels to the notes the frame was made
        // from rather than to a second model of them.
        var layout = new GpuSceneLayout(height * width / (float)height, height, .205f);
        GpuInstanceList<GpuNoteInstance> NotesOf(GpuLook look)
        {
            var input = new GpuFrameInput { Notes = song, MaxNoteDuration = .3, Position = position, Playing = true, Look = look };
            var notes = new GpuInstanceList<GpuNoteInstance>(16);
            new GpuStageSimulation().BuildNotes(input, layout, notes);
            return notes;
        }
        byte[] Draw(GpuStageRenderer renderer, PianoVisualSettings settings, byte picture, ArCalibration? keys)
        {
            Publish(picture);
            var feed = new GpuStageFeed();
            feed.SetStageHeight(height);
            feed.SetLook(LookOf(settings));
            feed.SetState(song, position, true, new HashSet<int>());
            feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, false, 1f);
            // The AR layer is told to draw neither notes nor lit keys, so every note pixel below comes from the stage's own
            // note pass and nothing of the layer's own can sit under a probe. (The keys would light by themselves: the
            // simulation answers the song, and two of these three notes are sounding.)
            if (keys is not null) feed.Ar = new GpuArInput(keys, ArNoteStyle.Default, 1f, KeyLight: 0f, ShowNotes: false);
            return GpuRenderLoop.RenderFrames(renderer, feed, width, height, 0, 1 / 60.0, false, out _);
        }
        byte[] DrawBare(GpuStageRenderer renderer, PianoVisualSettings settings)
        {
            var feed = new GpuStageFeed();
            feed.SetStageHeight(height);
            feed.SetLook(LookOf(settings));
            feed.SetState(song, position, true, new HashSet<int>());
            return GpuRenderLoop.RenderFrames(renderer, feed, width, height, 0, 1 / 60.0, false, out _);
        }
        (int R, int G, int B) At(byte[] pixels, double x, double y)
        {
            var px = (int)Math.Clamp(Math.Round(x), 0, width - 1);
            var py = (int)Math.Clamp(Math.Round(y), 0, height - 1);
            return (pixels[(py * width + px) * 4 + 2], pixels[(py * width + px) * 4 + 1], pixels[(py * width + px) * 4]);
        }
        int Level(byte[] pixels, double x, double y) { var c = At(pixels, x, y); return Math.Max(c.R, Math.Max(c.G, c.B)); }
        int Spread(byte[] pixels, double x, double y) { var c = At(pixels, x, y); return Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)); }
        // how far a pixel is from the flat grey picture: 0 where nothing was drawn over it
        int Off(byte[] pixels, double x, double y) { var c = At(pixels, x, y); return Math.Max(Math.Abs(c.R - 90), Math.Max(Math.Abs(c.G - 90), Math.Abs(c.B - 90))); }

        using var renderer = GpuStageRenderer.Create(forceWarp: true);
        var look = LookOf(quiet);
        var notes = NotesOf(look);
        Assert(notes.Count == 3, $"Three notes should be built for this song at {position:0.00} s, got {notes.Count}.");

        // ---- where the notes land -------------------------------------------------------------------
        var straight = Draw(renderer, quiet, 90, calibration);
        Assert(renderer.ArScene.Valid, "The renderer should hold a scene map for the output it drew.");
        Assert(renderer.ArQuadCount == 0 && renderer.ArKeyQuadCount == 0,
            $"With neither notes nor lit keys asked of it, the AR layer should draw nothing; it drew {renderer.ArQuadCount} quads.");
        Assert(renderer.CameraTexture is { Uploads: > 0 },
            $"The camera picture should reach the GPU ({renderer.CameraTexture?.Uploads ?? 0} uploads).");
        // Held on to across the rest of the checks: the frames below are drawn from the same output and the same settings, so
        // this is the map every one of them used.
        var map = renderer.ArScene;
        Assert(Math.Abs(map.SceneWidth - layout.Width) < .01 && Math.Abs(map.HitY - layout.HitY) < .01,
            $"The map must be the one for this output's own scene, and is {map.SceneWidth:0.0} wide with its hit line at {map.HitY:0.0}.");
        var placed = new List<(double X, double Y)>();
        for (var i = 0; i < notes.Count; i++)
        {
            var rect = notes.Span[i].Rect;
            double cx = rect.X + rect.Z / 2, cy = rect.Y + rect.W / 2;
            Assert(map.TryProject(cx, cy, out var outX, out var outY, out var weight), $"Note {i} should project onto the picture.");
            Assert(weight >= ArProjection.MinW, $"Note {i}'s weight should be one the rasteriser can divide by ({weight:0.000}).");
            Assert(Spread(straight, outX, outY) > 30,
                $"Note {i} should be drawn where the scene map puts it ({outX:0},{outY:0}), and its spread there is {Spread(straight, outX, outY)}.");
            // At this output the stage's own transform is the identity (zoom 1, no offset, scene height = output height), so the
            // stage's own place for the note is its scene place - and the whole point of AR-6.2b is that nothing is drawn there.
            Assert(Off(straight, cx, cy) <= 6,
                $"Note {i} must not be drawn at the stage's own place any more ({cx:0},{cy:0}), which reads {Off(straight, cx, cy)} off the picture.");
            placed.Add((outX, outY));
        }

        // ---- and where they land with no camera at all: the stage's own place, exactly as before -----
        var bare = DrawBare(renderer, quiet);
        Assert(!renderer.ArScene.Valid, "With no camera picture there is no scene map, so the note pass must draw the stage's own way.");
        for (var i = 0; i < notes.Count; i++)
        {
            var rect = notes.Span[i].Rect;
            double cx = rect.X + rect.Z / 2, cy = rect.Y + rect.W / 2;
            Assert(Spread(bare, cx, cy) > 30, $"With no camera, note {i} should still be drawn at the stage's own place ({cx:0},{cy:0}).");
            Assert(Level(bare, placed[i].X, placed[i].Y) <= 6,
                $"With no camera, nothing should be drawn at note {i}'s place on the keys ({placed[i].X:0},{placed[i].Y:0}); it reads {Level(bare, placed[i].X, placed[i].Y)}.");
        }

        // ---- the stage's own note shader reaches the notes on the keys -------------------------------
        var styles = new List<(string Name, byte[] Pixels)>();
        foreach (var name in PianoVisualSettings.NoteStyles)
        {
            var settings = quiet.Clone(); settings.NoteStyle = name;
            styles.Add((name, Draw(renderer, settings, 0, calibration)));
        }
        var leastStyleDifference = int.MaxValue;
        for (var i = 0; i < styles.Count; i++)
            for (var j = i + 1; j < styles.Count; j++)
            {
                // summed over the two notes that hang over the keys, so one style's own palette cannot carry the check
                var difference = 0;
                foreach (var (x, y) in placed.Take(2))
                    for (var dy = -12; dy <= 12; dy++)
                        for (var dx = -6; dx <= 6; dx++)
                        {
                            var a = At(styles[i].Pixels, x + dx, y + dy);
                            var b = At(styles[j].Pixels, x + dx, y + dy);
                            difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                        }
                leastStyleDifference = Math.Min(leastStyleDifference, difference);
                Assert(difference > 400,
                    $"{styles[i].Name} and {styles[j].Name} notes must not look the same once they are drawn on the keys (they differ by {difference}).");
            }

        // ---- the note's glow reaches the picture beside it -------------------------------------------
        // NoteGlow is read through BloomIntensity, so both frames bloom alike and the only difference between them is the halo
        // the note sheds. NoteEdge is 0 to keep the body under the bloom threshold, so the body cannot shed any of its own.
        var unlit = quiet.Clone(); unlit.NoteGlow = 0; unlit.NoteEdge = 0; unlit.BloomIntensity = 65;
        var lit = unlit.Clone(); lit.NoteGlow = 100;
        var unlitFrame = Draw(renderer, unlit, 0, calibration);
        var litFrame = Draw(renderer, lit, 0, calibration);
        var first = notes.Span[0].Rect;
        // two scene pixels to the left of the note's own left edge: outside it, well inside the quad the vertex shader builds
        // for it (which reaches NoteA.w + NoteA.z + 2 scene px out), and far enough out that rounding to a pixel cannot land
        // the probe on the note itself
        Assert(map.TryProject(first.X - 2, first.Y + first.W / 2, out var glowX, out var glowY, out _),
            "The pixel beside the note should project onto the picture.");
        Assert(Level(litFrame, glowX, glowY) > Level(unlitFrame, glowX, glowY) + 40 && Level(unlitFrame, glowX, glowY) <= 30,
            $"A note's glow should light the picture beside it ({glowX:0},{glowY:0}): {Level(litFrame, glowX, glowY)} with glow against {Level(unlitFrame, glowX, glowY)} without.");

        // ---- and its own width, as wide as the map makes it ------------------------------------------
        // Measured across the Solid note at its middle height: the lit span has to be the note's width in scene pixels times
        // what the map makes of it there. One projected point says where a note is; this says how big it is, which is the
        // other half of the projection being right rather than a guess that landed near the middle.
        // one Assert per edge: an && would short-circuit, and the compiler would be right not to trust that the second
        // edge's numbers were ever written
        Assert(map.TryProject(first.X, first.Y + first.W / 2, out var leftX, out var leftY, out _),
            "The note's left edge should project onto the picture.");
        Assert(map.TryProject(first.X + first.Z, first.Y + first.W / 2, out var rightX, out var rightY, out _),
            "The note's right edge should project onto the picture.");
        var expected = rightX - leftX;
        var row = (leftY + rightY) / 2;
        var litSpan = 0;
        for (var x = (int)Math.Floor(leftX) - 4; x <= (int)Math.Ceiling(rightX) + 4; x++)
            if (Level(styles[0].Pixels, x, row) > 40) litSpan++;
        Assert(expected > 3 && Math.Abs(litSpan - expected) <= 2.5,
            $"The note should be as wide on the keys as the map makes its {first.Z:0.0} scene pixels: {expected:0.0} px from corner to corner, {litSpan} px lit.");

        // ---- a note that runs past the horizon is cut, not smeared -------------------------------------
        // These corners widen towards the player, so this map's horizon is half a key depth behind the keys and the CPU refuses
        // the top of every note in the song. The clip w the vertex shader hands the rasteriser is what cuts the quad there, so
        // the picture over the keys and in front of them must stay untouched: a NaN or an infinity would smear it.
        var folded = ArCalibration.Create(KeyboardLayout.Standard88, [(80, 100), (1200, 100), (800, 600), (400, 600)], cw, ch, out problem)
            ?? throw new InvalidOperationException($"The folding corners were refused: {problem}");
        var foldedFrame = Draw(renderer, quiet, 90, folded);
        var foldedMap = renderer.ArScene;
        Assert(foldedMap.Valid, "The folding calibration should still give a usable map at the keys.");
        Assert(!foldedMap.TryProject(first.X + first.Z / 2, first.Y, out _, out _, out _),
            "The top of the note should be past this map's horizon, so the CPU refuses it.");
        Assert(foldedMap.TryProject(first.X + first.Z / 2, first.Y + first.W, out _, out var bottomY, out _),
            "The bottom of the note is on the keys, so the CPU still answers for it.");
        var smeared = 0;
        for (var y = (int)bottomY + 40; y < height; y++)
            for (var x = 0; x < width; x += 4)
                if (Off(foldedFrame, x, y) > 6) smeared++;
        Assert(smeared == 0, $"Nothing may be drawn over the keys once the notes run past the horizon; {smeared} pixels were.");
        var drawn = 0;
        for (var y = 0; y <= (int)bottomY; y++)
            for (var x = 0; x < width; x += 2)
                if (Spread(foldedFrame, x, y) > 25) drawn++;
        Assert(drawn > 40, $"The part of the notes this side of the horizon should still be drawn; {drawn} pixels were.");

        Results.Add($"PASS ar scene notes: {notes.Count} notes drawn by the stage's own pass through the scene map " +
                    $"(the first at {placed[0].X:0},{placed[0].Y:0} instead of the stage's {notes.Span[0].Rect.X + notes.Span[0].Rect.Z / 2:0},{notes.Span[0].Rect.Y + notes.Span[0].Rect.W / 2:0}), " +
                    $"all {PianoVisualSettings.NoteStyles.Length} note styles apart by at least {leastStyleDifference}, {litSpan} px lit where the map puts {expected:0.0} px of note, " +
                    $"the glow reaching {Level(litFrame, glowX, glowY)} beside it, and a note past the horizon cut with {drawn} px still drawn and nothing over the keys, " +
                    $"in {started.Elapsed.TotalSeconds:0.0} s on {renderer.AdapterName}.");
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
        var keyCount = visualSettings.KeyboardLayoutChoice.KeyCount;
        (double X, double Y)[] truth = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
        (double X, double Y)[] flat = [(100, 300), (400, 300), (700, 300), (1000, 300)];

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
            Assert(SameCorners(window.ArCalibration.Store.Get("verification camera", W, H, keyCount), truth), "The calibration should be stored for this camera, picture size and keyboard size.");
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
                && window.ArCalibration.Store.Get("verification camera", W, H, keyCount) is null && window.ArCalibration.Store.Get("another camera", W, H, keyCount) is null,
            "Corners marked for one camera must not be saved for another that replaced it while the window was open.");

        // ---- leave everything as it was ----
        Invoke(window, "ApplyArSource", "verification camera", W, H);
        window.ArCalibration.Reset();
        Invoke(window, "ApplyArSource", "another camera", W, H);
        window.ArCalibration.Reset();
        Invoke(window, "ApplyArSource", null!, 0, 0);
        ring.Clear();
        visualSettings.CameraMirror = mirrorWas;
        Invoke(window, "UpdateArFeed");
        Results.Add("PASS calibration window: refuses without a camera, picture or the right size; the picture as the stage shows it; four clicks to a saved calibration, mirrored or not; Done shut on bad corners; New picture; nothing saved when cancelled or when the camera changed behind it.");
    }
}
