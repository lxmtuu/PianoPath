namespace PianoPath;

/// <summary>Where one export watermark sits on a scene: the GPU sprite sized and placed by these DIPs.</summary>
internal readonly record struct ArVideoWatermarkPlacement(float CenterX, float CenterY, float HalfHeight, float Aspect);

/// <summary>
/// Places the render-to-video watermark («KEYFLOW» in the look) on a scene of any size, so the offline
/// engine and the live GPU window agree on where the strip lands: centred above the key line, the same
/// distance from it as the software stage draws it, shrinking only when the scene is too narrow to hold
/// the strip whole. Pure so the Linux test job can lock the placement without a graphics device.
/// </summary>
internal static class ArVideoWatermark
{
    /// <summary>Height of the strip in DIPs, matching the software stage's 11 pt caption.</summary>
    internal const float StripHeight = 22f;
    /// <summary>Distance from the bottom of the strip to the hit line; the software stage uses 18 DIPs.</summary>
    internal const float AboveKeys = 18f;
    internal const float MarginX = 8f;

    /// <summary>
    /// The placement for a scene of <paramref name="width"/>×<paramref name="height"/> DIPs whose hit line sits
    /// at <paramref name="hitY"/>, for a strip bitmap of <paramref name="imageWidth"/>×<paramref name="imageHeight"/>
    /// pixels. Returns null when there is no picture to draw (or nothing positive to measure).
    /// </summary>
    internal static ArVideoWatermarkPlacement? Place(float width, float height, float hitY, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || width <= 0 || height <= 0) return null;
        var aspect = imageWidth / (float)imageHeight;
        var half = StripHeight / 2f;
        // A narrow scene keeps the strip whole inside its margins by scaling the strip down.
        var maxHalfWidth = MathF.Max(4f, width / 2f - MarginX);
        var halfWidth = half * aspect;
        if (halfWidth > maxHalfWidth) half *= MathF.Max(0.25f, maxHalfWidth / halfWidth);
        var centerY = MathF.Min(hitY - AboveKeys - half, height - half);
        if (centerY < half) centerY = half;
        return new ArVideoWatermarkPlacement(width / 2f, centerY, half, aspect);
    }
}
