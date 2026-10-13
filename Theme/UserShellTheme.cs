using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// A shell theme the user made: five colours and a backdrop family. The full twenty-token palette of
/// <see cref="ShellTheme"/> is <em>derived</em> from those five, which is the whole point — a hand-made
/// theme cannot end up with an unreadable border or a control that is darker than its window, because
/// the relations between the surfaces are computed instead of typed in.
///
/// <para>
/// The stored file is this class, so it stays small and hand-editable: change <c>Accent</c>, restart, and
/// every surface that uses the accent follows. The derivation lives in <see cref="UserShellThemes"/>.
/// </para>
/// </summary>
internal sealed class UserShellTheme
{
    /// <summary>The name the user typed; also the base of the theme id.</summary>
    public string Name { get; set; } = "";

    /// <summary>Backdrop family: Acoustic, Obsidian or Imperial. See <see cref="BackdropStyle"/>.</summary>
    public string Backdrop { get; set; } = nameof(BackdropStyle.Acoustic);

    /// <summary>The one colour every highlight, ring and selection is painted with.</summary>
    public string Accent { get; set; } = "#D4AF37";

    /// <summary>The companion colour the accent gradients run into.</summary>
    public string AccentAlt { get; set; } = "#F5D77F";

    /// <summary>Glow used by hover states and the backdrop motes' light.</summary>
    public string Glow { get; set; } = "#FFD275";

    /// <summary>Base of every dark surface: window, panels, controls, borders and the scroll track.</summary>
    public string Surface { get; set; } = "#0B0D12";

    /// <summary>Colour of the floating backdrop motes, and the light end of the hairlines.</summary>
    public string Mote { get; set; } = "#E5C06E";
}

/// <summary>
/// The derivation from a <see cref="UserShellTheme"/> to a full <see cref="ShellTheme"/>, and the rules
/// that keep such a theme readable.
///
/// <para>
/// Every value is computed from the five seeds: <see cref="PianoVisualSettings"/>'s surfaces are painted
/// under the application's light text, so the base surface is clamped into a dark band, and the surfaces
/// above it (control, hover, border, track, popup) are progressively lightened. The checks assert those
/// relations, which means a stored theme that was hand-edited into nonsense is still repaired rather than
/// refused.
/// </para>
/// </summary>
internal static class UserShellThemes
{
    /// <summary>Prefix of the id of a theme the user made; how the pickers and the settings tell them apart.</summary>
    internal const string IdPrefix = "user-";

    /// <summary>The darkest and lightest base surface a user theme may have.</summary>
    internal const int MinSurface = 0x06;
    // The brightest derived text surface is ControlHover, which lightens the base by 30%. Keeping the
    // base at or below 0x34 keeps the default near-white text above WCAG's 4.5:1 normal-text threshold.
    internal const int MaxSurface = 0x34;

    /// <summary>Minimum contrast for a non-text accent mark against its surface.</summary>
    internal const double MinAccentContrastRatio = 3.0;
    /// <summary>Minimum contrast for normal-size text on a derived surface.</summary>
    internal const double MinTextContrastRatio = 4.5;

    internal static readonly Color DefaultAccent = Color.FromRgb(0xD4, 0xAF, 0x37);
    internal static readonly Color DefaultText = Color.FromRgb(0xF4, 0xF5, 0xF9);

    /// <summary>True when a theme id belongs to a theme the user made.</summary>
    internal static bool IsUserTheme(string? id) => id?.TrimStart().StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The id a theme with this name gets: stable, lowercase, no characters a file name cannot hold.</summary>
    internal static string Id(string name)
    {
        var slug = new string((name ?? "").Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        if (slug.Length == 0) slug = "custom";
        return IdPrefix + (slug.Length > 32 ? slug[..32].Trim('-') : slug);
    }

    /// <summary>The backdrop family of a stored value, defaulting to acoustic for anything unknown.</summary>
    internal static BackdropStyle Backdrop(string? stored) =>
        Enum.TryParse<BackdropStyle>(stored?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : BackdropStyle.Acoustic;

    /// <summary>The tooltip of a user theme: a key, so it translates like the built-in blurbs do.</summary>
    internal static string BlurbKey(BackdropStyle backdrop) => backdrop switch
    {
        BackdropStyle.Obsidian => "Custom interface theme over the obsidian backdrop.",
        BackdropStyle.Imperial => "Custom interface theme over the imperial backdrop.",
        _ => "Custom interface theme over the acoustic backdrop."
    };

    /// <summary>
    /// The palette of a user theme. <paramref name="theme"/> is repaired first, so a file that was edited
    /// by hand (a missing colour, a surface that is nearly white) still produces a usable chrome.
    /// </summary>
    internal static ShellTheme Build(UserShellTheme theme)
    {
        var surface = ClampSurface(ReadColour(theme.Surface, Color.FromRgb(0x0B, 0x0D, 0x12)));
        var accent = ReadColour(theme.Accent, DefaultAccent);
        if (!HasContrast(accent, surface)) accent = DefaultAccent;
        var accentAlt = ReadColour(theme.AccentAlt, Lighten(accent, .35));
        if (!HasContrast(accentAlt, surface)) accentAlt = Lighten(accent, .35);
        // A single foreground is drawn over the whole accent gradient. If black and white both lose
        // 4.5:1 somewhere along it, keep the accent readable by collapsing the companion back to it.
        if (!HasReadableAccentText(accent, accentAlt)) accentAlt = accent;
        var glow = ReadColour(theme.Glow, accentAlt);
        var mote = ReadColour(theme.Mote, accentAlt);
        var backdrop = Backdrop(theme.Backdrop);
        var name = string.IsNullOrWhiteSpace(theme.Name) ? "Custom theme" : theme.Name.Trim();
        return new ShellTheme(
            Id(name), name, BlurbKey(backdrop), backdrop,
            Accent: accent,
            AccentAlt: accentAlt,
            AccentSoft: Color.FromArgb(0x30, accent.R, accent.G, accent.B),
            Glow: glow,
            Window: Darken(surface, .55),
            Panel: Alpha(Lighten(surface, .10), 0xF0),
            PanelTop: Alpha(Lighten(surface, .22), 0xF6),
            PanelBottom: Alpha(surface, 0xEE),
            PanelAlt: Lighten(surface, .06),
            Control: surface,
            ControlHover: Lighten(surface, .30),
            Border: Lighten(surface, .45),
            ControlBorder: Lighten(surface, .60),
            Track: Lighten(surface, .14),
            Popup: Alpha(Lighten(surface, .05), 0xFA),
            Mote: mote,
            MoteAlt: Lighten(mote, .35));
    }

    /// <summary>Seeds taken from an existing theme, so the studio opens on the look the user is running.</summary>
    internal static UserShellTheme FromTheme(ShellTheme theme, string name) => new()
    {
        Name = name,
        Backdrop = theme.Backdrop.ToString(),
        Accent = Hex(theme.Accent),
        AccentAlt = Hex(theme.AccentAlt),
        Glow = Hex(theme.Glow),
        Surface = Hex(theme.Control),
        Mote = Hex(theme.Mote)
    };

    /// <summary>The colour a stored hex string names, or <paramref name="fallback"/> when it names none.</summary>
    internal static Color ReadColour(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(hex.Trim())!;
            // Theme seeds are opaque RGB values. Hand-edited #AARRGGBB values are accepted for recovery,
            // but their alpha is discarded so a transparent accent cannot make the chrome disappear.
            return Color.FromRgb(parsed.R, parsed.G, parsed.B);
        }
        catch { return fallback; }
    }

    /// <summary>The <c>#RRGGBB</c> text of a colour, which is what a theme file stores.</summary>
    internal static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    /// <summary>True when an accent has the 3:1 contrast needed for a non-text visual cue.</summary>
    internal static bool HasContrast(Color accent, Color surface) =>
        ContrastRatio(accent, surface) >= MinAccentContrastRatio;

    /// <summary>True when the fixed near-white chrome text remains readable on a derived surface.</summary>
    internal static bool HasReadableText(Color surface) =>
        ContrastRatio(DefaultText, surface) >= MinTextContrastRatio;

    /// <summary>True when one black-or-white foreground stays readable across the full accent gradient.</summary>
    internal static bool HasReadableAccentText(Color accent, Color accentAlt)
    {
        var middle = Blend(accent, accentAlt, .55);
        var black = Math.Min(ContrastRatio(Colors.Black, accent), Math.Min(ContrastRatio(Colors.Black, middle), ContrastRatio(Colors.Black, accentAlt)));
        var white = Math.Min(ContrastRatio(Colors.White, accent), Math.Min(ContrastRatio(Colors.White, middle), ContrastRatio(Colors.White, accentAlt)));
        return Math.Max(black, white) >= MinTextContrastRatio;
    }

    /// <summary>Best black-or-white foreground for the full accent gradient.</summary>
    internal static Color AccentButtonText(Color accent, Color accentAlt)
    {
        var middle = Blend(accent, accentAlt, .55);
        var white = Math.Min(ContrastRatio(Colors.White, accent), Math.Min(ContrastRatio(Colors.White, middle), ContrastRatio(Colors.White, accentAlt)));
        var black = Math.Min(ContrastRatio(Colors.Black, accent), Math.Min(ContrastRatio(Colors.Black, middle), ContrastRatio(Colors.Black, accentAlt)));
        return black >= white ? Colors.Black : Colors.White;
    }

    /// <summary>WCAG relative-luminance contrast ratio for two opaque sRGB colours.</summary>
    internal static double ContrastRatio(Color foreground, Color background)
    {
        static double Luminance(Color colour)
        {
            static double Linear(byte channel)
            {
                var value = channel / 255.0;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(colour.R) + .7152 * Linear(colour.G) + .0722 * Linear(colour.B);
        }

        var first = Luminance(foreground);
        var second = Luminance(background);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + .05) / (darker + .05);
    }

    private static Color ClampSurface(Color colour)
    {
        // The chrome is painted with light text, so the base surface is held inside a dark band: the brightest
        // channel decides, and the whole colour is scaled to fit instead of being clipped per channel.
        var peak = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        if (peak > MaxSurface)
        {
            var scale = (double)MaxSurface / peak;
            colour = Color.FromRgb((byte)(colour.R * scale), (byte)(colour.G * scale), (byte)(colour.B * scale));
        }
        var floor = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        if (floor < MinSurface)
        {
            var lift = MinSurface - floor;
            colour = Color.FromRgb((byte)Math.Min(255, colour.R + lift), (byte)Math.Min(255, colour.G + lift), (byte)Math.Min(255, colour.B + lift));
        }
        return colour;
    }

    private static Color Blend(Color first, Color second, double amount) => Color.FromRgb(
        (byte)(first.R + (second.R - first.R) * amount),
        (byte)(first.G + (second.G - first.G) * amount),
        (byte)(first.B + (second.B - first.B) * amount));

    private static Color Lighten(Color colour, double amount) => Color.FromRgb(
        (byte)(colour.R + (255 - colour.R) * amount),
        (byte)(colour.G + (255 - colour.G) * amount),
        (byte)(colour.B + (255 - colour.B) * amount));

    private static Color Darken(Color colour, double amount) => Color.FromRgb(
        (byte)(colour.R * (1 - amount)),
        (byte)(colour.G * (1 - amount)),
        (byte)(colour.B * (1 - amount)));

    private static Color Alpha(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);
}
