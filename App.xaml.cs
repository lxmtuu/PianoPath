using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;

namespace PianoPath;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Where the look lives. --settings-dir points it anywhere (portable setup, CI); a verification
        // run gets a private folder so it can never overwrite the settings or presets the user saved.
        var settingsDirectory = e.Args.FirstOrDefault(argument => argument.StartsWith("--settings-dir=", StringComparison.Ordinal))?["--settings-dir=".Length..];
        if (!string.IsNullOrWhiteSpace(settingsDirectory)) PianoVisualSettingsStore.UseDirectory(settingsDirectory);
        else if (e.Args.Contains("--verify")) PianoVisualSettingsStore.UseDirectory(Path.Combine(Path.GetTempPath(), "keyflow-verify-settings"));

        if (e.Args.Contains("--verify")) { ShutdownMode = ShutdownMode.OnExplicitShutdown; VerificationSuite.Run(e.Args, this); return; }

        // Publish the saved language and shell theme before any window exists, so the very first
        // frame is already translated and themed instead of flashing the XAML defaults for a frame.
        // --lang=<en|vi> overrides the stored language for one run only, which is how CI renders the
        // Vietnamese preview of the General page without touching anybody's settings file.
        var stored = PianoVisualSettingsStore.Load();
        var languageOverride = e.Args.FirstOrDefault(argument => argument.StartsWith("--lang=", StringComparison.Ordinal))?["--lang=".Length..];
        Loc.Apply(string.IsNullOrWhiteSpace(languageOverride) ? stored.Language : languageOverride);
        ShellThemeManager.Apply(stored.ShellTheme);

        var window = new MainWindow();
        MainWindow = window;
        var snapshotIndex = Array.IndexOf(e.Args, "--snapshot");
        // Automated captures: the chrome must not animate or hide while a screenshot is pending, and
        // they may ask for a specific surface with --menu / --show-settings / --play-dialog / --shortcuts.
        var automated = snapshotIndex >= 0 || e.Args.Contains("--show-settings") || e.Args.Contains("--play-dialog") || e.Args.Contains("--shortcuts");
        // Automated captures wait several seconds for the SoundFont and must not animate:
        // a frozen chrome keeps every screenshot identical and the run inexpensive.
        if (automated) { window.AutoHideChrome = false; window.DisableChromeMotion(); }
        // Normal launches open on the concert main menu; automated captures go straight to the stage
        // unless the capture asks for the shell with --menu (used to refresh the README screenshots).
        if (!automated || e.Args.Contains("--menu")) window.ShowStartupMenu();
        if (e.Args.Contains("--show-settings"))
        {
            if (window.FindName("SettingsPanel") is System.Windows.Controls.Border panel) panel.Visibility = Visibility.Visible;
            if (window.FindName("LiveChromeOverlay") is System.Windows.Controls.Grid chrome) chrome.Visibility = Visibility.Collapsed;
            if (window.FindName("RecordButton") is System.Windows.Controls.Button recorder) recorder.Visibility = Visibility.Collapsed;
            if (window.FindName("SettingsTabs") is System.Windows.Controls.TabControl tabs)
            {
                var tab = e.Args.FirstOrDefault(a => a.StartsWith("--settings-tab=", StringComparison.Ordinal))?[15..];
                var page = tab?.ToLowerInvariant() switch
                {
                    "notes" => SettingsPages.Notes,
                    "theme" or "themes" => SettingsPages.Theme,
                    "particles" or "embers" => SettingsPages.Particles,
                    "keyboard" or "keys" => SettingsPages.Keyboard,
                    "background" or "scene" => SettingsPages.Background,
                    "camera" or "fx" => SettingsPages.Camera,
                    "audio" or "sound" => SettingsPages.Audio,
                    "midi" => SettingsPages.Midi,
                    "practice" => SettingsPages.Practice,
                    "recording" or "record" => SettingsPages.Recording,
                    "general" or "language" or "app" => SettingsPages.General,
                    _ => SettingsPages.Style
                };
                tabs.SelectedIndex = Math.Max(0, SettingsPages.IndexOf(page));
            }
        }
        // The play dialog and the shortcut card are captured on their own, over a stage that stays visible.
        if (e.Args.Contains("--play-dialog")) window.OpenPlayDialog();
        if (e.Args.Contains("--shortcuts")) window.ShowShortcuts();
        if (snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length)
        {
            if (e.Args.Contains("--compact")) { window.WindowState = WindowState.Normal; window.Width = 1080; window.Height = 700; }
            var target = e.Args[snapshotIndex + 1];
            var captured = false; var previewPressed = false; var loadedWait = Stopwatch.StartNew();
            void PressPreview()
            {
                if (previewPressed) return;
                previewPressed = true;
                if (e.Args.Contains("--play-preview")) VerificationSuite.PressPreviewNote(window, 60);
            }
            // The SoundFont scan and the first render both settle over a few seconds; the timer waits
            // for the instrument (bounded, so a silent CI runner still gets its screenshot) and the
            // watchdog guarantees a file even in a session that never raises ContentRendered.
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            void TryCapture()
            {
                if (captured || (!window.HasSoundFont && loadedWait.Elapsed < TimeSpan.FromSeconds(8))) return;
                captured = true; settle.Stop();
                VerificationSuite.Capture(window, target); Shutdown(0);
            }
            settle.Tick += (_, _) => TryCapture();
            window.ContentRendered += (_, _) => { PressPreview(); loadedWait.Restart(); settle.Start(); };
            var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            watchdog.Tick += (_, _) => { watchdog.Stop(); if (captured) return; PressPreview(); TryCapture(); if (!captured) { captured = true; VerificationSuite.Capture(window, target); Shutdown(0); } };
            watchdog.Start();
        }
        window.Show();
    }
}
