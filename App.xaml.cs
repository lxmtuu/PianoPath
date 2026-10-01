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

        // --encode-take=<file> is how the verification run has a child process write its MP4 take: the encoders
        // are native code that can take a process down with it, so the writing happens out of the run's way and
        // every line this mode prints goes back into the run's log. See Diagnostics/Mp4TakeAttempt.cs.
        var takePath = e.Args.FirstOrDefault(argument => argument.StartsWith("--encode-take=", StringComparison.Ordinal));
        if (takePath is not null) { ShutdownMode = ShutdownMode.OnExplicitShutdown; Shutdown(Mp4TakeAttempt.Run(takePath["--encode-take=".Length..])); return; }

        // --encode-probe=<file.avi> is the same arrangement for the encoder-free plumbing probe, which runs only
        // when a run produced no take at all. See Diagnostics/EncodeProbeAttempt.cs.
        var probePath = e.Args.FirstOrDefault(argument => argument.StartsWith("--encode-probe=", StringComparison.Ordinal));
        if (probePath is not null) { ShutdownMode = ShutdownMode.OnExplicitShutdown; Shutdown(EncodeProbeAttempt.Run(probePath["--encode-probe=".Length..])); return; }

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
        // The GPU stage is the main stage by default now. --gpu stays for older launch scripts and CI;
        // --software draws one run with the WPF renderer instead (deterministic captures, the software
        // column of the preset gallery) without touching the settings file.
        if (e.Args.Contains("--gpu")) window.UseGpuForSession();
        if (e.Args.Contains("--software")) window.UseSoftwareForSession();
        // --preset=<name> applies a built-in look (spaces optional: --preset=GalaxyVoyage); CI renders the
        // GPU effect previews with it, each from a private settings folder.
        var presetName = e.Args.FirstOrDefault(argument => argument.StartsWith("--preset=", StringComparison.Ordinal))?["--preset=".Length..];
        if (!string.IsNullOrWhiteSpace(presetName)) window.PreviewPreset(presetName);
        // --background-image=<path> hangs a picture behind the keys for this run only. Nothing reaches
        // the settings file: MainWindow.PreviewBackgroundImage sets the stage directly instead of going
        // through the row handlers that arm the auto-save timer. CI uses it to render the README preview
        // of the feature from a generated sample (tools/make_stage_background.py) rather than from
        // somebody's screenshot, so the image stays reproducible and free of third-party artwork.
        var backgroundImage = e.Args.FirstOrDefault(argument => argument.StartsWith("--background-image=", StringComparison.Ordinal))?["--background-image=".Length..];
        if (!string.IsNullOrWhiteSpace(backgroundImage)) window.PreviewBackgroundImage(backgroundImage);
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
            // The previews have to come out byte for byte the same from one build to the next, or the drift check
            // cannot tell "the interface changed" from "the machine was a little slower". The picture is therefore
            // taken a fixed number of fixed-length steps after a reset, not a number of seconds after the window
            // opened: 480 steps of 1/60 s is the eight seconds a held note used to have by the time the old
            // wall-clock delay ran out, so the stage shows the same steady state, only reproducibly.
            const double previewStepSeconds = 1.0 / 60;
            const int previewFrames = 480;
            void PressPreview()
            {
                if (previewPressed) return;
                previewPressed = true;
                if (e.Args.Contains("--play-preview")) VerificationSuite.PressPreviewNote(window, 60);
                // --play-chord holds a spread chord as well, so the hold effects that link keys (electric arcs) show
                if (e.Args.Contains("--play-chord")) foreach (var pitch in new[] { 48, 55, 64, 67, 72 }) VerificationSuite.PressPreviewNote(window, pitch);
            }
            // The SoundFont scan and the first render both settle over a few seconds; the timer waits for the
            // instrument (bounded, so a silent CI runner still gets its screenshot) and the watchdog guarantees a
            // file even in a session that never raises ContentRendered.
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            // One synchronous sequence, so nothing can slip in between its steps: stop the render thread, reset the
            // animators, press the preview note, step the stage and build the GPU frame for that same moment,
            // capture. Both the settled path and the watchdog run it, so even the fallback is reproducible.
            void Shoot(string how)
            {
                if (captured) return;
                captured = true; settle.Stop(); watchdog.Stop();
                try
                {
                    window.BeginDeterministicPreview();
                    PressPreview();
                    window.RunDeterministicPreviewFrames(previewFrames, previewStepSeconds);
                }
                catch (Exception ex)
                {
                    // The picture is still taken, only not reproducibly: losing it would fail the whole build
                    // for the sake of a property the report line and the build's second render will flag.
                    Trace.WriteLine($"Keyflow: the deterministic preview sequence failed: {ex}");
                    how += "-unprepared";
                }
                VerificationSuite.Capture(window, target);
                ReportPreview(window, target, how, loadedWait.Elapsed.TotalSeconds);
                Shutdown(0);
            }
            void TryCapture()
            {
                if (captured || (!window.HasSoundFont && loadedWait.Elapsed < TimeSpan.FromSeconds(8))) return;
                Shoot("settled");
            }
            settle.Tick += (_, _) => TryCapture();
            // ContentRendered only arms the polling timer. The preview note is pressed inside Shoot, after the
            // reset: pressed here instead, its onset would land at whatever moment the window finished loading.
            window.ContentRendered += (_, _) => { loadedWait.Restart(); settle.Start(); };
            watchdog.Tick += (_, _) => Shoot("watchdog");
            watchdog.Start();
        }
        window.Show();
    }

    /// <summary>
    /// When the build asks for it (<c>KEYFLOW_PREVIEW_REPORTS</c> names a folder outside the repository),
    /// writes one line about a finished screenshot: a short hash of the PNG that was just written, how the
    /// capture was reached, and what drew the stage.
    ///
    /// The hash is the point. A finished run's job log cannot be read afterwards, the committed pictures are
    /// only visible one commit later, and the question that keeps coming up is whether two renders of the
    /// same code agree. The build publishes these lines as one annotation, so two runs can be compared by
    /// eye. The folder is outside <c>docs/previews</c> because everything inside it is committed, and a line
    /// that carries a measured duration would otherwise change the commit on every run.
    /// </summary>
    private static void ReportPreview(MainWindow window, string target, string how, double seconds)
    {
        var folder = Environment.GetEnvironmentVariable("KEYFLOW_PREVIEW_REPORTS");
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            var full = Path.GetFullPath(target);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(full)))[..12].ToLowerInvariant();
            var name = $"{Path.GetFileName(Path.GetDirectoryName(full))}-{Path.GetFileName(full)}";
            var engine = window.GpuStageActive ? (window.GpuLoop?.AdapterName ?? "gpu") : "software";
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".txt"), $"{name} sha={hash} how={how} engine={engine} soundfont={window.HasSoundFont} seconds={seconds:F1}");
        }
        catch (Exception)
        {
            // a diagnostic must never cost the screenshot
        }
    }
}
