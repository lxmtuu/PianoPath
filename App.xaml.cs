using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;

namespace PianoPath;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--verify")) { ShutdownMode = ShutdownMode.OnExplicitShutdown; VerificationSuite.Run(e.Args, this); return; }

        // Publish the saved shell theme before any window exists, so the very first frame is themed
        // instead of flashing the XAML defaults and repainting one frame later.
        ShellThemeManager.Apply(PianoVisualSettingsStore.Load().ShellTheme);

        var window = new MainWindow();
        MainWindow = window;
        var snapshotIndex = Array.IndexOf(e.Args, "--snapshot");
        var automated = snapshotIndex >= 0 || e.Args.Contains("--show-settings");
        // Automated captures wait several seconds for the SoundFont and must not animate:
        // a frozen chrome keeps every screenshot identical and the run inexpensive.
        if (automated) { window.AutoHideChrome = false; window.DisableChromeMotion(); }
        // Normal launches open on the concert main menu; automated captures go straight to the stage.
        if (!automated) window.ShowStartupMenu();
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
                    _ => SettingsPages.Style
                };
                tabs.SelectedIndex = Math.Max(0, SettingsPages.IndexOf(page));
            }
        }
        if (snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length)
        {
            if (e.Args.Contains("--compact")) { window.WindowState = WindowState.Normal; window.Width = 1080; window.Height = 700; }
            window.ContentRendered += (_, _) =>
            {
                if (e.Args.Contains("--play-preview")) VerificationSuite.PressPreviewNote(window, 60);
                var loadedWait = Stopwatch.StartNew();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                timer.Tick += (_, _) =>
                {
                    if (!window.HasSoundFont && loadedWait.Elapsed < TimeSpan.FromSeconds(8)) return;
                    timer.Stop(); VerificationSuite.Capture(window, e.Args[snapshotIndex + 1]); Shutdown(0);
                };
                timer.Start();
            };
        }
        window.Show();
    }
}
