using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Which family of animated chrome backdrop a theme draws behind the interface.</summary>
internal enum BackdropStyle
{
    /// <summary>Slow ribbons of colour, like stage haze under coloured spots (concert hall).</summary>
    Aurora,
    /// <summary>Falling cherry-blossom petals over a moonlit sky (Your Lie in April).</summary>
    Sakura,
    /// <summary>Heavy velvet curtain folds with golden dust in the air.</summary>
    Curtain
}

/// <summary>
/// A complete look for the interface chrome: surfaces, accents and the animated backdrop family.
///
/// The stage itself is themed by <see cref="PianoVisualSettings"/>; this record only describes the
/// frame around it, which keeps "how the piano looks" and "how the app looks" independent — you can
/// run a Sakura stage inside the Concert Noir shell or the other way round.
/// </summary>
internal sealed record ShellTheme(
    string Id,
    string Name,
    string Blurb,
    BackdropStyle Backdrop,
    Color Accent,
    Color AccentAlt,
    Color AccentSoft,
    Color Glow,
    Color Window,
    Color Panel,
    Color PanelTop,
    Color PanelBottom,
    Color PanelAlt,
    Color Control,
    Color ControlHover,
    Color Border,
    Color ControlBorder,
    Color Track,
    Color Popup,
    Color Petal,
    Color PetalAlt)
{
    /// <summary>True when the palette is light enough that white hairlines would wash out.</summary>
    internal bool DeepSurfaces => Window.R + Window.G + Window.B < 120;
}

/// <summary>
/// The three built-in interface themes.
///
/// <list type="bullet">
/// <item><b>Sakura Nocturne</b> — the default: indigo night, blossom pink and gold. The palette of a
/// piano recital under a tree in bloom.</item>
/// <item><b>Concert Noir</b> — the original neon violet/cyan studio look.</item>
/// <item><b>Velvet Gold</b> — deep burgundy and brass, the colour of a concert grand on a lit stage.</item>
/// </list>
/// </summary>
internal static class ShellThemes
{
    internal const string DefaultId = "sakura";

    internal static readonly ShellTheme SakuraNocturne = new(
        "sakura", "Sakura Nocturne", "Indigo night, blossom pink and gold — a recital under the trees.", BackdropStyle.Sakura,
        Accent: Color.FromRgb(0xFF, 0x7B, 0xAC),
        AccentAlt: Color.FromRgb(0xFF, 0xC9, 0x6B),
        AccentSoft: Color.FromArgb(0x2E, 0xFF, 0x7B, 0xAC),
        Glow: Color.FromRgb(0x9B, 0xB6, 0xFF),
        Window: Color.FromRgb(0x08, 0x07, 0x12),
        Panel: Color.FromArgb(0xF0, 0x14, 0x12, 0x24),
        PanelTop: Color.FromArgb(0xF6, 0x1D, 0x1A, 0x31),
        PanelBottom: Color.FromArgb(0xEE, 0x11, 0x10, 0x20),
        PanelAlt: Color.FromRgb(0x11, 0x0F, 0x1E),
        Control: Color.FromRgb(0x1A, 0x17, 0x2A),
        ControlHover: Color.FromRgb(0x26, 0x21, 0x3C),
        Border: Color.FromRgb(0x2A, 0x25, 0x40),
        ControlBorder: Color.FromRgb(0x33, 0x2D, 0x4C),
        Track: Color.FromRgb(0x25, 0x20, 0x3A),
        Popup: Color.FromArgb(0xFA, 0x16, 0x13, 0x27),
        Petal: Color.FromRgb(0xFF, 0xB3, 0xCF),
        PetalAlt: Color.FromRgb(0xFF, 0xE1, 0xEE));

    internal static readonly ShellTheme ConcertNoir = new(
        "noir", "Concert Noir", "Near-black studio with a violet and cyan accent — the classic Keyflow look.", BackdropStyle.Aurora,
        Accent: Color.FromRgb(0x8B, 0x5C, 0xFF),
        AccentAlt: Color.FromRgb(0x25, 0xD0, 0xFF),
        AccentSoft: Color.FromArgb(0x2E, 0x8B, 0x5C, 0xFF),
        Glow: Color.FromRgb(0xB0, 0x92, 0xFF),
        Window: Color.FromRgb(0x07, 0x07, 0x0C),
        Panel: Color.FromArgb(0xF0, 0x0F, 0x10, 0x17),
        PanelTop: Color.FromArgb(0xF6, 0x17, 0x1A, 0x25),
        PanelBottom: Color.FromArgb(0xEE, 0x0D, 0x0E, 0x15),
        PanelAlt: Color.FromRgb(0x0D, 0x0E, 0x15),
        Control: Color.FromRgb(0x15, 0x17, 0x1F),
        ControlHover: Color.FromRgb(0x1E, 0x22, 0x30),
        Border: Color.FromRgb(0x23, 0x26, 0x33),
        ControlBorder: Color.FromRgb(0x2B, 0x2F, 0x3E),
        Track: Color.FromRgb(0x22, 0x25, 0x33),
        Popup: Color.FromArgb(0xFA, 0x10, 0x12, 0x19),
        Petal: Color.FromRgb(0xC9, 0xB6, 0xFF),
        PetalAlt: Color.FromRgb(0xE7, 0xE1, 0xFF));

    internal static readonly ShellTheme VelvetGold = new(
        "velvet", "Velvet Gold", "Burgundy velvet, brass and warm candlelight — an evening at the concert hall.", BackdropStyle.Curtain,
        Accent: Color.FromRgb(0xE8, 0xB1, 0x4C),
        AccentAlt: Color.FromRgb(0xFF, 0xE6, 0xB0),
        AccentSoft: Color.FromArgb(0x33, 0xE8, 0xB1, 0x4C),
        Glow: Color.FromRgb(0xFF, 0x9E, 0x6B),
        Window: Color.FromRgb(0x0C, 0x05, 0x08),
        Panel: Color.FromArgb(0xF0, 0x1B, 0x0D, 0x13),
        PanelTop: Color.FromArgb(0xF6, 0x27, 0x13, 0x1B),
        PanelBottom: Color.FromArgb(0xEE, 0x17, 0x0B, 0x11),
        PanelAlt: Color.FromRgb(0x16, 0x0A, 0x10),
        Control: Color.FromRgb(0x22, 0x12, 0x18),
        ControlHover: Color.FromRgb(0x30, 0x1A, 0x22),
        Border: Color.FromRgb(0x3A, 0x1E, 0x26),
        ControlBorder: Color.FromRgb(0x45, 0x25, 0x2F),
        Track: Color.FromRgb(0x33, 0x1B, 0x22),
        Popup: Color.FromArgb(0xFA, 0x1D, 0x0E, 0x14),
        Petal: Color.FromRgb(0xFF, 0xC9, 0x8A),
        PetalAlt: Color.FromRgb(0xFF, 0xE9, 0xC9));

    internal static readonly ShellTheme[] All = [SakuraNocturne, ConcertNoir, VelvetGold];

    internal static ShellTheme Default => SakuraNocturne;

    /// <summary>Resolves a stored theme name; unknown or empty values fall back to the default look.</summary>
    internal static ShellTheme Find(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? All.FirstOrDefault(theme => string.Equals(theme.Name, id?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Default;
}

/// <summary>
/// Installs the selected <see cref="ShellTheme"/> into <see cref="Application.Resources"/>.
///
/// XAML references theme tokens with <c>DynamicResource</c>, so writing a fresh brush under the same
/// key restyles every open surface at once — no window reload, no template surgery. Brushes created
/// here are never frozen (they are replaced instead of mutated), which keeps the swap safe even when
/// a template already captured the previous instance.
/// </summary>
internal static class ShellThemeManager
{
    /// <summary>Raised after a theme has been published, so the animated backdrop can restyle.</summary>
    internal static event Action<ShellTheme>? Changed;

    internal static ShellTheme Current { get; private set; } = ShellThemes.Default;

    /// <summary>Applies a theme by id or display name; returns the theme that ended up active.</summary>
    internal static ShellTheme Apply(string? id) => Apply(ShellThemes.Find(id));

    internal static ShellTheme Apply(ShellTheme theme)
    {
        Current = theme;
        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            Set(resources, "AccentBrush", new SolidColorBrush(theme.Accent));
            Set(resources, "Accent2Brush", new SolidColorBrush(theme.AccentAlt));
            Set(resources, "AccentSoftBrush", new SolidColorBrush(theme.AccentSoft));
            Set(resources, "GlowBrush", new SolidColorBrush(theme.Glow));
            Set(resources, "PetalBrush", new SolidColorBrush(theme.Petal));
            Set(resources, "AccentGradientBrush", Gradient(theme.Accent, theme.AccentAlt));
            Set(resources, "CurtainGradientBrush", Gradient(theme.Accent, theme.Glow, horizontal: true));
            Set(resources, "WindowBrush", new SolidColorBrush(theme.Window));
            Set(resources, "PanelBrush", new SolidColorBrush(theme.Panel));
            Set(resources, "PanelChromeBrush", Gradient(theme.PanelTop, theme.PanelBottom));
            Set(resources, "PanelAltBrush", new SolidColorBrush(theme.PanelAlt));
            Set(resources, "PanelBorderBrush", new SolidColorBrush(theme.Border));
            Set(resources, "ControlBrush", new SolidColorBrush(theme.Control));
            Set(resources, "ControlHoverBrush", new SolidColorBrush(theme.ControlHover));
            Set(resources, "ControlBorderBrush", new SolidColorBrush(theme.ControlBorder));
            Set(resources, "PopupBrush", new SolidColorBrush(theme.Popup));
            Set(resources, "TrackBrush", new SolidColorBrush(theme.Track));
            Set(resources, "HairlineBrush", new SolidColorBrush(Color.FromArgb(theme.DeepSurfaces ? (byte)0x1F : (byte)0x2E, 255, 255, 255)));
            Set(resources, "TopSheenBrush", Sheen(theme));
            Set(resources, "BackdropTopBrush", new SolidColorBrush(theme.PanelTop));
            Set(resources, "AccentColor", theme.Accent);
            Set(resources, "AccentColor2", theme.AccentAlt);
        }
        Changed?.Invoke(theme);
        return theme;
    }

    private static void Set(ResourceDictionary resources, string key, object value) => resources[key] = value;

    private static LinearGradientBrush Gradient(Color from, Color to, bool horizontal = false) => Freeze(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = horizontal ? new Point(1, 0) : new Point(1, 1),
        GradientStops =
        {
            new GradientStop(from, 0),
            new GradientStop(Blend(from, to, .55), .55),
            new GradientStop(to, 1)
        }
    });

    private static LinearGradientBrush Sheen(ShellTheme theme) => Freeze(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x24, theme.AccentAlt.R, theme.AccentAlt.G, theme.AccentAlt.B), 0),
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
        }
    });

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static T Freeze<T>(T freezable) where T : Freezable { if (freezable.CanFreeze) freezable.Freeze(); return freezable; }
}
