using System.Text.Json;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The whole way a calibration is made, on a picture of a keyboard whose key positions are known: the picture goes through the camera's
/// ring, the window takes a still of it, the corners are clicked and refined with the mouse in a window of some size (turned round or not),
/// Done makes the calibration, and the session saves it and finds it again in a fresh run. At the end the keys the calibration gives lie on
/// the keys of the picture — read from the picture's own pixels, not from the arithmetic that drew them.
/// </summary>
public sealed class CalibrationFromPictureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ar-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    public static TheoryData<int, bool, int> Cases
    {
        get
        {
            var data = new TheoryData<int, bool, int>();
            var viewport = 0;
            foreach (var keys in new[] { 88, 76, 61, 49 })
                foreach (var mirror in new[] { false, true })
                    data.Add(keys, mirror, viewport++ % 3);
            return data;
        }
    }

    private static readonly (double Width, double Height)[] Viewports = [(1000, 600), (640, 480), (1920, 1080)];

    /// <summary>The camera thread's work: a picture as the camera delivers it, into the ring, turned round first when it would have been.</summary>
    private static CameraFrameRing Camera(byte[] rgb, int width, int height, bool threadMirrors, out byte[] delivered)
    {
        delivered = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            delivered[i * 4] = rgb[i * 3 + 2]; delivered[i * 4 + 1] = rgb[i * 3 + 1]; delivered[i * 4 + 2] = rgb[i * 3]; delivered[i * 4 + 3] = 255;
        }
        var inRing = (byte[])delivered.Clone();
        if (threadMirrors) CalibrationPicture.FlipRows(inRing, width, height);
        var ring = new CameraFrameRing();
        var buffer = ring.BeginWrite(inRing.Length, out var slot);
        Buffer.BlockCopy(inRing, 0, buffer, 0, inRing.Length);
        ring.Publish(slot, width, height, 1);
        return ring;
    }

    private static (double X, double Y) Screen(CalibrationWizard wizard, (double X, double Y) point) => wizard.View.ToOutput(point.X, point.Y);

    /// <summary>A person's clicks: each corner a little off (up to <paramref name="jitter"/> window pixels), in a fixed pattern.</summary>
    private static void ClickCorners(CalibrationWizard wizard, (double X, double Y)[] truth, double jitter)
    {
        (double X, double Y)[] offsets = [(1, -1), (-1, 1), (1, 1), (-1, -1)];
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = Screen(wizard, truth[i]);
            Assert.True(wizard.PointerDown(x + offsets[i].X * jitter, y + offsets[i].Y * jitter));
            wizard.PointerUp();
        }
    }

    /// <summary>The refinement: each corner picked up where it sits and dragged to where the keys really are, as the person does watching the grid.</summary>
    private static void DragToTruth(CalibrationWizard wizard, (double X, double Y)[] truth)
    {
        // the corners are in the order they were clicked, which here is the order of the truth
        for (var i = 0; i < 4; i++)
        {
            var (px, py) = wizard.PointOnScreen(i);
            Assert.True(wizard.PointerDown(px, py));
            var (tx, ty) = Screen(wizard, truth[i]);
            Assert.True(wizard.PointerMove(tx, ty));
            wizard.PointerUp();
        }
    }

    private static int Brightness(byte[] rgb, int width, int x, int y) => (rgb[(y * width + x) * 3] + rgb[(y * width + x) * 3 + 1] + rgb[(y * width + x) * 3 + 2]) / 3;

    /// <summary>Every key of the grid the window shows has to lie on a key of the same colour in the picture.</summary>
    private static void AssertGridOnTheKeys(CalibrationWizard wizard, byte[] rgb, int width, int height, int keys)
    {
        var grid = wizard.PreviewKeys();
        Assert.Equal(keys, grid.Count);
        var wrong = new List<string>();
        foreach (var key in grid)
        {
            var (sx, sy) = (key.Corners.Average(c => c.X), key.Corners.Average(c => c.Y));
            var (ix, iy) = wizard.View.ToImage(sx, sy);
            var (x, y) = ((int)Math.Round(ix), (int)Math.Round(iy));
            Assert.InRange(x, 0, width - 1); Assert.InRange(y, 0, height - 1);
            var brightness = Brightness(rgb, width, x, y);
            if (key.Black ? brightness >= 90 : brightness <= 120) wrong.Add($"{(key.Black ? "black" : "white")} {key.Pitch} reads {brightness} at ({x}, {y})");
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong.Take(6)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_keyboard_picture_marked_with_the_mouse_gives_a_saved_calibration_whose_keys_lie_on_the_keys(int keys, bool mirror, int viewportIndex)
    {
        var frame = SyntheticKeyboardFrame.Render(keys);
        const int W = SyntheticKeyboardFrame.Width, H = SyntheticKeyboardFrame.Height;
        var truth = SyntheticKeyboardFrame.Corners[keys];
        var layout = frame.Layout;

        // the thread that reads the camera turns frames round when the overlay is not the one drawing them; the window turns them back
        var ring = Camera(frame.Rgb, W, H, threadMirrors: mirror, out var delivered);
        var picture = CalibrationPicture.Capture(ring, framesAreMirrored: mirror);
        Assert.NotNull(picture);
        Assert.Equal(delivered, picture!.Bgra);
        Assert.Equal((W, H), (picture.Width, picture.Height));

        var (viewWidth, viewHeight) = Viewports[viewportIndex];
        var wizard = new CalibrationWizard(picture.Width, picture.Height, layout);
        wizard.SetViewport(viewWidth, viewHeight, mirror);

        // 1) clicks that are a couple of pixels off: usable at once, and the keys are near the keys already
        const double jitter = 2;
        ClickCorners(wizard, truth, jitter);
        Assert.Equal(WizardStep.Adjust, wizard.Step);
        var roughly = Assert.IsType<ArCalibration>(wizard.Finish(out _));
        var jitterInPicture = jitter / wizard.View.Scale;
        var gain = keys switch { 88 => 1.98, 76 => 1.76, 61 => 1.44, _ => 1.21 };
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
        {
            var (x, y) = roughly.KeyTopCentre(pitch);
            var (ex, ey) = frame.KeyTopCentre(pitch);
            var allowed = 4 * gain * jitterInPicture * 1.5 + .5;
            Assert.True(Math.Abs(x - ex) < allowed && Math.Abs(y - ey) < allowed, $"key {pitch}: ({x:F1}, {y:F1}) against ({ex:F1}, {ey:F1}), allowed {allowed:F1}");
        }

        // 2) dragged onto the real corners: the keys are where the picture's are, and the grid lies on them
        DragToTruth(wizard, truth);
        AssertGridOnTheKeys(wizard, frame.Rgb, W, H, keys);
        var calibration = Assert.IsType<ArCalibration>(wizard.Finish(out var problem));
        Assert.Equal(CalibrationProblem.None, problem);
        for (var k = 0; k < 4; k++)
        {
            Assert.True(Math.Abs(calibration.Corners[k].X - truth[k].X) < 1e-6 && Math.Abs(calibration.Corners[k].Y - truth[k].Y) < 1e-6,
                $"corner {k}: {calibration.Corners[k]} against {truth[k]}");
        }

        // 3) saved for this camera, found again by a fresh run, and still on the keys
        var session = new ArCalibrationSession(new ArCalibrationStore(_dir));
        session.Use("usb#cam", W, H, layout);
        Assert.Equal(ArSaveResult.Saved, session.Save(calibration.Corners, out _));
        var next = new ArCalibrationSession(new ArCalibrationStore(_dir));
        next.Use("USB#CAM", W, H, layout);
        Assert.Equal(ArCalibrationState.Ready, next.State);
        var reloaded = next.Current!;
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
        {
            var (x, y) = reloaded.KeyTopCentre(pitch);
            var (ex, ey) = frame.KeyTopCentre(pitch);
            Assert.True(Math.Abs(x - ex) < .5 && Math.Abs(y - ey) < .5, $"key {pitch}: ({x:F2}, {y:F2}) against ({ex:F2}, {ey:F2})");
            var brightness = Brightness(frame.Rgb, W, (int)x, (int)y);
            Assert.True(KeyboardLayout.IsBlack(pitch) ? brightness < 90 : brightness > 120, $"key {pitch} reads {brightness} in the picture");
        }
        // another keyboard size, or another picture size, is not this calibration
        next.Use("usb#cam", W, H, KeyboardLayout.ForKeyCount(keys == 88 ? 61 : 88));
        Assert.NotEqual(ArCalibrationState.Ready, next.State);
    }

    [Fact]
    public void The_committed_sample_picture_and_its_ground_truth_agree_with_a_calibration_made_through_the_window()
    {
        var (width, height, rgb) = SyntheticKeyboardFrameTests.ReadPng(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ar-keyboard-topdown.png"));
        var truthJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ar-keyboard-topdown.json"))).RootElement;
        (double X, double Y)[] corners = [.. truthJson.GetProperty("corners").EnumerateArray().Select(c => (c[0].GetDouble(), c[1].GetDouble()))];
        var ring = Camera(rgb, width, height, threadMirrors: false, out _);
        var picture = CalibrationPicture.Capture(ring, framesAreMirrored: false)!;
        var wizard = new CalibrationWizard(picture.Width, picture.Height, KeyboardLayout.Standard88);
        wizard.SetViewport(1000, 600, false);
        ClickCorners(wizard, corners, jitter: 3);
        DragToTruth(wizard, corners);
        AssertGridOnTheKeys(wizard, rgb, width, height, 88);
        var calibration = Assert.IsType<ArCalibration>(wizard.Finish(out _));
        var count = 0;
        foreach (var entry in truthJson.GetProperty("keyTopCentres").EnumerateObject())
        {
            var pitch = int.Parse(entry.Name);
            var (x, y) = calibration.KeyTopCentre(pitch);
            Assert.True(Math.Abs(x - entry.Value[0].GetDouble()) < .5 && Math.Abs(y - entry.Value[1].GetDouble()) < .5, $"key {pitch}");
            var brightness = Brightness(rgb, width, (int)x, (int)y);
            Assert.True(KeyboardLayout.IsBlack(pitch) ? brightness < 90 : brightness > 120, $"key {pitch} reads {brightness} in the sample picture");
            count++;
        }
        Assert.Equal(88, count);
    }

    [Fact]
    public void Corners_a_dozen_pixels_off_show_in_the_grid_as_keys_on_the_wrong_colour()
    {
        // The reason for the grid: if the corners are off, the person sees keys that do not sit on keys. Here the same check reports it.
        var frame = SyntheticKeyboardFrame.Render(88);
        var truth = SyntheticKeyboardFrame.Corners[88];
        var wizard = new CalibrationWizard(SyntheticKeyboardFrame.Width, SyntheticKeyboardFrame.Height, frame.Layout);
        wizard.SetViewport(1000, 600, false);
        var off = truth.Select(p => (p.X + 14, p.Y + 22)).ToArray();
        ClickCorners(wizard, off, jitter: 0);
        Assert.Equal(WizardStep.Adjust, wizard.Step);
        Assert.ThrowsAny<Exception>(() => AssertGridOnTheKeys(wizard, frame.Rgb, SyntheticKeyboardFrame.Width, SyntheticKeyboardFrame.Height, 88));
        DragToTruth(wizard, truth);
        AssertGridOnTheKeys(wizard, frame.Rgb, SyntheticKeyboardFrame.Width, SyntheticKeyboardFrame.Height, 88);
    }
}
