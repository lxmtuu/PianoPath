using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The theme studio: name a theme the user made, pick its backdrop family and its five seed colours, and
/// watch the derived chrome update while typing.
///
/// <para>
/// Only the seeds are edited — the twenty tokens of the palette come from
/// <see cref="UserShellThemes.Build"/> — which is what keeps a hand-made theme coherent. The preview strip
/// shows the surfaces that follow from the seeds, so the choice is visible before the theme is saved.
/// </para>
/// </summary>
internal sealed class ThemeStudioWindow : Window
{
    private static readonly (string Property, string Label, string Tooltip)[] Fields =
    [
        (nameof(UserShellTheme.Accent), "Accent", "Every highlight, ring and selection is painted with this colour."),
        (nameof(UserShellTheme.AccentAlt), "Accent companion", "The colour the accent gradients run into."),
        (nameof(UserShellTheme.Glow), "Glow", "Hover states and the light of the backdrop motes."),
        (nameof(UserShellTheme.Surface), "Surface", "Base of the window, panels, controls and borders. Kept dark so the text stays readable."),
        (nameof(UserShellTheme.Mote), "Mote", "The floating background particles and the light end of the hairlines."),
    ];

    private readonly Dictionary<string, TextBox> _colourBoxes = [];
    private readonly Dictionary<string, Border> _swatches = [];
    private readonly StackPanel _preview = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _message;
    private readonly TextBox _nameBox;
    private readonly ComboBox _backdropBox;

    internal TextBox NameBox => _nameBox;
    internal ComboBox BackdropBox => _backdropBox;
    internal FrameworkElement PreviewHost => _preview;
    internal string? Message => _message.Text;

    /// <summary>The colour box of a seed, so the checks can drive the dialog without a mouse.</summary>
    internal TextBox ColourBox(string property) => _colourBoxes[property];

    /// <summary>The theme the studio will save, or null while it is not valid yet.</summary>
    internal UserShellTheme? Result { get; private set; }

    internal ThemeStudioWindow(UserShellTheme seed, string titleKey)
    {
        Title = Loc.T(titleKey);
        Width = 430; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.ToolWindow; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(18, 16, 24)); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        var heading = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(231, 218, 244)), Margin = new Thickness(0, 0, 0, 12) };
        Loc.Set(heading, "THEME STUDIO");
        root.Children.Add(heading);

        _nameBox = new TextBox
        {
            Text = seed.Name, FontSize = 12, MaxLength = 32, Height = 30, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 10),
            VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(29, 25, 36)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(79, 65, 97))
        };
        Loc.Set(_nameBox, "Name", AutomationProperties.NameProperty);
        root.Children.Add(Row("Name", _nameBox));

        _backdropBox = new ComboBox { Width = 150, Height = 30, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Center };
        Loc.Set(_backdropBox, "Backdrop", AutomationProperties.NameProperty);
        foreach (var style in new[] { BackdropStyle.Acoustic, BackdropStyle.Obsidian, BackdropStyle.Imperial })
            _backdropBox.Items.Add(new ComboBoxItem { Content = Loc.T(style.ToString()), Tag = style });
        _backdropBox.SelectedIndex = Math.Max(0, Array.IndexOf(Enum.GetValues<BackdropStyle>(), UserShellThemes.Backdrop(seed.Backdrop)));
        root.Children.Add(Row("Backdrop", _backdropBox));

        foreach (var (property, label, tooltip) in Fields) root.Children.Add(ColourRow(property, label, tooltip, SeedValue(seed, property)));

        var previewCaption = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)), Margin = new Thickness(0, 4, 0, 4) };
        Loc.Set(previewCaption, "PREVIEW");
        root.Children.Add(previewCaption);
        root.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(12, 11, 16)), BorderBrush = new SolidColorBrush(Color.FromRgb(60, 52, 74)), BorderThickness = new Thickness(1),
            Child = _preview
        });

        _message = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(233, 160, 160)), Margin = new Thickness(0, 2, 0, 0) };
        root.Children.Add(_message);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = MakeButton("Cancel", false); cancel.Margin = new Thickness(0, 0, 8, 0); cancel.Click += (_, _) => DialogResult = false;
        var save = MakeButton("Save theme", true); save.IsDefault = true; save.Click += (_, _) => TrySave();
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
        RefreshPreview();
    }

    /// <summary>
    /// Reads the fields and, when they are valid, sets <see cref="Result"/>. Returns the message key to
    /// print when they are not — the dialog stays open instead of saving a theme without a name or with a
    /// colour that is not a colour.
    /// </summary>
    internal bool TryBuild(out UserShellTheme? theme, out string? errorKey)
    {
        theme = null; errorKey = null;
        var name = _nameBox.Text.Trim();
        if (name.Length == 0) { errorKey = "A theme needs a name."; return false; }
        var seeds = new Dictionary<string, string>();
        foreach (var (property, _, _) in Fields)
        {
            var text = _colourBoxes[property].Text.Trim();
            if (!IsHex(text)) { errorKey = "One of the colours is not a hex value like #1A2B3C."; return false; }
            seeds[property] = text;
        }
        theme = new UserShellTheme
        {
            Name = name,
            Backdrop = (_backdropBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? nameof(BackdropStyle.Acoustic),
            Accent = seeds[nameof(UserShellTheme.Accent)],
            AccentAlt = seeds[nameof(UserShellTheme.AccentAlt)],
            Glow = seeds[nameof(UserShellTheme.Glow)],
            Surface = seeds[nameof(UserShellTheme.Surface)],
            Mote = seeds[nameof(UserShellTheme.Mote)]
        };
        return true;
    }

    private void TrySave()
    {
        if (!TryBuild(out var theme, out var errorKey)) { Loc.Set(_message, errorKey!); return; }
        Result = theme;
        DialogResult = true;
    }

    /// <summary>Every colour the palette derives, as a strip of swatches under the seed fields.</summary>
    internal void RefreshPreview()
    {
        if (!TryBuild(out var theme, out _)) { RefreshPreviewFromSeeds(); return; }
        ShowPreview(UserShellThemes.Build(theme!));
    }

    /// <summary>While a field is mid-edit, the preview follows whatever the seeds currently mean.</summary>
    private void RefreshPreviewFromSeeds()
    {
        var seeds = new UserShellTheme
        {
            Name = _nameBox.Text,
            Backdrop = (_backdropBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? nameof(BackdropStyle.Acoustic),
            Accent = Colour(nameof(UserShellTheme.Accent)),
            AccentAlt = Colour(nameof(UserShellTheme.AccentAlt)),
            Glow = Colour(nameof(UserShellTheme.Glow)),
            Surface = Colour(nameof(UserShellTheme.Surface)),
            Mote = Colour(nameof(UserShellTheme.Mote))
        };
        ShowPreview(UserShellThemes.Build(seeds));
    }

    private void ShowPreview(ShellTheme theme)
    {
        _preview.Children.Clear();
        foreach (var (colour, label) in new (Color, string)[]
        {
            (theme.Window, "Window"), (theme.PanelAlt, "Panel"), (theme.Control, "Control"), (theme.ControlHover, "Hover"),
            (theme.Border, "Border"), (theme.Track, "Track"), (theme.Popup, "Popup"),
            (theme.Accent, "Accent"), (theme.AccentAlt, "Accent 2"), (theme.Glow, "Glow"), (theme.Mote, "Mote")
        })
        {
            var swatch = new Border
            {
                Width = 30, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(colour), BorderBrush = new SolidColorBrush(Color.FromRgb(70, 62, 86)), BorderThickness = new Thickness(1)
            };
            // The token name travels as a key, so the strip is translated with the rest of the studio.
            Loc.Set(swatch, label, FrameworkElement.ToolTipProperty);
            AutomationProperties.SetName(swatch, Loc.T(label));
            _preview.Children.Add(swatch);
        }
    }

    private string Colour(string property) => IsHex(_colourBoxes[property].Text.Trim()) ? _colourBoxes[property].Text.Trim() : SeedValue(new UserShellTheme(), property);

    private static string SeedValue(UserShellTheme seed, string property) => property switch
    {
        nameof(UserShellTheme.Accent) => seed.Accent,
        nameof(UserShellTheme.AccentAlt) => seed.AccentAlt,
        nameof(UserShellTheme.Glow) => seed.Glow,
        nameof(UserShellTheme.Surface) => seed.Surface,
        _ => seed.Mote
    };

    private static bool IsHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '#') return false;
        var digits = text[1..];
        return digits.Length is 6 or 8 && digits.All(Uri.IsHexDigit);
    }

    private FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = Loc.T(label), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(200, 190, 215)) };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text); grid.Children.Add(control);
        return grid;
    }

    private FrameworkElement ColourRow(string property, string label, string tooltip, string initial)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new Border
        {
            Width = 30, Height = 26, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(UserShellThemes.ReadColour(initial, Colors.Gray)), BorderBrush = new SolidColorBrush(Color.FromRgb(120, 110, 140)), BorderThickness = new Thickness(1)
        };
        var button = new Button { Width = 30, Height = 26, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), Tag = property };
        Loc.Set(button, "Open color picker", FrameworkElement.ToolTipProperty);
        Loc.Set(button, label, AutomationProperties.NameProperty);
        var box = new TextBox
        {
            Text = initial, Width = 96, Height = 26, FontSize = 10.5, MaxLength = 9, Tag = property,
            CharacterCasing = CharacterCasing.Upper, VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(29, 25, 36)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(79, 65, 97))
        };
        Loc.Set(box, label, AutomationProperties.NameProperty);
        button.Click += (_, _) =>
        {
            var picker = new ColorPickerWindow(box.Text) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedHex is { } hex) { box.Text = hex; Update(property); }
        };
        box.TextChanged += (_, _) => Update(property);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; TrySave(); } };
        _colourBoxes[property] = box; _swatches[property] = swatch;
        panel.Children.Add(button); panel.Children.Add(swatch); panel.Children.Add(box);
        return Row(label, panel);
    }

    private void Update(string property)
    {
        var colour = UserShellThemes.ReadColour(_colourBoxes[property].Text, Colors.Gray);
        if (_swatches.TryGetValue(property, out var swatch)) swatch.Background = new SolidColorBrush(colour);
        RefreshPreview();
    }

    private static Button MakeButton(string label, bool primary) => new()
    {
        Content = Loc.T(label), Height = 30, MinWidth = 96, Padding = new Thickness(12, 0, 12, 0),
        Foreground = Brushes.White,
        Background = new SolidColorBrush(primary ? Color.FromRgb(125, 61, 174) : Color.FromRgb(43, 38, 51)),
        BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(184, 119, 229) : Color.FromRgb(80, 71, 91)),
        BorderThickness = new Thickness(1)
    };
}
