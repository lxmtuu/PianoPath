namespace PianoPath;

/// <summary>Portable, bounded factorial plan; not a new effect recipe or a user-facing setting.</summary>
internal static class HeldParticleControlPlan
{
    internal const int Width = 1280, Height = 720, Fps = 60, Frames = 36;
    internal const double Fov = 70;
    internal sealed record Palette(string Id, string? FixedColor);
    internal sealed record Pattern(string Id, IReadOnlyList<int> Pitches);
    internal sealed record View(string Id, IReadOnlyList<(double X, double Y)>? Corners)
    {
        internal bool OverCamera => Corners is not null;
    }
    internal sealed record Variant(string Id, bool Particles, bool KeyLight);
    internal sealed record Scene(string Family, Palette Palette, Pattern Pattern, View View)
    {
        internal string Id => $"{Family}-{Palette.Id}-{Pattern.Id}-{View.Id}";
    }
    internal static IReadOnlyList<Palette> Palettes { get; } = Array.AsReadOnly(new[]
    {
        new Palette("prism", null), new Palette("amber", "#FF9A4D"), new Palette("indigo", "#666DFF")
    });
    internal static IReadOnlyList<Pattern> Patterns { get; } = Array.AsReadOnly(new[]
    {
        new Pattern("white", Array.AsReadOnly(new[] { 60 })),
        new Pattern("black", Array.AsReadOnly(new[] { 61 })),
        new Pattern("chord", Array.AsReadOnly(new[] { 60, 61, 64, 67 }))
    });
    internal static IReadOnlyList<View> Views { get; } = Array.AsReadOnly(new[]
    {
        new View("live-stage", null),
        new View("ar-reference", Array.AsReadOnly(new (double X, double Y)[] { (70,292),(1214,268),(1236,421),(52,449) })),
        new View("ar-oblique", ProjectPlane(55, -6)),
        new View("ar-near-overhead", ProjectPlane(7, 0))
    });
    // This ordering is also the quartet order in the contact sheets. No inferred additive decomposition.
    internal static IReadOnlyList<Variant> Variants { get; } = Array.AsReadOnly(new[]
    {
        new Variant("neither", false, false), new Variant("light-only", false, true),
        new Variant("particles-only", true, false), new Variant("combined", true, true)
    });
    internal static IEnumerable<Scene> Scenes =>
        from family in new[] { "wisps", "flames" }
        from palette in Palettes from pattern in Patterns from view in Views
        select new Scene(family, palette, pattern, view);

    // A physical plane projected with the same intrinsics used by calibration. Pitch is its angle away
    // from camera-facing; roll turns the picture. Not an arbitrary trapezoid called a camera pose.
    private static IReadOnlyList<(double X, double Y)> ProjectPlane(double pitchDegrees, double rollDegrees)
    {
        var pitch = pitchDegrees * Math.PI / 180; var roll = rollDegrees * Math.PI / 180;
        var ratio = ArCameraPose.WidthDepthRatio(KeyboardLayout.Standard88);
        var focal = Width / (2 * Math.Tan(Fov * Math.PI / 360));
        return Array.AsReadOnly(new[] { (0d,0d),(1d,0d),(1d,1d),(0d,1d) }.Select(p =>
        {
            var x = (p.Item1 - .5) * ratio; var y = (p.Item2 - .5) * Math.Cos(pitch);
            var z = 7.2 + (p.Item2 - .5) * Math.Sin(pitch);
            return (Width / 2d + focal * (x * Math.Cos(roll) - y * Math.Sin(roll)) / z,
                Height / 2d + focal * (x * Math.Sin(roll) + y * Math.Cos(roll)) / z);
        }).ToArray());
    }

    /// <summary>Reprojects the existing synthetic picture as one flat plane, using exactly the capture's
    /// corners. Nearest-neighbour, no new texture/shading/physical-camera claim; padding is dark grey.</summary>
    internal static byte[] CameraFor(View view, byte[] source)
    {
        if (!view.OverCamera || source.Length != Width * Height * 4) throw new ArgumentException("AR BGRA fixture required.");
        if (view.Id == "ar-reference") return (byte[])source.Clone();
        var toSource = Homography.FromPoints(view.Corners!, Views[1].Corners!)
            ?? throw new ArgumentException("Invalid camera-plane corners.");
        var pixels = new byte[source.Length];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var p = toSource.Apply(x + .5, y + .5); var dest = (y * Width + x) * 4;
            var sx = (int)Math.Floor(p.X); var sy = (int)Math.Floor(p.Y);
            if (double.IsFinite(p.X) && double.IsFinite(p.Y) && sx >= 0 && sx < Width && sy >= 0 && sy < Height)
                Buffer.BlockCopy(source, (sy * Width + sx) * 4, pixels, dest, 4);
            else { pixels[dest] = pixels[dest + 1] = pixels[dest + 2] = 24; pixels[dest + 3] = 255; }
        }
        return pixels;
    }
}
