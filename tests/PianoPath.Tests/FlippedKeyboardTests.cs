using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// A camera on the far side of the keyboard, looking at the player, or a picture that arrives turned round, shows the keyboard rotated or
/// mirrored: low notes on the right, the far edge of the keys (where the black keys start) at the bottom. The window asks how the keyboard
/// looks in the picture, and with the right answer the corners go to the right keys of the right pitch. These tests make pictures of a
/// keyboard in every way it can be turned, mark them with the mouse, and read the result from the picture's own pixels.
/// </summary>
public sealed class FlippedKeyboardTests : IDisposable
{
    private const int W = SyntheticKeyboardFrame.Width, H = SyntheticKeyboardFrame.Height;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ar-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>The picture turned left-right and/or top-bottom, as a camera in another place (or a source that mirrors) would deliver it.</summary>
    private static byte[] Turned(byte[] rgb, bool flipX, bool flipY)
    {
        var result = new byte[rgb.Length];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var from = (y * W + x) * 3;
                var to = ((flipY ? H - 1 - y : y) * W + (flipX ? W - 1 - x : x)) * 3;
                Buffer.BlockCopy(rgb, from, result, to, 3);
            }
        return result;
    }

    private static int Brightness(byte[] rgb, int x, int y) => (rgb[(y * W + x) * 3] + rgb[(y * W + x) * 3 + 1] + rgb[(y * W + x) * 3 + 2]) / 3;

    private static CameraFrameRing Ring(byte[] rgb, bool threadMirrors)
    {
        var bgra = new byte[W * H * 4];
        for (var i = 0; i < W * H; i++) { bgra[i * 4] = rgb[i * 3 + 2]; bgra[i * 4 + 1] = rgb[i * 3 + 1]; bgra[i * 4 + 2] = rgb[i * 3]; bgra[i * 4 + 3] = 255; }
        if (threadMirrors) CalibrationPicture.FlipRows(bgra, W, H);
        var ring = new CameraFrameRing();
        var buffer = ring.BeginWrite(bgra.Length, out var slot);
        Buffer.BlockCopy(bgra, 0, buffer, 0, bgra.Length);
        ring.Publish(slot, W, H, 1);
        return ring;
    }

    private static (double X, double Y) Screen(CalibrationWizard wizard, (double X, double Y) point) => wizard.View.ToOutput(point.X, point.Y);

    private static void Click(CalibrationWizard wizard, (double X, double Y)[] corners)
    {
        foreach (var corner in corners)
        {
            var (x, y) = Screen(wizard, corner);
            Assert.True(wizard.PointerDown(x, y));
            wizard.PointerUp();
        }
    }

    /// <summary>Whether every key of the grid sits on a key of its own colour in the picture the person sees.</summary>
    private static bool GridOnTheKeys(CalibrationWizard wizard, byte[] rgb, int keys)
    {
        var grid = wizard.PreviewKeys();
        if (grid.Count != keys) return false;
        foreach (var key in grid)
        {
            var (ix, iy) = wizard.View.ToImage(key.Corners.Average(c => c.X), key.Corners.Average(c => c.Y));
            var (x, y) = ((int)Math.Round(ix), (int)Math.Round(iy));
            if (x < 0 || y < 0 || x >= W || y >= H) return false;
            var brightness = Brightness(rgb, x, y);
            if (key.Black ? brightness >= 90 : brightness <= 120) return false;
        }
        return true;
    }

    /// <summary>The ways the picture can be: how it is turned compared with the keyboard seen from behind, whether the window shows it mirrored, and what the person sees then.</summary>
    public static TheoryData<int, bool, bool, bool, KeyboardOrientation> Cases
    {
        get
        {
            var data = new TheoryData<int, bool, bool, bool, KeyboardOrientation>();
            foreach (var keys in new[] { 88, 76, 61, 49 })
            {
                data.Add(keys, false, false, false, KeyboardOrientation.Upright);
                data.Add(keys, false, false, true, KeyboardOrientation.LowRight);                 // the window mirrors it: the low notes are on the right
                data.Add(keys, true, false, false, KeyboardOrientation.LowRight);                 // the picture is mirrored left to right
                data.Add(keys, true, false, true, KeyboardOrientation.Upright);                   // mirrored, and shown mirrored again
                data.Add(keys, false, true, false, KeyboardOrientation.FarBottom);                // upside down only
                data.Add(keys, false, true, true, KeyboardOrientation.LowRightFarBottom);
                data.Add(keys, true, true, false, KeyboardOrientation.LowRightFarBottom);         // turned half way round: the camera on the far side
                data.Add(keys, true, true, true, KeyboardOrientation.FarBottom);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_keyboard_turned_any_way_is_marked_with_the_mouse_and_its_keys_lie_on_the_keys(int keys, bool flipX, bool flipY, bool mirror, KeyboardOrientation seen)
    {
        var frame = SyntheticKeyboardFrame.Render(keys);
        var layout = frame.Layout;
        var rgb = Turned(frame.Rgb, flipX, flipY);
        var truth = CalibrationQuad.Reflect(SyntheticKeyboardFrame.Corners[keys], W, H, flipX, flipY);          // back-left, back-right, front-right, front-left: of the keyboard, wherever they are now

        // what the person sees tells the orientation; what the camera delivers is what the window works on
        Assert.Equal(seen, KeyboardOrientations.Of(truth, W, mirror));
        var picture = CalibrationPicture.Capture(Ring(rgb, threadMirrors: mirror), framesAreMirrored: mirror)!;
        Assert.Equal(rgb.Length / 3, picture.Bgra.Length / 4);

        var wizard = new CalibrationWizard(W, H, layout);
        wizard.SetViewport(1000, 600, mirror);
        wizard.SetOrientation(seen);
        Click(wizard, truth);
        Assert.Equal(WizardStep.Adjust, wizard.Step);
        Assert.Equal(CalibrationProblem.None, wizard.Problem);
        Assert.True(wizard.CanFinish);
        Assert.True(GridOnTheKeys(wizard, rgb, keys), "grid not on the keys right after the clicks");

        var calibration = Assert.IsType<ArCalibration>(wizard.Finish(out var problem));
        Assert.Equal(CalibrationProblem.None, problem);
        for (var k = 0; k < 4; k++)
            Assert.True(Math.Abs(calibration.Corners[k].X - truth[k].X) < 1e-6 && Math.Abs(calibration.Corners[k].Y - truth[k].Y) < 1e-6, $"corner {k} was named wrongly: {calibration.Corners[k]} against {truth[k]}");

        // every key at the key of the picture, and low pitches where the keyboard's low end is
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
        {
            var (x, y) = calibration.KeyTopCentre(pitch);
            var (ex, ey) = frame.KeyTopCentre(pitch);
            (ex, ey) = (flipX ? W - ex : ex, flipY ? H - ey : ey);
            Assert.True(Math.Abs(x - ex) < 1 && Math.Abs(y - ey) < 1, $"key {pitch}: ({x:F1}, {y:F1}) against ({ex:F1}, {ey:F1})");
            var brightness = Brightness(rgb, (int)x, (int)y);
            Assert.True(KeyboardLayout.IsBlack(pitch) ? brightness < 90 : brightness > 120, $"key {pitch} reads {brightness} in the picture");
        }

        // the near edge of the keys (what a note falls to) is the one at the player's side: the bottom of the picture only when the far edge is at the top
        var (_, farY) = calibration.ToPicture(.5, 0);
        var (_, nearY) = calibration.ToPicture(.5, 1);
        Assert.Equal(!flipY, nearY > farY);

        // saved for this camera, and found again by a fresh run, with the same keys
        var session = new ArCalibrationSession(new ArCalibrationStore(_dir));
        session.Use("usb#flipped", W, H, layout, ArCalibrationProfiles.LegacyId);
        Assert.Equal(ArSaveResult.Saved, session.Save(calibration.Corners, out _));
        var next = new ArCalibrationSession(new ArCalibrationStore(_dir));
        next.Use("usb#flipped", W, H, layout, ArCalibrationProfiles.LegacyId);
        Assert.Equal(ArCalibrationState.Ready, next.State);
        Assert.Equal(calibration.Corners, next.Current!.Corners);
        // and the window opens on the same choice the next time
        Assert.Equal(seen, KeyboardOrientations.Of(next.Current.Corners, W, mirror));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_wrong_choice_shows_in_the_grid_and_the_right_one_puts_it_right_without_clicking_again(bool flipX, bool flipY)
    {
        var frame = SyntheticKeyboardFrame.Render(88);
        var rgb = Turned(frame.Rgb, flipX, flipY);
        var truth = CalibrationQuad.Reflect(SyntheticKeyboardFrame.Corners[88], W, H, flipX, flipY);
        var right = KeyboardOrientations.Of(truth, W, mirrored: false);
        var wizard = new CalibrationWizard(W, H, frame.Layout);
        wizard.SetViewport(1000, 600, false);
        wizard.SetOrientation(right);
        Click(wizard, truth);
        Assert.True(GridOnTheKeys(wizard, rgb, 88));

        var points = wizard.Points.ToArray();
        foreach (var other in Enum.GetValues<KeyboardOrientation>().Where(o => o != right))
        {
            wizard.SetOrientation(other);
            Assert.Equal(points, wizard.Points);                                               // the corners did not move
            Assert.False(wizard.CanFinish && GridOnTheKeys(wizard, rgb, 88), $"{other} was taken for {right}");
        }
        wizard.SetOrientation(right);
        Assert.True(GridOnTheKeys(wizard, rgb, 88));
        Assert.NotNull(wizard.Finish(out _));
    }

    [Fact]
    public void An_outline_that_goes_round_the_other_way_is_a_keyboard_seen_from_the_far_side_and_a_bow_tie_still_is_not()
    {
        var s = SyntheticKeyboardFrame.Corners[88];
        Assert.Equal(CalibrationProblem.WrongOrder, CalibrationQuad.Check([s[3], s[2], s[1], s[0]], W, H));          // the strict check still says so
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.CheckAnyWinding([s[3], s[2], s[1], s[0]], W, H));
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.CheckAnyWinding([s[1], s[0], s[3], s[2]], W, H));
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.CheckAnyWinding(s, W, H));
        Assert.Equal(CalibrationProblem.NotConvex, CalibrationQuad.CheckAnyWinding([s[0], s[1], s[3], s[2]], W, H));
        Assert.Equal(CalibrationProblem.NotFourPoints, CalibrationQuad.CheckAnyWinding(s.Take(3).ToArray(), W, H));
        Assert.Equal(CalibrationProblem.OutsideImage, CalibrationQuad.CheckAnyWinding([s[3], s[2], s[1], (s[0].X, 5000)], W, H));
        Assert.Equal(CalibrationProblem.TooSmall, CalibrationQuad.CheckAnyWinding([(600, 330), (700, 330), (700, 300), (600, 300)], W, H));
        Assert.NotNull(ArCalibration.Create(KeyboardLayout.Standard88, [s[3], s[2], s[1], s[0]], W, H, out _));
        Assert.Null(ArCalibration.Create(KeyboardLayout.Standard88, [s[0], s[1], s[3], s[2]], W, H, out _));
    }

    [Fact]
    public void Turning_points_round_twice_gives_them_back_and_the_orientation_reads_back_what_made_it()
    {
        var s = SyntheticKeyboardFrame.Corners[61];
        foreach (var (fx, fy) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var turned = CalibrationQuad.Reflect(s, W, H, fx, fy);
            var back = CalibrationQuad.Reflect(turned, W, H, fx, fy);
            for (var i = 0; i < 4; i++) { Assert.Equal(s[i].X, back[i].X, 9); Assert.Equal(s[i].Y, back[i].Y, 9); }
            foreach (var mirrored in new[] { false, true })
            {
                var seen = KeyboardOrientations.Of(turned, W, mirrored);
                Assert.Equal(fx != mirrored, seen.LowOnRight());
                Assert.Equal(fy, seen.FarAtBottom());
            }
        }
        Assert.Equal(KeyboardOrientation.Upright, KeyboardOrientations.Of([(0, 0)], W, false));      // nothing known: the usual way
    }
}
