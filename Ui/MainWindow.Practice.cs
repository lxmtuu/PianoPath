using System.Windows;
using System.Windows.Controls;

namespace PianoPath;

/// <summary>
/// The slow-down curve of the practice session: after a run of misses the song steps down, after a run
/// of correct notes it steps back up towards the normal speed.
///
/// <para>
/// The curve is off by default, so free play and ordinary playback are never touched. Every step goes
/// through <see cref="TempoSlider"/>, the control the user would move, so the transport label, the
/// metronome and the settings file follow their ordinary paths instead of a private shortcut.
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Percent taken off the playback tempo by one slow-down step.</summary>
    internal const int PracticeTempoMissStep = 5;
    /// <summary>Percent given back by one recovery step.</summary>
    internal const int PracticeTempoHitStep = 2;
    /// <summary>Correct notes in a row that earn one recovery step.</summary>
    internal const int PracticeTempoHitsToSpeedUp = 4;

    private int _practiceMissRun, _practiceHitRun;

    /// <summary>The two run counters, exposed so <c>--verify</c> can prove they reset as the rules say.</summary>
    internal int PracticeMissRun => _practiceMissRun;
    internal int PracticeHitRun => _practiceHitRun;

    /// <summary>A note was scored: extend the run and, when a run is long enough, step the tempo.</summary>
    internal void RecordPracticeNote(bool hit)
    {
        if (!_visualSettings.PracticeAutoTempo)
        {
            _practiceMissRun = _practiceHitRun = 0;
            return;
        }
        if (hit)
        {
            _practiceMissRun = 0;
            if (++_practiceHitRun < PracticeTempoHitsToSpeedUp) return;
            _practiceHitRun = 0;
            StepPracticeTempo(PracticeTempoHitStep);
        }
        else
        {
            _practiceHitRun = 0;
            if (++_practiceMissRun <= _visualSettings.PracticeMissThreshold) return;
            _practiceMissRun = 0;
            StepPracticeTempo(-PracticeTempoMissStep);
        }
    }

    /// <summary>Forgets both runs; called when the score restarts so a new take starts from zero.</summary>
    internal void ResetPracticeTempoRuns() => _practiceMissRun = _practiceHitRun = 0;

    /// <summary>
    /// Moves the playback tempo by one step and lets the slider do the rest. The auto curve never
    /// speeds the song up past 100 %, so it only ever undoes its own slow-downs.
    /// </summary>
    private void StepPracticeTempo(int delta)
    {
        if (TempoSlider is null) return;
        var ceiling = Math.Min(100, TempoSlider.Maximum);
        var target = Math.Clamp(TempoSlider.Value + delta, TempoSlider.Minimum, ceiling);
        if (Math.Abs(target - TempoSlider.Value) < .01) return;
        TempoSlider.Value = target;
    }

    private void AutoPracticeTempo_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _loadingVisualSettings) return;
        var value = AutoPracticeTempoCheck.IsChecked == true;
        _visualSettings.PracticeAutoTempo = value;
        AutoPracticeMissSlider.IsEnabled = value;
        ResetPracticeTempoRuns();
        MarkModified(nameof(PianoVisualSettings.PracticeAutoTempo));
        ApplyVisualSettings(value ? "{0} on" : "{0} off", false, Loc.T("Auto practice tempo"));
    }

    private void AutoPracticeMiss_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings) return;
        _visualSettings.PracticeMissThreshold = (int)Math.Round(e.NewValue);
        if (AutoPracticeMissLabel is not null) AutoPracticeMissLabel.Text = _visualSettings.PracticeMissThreshold.ToString();
        MarkModified(nameof(PianoVisualSettings.PracticeMissThreshold));
        ApplyVisualSettings("Visual changes apply live");
    }
}
