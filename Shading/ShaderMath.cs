using System.Windows.Media;

namespace PianoPath;

/// <summary>A three component vector in world space: X across the keyboard, Y up, Z depth away from the player.</summary>
internal readonly struct Vec3
{
    internal readonly double X, Y, Z;
    internal Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
    internal static Vec3 Zero => new(0, 0, 0);
    internal static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    internal static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    internal static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    internal static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    internal static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    internal static Vec3 operator *(Vec3 a, Vec3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    internal static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    internal double LengthSquared => X * X + Y * Y + Z * Z;
    internal double Length => Math.Sqrt(LengthSquared);
    internal Vec3 Normalized()
    {
        var length = Length;
        return length < 1e-9 ? new Vec3(0, 1, 0) : new Vec3(X / length, Y / length, Z / length);
    }
    internal static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
    /// <summary>Mirrors <paramref name="incident"/> (pointing at the surface) around <paramref name="normal"/>.</summary>
    internal static Vec3 Reflect(Vec3 incident, Vec3 normal) => incident - normal * (2 * Dot(incident, normal));
}

/// <summary>
/// Allocation-free shading maths for the Keyflow software renderer. The formulas follow the shading
/// model Unreal Engine ships by default: rendering in linear light, a Cook-Torrance micro-facet BRDF
/// (GGX distribution, Smith geometry, Schlick fresnel), an analytic image based ambient term, the
/// ACES filmic tonemapper and ordered dithering before the sRGB write-back.
/// </summary>
internal static class ShaderMath
{
    internal const double Pi = Math.PI;
    private static readonly double[] LinearTable = BuildLinearTable();
    /// <summary>4×4 Bayer matrix; breaks up gradient banding exactly like a dithered post process.</summary>
    private static readonly double[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    private static double[] BuildLinearTable()
    {
        var table = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var channel = i / 255.0;
            table[i] = channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return table;
    }

    internal static double ToLinear(byte channel) => LinearTable[channel];
    internal static Vec3 ToLinear(Color color) => new(ToLinear(color.R), ToLinear(color.G), ToLinear(color.B));
    internal static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
    internal static double Smoothstep(double edge0, double edge1, double value)
    {
        if (Math.Abs(edge1 - edge0) < 1e-9) return value < edge0 ? 0 : 1;
        var t = Clamp01((value - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }

    internal static byte EncodeSrgb(double linear, double dither)
    {
        var value = linear + dither / 255.0;
        var encoded = value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(Math.Max(value, 0), 1 / 2.4) - .055;
        return (byte)Math.Clamp((int)Math.Round(encoded * 255), 0, 255);
    }

    /// <summary>Stephen Narkowicz' fitted ACES curve, the tonemapper Unreal selects with "Filmic".</summary>
    internal static Vec3 AcesTonemap(Vec3 color) => new(Aces(color.X), Aces(color.Y), Aces(color.Z));

    private static double Aces(double value)
    {
        const double a = 2.51, b = .03, c = 2.43, d = .59, e = .14;
        var v = Math.Max(value, 0);
        return Clamp01(v * (a * v + b) / (v * (c * v + d) + e));
    }

    /// <summary>GGX/Trowbridge-Reitz normal distribution function.</summary>
    internal static double DistributionGgx(double roughness, double noh)
    {
        var alpha = Math.Max(roughness * roughness, 1e-4);
        var alpha2 = alpha * alpha;
        var denominator = noh * noh * (alpha2 - 1) + 1;
        return alpha2 / (Pi * denominator * denominator + 1e-7);
    }

    /// <summary>Schlick-GGX geometry term, the visibility part of the Smith approximation.</summary>
    internal static double GeometrySmith(double roughness, double nov, double nol)
    {
        var k = Math.Max(roughness * roughness, 1e-4) * .5;
        return nov / (nov * (1 - k) + k) * (nol / (nol * (1 - k) + k));
    }

    internal static Vec3 FresnelSchlick(double cosTheta, Vec3 f0)
    {
        var power = Math.Pow(1 - Clamp01(cosTheta), 5);
        return f0 + (new Vec3(1, 1, 1) - f0) * power;
    }

    internal static double Luminance(Vec3 color) => .2126 * color.X + .7152 * color.Y + .0722 * color.Z;

    internal static Vec3 AdjustSaturation(Vec3 color, double saturation)
    {
        if (Math.Abs(saturation - 1) < 1e-4) return color;
        var luma = Luminance(color);
        return new Vec3(luma + (color.X - luma) * saturation, luma + (color.Y - luma) * saturation, luma + (color.Z - luma) * saturation);
    }

    internal static Vec3 AdjustContrast(Vec3 color, double contrast)
    {
        if (Math.Abs(contrast - 1) < 1e-4) return color;
        return new Vec3((color.X - .5) * contrast + .5, (color.Y - .5) * contrast + .5, (color.Z - .5) * contrast + .5);
    }

    /// <summary>Deterministic hash in [0,1); keeps a baked frame stable instead of shimmering between renders.</summary>
    internal static double Hash(int x, int y, int seed)
    {
        var n = (uint)(x * 1619 + y * 31337 + seed * 6971);
        n = (n << 13) ^ n;
        n = n * (n * n * 15731 + 789221) + 1376312589;
        return (n & 0x7FFFFFFF) / (double)0x7FFFFFFF;
    }

    internal static double Dither(int x, int y) => (Bayer[(y & 3) * 4 + (x & 3)] + .5) / 16.0 - .5;
}
