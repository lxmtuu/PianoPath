using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The N12(e) numeric plan has to stay a plan: a slider axis is only worth shooting at three values if those
/// values are inside the product's own range, far enough apart to be a claim, and arranged through a scene the
/// discrete matrix already pins. These checks run on Linux; <c>tools/check_sources.py</c> pins the ranges to the
/// dock sliders, which is the half that needs WPF.
/// </summary>
public class OptionNumericPlanTests
{
    [Fact]
    public void Nine_axes_cover_the_sliders_that_size_or_brighten_a_note()
    {
        Assert.Equal(9, OptionNumericPlan.Axes.Count);
        Assert.Equal("note-width", OptionNumericPlan.Axes[0].Id);
        Assert.Equal(54, OptionNumericPlan.CaptureCount);
        Assert.Equal(
            new[] { "note-width", "bar-width", "note-speed", "trail-intensity", "wave-intensity", "wave-size",
                    "burst-amount", "flash-intensity", "release-intensity" },
            OptionNumericPlan.Axes.Select(axis => axis.Id).ToArray());
    }

    [Fact]
    public void Every_axis_is_three_points_inside_the_products_own_range()
    {
        foreach (var axis in OptionNumericPlan.Axes)
        {
            Assert.Equal(3, axis.Values.Count);
            // Ascending and strictly distinct: a repeated value would compare a frame with itself.
            Assert.Equal(axis.Values.Order().ToArray(), axis.Values.ToArray());
            Assert.Equal(3, axis.Values.Distinct().Count());
            Assert.True(axis.Minimum < axis.Maximum, $"{axis.Id} declares an empty range");
            Assert.All(axis.Values, value => Assert.InRange(value, axis.Minimum, axis.Maximum));
            // At least one value at each end of the slider: an axis that only samples the middle would not
            // catch a clamp living at an endpoint.
            Assert.Contains(axis.Values, value => value == axis.Minimum || value == axis.Maximum);
        }
    }

    [Fact]
    public void A_width_axis_is_shot_at_the_two_values_the_old_floor_collapsed()
    {
        // 30 and 45 both drew a 60% bar on the software stage, so their frames were identical. The plan has to
        // keep asking about exactly that pair, on both the falling note and the held bar.
        foreach (var id in new[] { "note-width", "bar-width" })
        {
            var axis = OptionNumericPlan.Axes.Single(axis => axis.Id == id);
            Assert.Equal("NoteWidth", axis.Setting);
            Assert.Equal(new[] { 30d, 45d, 100d }, axis.Values.ToArray());
            Assert.Equal(30, axis.Minimum);
            Assert.Equal(100, axis.Maximum);
        }
        Assert.Equal(OptionMatrixPlan.Scene.Falling, OptionNumericPlan.Axes.Single(axis => axis.Id == "note-width").Scene);
        Assert.Equal(OptionMatrixPlan.Scene.Held, OptionNumericPlan.Axes.Single(axis => axis.Id == "bar-width").Scene);
    }

    [Fact]
    public void Each_axis_arranges_itself_through_a_family_the_flat_matrix_already_pins()
    {
        foreach (var axis in OptionNumericPlan.Axes)
        {
            var family = axis.Arrange; // throws if the id is unknown
            Assert.Contains(axis.FamilyValue, family.Values);
            // A value the flat matrix never captures would mean this axis has no pinned scene behind it.
            Assert.Contains(axis.FamilyValue, OptionMatrixPlan.CapturedValues(family));
            // The scene is the family's own moment, or the held key for a family that falls: the numeric
            // review must not invent a fifth moment of its own.
            var allowed = family.Scene == OptionMatrixPlan.Scene.Falling
                ? new[] { OptionMatrixPlan.Scene.Falling, OptionMatrixPlan.Scene.Held }
                : new[] { family.Scene };
            Assert.Contains(axis.Scene, allowed);
            Assert.True(axis.Frames >= 2);
        }
        // Two axes may share a setting (width on a note and on a bar); no two may share an id.
        Assert.Equal(9, OptionNumericPlan.Axes.Select(axis => axis.Id).Distinct().Count());
        Assert.Equal(8, OptionNumericPlan.Axes.Select(axis => axis.Setting).Distinct().Count());
    }

    [Fact]
    public void The_held_scene_has_its_own_moment_and_no_song()
    {
        Assert.Equal(30, OptionMatrixPlan.Frames(OptionMatrixPlan.Scene.Held));
        Assert.Equal(.5, OptionMatrixPlan.Seconds(OptionMatrixPlan.Scene.Held), 9);
        Assert.Empty(OptionMatrixPlan.Notes(OptionMatrixPlan.Scene.Held));
        Assert.False(OptionMatrixPlan.Playing(OptionMatrixPlan.Scene.Held));
        // The new scene must not disturb the four the flat matrix already measures.
        Assert.Equal(90, OptionMatrixPlan.Frames(OptionMatrixPlan.Scene.Falling));
        Assert.Equal(8, OptionMatrixPlan.Frames(OptionMatrixPlan.Scene.Impact));
        Assert.Equal(16, OptionMatrixPlan.Frames(OptionMatrixPlan.Scene.Release));
        Assert.Equal(90, OptionMatrixPlan.Frames(OptionMatrixPlan.Scene.Ambient));
    }

    [Theory]
    [InlineData(30, "30")]
    [InlineData(12.5, "12p5")]
    [InlineData(0, "0")]
    [InlineData(100, "100")]
    public void Labels_are_stable_file_names(double value, string expected)
    {
        Assert.Equal(expected, OptionNumericPlan.Label(value));
        Assert.All(OptionNumericPlan.Label(value), c => Assert.True(char.IsLetterOrDigit(c) || c == 'p'));
    }
}
