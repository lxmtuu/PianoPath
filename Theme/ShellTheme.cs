using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Which family of animated chrome backdrop a theme draws behind the interface.</summary>
internal enum BackdropStyle
{
    /// <summary>Harmonic acoustic standing resonance waves and delicate concert dust motes.</summary>
    Acoustic,
    /// <summary>Deep obsidian recital hall with silvery acoustic waves and stardust motes.</summary>
    Obsidian,
    /// <summary>Rich mahogany concert hall with warm burnished brass reflections and golden dust.</summary>
    Imperial
}

/// <summary>
/// A complete look for the interface chrome: surfaces, accents and the animated backdrop family.
///
/// The stage itself is themed by <see cref="PianoVisualSettings"/>; this record only describes the
/// frame around it, which keeps "how the piano looks" and "how the app looks" independent — you can
/// run a Concert Grand stage inside the Obsidian shell or the other way round.
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
    Color Mote,
    Color MoteAlt)
{
    /// <summary>True when the palette is light enough that white hairlines would wash out.</summary>
    internal bool DeepSurfaces => Window.R + Window.G + Window.B < 120;
}

/// <summary>
/// The three built-in piano concert interface themes.
///
/// <list type="bullet">
/// <item><b>Concert Grand</b> — the flagship concert look: Steinway ebony lacquer, warm champagne gold and ivory sheen.</item>
/// <item><b>Concert Noir</b> — midnight obsidian slate with silvery acoustic platinum accents.</item>
/// <item><b>Velvet Gold</b> — deep mahogany, imperial concert velvet and burnished antique brass.</item>
/// </list>
///
/// An id is a stable slug derived from the name, so stored settings and presets read the same as the
/// picker does. Older releases stored <c>sakura</c>, <c>noir</c> and <c>velvet</c>; those legacy ids
/// (and the retired "Sakura Nocturne" display name) are still resolved through
/// <see cref="LegacyIds"/> and rewritten to the canonical id by
/// <see cref="PianoVisualSettings.ApplyMigrations"/>, so no saved look is lost.
/// </summary>
internal static class ShellThemes
{
    internal const string ConcertGrandId = "concert-grand";
    internal const string ConcertNoirId = "concert-noir";
    internal const string VelvetGoldId = "velvet-gold";

    internal const string DefaultId = ConcertGrandId;

    /// <summary>The palette published while Windows runs in high-contrast mode; not a user choice.</summary>
    internal const string HighContrastId = "high-contrast";

    /// <summary>Deprecated ids and display names from earlier releases, mapped to the current theme.</summary>
    internal static readonly Dictionary<string, string> LegacyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sakura"] = ConcertGrandId,
        ["sakura-nocturne"] = ConcertGrandId,
        ["Sakura Nocturne"] = ConcertGrandId,
        ["noir"] = ConcertNoirId,
        ["velvet"] = VelvetGoldId,
    };

    internal static readonly ShellTheme ConcertGrand = new(
        ConcertGrandId, "Concert Grand", "Steinway ebony lacquer & warm champagne gold — the prestigious concert grand look.", BackdropStyle.Acoustic,
        Accent: Color.FromRgb(0xD4, 0xAF, 0x37),
        AccentAlt: Color.FromRgb(0xF5, 0xD7, 0x7F),
        AccentSoft: Color.FromArgb(0x30, 0xD4, 0xAF, 0x37),
        Glow: Color.FromRgb(0xFF, 0xD2, 0x75),
        Window: Color.FromRgb(0x06, 0x07, 0x0A),
        Panel: Color.FromArgb(0xF0, 0x0B, 0x0D, 0x12),
        PanelTop: Color.FromArgb(0xF6, 0x14, 0x18, 0x22),
        PanelBottom: Color.FromArgb(0xEE, 0x09, 0x0B, 0x10),
        PanelAlt: Color.FromRgb(0x0F, 0x12, 0x18),
        Control: Color.FromRgb(0x12, 0x15, 0x1D),
        ControlHover: Color.FromRgb(0x1C, 0x20, 0x2C),
        Border: Color.FromRgb(0x26, 0x2B, 0x3B),
        ControlBorder: Color.FromRgb(0x32, 0x38, 0x4D),
        Track: Color.FromRgb(0x1C, 0x20, 0x2C),
        Popup: Color.FromArgb(0xFA, 0x0E, 0x10, 0x17),
        Mote: Color.FromRgb(0xE5, 0xC0, 0x6E),
        MoteAlt: Color.FromRgb(0xFF, 0xF0, 0xC2));

    internal static readonly ShellTheme ConcertNoir = new(
        ConcertNoirId, "Concert Noir", "Midnight obsidian slate with acoustic sapphire and silvery platinum accents.", BackdropStyle.Obsidian,
        Accent: Color.FromRgb(0x6C, 0x8D, 0xF0),
        AccentAlt: Color.FromRgb(0xA8, 0xC2, 0xFB),
        AccentSoft: Color.FromArgb(0x2E, 0x6C, 0x8D, 0xF0),
        Glow: Color.FromRgb(0x8F, 0xAE, 0xFF),
        Window: Color.FromRgb(0x05, 0x06, 0x09),
        Panel: Color.FromArgb(0xF0, 0x0A, 0x0C, 0x11),
        PanelTop: Color.FromArgb(0xF6, 0x12, 0x15, 0x20),
        PanelBottom: Color.FromArgb(0xEE, 0x08, 0x09, 0x0D),
        PanelAlt: Color.FromRgb(0x0C, 0x0E, 0x14),
        Control: Color.FromRgb(0x11, 0x14, 0x1C),
        ControlHover: Color.FromRgb(0x1A, 0x1E, 0x2B),
        Border: Color.FromRgb(0x22, 0x28, 0x38),
        ControlBorder: Color.FromRgb(0x2C, 0x34, 0x4A),
        Track: Color.FromRgb(0x18, 0x1D, 0x29),
        Popup: Color.FromArgb(0xFA, 0x0D, 0x0F, 0x14),
        Mote: Color.FromRgb(0xA8, 0xC2, 0xFB),
        MoteAlt: Color.FromRgb(0xE0, 0xEB, 0xFF));

    internal static readonly ShellTheme VelvetGold = new(
        VelvetGoldId, "Velvet Gold", "Rich mahogany, imperial concert velvet and burnished antique brass.", BackdropStyle.Imperial,
        Accent: Color.FromRgb(0xE5, 0xA9, 0x3C),
        AccentAlt: Color.FromRgb(0xFF, 0xD4, 0x80),
        AccentSoft: Color.FromArgb(0x35, 0xE5, 0xA9, 0x3C),
        Glow: Color.FromRgb(0xFF, 0xA7, 0x4D),
        Window: Color.FromRgb(0x0B, 0x06, 0x08),
        Panel: Color.FromArgb(0xF0, 0x15, 0x0B, 0x10),
        PanelTop: Color.FromArgb(0xF6, 0x20, 0x10, 0x18),
        PanelBottom: Color.FromArgb(0xEE, 0x11, 0x08, 0x0D),
        PanelAlt: Color.FromRgb(0x14, 0x0A, 0x0F),
        Control: Color.FromRgb(0x1D, 0x0F, 0x16),
        ControlHover: Color.FromRgb(0x2A, 0x16, 0x20),
        Border: Color.FromRgb(0x36, 0x1D, 0x29),
        ControlBorder: Color.FromRgb(0x44, 0x25, 0x34),
        Track: Color.FromRgb(0x27, 0x14, 0x1E),
        Popup: Color.FromArgb(0xFA, 0x17, 0x0C, 0x12),
        Mote: Color.FromRgb(0xFF, 0xD4, 0x80),
        MoteAlt: Color.FromRgb(0xFF, 0xF2, 0xD1));

    internal static readonly ShellTheme[] All = [ConcertGrand, ConcertNoir, VelvetGold];

    internal static ShellTheme Default => ConcertGrand;

    /// <summary>
    /// Everything the pickers offer: the built-in themes first, then the ones the user made
    /// (<see cref="UserThemeStore"/>), which is also exactly what <see cref="Find"/> resolves.
    /// </summary>
    internal static IEnumerable<ShellTheme> Everything => All.Concat(UserThemeStore.Default.Load());

    /// <summary>
    /// Resolves a stored theme name: canonical or legacy id first, then the display name; unknown or
    /// empty values fall back to the default look.
    /// </summary>
    internal static ShellTheme Find(string? id)
    {
        var value = id?.Trim();
        if (string.IsNullOrEmpty(value)) return Default;
        if (LegacyIds.TryGetValue(value, out var canonical)) value = canonical;
        var themes = Everything.ToList();
        return themes.FirstOrDefault(theme => string.Equals(theme.Id, value, StringComparison.OrdinalIgnoreCase))
            ?? themes.FirstOrDefault(theme => string.Equals(theme.Name, value, StringComparison.OrdinalIgnoreCase))
            ?? Default;
    }

    /// <summary>Canonical id for a stored value; what the settings file and presets are rewritten to.</summary>
    internal static string Normalize(string? id) => Find(id).Id;

    /// <summary>
    /// The palette used when Windows is in high-contrast mode. It is built from <see cref="SystemColors"/>
    /// at call time (not cached) because those colours change with the system theme, and it deliberately
    /// keeps the stage settings alone: high contrast restyles the chrome, never the user's piano.
    /// </summary>
    internal static ShellTheme HighContrast() => new(
        HighContrastId, "High Contrast",
        "Windows high-contrast colours: system window, text, control and highlight colours replace the concert palette.",
        BackdropStyle.Obsidian,
        Accent: SystemColors.HighlightColor,
        AccentAlt: SystemColors.HotTrackColor,
        AccentSoft: Color.FromArgb(0x40, SystemColors.HighlightColor.R, SystemColors.HighlightColor.G, SystemColors.HighlightColor.B),
        Glow: SystemColors.HotTrackColor,
        Window: SystemColors.WindowColor,
        Panel: SystemColors.ControlColor,
        PanelTop: SystemColors.ControlColor,
        PanelBottom: SystemColors.ControlColor,
        PanelAlt: SystemColors.ControlColor,
        Control: SystemColors.ControlColor,
        ControlHover: SystemColors.ControlLightColor,
        Border: SystemColors.ControlDarkColor,
        ControlBorder: SystemColors.ControlDarkDarkColor,
        Track: SystemColors.ControlLightColor,
        Popup: SystemColors.WindowColor,
        Mote: SystemColors.GrayTextColor,
        MoteAlt: SystemColors.WindowColor);
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

    /// <summary>
    /// True while the chrome is painted from <see cref="ShellThemes.HighContrast"/> instead of the
    /// chosen concert theme. It follows Windows automatically; <see cref="ForceHighContrast"/> lets
    /// <c>--verify</c> exercise the branch on a runner that is not actually in high-contrast mode.
    /// </summary>
    internal static bool IsHighContrast => ForceHighContrast || SystemParameters.HighContrast;

    /// <summary>Test hook for the high-contrast branch; never set outside <c>--verify</c>.</summary>
    internal static bool ForceHighContrast { get; set; }

    /// <summary>
    /// Windows raises <see cref="SystemParameters.StaticPropertyChanged"/> when an accessibility setting
    /// changes. High contrast decides which palette wins, and the chrome has to repaint while the user is
    /// looking at it, so the manager subscribes once for the life of the process.
    /// </summary>
    static ShellThemeManager() => SystemParameters.StaticPropertyChanged += (_, e) => OnSystemParametersChanged(e.PropertyName);

    /// <summary>
    /// The body of that hook. Split out of the event handler so <c>--verify</c> can raise the
    /// notification on a runner whose Windows never actually switches to high contrast.
    /// </summary>
    internal static void OnSystemParametersChanged(string? propertyName)
    {
        // A null name means "something changed, the exact property is unknown" and is treated as ours;
        // every other name is only ours when it is the contrast switch, because repainting on each
        // system-parameter change would rebuild the whole palette whenever Windows reports anything.
        if (propertyName is not (null or nameof(SystemParameters.HighContrast))) return;
        // The notification does not have to arrive on the interface thread, and the palette lives in the
        // application resources, so the repaint is posted to the dispatcher when it did not.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(() => Apply(Current))); return; }
        Apply(Current);
    }

    /// <summary>Applies a theme by id or display name; returns the theme that ended up active.</summary>
    internal static ShellTheme Apply(string? id) => Apply(ShellThemes.Find(id));

    internal static ShellTheme Apply(ShellTheme theme)
    {
        Current = theme;
        // High contrast wins over the concert palette while it is on, but the user's choice is kept in
        // Current: turning the system setting off (and re-applying) brings the chosen theme straight back.
        var published = IsHighContrast ? ShellThemes.HighContrast() : theme;
        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            Set(resources, "AccentBrush", new SolidColorBrush(published.Accent));
            Set(resources, "Accent2Brush", new SolidColorBrush(published.AccentAlt));
            Set(resources, "AccentSoftBrush", new SolidColorBrush(published.AccentSoft));
            Set(resources, "GlowBrush", new SolidColorBrush(published.Glow));
            Set(resources, "MoteBrush", new SolidColorBrush(published.Mote));
            Set(resources, "AccentGradientBrush", Gradient(published.Accent, published.AccentAlt));
            Set(resources, "CurtainGradientBrush", Gradient(published.Accent, published.Glow, horizontal: true));
            Set(resources, "WindowBrush", new SolidColorBrush(published.Window));
            Set(resources, "PanelBrush", new SolidColorBrush(published.Panel));
            Set(resources, "PanelChromeBrush", Gradient(published.PanelTop, published.PanelBottom));
            Set(resources, "PanelAltBrush", new SolidColorBrush(published.PanelAlt));
            Set(resources, "PanelBorderBrush", new SolidColorBrush(published.Border));
            Set(resources, "ControlBrush", new SolidColorBrush(published.Control));
            Set(resources, "ControlHoverBrush", new SolidColorBrush(published.ControlHover));
            Set(resources, "ControlBorderBrush", new SolidColorBrush(published.ControlBorder));
            Set(resources, "PopupBrush", new SolidColorBrush(published.Popup));
            Set(resources, "TrackBrush", new SolidColorBrush(published.Track));
            Set(resources, "HairlineBrush", new SolidColorBrush(Color.FromArgb(published.DeepSurfaces ? (byte)0x1F : (byte)0x2E, 255, 255, 255)));
            Set(resources, "TopSheenBrush", Sheen(published));
            Set(resources, "BackdropTopBrush", new SolidColorBrush(published.PanelTop));
            Set(resources, "AccentColor", published.Accent);
            Set(resources, "AccentColor2", published.AccentAlt);
        }
        Changed?.Invoke(published);
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
            new GradientStop(Color.FromArgb(0x28, theme.AccentAlt.R, theme.AccentAlt.G, theme.AccentAlt.B), 0),
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
        }
    });

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static T Freeze<T>(T freezable) where T : Freezable { if (freezable.CanFreeze) freezable.Freeze(); return freezable; }
}
