using System.Windows;
using System.Windows.Controls;

namespace PianoPath;

/// <summary>
/// Sending a look to somebody else as one line of text.
///
/// <para>
/// The dock's Style page holds the box and the two buttons; everything that decides whether a code is
/// valid lives in <see cref="VisualPresetShare"/>, so the same rules run here and in <c>--verify</c>.
/// Applying a code is a settings change like any other: it commits the undo step first, moves the dock
/// controls, saves and repaints, and finally fills the box with the code it just applied, which makes
/// "copy what I am looking at" the same gesture as "keep this look".
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Puts the current look into the box as a code and returns it; used by the COPY button and the checks.</summary>
    internal string RefreshShareCode()
    {
        var code = VisualPresetShare.Encode(_visualSettings);
        if (ShareCodeBox is not null) ShareCodeBox.Text = code;
        return code;
    }

    /// <summary>
    /// Applies a pasted code. Returns false and prints the reason when the text is not a Keyflow look, so
    /// the check can assert both outcomes; a valid code goes through the ordinary settings path.
    /// </summary>
    internal bool ApplyShareCode(string? code)
    {
        if (!VisualPresetShare.TryDecode(code, out var look, out var error))
        {
            if (ShareCodeStatusLabel is not null) ShareCodeStatusLabel.Text = Loc.F("That code was not applied: {0}", Loc.T(error));
            return false;
        }
        CommitHistory();
        _visualSettings.CopyFrom(look, keepBackgroundImage: true);
        if (_visualSettings.BackgroundMode == "Image" && string.IsNullOrWhiteSpace(_visualSettings.BackgroundImagePath)) _visualSettings.BackgroundMode = "Solid";
        RefreshSettingControls();
        ApplyVisualSettings("Shared look applied: {0}", false, VisualPresets.DisplayName(_visualSettings.PresetName));
        if (ShareCodeStatusLabel is not null) Loc.Format(ShareCodeStatusLabel, "Shared look applied: {0}", VisualPresets.DisplayName(_visualSettings.PresetName));
        RefreshShareCode();
        return true;
    }

    private void CopyShareCode_Click(object sender, RoutedEventArgs e)
    {
        var code = RefreshShareCode();
        try
        {
            Clipboard.SetText(code);
            Loc.Set(ShareCodeStatusLabel, "Code copied to the clipboard.");
        }
        catch
        {
            // A locked or unavailable clipboard must not lose the code: it is in the box either way.
            Loc.Set(ShareCodeStatusLabel, "The code is ready in the box; select and copy it from there.");
        }
    }

    private void ApplyShareCode_Click(object sender, RoutedEventArgs e) => ApplyShareCode(ShareCodeBox?.Text);
}
