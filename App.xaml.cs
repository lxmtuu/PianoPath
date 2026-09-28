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

        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Contains("--show-settings"))
        {
            if (window.FindName("SettingsPanel") is System.Windows.Controls.Border panel) panel.Visibility = Visibility.Visible;
            if (window.FindName("LiveChromeOverlay") is System.Windows.Controls.Grid chrome) chrome.Visibility = Visibility.Collapsed;
            if (window.FindName("RecordButton") is System.Windows.Controls.Button recorder) recorder.Visibility = Visibility.Collapsed;
            if (window.FindName("SettingsTabs") is System.Windows.Controls.TabControl tabs)
            {
                var tab = e.Args.FirstOrDefault(a => a.StartsWith("--settings-tab=", StringComparison.Ordinal))?[15..];
                tabs.SelectedIndex = tab?.ToLowerInvariant() switch { "notes" => 1, "particles" => 2, "camera" => 3, "audio" => 4, "practice" => 5, _ => 0 };
            }
        }
        var snapshotIndex = Array.IndexOf(e.Args, "--snapshot");
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
