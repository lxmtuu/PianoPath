namespace PianoPath;

/// <summary>How a camera picture is laid into the stage when its shape differs from the stage's.</summary>
internal enum PictureFit
{
    /// <summary>The whole picture is shown; the spare room is left empty (bars).</summary>
    Fit,
    /// <summary>The picture fills the stage; what does not fit is cropped.</summary>
    Fill
}

/// <summary>
/// Where the camera picture lands on the output, and how a point in the picture (pixels, origin top left, the way the camera
/// delivered it) maps to a point on the output (pixels, origin top left) and back.
/// </summary>
/// <remarks>
/// <para>Everything the AR layer draws — the key outlines, the notes — is worked out in picture pixels, because that is
/// where the calibration was made. This is the one place that turns picture pixels into screen pixels, so the camera layer
/// and the AR layer cannot disagree about where the picture is. The shader receives <see cref="Left"/>, <see cref="Top"/>,
/// <see cref="Width"/>, <see cref="Height"/> and <see cref="Mirror"/>, and nothing else.</para>
/// <para>Mirroring flips the picture left to right on the output only; the calibration stays in the unmirrored picture,
/// so turning the mirror on or off never invalidates it.</para>
/// </remarks>
internal readonly struct ArView
{
    /// <summary>False when the picture or the output has no size; every mapping then returns its input unchanged.</summary>
    internal bool Valid { get; }
    internal int ImageWidth { get; }
    internal int ImageHeight { get; }
    internal double OutputWidth { get; }
    internal double OutputHeight { get; }
    internal PictureFit Fit { get; }
    internal bool Mirror { get; }
    /// <summary>Output pixels covered by the whole picture (may reach beyond the output for <see cref="PictureFit.Fill"/>).</summary>
    internal double Left { get; }
    internal double Top { get; }
    internal double Width { get; }
    internal double Height { get; }

    internal ArView(int imageWidth, int imageHeight, double outputWidth, double outputHeight, PictureFit fit, bool mirror)
    {
        ImageWidth = imageWidth; ImageHeight = imageHeight; OutputWidth = outputWidth; OutputHeight = outputHeight; Fit = fit; Mirror = mirror;
        Valid = imageWidth > 0 && imageHeight > 0 && outputWidth > 0 && outputHeight > 0 && double.IsFinite(outputWidth) && double.IsFinite(outputHeight);
        if (!Valid) { Left = Top = 0; Width = outputWidth; Height = outputHeight; return; }
        var scale = fit == PictureFit.Fit
            ? Math.Min(outputWidth / imageWidth, outputHeight / imageHeight)
            : Math.Max(outputWidth / imageWidth, outputHeight / imageHeight);
        Width = imageWidth * scale; Height = imageHeight * scale;
        Left = (outputWidth - Width) / 2; Top = (outputHeight - Height) / 2;
    }

    /// <summary>Output pixels per picture pixel.</summary>
    internal double Scale => Valid ? Width / ImageWidth : 1;

    /// <summary>A point of the picture, on the output.</summary>
    internal (double X, double Y) ToOutput(double x, double y)
    {
        if (!Valid) return (x, y);
        var u = x / ImageWidth;
        if (Mirror) u = 1 - u;
        return (Left + u * Width, Top + y / ImageHeight * Height);
    }

    /// <summary>A point of the output, in the picture (outside 0..width / 0..height when it falls on the bars or beyond the crop).</summary>
    internal (double X, double Y) ToImage(double x, double y)
    {
        if (!Valid) return (x, y);
        var u = (x - Left) / Width;
        if (Mirror) u = 1 - u;
        return (u * ImageWidth, (y - Top) / Height * ImageHeight);
    }

    /// <summary>True when the output point lies on the picture rather than on a bar.</summary>
    internal bool CoversOutputPoint(double x, double y) =>
        Valid && x >= Left && x <= Left + Width && y >= Top && y <= Top + Height;

    /// <summary>The share of the picture that is visible on the output, 0..1 (below 1 only for <see cref="PictureFit.Fill"/> on a different shape).</summary>
    internal double VisibleShare
    {
        get
        {
            if (!Valid) return 0;
            var w = Math.Min(OutputWidth, Width) / Width;
            var h = Math.Min(OutputHeight, Height) / Height;
            return w * h;
        }
    }

    /// <summary>True when every point of the quad (picture pixels) is on the output — what "the keyboard is in view" means for the AR layer.</summary>
    internal bool ContainsQuad(IReadOnlyList<(double X, double Y)> quad)
    {
        if (!Valid) return false;
        foreach (var point in quad)
        {
            var (x, y) = ToOutput(point.X, point.Y);
            if (x < 0 || y < 0 || x > OutputWidth || y > OutputHeight) return false;
        }
        return quad.Count > 0;
    }
}
