using System.IO.Compression;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The drawn keyboard picture the AR checks run on. It has to be the same picture <c>tools/make_ar_sample.py</c> committed
/// (so the two generators cannot drift), and a picture whose keys are where the homography says and look like keys.
/// </summary>
public class SyntheticKeyboardFrameTests
{
    public static TheoryData<int> Sizes => new() { 88, 76, 61, 49 };

    private static readonly Dictionary<int, Lazy<SyntheticKeyboardFrame>> Frames =
        new[] { 88, 76, 61, 49 }.ToDictionary(keys => keys, keys => new Lazy<SyntheticKeyboardFrame>(() => SyntheticKeyboardFrame.Render(keys)));

    private static SyntheticKeyboardFrame Frame(int keys) => Frames[keys].Value;

    [Fact]
    public void The_same_input_draws_the_same_bytes_twice()
    {
        var again = SyntheticKeyboardFrame.Render(61);
        Assert.True(Frame(61).Rgb.AsSpan().SequenceEqual(again.Rgb));
    }

    [Fact]
    public void It_draws_the_picture_the_Python_generator_committed()
    {
        var (width, height, python) = ReadPng(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ar-keyboard-topdown.png"));
        Assert.Equal((SyntheticKeyboardFrame.Width, SyntheticKeyboardFrame.Height), (width, height));
        var mine = Frame(88).Rgb;
        Assert.Equal(python.Length, mine.Length);

        // Both sides are IEEE doubles, but the two maths libraries may differ in the last bit of a sine, which can move one
        // pixel by one level — or, on the hairline between two keys, by more. So: nearly all within one level, almost none far off.
        long sum = 0, withinOne = 0, far = 0;
        for (var i = 0; i < mine.Length; i++)
        {
            var d = Math.Abs(mine[i] - python[i]);
            sum += d;
            if (d <= 1) withinOne++;
            if (d > 24) far++;
        }
        Assert.True(withinOne >= mine.Length * 0.999, $"only {withinOne * 100.0 / mine.Length:F3}% of the bytes are within one level of the Python picture");
        Assert.True(far <= mine.Length * 0.0005, $"{far} bytes differ from the Python picture by more than 24 levels");
        Assert.True(sum / (double)mine.Length < 0.1, $"mean difference {sum / (double)mine.Length:F4} levels");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void The_centre_of_every_white_key_is_light_and_of_every_black_key_dark(int keys)
    {
        var frame = Frame(keys);
        var layout = frame.Layout;
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
        {
            var (cx, cy) = frame.KeyTopCentre(pitch);
            var (r, g, b) = frame.PixelAt((int)cx, (int)cy);
            var brightness = (r + g + b) / 3.0;
            if (KeyboardLayout.IsBlack(pitch)) Assert.True(brightness < 90, $"black key {pitch} of {keys} is {brightness:F0} bright at ({cx:F1}, {cy:F1})");
            else Assert.True(brightness > 120, $"white key {pitch} of {keys} is {brightness:F0} bright at ({cx:F1}, {cy:F1})");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void The_key_area_sits_inside_the_picture_and_its_corners_are_where_the_pose_says(int keys)
    {
        var frame = Frame(keys);
        var corners = SyntheticKeyboardFrame.Corners[keys];
        var keyboardCorners = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) };
        for (var k = 0; k < 4; k++)
        {
            Assert.InRange(corners[k].X, 0, SyntheticKeyboardFrame.Width);
            Assert.InRange(corners[k].Y, 0, SyntheticKeyboardFrame.Height);
            var (x, y) = frame.ToImage.Apply(keyboardCorners[k].Item1, keyboardCorners[k].Item2);
            Assert.Equal(corners[k].X, x, 8);
            Assert.Equal(corners[k].Y, y, 8);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Keys_further_up_the_keyboard_are_further_right_in_the_picture(int keys)
    {
        var frame = Frame(keys);
        var layout = frame.Layout;
        // Along one line across the keys (v = 0.5), so a black key's shorter reach does not matter.
        for (var pitch = layout.FirstPitch; pitch < layout.LastPitch; pitch++)
            Assert.True(frame.ToImage.Apply(layout.CenterX(pitch + 1), 0.5).X > frame.ToImage.Apply(layout.CenterX(pitch), 0.5).X, $"key {pitch + 1} of {keys}");
    }

    /// <summary>Reads an 8-bit RGB, non-interlaced PNG whose rows all use filter 0 — what <c>make_ar_sample.py</c> writes.</summary>
    private static (int Width, int Height, byte[] Rgb) ReadPng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "not a PNG");
        int width = 0, height = 0;
        using var data = new MemoryStream();
        for (var pos = 8; pos < bytes.Length;)
        {
            var length = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
            var kind = System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4);
            var body = pos + 8;
            if (kind == "IHDR")
            {
                width = (bytes[body] << 24) | (bytes[body + 1] << 16) | (bytes[body + 2] << 8) | bytes[body + 3];
                height = (bytes[body + 4] << 24) | (bytes[body + 5] << 16) | (bytes[body + 6] << 8) | bytes[body + 7];
                Assert.Equal(8, bytes[body + 8]);  // bit depth
                Assert.Equal(2, bytes[body + 9]);  // colour type: RGB
                Assert.Equal(0, bytes[body + 12]); // not interlaced
            }
            else if (kind == "IDAT") data.Write(bytes, body, length);
            pos = body + length + 4; // the chunk's CRC
        }
        data.Position = 0;
        using var inflate = new ZLibStream(data, CompressionMode.Decompress);
        var raw = new MemoryStream();
        inflate.CopyTo(raw);
        var rawBytes = raw.ToArray();
        var stride = width * 3;
        Assert.Equal((stride + 1) * height, rawBytes.Length);
        var rgb = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, rawBytes[y * (stride + 1)]); // filter 0 (none)
            Buffer.BlockCopy(rawBytes, y * (stride + 1) + 1, rgb, y * stride, stride);
        }
        return (width, height, rgb);
    }
}
