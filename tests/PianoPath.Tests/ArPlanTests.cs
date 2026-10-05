using Xunit;

namespace PianoPath.Tests;

/// <summary>What the stage does for each combination of the overlay's switch, the engine, the camera and the calibration.</summary>
public class ArPlanTests
{
    private static readonly ArOptions On = ArOptions.Default with { Enabled = true };
    private static readonly ArCalibration Calibration = ArProjectionTests.Calibrate([(70, 292), (1214, 268), (1236, 421), (52, 449)]);

    private static ArCalibrationSnapshot Snapshot(ArCalibrationState state) => state switch
    {
        ArCalibrationState.NoSource => new(state, null, "", 0, 0, 0, 1),
        ArCalibrationState.Ready => new(state, Calibration, "cam", 1280, 720, 88, 1),
        _ => new(state, null, "cam", 1280, 720, 88, 1),
    };

    [Theory]
    [InlineData(false, false, "NoSource")]
    [InlineData(true, true, "Ready")]
    [InlineData(true, true, "Outdated")]
    [InlineData(false, true, "NotCalibrated")]
    public void With_the_switch_off_nothing_is_asked_whatever_the_rest(bool gpu, bool source, string state)
    {
        var plan = ArPlan.Decide(ArOptions.Default, gpu, source, Snapshot(Enum.Parse<ArCalibrationState>(state)), chromaBackground: false);
        Assert.Equal(ArPlan.Off, plan);
        Assert.False(plan.NeedsCamera || plan.RawFrames || plan.CameraLayer || plan.ArLayer || plan.HideStageLayers);
    }

    [Fact]
    public void Without_the_engine_the_overlay_asks_for_nothing_and_says_why()
    {
        var plan = ArPlan.Decide(On, gpuActive: false, sourceOpen: true, Snapshot(ArCalibrationState.Ready), false);
        Assert.Equal(ArStatus.NeedsGpu, plan.Status);
        Assert.False(plan.NeedsCamera || plan.RawFrames || plan.CameraLayer || plan.ArLayer || plan.HideStageLayers);
    }

    [Fact]
    public void A_green_screen_background_takes_the_cameras_place()
    {
        var plan = ArPlan.Decide(On, true, true, Snapshot(ArCalibrationState.Ready), chromaBackground: true);
        Assert.Equal(ArStatus.ChromaBackground, plan.Status);
        Assert.False(plan.NeedsCamera || plan.CameraLayer || plan.ArLayer || plan.HideStageLayers);
    }

    [Theory]
    [InlineData(false, "NoSource")]
    [InlineData(false, "Ready")]       // a calibration with nothing open cannot be used
    [InlineData(true, "NoSource")]     // nor an open camera the calibration has not heard of
    public void Until_a_camera_is_open_the_overlay_asks_for_the_camera_and_shows_nothing(bool source, string state)
    {
        var plan = ArPlan.Decide(On, true, source, Snapshot(Enum.Parse<ArCalibrationState>(state)), false);
        Assert.Equal(ArStatus.NoCamera, plan.Status);
        Assert.True(plan.NeedsCamera && plan.RawFrames);
        Assert.False(plan.CameraLayer || plan.ArLayer || plan.HideStageLayers);
    }

    [Theory]
    [InlineData("NotCalibrated", "NeedsCalibration")]
    [InlineData("Outdated", "Outdated")]
    public void An_open_camera_that_is_not_calibrated_is_the_background_and_the_stage_stays_as_it_was(string state, string status)
    {
        var plan = ArPlan.Decide(On, true, true, Snapshot(Enum.Parse<ArCalibrationState>(state)), false);
        Assert.Equal(Enum.Parse<ArStatus>(status), plan.Status);
        Assert.True(plan.NeedsCamera && plan.RawFrames && plan.CameraLayer);
        Assert.False(plan.ArLayer || plan.HideStageLayers);
    }

    [Fact]
    public void A_calibrated_camera_gets_the_overlay_and_the_stage_steps_aside()
    {
        var plan = ArPlan.Decide(On, true, true, Snapshot(ArCalibrationState.Ready), false);
        Assert.Equal(ArStatus.Active, plan.Status);
        Assert.True(plan.NeedsCamera && plan.RawFrames && plan.CameraLayer && plan.ArLayer && plan.HideStageLayers);
    }

    [Fact]
    public void A_ready_state_without_a_calibration_is_not_trusted()
    {
        var broken = new ArCalibrationSnapshot(ArCalibrationState.Ready, null, "cam", 1280, 720, 88, 1);
        var plan = ArPlan.Decide(On, true, true, broken, false);
        Assert.False(plan.ArLayer || plan.HideStageLayers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_stage_only_steps_aside_where_the_overlay_is_drawn_and_the_camera_is_never_read_without_the_engine(bool gpu)
    {
        foreach (var state in Enum.GetValues<ArCalibrationState>())
            foreach (var source in new[] { false, true })
                foreach (var chroma in new[] { false, true })
                {
                    var plan = ArPlan.Decide(On, gpu, source, Snapshot(state), chroma);
                    Assert.True(!plan.HideStageLayers || plan.ArLayer);            // hidden stage only ever means the overlay is there
                    Assert.True(!plan.ArLayer || plan.CameraLayer);                // the overlay lies on the camera picture
                    Assert.True(!plan.CameraLayer || plan.RawFrames);              // the engine takes the camera's frames unmirrored
                    Assert.True(!plan.CameraLayer || (gpu && source && !chroma));
                    Assert.True(!plan.NeedsCamera || gpu);
                }
    }

    [Fact]
    public void Every_option_the_settings_can_hold_gives_a_plan_that_is_consistent()
    {
        foreach (var mode in new[] { ArModes.Learn, ArModes.Perform, "?" })
        {
            var options = new ArOptions(true, mode, 50, 3, 60).Normalised();
            var plan = ArPlan.Decide(options, true, true, Snapshot(ArCalibrationState.Ready), false);
            Assert.Equal(ArStatus.Active, plan.Status);               // the mode is a matter of what is drawn, not whether
        }
    }
}
