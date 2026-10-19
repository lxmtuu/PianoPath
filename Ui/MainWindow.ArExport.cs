using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PianoPath;

public partial class MainWindow
{
    private TextBlock? _arExportStatus;
    private volatile bool _arExportBusy;

    /// <summary>
    /// The AR render-to-video card on the Recording page (roadmap §14.10 AR‑S7, export UI): one button
    /// renders the loaded song over the camera video file offline — no live clock, a fixed time step, the
    /// engine's own sweep — to one AVI plus one WAV, exactly the take the live stage would show.
    /// </summary>
    private void BuildArExportCard()
    {
        var card = Card(RecordingSettingsHost, "AR RENDER‑TO‑VIDEO",
            "Offline and deterministic: the song plays against the camera video file at the chosen frame rate, the GPU draws every tick — AR overlay, watermark, everything the look switches on — and the piano is mixed tick-exact into a WAV beside the video.");
        Note(card, "Needs a song open (Play menu), a camera video file chosen on the Camera page, and the keyboard calibrated on that file. The render follows the Recording OUTPUT choices (resolution cap and frame rate) and honours Record audio and the Watermark switch. A take takes about as long to render as the song lasts.");
        ButtonRow(card, ("RENDER AR TAKE…", ExportArTake_Click, null));
        _arExportStatus = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
        Register(card, _arExportStatus, null, "ar export render take offline video avi wav");
        RefreshArExportStatus();
        // The camera file and the song can arrive after the page was built; the status re-reads every visit.
        RecordingSettingsHost.IsVisibleChanged += (_, _) => { if (RecordingSettingsHost.IsVisible) RefreshArExportStatus(); };
    }

    private void SetArExportStatus(string text) { if (_arExportStatus is not null) _arExportStatus.Text = text; }

    private void RefreshArExportStatus()
    {
        if (_arExportStatus is null) return;
        var song = _allNotes.Count > 0 ? Loc.F("song «{0}» ({1:0.#} s)", _songLabel, SongDuration()) : Loc.T("no song open yet");
        var video = string.IsNullOrWhiteSpace(_visualSettings.CameraVideoPath) ? Loc.T("no camera video file yet")
            : Loc.F("camera file «{0}»", Path.GetFileName(_visualSettings.CameraVideoPath));
        var calibration = _arCalibration?.Current is null ? Loc.T("keyboard not calibrated on that source") : Loc.T("keyboard calibrated");
        SetArExportStatus(Loc.F("Ready when: {0} · {1} · {2}.", song, video, calibration));
    }

    private void ExportArTake_Click(object sender, RoutedEventArgs e)
    {
        if (_arExportBusy) { SetArExportStatus(Loc.T("A take is already rendering; this line will say when it is done.")); return; }
        var videoPath = _visualSettings.CameraVideoPath;
        var missing = new List<string>();
        if (_allNotes.Count == 0) missing.Add(Loc.T("open a MIDI file (Play menu)"));
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) missing.Add(Loc.T("choose a camera video file (Camera page)"));
        var calibration = _arCalibration?.Current;
        if (calibration is null) missing.Add(Loc.T("calibrate the keyboard on that source (Camera page)"));
        // Naming the calibration in the guard shows the compiler what the list above already proves; the
        // condition is redundant at runtime because a null calibration always adds its own line to `missing`.
        if (missing.Count > 0 || calibration is null) { SetArExportStatus(Loc.F("Not yet: {0}.", string.Join("; ", missing))); return; }

        var dialog = new SaveFileDialog
        {
            Filter = Loc.T("AVI video (*.avi)|*.avi"),
            Title = Loc.T("Render the AR take to…"),
            FileName = Loc.F("AR take — {0}.avi", string.IsNullOrWhiteSpace(_songLabel) ? "song" : Path.GetFileNameWithoutExtension(_songLabel)),
        };
        if (dialog.ShowDialog(this) != true) return;

        // Everything the worker thread needs is frozen here, on the UI thread: the song (independent copies,
        // so practising meanwhile cannot move the take), the look exactly as the stage draws it, the AR
        // choices, the calibration and the camera's own flags. The alignment-check guides stay out of exports.
        var notes = _allNotes.Select(note => new NoteEvent { Pitch = note.Pitch, Start = note.Start, Duration = note.Duration, Velocity = note.Velocity, Track = note.Track }).ToList();
        var look = Stage.SnapshotGpuLook();
        var arOptions = _visualSettings.ArOptionsValue;
        var arInput = new GpuArInput(calibration, arOptions.NoteStyle, arOptions.LayerOpacity, arOptions.KeyLightStrength, arOptions.ShowsNotes,
            (float)arOptions.DelaySeconds, arOptions.Quality, arOptions.NotesProjected, MinAspect: arOptions.NoteMinAspect,
            ShowLiveTrails: arOptions.LivePressTrails, NoteDirection: arOptions.NoteDirection,
            ShowRisingMidiNotes: arOptions.ShowsRisingMidiNotes, AlignmentGuides: false,
            ShowDamperStrip: _visualSettings.ArDamperStrip, AlignmentGuideColor: ArGuideColor(),
            ViewProfileId: _visualSettings.ArViewProfileId);
        var frameRate = (int)Math.Clamp(_visualSettings.RecordingFrameRate, 15, 60);
        var resolutionCap = _visualSettings.RecordingResolution switch { "720p" => 1280, "1080p" => 1920, _ => 0 };
        var stageHeight = Math.Max(120, Stage.ActualHeight);
        var seconds = SongDuration() + 1.0;   // a one-second settle tail, so the last notes finish falling
        var withAudio = _visualSettings.RecordAudio && _audio.HasInstrument;
        var target = dialog.FileName;
        _arExportBusy = true;
        var job = new ArExportJob(target, videoPath!, notes, look, arInput, _visualSettings.CameraMirror, stageHeight, frameRate, seconds, withAudio, resolutionCap);
        Task.Run(() => RunArExport(job));
    }

    /// <summary>The frozen inputs of one export run; nothing on the worker thread may look back at the UI.</summary>
    private sealed record ArExportJob(string Target, string VideoPath,
        List<NoteEvent> Notes, GpuLook Look, GpuArInput Ar,
        bool Mirror, double StageHeight, int FrameRate, double Seconds, bool WithAudio, int ResolutionCap);
    // The legacy camera-file export rate policy is preserved; all scene clocks follow the resulting plan.

    private void RunArExport(ArExportJob job)
    {
        void Report(string text) => Dispatcher.BeginInvoke(() => SetArExportStatus(text));
        try
        {
            using var reader = CameraFrameReader.OpenFile(job.VideoPath, out var openError)
                ?? throw new InvalidOperationException(Loc.F("The camera video file would not open: {0}", openError));
            using var renderer = GpuStageRenderer.Create(forceWarp: false);
            var maxSide = job.ResolutionCap > 0 ? job.ResolutionCap : Math.Max(reader.Width, reader.Height);
            // Keep the existing half-requested-rate camera-file policy in this fix. It must not create
            // a second song clock: the scene/audio positions below always follow the actual plan rate.
            var plan = ArVideoRenderProfile.Plan(reader.Width, reader.Height, job.Seconds, job.FrameRate / 2, downsample: 1, maxSide);
            var ring = new CameraFrameRing();
            var videoBuffer = new byte[reader.Width * reader.Height * 4];
            var script = ArVideoExportScript.MapNotes(job.Notes.Select(note => (note.Start, note.End, note.Pitch, note.Velocity)), plan.Frames, plan.FrameRate);
            WavWriter? wav = null; SfzSynthesizer? synth = null;
            GpuStageFeed Feed(int tick)
            {
                // Wait for a decoded camera frame, up to four seconds. Decoder delivery time must not
                // advance the deterministic song/simulation clock.
                var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 4;
                while (!reader.TryReadFrame(videoBuffer, mirror: false, out var gotW, out var gotH) || gotW != reader.Width || gotH != reader.Height)
                {
                    if (reader.AtEnd) { reader.Rewind(); continue; }   // a take longer than the clip loops the camera, like the live overlay
                    if (Stopwatch.GetTimestamp() > deadline)
                        throw new InvalidOperationException(Loc.T("The camera video decoder stalled while the take was rendering."));
                    System.Threading.Thread.Sleep(2);
                }
                var buffer = ring.BeginWrite(videoBuffer.Length, out var slot);
                videoBuffer.CopyTo(buffer, 0);
                ring.Publish(slot, reader.Width, reader.Height, Stopwatch.GetTimestamp());
                var feed = new GpuStageFeed();
                feed.SetStageHeight(job.StageHeight);
                feed.SetLook(job.Look);
                // The planned output rate may differ from the requested camera rate. Song, audio and
                // simulation must all use the plan's clock, without wall-clock extrapolation.
                feed.SetDeterministicState(job.Notes, plan.PositionAt(tick), playing: true, new HashSet<int>());
                feed.SetDeterministicBeatPulse(0);
                // Only onsets need explicit hits. Song intervals drive held emitters and the existing
                // song-release scanner owns Note Off; adding live releases here would double them.
                foreach(var note in ArVideoExportScript.EventsForTick(script,tick))
                    if(note.On)feed.Impact(note.Pitch,note.Velocity/127.0);
                feed.Camera = new GpuCameraInput(ring, PictureFit.Fit, job.Mirror, 1f);
                feed.Ar = job.Ar;
                if (job.Look.ShowWatermark) feed.Watermark = PianoStage.GpuWatermark;
                return feed;
            }
            var take = default(ArVideoTake);
            using (var recorder = new AviVideoRecorder(job.Target, plan.Width, plan.Height, plan.FrameRate))
            {
                ArVideoAudio? audio = null;
                if (job.WithAudio)
                {
                    synth = new SfzSynthesizer(SfzReader.Read(SalamanderPiano.SfzPath), ArVideoMixdown.SampleRate);
                    wav = WavWriter.TryCreate(AudioTrackPath(job.Target, false));
                    if (wav is not null) audio = new ArVideoAudio(synth, tick => ArVideoExportScript.EventsForTick(script, tick), wav);
                }
                var lastReport = -1;
                take = ArRenderToVideo.Run(renderer, Feed, plan, recorder,
                    onFrame: (i, _) =>
                    {
                        if (i - lastReport < 30) return;
                        lastReport = i;
                        Report(Loc.F("Rendering… tick {0} of {1} ({2:0}%).", i + 1, plan.Frames, (i + 1) * 100.0 / plan.Frames));
                    },
                    audio: audio);
            }
            var wavPath = wav?.Path; wav?.Dispose();
            var audioLine = wavPath is not null
                ? Loc.F("\n\nAudio: {0} ({1:0.#} s, 16-bit stereo WAV) — mux it with\nffmpeg -i \"{2}\" -i \"{0}\" -c:v copy -c:a aac \"{3}\"", wavPath, take.AudioSeconds, job.Target, MuxedName(job.Target))
                : "";
            Dispatcher.BeginInvoke(() =>
            {
                SetArExportStatus(Loc.F("Done: {0} frames at {1}×{2}, {3:0.#} s of video{4}.", take.FramesWritten, take.Width, take.Height, take.Seconds, take.AudioSeconds > 0 ? Loc.F(" and {0:0.#} s of voice", take.AudioSeconds) : ""));
                RefreshArExportStatus();
            });
            Dispatcher.BeginInvoke(() => ShowMessage(Loc.F("AR take rendered.\n{0}\n\n{1} frames at {2}×{3}, {4:0.#} fps.{5}", job.Target, take.FramesWritten, take.Width, take.Height, (double)plan.FrameRate, audioLine),
                "Recording complete", MessageBoxImage.Information));
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => SetArExportStatus(Loc.F("The render stopped: {0}", ex.Message)));
        }
        finally { _arExportBusy = false; }
    }
}
