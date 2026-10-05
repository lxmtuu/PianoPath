using System.Globalization;

namespace PianoPath;

/// <summary>
/// One video mode a camera offers: a frame size, a frame rate and the format the camera sends it in. Media
/// Foundation calls these the device's native media types.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="RateNumerator">Frame rate as a fraction, numerator (30000/1001 is 29.97 fps); zero when unknown.</param>
/// <param name="RateDenominator">Frame rate as a fraction, denominator.</param>
/// <param name="Format">The format as its four letters ("MJPG", "YUY2", "NV12"…); empty when not known.</param>
internal readonly record struct CameraMode(int Width, int Height, int RateNumerator, int RateDenominator, string Format)
{
    /// <summary>Frames per second; 0 when the mode does not say.</summary>
    internal double Fps => RateDenominator > 0 && RateNumerator > 0 ? (double)RateNumerator / RateDenominator : 0;

    /// <summary>
    /// What the dock prints: "720p · 60 fps · MJPG". The size is the usual name for the common heights and "W × H"
    /// for anything else; a part the mode does not know is left out.
    /// </summary>
    internal string Label
    {
        get
        {
            var size = Height is 360 or 480 or 540 or 720 or 1080 or 1440 or 2160 && Width >= Height
                ? $"{Height}p"
                : string.Create(CultureInfo.InvariantCulture, $"{Width} × {Height}");
            var parts = new List<string> { size };
            if (Fps > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Fps)} fps"));
            if (Format.Length > 0) parts.Add(Format);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Which of a camera's modes the overlay asks for, and how a format is named. Pure, so the rule is stated by
/// tests instead of being found out on somebody's webcam.
/// </summary>
internal static class CameraModes
{
    /// <summary>The picture height the overlay aims for: enough to see a keyboard, light enough to decode at 60 fps.</summary>
    internal const int PreferredHeight = 720;

    /// <summary>The frame rate beyond which a faster camera is not preferred; the stage draws no faster than a screen anyway.</summary>
    internal const int PreferredRate = 60;

    /// <summary>
    /// The index in <paramref name="modes"/> of the mode to use, or -1 when there is no usable one. In order of
    /// importance: a format the reader can turn into RGB (a compressed one it may not decode comes last); a height
    /// as close to <see cref="PreferredHeight"/> as the camera has (the taller one on a tie); the highest rate up to
    /// <see cref="PreferredRate"/>, where 59.94 counts as 60; the shape nearest 16:9; and the format that costs a
    /// USB bus the least (MJPG, then NV12, YUY2, other raw ones).
    /// </summary>
    internal static int Choose(IReadOnlyList<CameraMode> modes)
    {
        var best = -1;
        (int Unusable, int HeightDistance, int Taller, int Slow, int Shape, int Format) bestKey = default;
        for (var index = 0; index < modes.Count; index++)
        {
            var mode = modes[index];
            if (mode.Width <= 0 || mode.Height <= 0) continue;
            var key = (
                Unusable: FormatRank(mode.Format) >= 9 ? 1 : 0,
                HeightDistance: Math.Abs(mode.Height - PreferredHeight),
                Taller: -mode.Height,
                Slow: -(int)Math.Min(Math.Round(mode.Fps), PreferredRate),
                Shape: Math.Abs(mode.Width * 9 - mode.Height * 16),
                Format: FormatRank(mode.Format));
            if (best < 0 || key.CompareTo(bestKey) < 0) { best = index; bestKey = key; }
        }
        return best;
    }

    /// <summary>How well a format suits the overlay: lower is better, 9 is one the reader may not be able to decode.</summary>
    internal static int FormatRank(string format) => format switch
    {
        "MJPG" => 0,
        "NV12" => 1,
        "YUY2" => 2,
        "UYVY" or "I420" or "IYUV" or "YV12" => 3,
        "RGB32" or "ARGB32" or "RGB24" => 4,
        _ => 9,
    };

    /// <summary>
    /// The name of a Media Foundation video subtype. Most are a four-letter code written into the first four bytes
    /// of the GUID ({47504A4D-…} is "MJPG"); the uncompressed RGB ones are small numbers (22 is RGB32). A GUID that
    /// is neither gives an empty name.
    /// </summary>
    internal static string FormatName(Guid subtype)
    {
        var text = subtype.ToString("D");
        if (!text.EndsWith("-0000-0010-8000-00aa00389b71", StringComparison.OrdinalIgnoreCase)) return "";
        var code = uint.Parse(text.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        switch (code)
        {
            case 20: return "RGB24";
            case 21: return "ARGB32";
            case 22: return "RGB32";
            case 23: return "RGB565";
            case 24: return "RGB555";
        }
        if (code < 0x20202020) return "";
        var letters = new char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((code >> (8 * i)) & 0xFF);
            if (c < ' ' || c > '~') return "";
            letters[i] = char.ToUpperInvariant(c);
        }
        return new string(letters).TrimEnd();
    }
}
