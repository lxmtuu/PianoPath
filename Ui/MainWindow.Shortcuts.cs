using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The keyboard &amp; shortcuts help card (F1).
///
/// Keyflow hides its chrome after a few idle seconds, which is deliberate — but it also means a new
/// user can lose the toolbar and never learn that Escape reopens the dock or that the letter keys are
/// a piano. The card documents the real bindings of <see cref="MainWindow.Window_KeyDown"/> and of the
/// transport, and <c>VerificationSuite</c> asserts both the card and the bindings behind it.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// The three columns of the card. Key labels are printed verbatim, so they must stay in sync with
    /// the key handling in <see cref="Window_KeyDown"/> (checked by the verification suite).
    /// </summary>
    private static readonly (string Title, (string Keys, string Text)[] Rows)[] ShortcutGroups =
    [
        ("PLAY THE STAGE", [
            ("A W S E D F T G Y H U J K", "Play one octave from middle C. Hold a key for a sustained note and release it to send Note Off."),
            ("Space", "Start or pause the loaded MIDI score."),
            ("Click the keys", "Press the on-screen keys with the mouse; the same colour, spark and flame response as a real keyboard."),
            ("MIDI keyboard", "A connected instrument plays through the same note pipeline, with velocity and all three pedals."),
        ]),
        ("MOVE AROUND", [
            ("F11", "Toggle full screen."),
            ("F1", "Open or close this card."),
            ("Esc", "Open or close the design dock; it clears the settings search box first."),
            ("Pointer idle", "After 2.8 s of stillness the header, transport, dock and REC button hide so only the stage is left. Move the mouse to bring them back."),
        ]),
        ("SESSION & CAPTURE", [
            ("A · B · ×", "Set the loop start and end at the playhead, or clear the A–B loop."),
            ("Drag the timeline", "Seek anywhere in the score. Skipped notes are not counted as misses."),
            ("Practice modes", "Follow along, wait for my note, or train one hand at a time (Practice page)."),
            ("REC", "Record the stage to AVI at the resolution and frame rate chosen on the Recording page."),
        ]),
    ];

    private bool _shortcutCardBuilt;

    /// <summary>True while the help card is on screen; the idle timer leaves the chrome alone then.</summary>
    internal bool ShortcutsVisible => ShortcutOverlay.Visibility == Visibility.Visible;

    /// <summary>Shows the help card (used by F1, the main menu link and <c>--shortcuts</c> captures).</summary>
    internal void ShowShortcuts()
    {
        BuildShortcutCard();
        SetChromeVisible(true);
        ShortcutOverlay.Visibility = Visibility.Visible;
        ChromeMotion.FadeIn(ShortcutOverlay, 180);
        ChromeMotion.PopIn(ShortcutCard);
        _lastPointerActivity = DateTime.UtcNow;
    }

    internal void HideShortcuts()
    {
        if (ShortcutOverlay.Visibility != Visibility.Visible) return;
        ShortcutOverlay.Visibility = Visibility.Collapsed;
        _lastPointerActivity = DateTime.UtcNow;
        if (ShortcutOverlay.IsKeyboardFocusWithin) Stage.Focus();
    }

    internal void ToggleShortcuts()
    {
        if (ShortcutsVisible) HideShortcuts();
        else ShowShortcuts();
    }

    /// <summary>The card is generated once, on first use, from <see cref="ShortcutGroups"/>.</summary>
    private void BuildShortcutCard()
    {
        if (_shortcutCardBuilt) return;
        _shortcutCardBuilt = true;
        foreach (var (title, rows) in ShortcutGroups)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            var heading = new TextBlock { Style = (Style)FindResource("EyebrowTextStyle"), Margin = new Thickness(0, 0, 0, 10) };
            Loc.Set(heading, title);
            column.Children.Add(heading);
            foreach (var (keys, text) in rows)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var chip = new Border
                {
                    Background = (Brush)FindResource("ControlBrush"),
                    BorderBrush = (Brush)FindResource("ControlBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = keys,
                        FontFamily = new FontFamily("Consolas, Segoe UI"),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                var description = new TextBlock
                {
                    Style = (Style)FindResource("MutedTextStyle"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                Loc.Set(description, text);
                Grid.SetColumn(description, 1);
                grid.Children.Add(chip);
                grid.Children.Add(description);
                column.Children.Add(grid);
            }
            ShortcutGrid.Children.Add(column);
        }
    }

    /// <summary>Re-generates the card in the new language; the key chips themselves never change.</summary>
    private void RefreshShortcutCard()
    {
        if (!_shortcutCardBuilt) return;
        ShortcutGrid.Children.Clear();
        _shortcutCardBuilt = false;
        BuildShortcutCard();
    }

    private void MainMenuShortcuts_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        ShowShortcuts();
    }

    private void ShortcutClose_Click(object sender, RoutedEventArgs e) => HideShortcuts();

    /// <summary>Clicking the dimmed backdrop closes the card; clicks inside the card must not.</summary>
    private void ShortcutOverlay_Click(object sender, MouseButtonEventArgs e) => HideShortcuts();

    private void ShortcutCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
