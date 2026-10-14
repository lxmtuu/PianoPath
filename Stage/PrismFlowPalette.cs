using System.Numerics;

namespace PianoPath;

/// <summary>Fixed, unsampled art-direction palette. Returns sRGB, interpolated in linear light.
/// Independent of WPF/layout/time: Stage resolves it once into the colour table shared with AR.</summary>
internal static class PrismFlowPalette
{
    // Do not expose mutable arrays: render snapshots and tests must see the same stops.
    internal const int StopCount = 6;
    internal static double StopPosition(int index) => index switch
    {
        0 => 0, 1 => .22, 2 => .44, 3 => .64, 4 => .82, 5 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    internal static Vector3 StopColor(int index) => index switch
    {
        0 => Rgb(0, 207, 232), 1 => Rgb(0, 227, 190), 2 => Rgb(169, 232, 121),
        3 => Rgb(242, 223, 133), 4 => Rgb(255, 173, 96), 5 => Rgb(255, 142, 148),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal static Vector3 ForPitch(int pitch, int lowPitch, int highPitch)
    {
        if (lowPitch < 0 || highPitch > 127 || highPitch <= lowPitch)
            throw new ArgumentOutOfRangeException(nameof(highPitch), "Palette range must be ascending MIDI pitches.");
        return Sample((pitch - lowPitch) / (double)(highPitch - lowPitch));
    }

    internal static Vector3 Sample(double position)
    {
        // Invalid diagnostic inputs have a deterministic, bounded fallback; infinities clamp to endpoints.
        var t = double.IsNaN(position) ? 0 : Math.Clamp(position, 0, 1);
        for (var i = 1; i < StopCount; i++)
        {
            if (t > StopPosition(i)) continue;
            var amount = (float)((t - StopPosition(i - 1)) / (StopPosition(i) - StopPosition(i - 1)));
            var a = Linear(StopColor(i - 1));
            var b = Linear(StopColor(i));
            var mixed = Vector3.Lerp(a, b, amount);
            return new Vector3(Encode(mixed.X), Encode(mixed.Y), Encode(mixed.Z));
        }
        return StopColor(StopCount - 1);
    }

    private static Vector3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    private static float Decode(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    private static float Encode(float c) => c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - .055f;
    private static Vector3 Linear(Vector3 c) => new(Decode(c.X), Decode(c.Y), Decode(c.Z));
}
