using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Embers-style shell: a startup main menu (Play / Design / Settings / About / Exit) and a
/// pre-flight Play dialog with per-hand style cards and quick layer switches. Every quick
/// switch mirrors a real stage setting, so the dock and the dialog never disagree.
/// </summary>
public partial class MainWindow
{
    internal void ShowStartupMenu()
    {
        SetChromeVisible(false);
        MainMenuOverlay.Visibility = Visibility.Visible;
        MainMenuVersionLabel.Text = $"Keyflow {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3"}";
    }

    private void HideStartupMenu() => MainMenuOverlay.Visibility = Visibility.Collapsed;

    private void MainMenu_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        CloseSettingsPanel();
        ShowStartupMenu();
    }

    private void MainMenuPlay_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        OpenPlayDialog();
    }

    private void MainMenuDesign_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SetChromeVisible(true);
        OpenSettingsPanel();
    }

    private void MainMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SetChromeVisible(true);
        OpenSettingsPanel();
        SettingsTabs.SelectedIndex = 6; // Audio
    }

    private void MainMenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Keyflow · Piano VFX Studio\n\nA real-time MIDI piano visualizer: ray-traced keyboard shading, particle embers, flames, halos and a full stage designer.\n\nSoundFont: FreePats YDP Grand Piano (CC BY 3.0).\nShading model: Cook-Torrance GGX + ACES filmic, in the spirit of Unreal Engine.",
            "About Keyflow", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MainMenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // =====================================================================================
    // Play dialog
    // =====================================================================================

    private void OpenPlayDialog()
    {
        _loadingVisualSettings = true;
        try
        {
            LayerBackgroundToggle.IsChecked = _visualSettings.ShowBackground;
            LayerNotesToggle.IsChecked = _visualSettings.ShowNotes;
            LayerEmbersToggle.IsChecked = _visualSettings.ShowEmbers;
            LayerHaloToggle.IsChecked = _visualSettings.ShowHalo;
            LayerFlameToggle.IsChecked = _visualSettings.ShowFlame;
            LayerKeysToggle.IsChecked = _visualSettings.ShowKeys;
            foreach (var child in ExtrasSubPanel.Children.OfType<CheckBox>())
                if (child.Tag is string property) child.IsChecked = (bool)Prop(property).GetValue(_visualSettings)!;
            PlaySpeedSlider.Value = Math.Clamp(_visualSettings.NoteFallSpeed, PlaySpeedSlider.Minimum, PlaySpeedSlider.Maximum);
            PlaySpeedLabel.Text = ((int)_visualSettings.NoteFallSpeed).ToString();
        }
        finally { _loadingVisualSettings = false; }

        var left = SafeColor(_visualSettings.ColorMode == "PerHand" ? _visualSettings.LeftHandColor : _visualSettings.NoteColorStart);
        var right = SafeColor(_visualSettings.ColorMode == "PerHand" ? _visualSettings.RightHandColor : _visualSettings.NoteColorEnd);
        LeftStyleCard.BorderBrush = new SolidColorBrush(left);
        RightStyleCard.BorderBrush = new SolidColorBrush(right);
        var presetDisplay = _visualSettings.PresetModified ? _visualSettings.PresetName + " *" : _visualSettings.PresetName;
        LeftStyleLabel.Text = presetDisplay;
        RightStyleLabel.Text = presetDisplay;
        if (PlayDialogHaloColorDot is not null)
            PlayDialogHaloColorDot.Background = new SolidColorBrush(SafeColor(_visualSettings.HaloColor));
        PlayDialogOverlay.Visibility = Visibility.Visible;
    }

    private static Color SafeColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Color.FromRgb(139, 92, 246); }
    }

    private void PlayDialogClose_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
    }

    private void PlayDialogOpenMidi_Click(object sender, RoutedEventArgs e)
    {
        var before = SongTitle.Text;
        OpenMidi_Click(sender, e);
        if (SongTitle.Text != before) PlayDialogMidiButton.Content = SongTitle.Text;
    }

    private void PlayDialogLive_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
        _lastPointerActivity = DateTime.UtcNow;
        Stop();
    }

    private void PlayDialogPlay_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
        _lastPointerActivity = DateTime.UtcNow;
        if (SongDuration() > 0) StartPlayback();
        else OpenMidi_Click(sender, e);
    }

    private void PlayDialogHaloColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(_visualSettings.HaloColor) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } hex) return;
        _visualSettings.HaloColor = hex;
        if (PlayDialogHaloColorDot is not null)
            PlayDialogHaloColorDot.Background = new SolidColorBrush(SafeColor(hex));
        if (_visualColorInputs.TryGetValue(nameof(PianoVisualSettings.HaloColor), out var input))
            input.Text = hex;
        if (_visualColorButtons.TryGetValue(nameof(PianoVisualSettings.HaloColor), out var button))
            SetColorSwatch(button, hex);
        MarkModified();
        ApplyVisualSettings("Halo color applied");
    }

    private void PlayDialogStyleCard_Click(object sender, MouseButtonEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        OpenSettingsPanel();
        SettingsTabs.SelectedIndex = 1; // Notes
    }

    private void PlayDialogDeepLink_Click(object sender, RoutedEventArgs e)
    {
        var tab = sender is FrameworkElement { DataContext: string index } && int.TryParse(index, out var parsed) ? parsed : 0;
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        OpenSettingsPanel();
        SettingsTabs.SelectedIndex = Math.Clamp(tab, 0, 9);
    }

    private void PlayDialogExtras_Click(object sender, RoutedEventArgs e) =>
        ExtrasSubPanel.Visibility = ExtrasSubPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void PlaySpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings) return;
        _visualSettings.NoteFallSpeed = Math.Clamp(PlaySpeedSlider.Value, 100, 1000);
        PlaySpeedLabel.Text = ((int)_visualSettings.NoteFallSpeed).ToString();
        if (_visualSliders.TryGetValue(nameof(PianoVisualSettings.NoteFallSpeed), out var dockSlider) && Math.Abs(dockSlider.Value - _visualSettings.NoteFallSpeed) > .01)
        {
            _loadingVisualSettings = true;
            try { dockSlider.Value = _visualSettings.NoteFallSpeed; if (_visualValueBoxes.TryGetValue(nameof(PianoVisualSettings.NoteFallSpeed), out var box)) box.Text = ((int)_visualSettings.NoteFallSpeed).ToString(); }
            finally { _loadingVisualSettings = false; }
        }
        MarkModified();
        ApplyVisualSettings($"Fall speed {(int)_visualSettings.NoteFallSpeed}");
    }

    private void PlaySpeedReset_Click(object sender, RoutedEventArgs e) => PlaySpeedSlider.Value = DefaultVisualSettings.NoteFallSpeed;
}
