using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The hand-over of the Camera delay between cameras (AR‑3.5): leaving one files the slider's value under its link,
/// opening one that was tuned before brings its own delay back, and a measured delay lands on the camera it was
/// taken with. These are the stories the window's wiring acts out; the session states them without a window.
/// </summary>
public sealed class ArDelaySessionTests : IDisposable
{
    private const string Webcam = @"\\?\usb#vid_046d&pid_0825";
    private const string ObsVirtual = "obs-virtual-camera";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ardelaysession-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ArDelaySession Session(out ArDelayStore store)
    {
        store = new ArDelayStore(_dir);
        return new ArDelaySession(store);
    }

    [Fact]
    public void With_nothing_open_there_is_nothing_to_file_and_opening_an_untuned_camera_says_stay()
    {
        var session = Session(out var store);
        Assert.Equal("", session.ActiveSource);
        session.Remember(120);                                       // a drag before any camera opens: machine-wide setting only
        Assert.Equal(0, store.Count);
        Assert.Null(session.SwitchTo(Webcam, 120));                  // this camera was never tuned: keep the slider where it is
        Assert.Equal(Webcam, session.ActiveSource);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Leaving_a_camera_files_the_delay_under_it_and_coming_back_brings_the_same_delay_back()
    {
        var session = Session(out var store);
        session.SwitchTo(Webcam, 0);
        Assert.Null(session.SwitchTo(ObsVirtual, 120));              // 120 is filed under the webcam; OBS was never tuned
        Assert.Equal(120d, store.Get(Webcam));
        Assert.Null(store.Get(ObsVirtual));
        Assert.Equal(120d, session.SwitchTo(Webcam, 45));             // the webcam's own delay returns; 45 is filed under OBS
        Assert.Equal(45d, store.Get(ObsVirtual));
    }

    [Fact]
    public void Reapplying_the_same_source_changes_nothing()
    {
        var session = Session(out var store);
        session.SwitchTo(Webcam, 0);
        store.Set(Webcam, 88);
        Assert.Null(session.SwitchTo(Webcam, 150));                  // the settings refresh re-tells the session what it already knows
        Assert.Equal(88d, store.Get(Webcam));                         // and nothing is filed under it on the way
    }

    [Fact]
    public void A_delay_tuned_while_a_camera_is_open_is_filed_under_it_the_measured_delay_story()
    {
        var session = Session(out var store);
        session.SwitchTo(Webcam, 0);
        session.Remember(140);                                       // MEASURE DELAY on the webcam, accepted
        session.SwitchTo(ObsVirtual, 140);                           // move away with the value still on the slider…
        session.SwitchTo(null, 140);                                 // …and close the camera
        Assert.Equal(140d, store.Get(ObsVirtual));
        Assert.Equal(140d, store.Get(Webcam));                        // the measured 140 stayed the webcam's own
    }

    [Fact]
    public void A_video_file_is_a_source_of_its_own_but_closing_first_never_loses_a_delay()
    {
        var session = Session(out var store);
        session.SwitchTo(@"C:\clips\take.mp4", 66);
        Assert.Null(session.SwitchTo(Webcam, 66));                   // the file's 66 is filed…
        Assert.Equal(66d, store.Get(@"C:\clips\take.mp4"));
        Assert.Null(session.SwitchTo(null, 90));                     // …the webcam's 90 is filed, and closing reports nothing
        Assert.Equal(90d, store.Get(Webcam));
    }

    [Fact]
    public void The_next_run_of_the_app_brings_each_cameras_delay_back_from_disk()
    {
        var first = Session(out _);
        first.SwitchTo(Webcam, 0);
        first.Remember(95);
        first.SwitchTo(null, 95);

        var second = Session(out _);                                  // a later run: new session, same folder
        Assert.Equal(95d, second.SwitchTo(Webcam.ToUpperInvariant(), 0));
        Assert.Equal(Webcam.ToUpperInvariant(), second.ActiveSource); // the source is the link the reader reports, kept verbatim
    }
}
