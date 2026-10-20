using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The N12(b) matrix has to stay a matrix: one family per settings axis, every value the product offers, one
/// off state each, and one capture per value. These checks run on Linux without WPF, so they pin the table that
/// the Windows capture driver walks — <c>tools/check_sources.py</c> pins the same table to
/// <see cref="PianoVisualSettings"/>, which is the half that needs the product class.
/// </summary>
public class OptionMatrixPlanTests
{
    [Fact]
    public void The_matrix_covers_eleven_settings_axes_and_every_value_they_offer()
    {
        Assert.Equal(11, OptionMatrixPlan.Families.Count);
        Assert.Equal(62, OptionMatrixPlan.Families.Sum(family => family.Values.Count));
        Assert.Equal(
            new[] { "note-style", "falling-trail", "impact-wave", "impact-burst", "impact-morph", "impact-flash",
                    "release", "ambient-energy", "ambient-nature", "ambient-light", "ambient-cosmic" },
            OptionMatrixPlan.Families.Select(family => family.Id).ToArray());
        Assert.Equal(
            new[] { "NoteStyle", "FallingTrail", "ImpactWave", "ImpactBurst", "ImpactMorph", "ImpactFlashStyle",
                    "ReleaseEffect", "AmbientEnergy", "AmbientNature", "AmbientLight", "AmbientCosmic" },
            OptionMatrixPlan.Families.Select(family => family.Setting).ToArray());
    }

    [Fact]
    public void Every_family_names_an_off_state_it_can_reach()
    {
        foreach (var family in OptionMatrixPlan.Families)
        {
            Assert.Contains(family.Neutral, family.Values);
            Assert.Equal(family.Values.Count, family.Values.Distinct(StringComparer.Ordinal).Count());
            // Only the two channels whose every value is an effect need a visibility gate; for the rest the
            // neutral is a real value and the baseline sets it.
            Assert.Equal(family.Neutral is "Embers" or "Flash", family.Gate is not null);
        }
    }

    [Fact]
    public void The_off_state_is_not_captured_twice_unless_a_gate_defines_it()
    {
        foreach (var family in OptionMatrixPlan.Families)
        {
            var captured = OptionMatrixPlan.CapturedValues(family);
            if (family.Gate is null)
            {
                Assert.DoesNotContain(family.Neutral, captured);
                Assert.Equal(family.Values.Count - 1, captured.Count);
                Assert.True(OptionMatrixPlan.IsNeutral(family, family.Neutral));
            }
            else
            {
                // Every value draws something, so the baseline is the channel switched off and all values stay.
                Assert.Equal(family.Values, captured);
                Assert.False(OptionMatrixPlan.IsNeutral(family, family.Neutral));
            }
        }
        Assert.Equal(64, OptionMatrixPlan.SceneCount);
        Assert.Equal(128, OptionMatrixPlan.SceneCount * 2);
    }

    [Fact]
    public void Each_scene_has_one_frame_count_and_one_sample_time()
    {
        foreach (var family in OptionMatrixPlan.Families)
        {
            var frames = OptionMatrixPlan.Frames(family);
            Assert.InRange(frames, 2, 120);
            Assert.Equal(frames / 60.0, OptionMatrixPlan.Seconds(family), 9);
            // The sample must land inside every lifetime the scene shows: 0.117 s after the hit for the impact
            // channels (0.26-0.55 s lives) and 0.133 s after the release for the release channel.
            if (family.Scene == OptionMatrixPlan.Scene.Impact) Assert.Equal(8, frames);
            if (family.Scene == OptionMatrixPlan.Scene.Release) Assert.Equal(16, frames);
        }
        Assert.Equal(90, OptionMatrixPlan.Frames(OptionMatrixPlan.Families.Single(family => family.Id == "falling-trail")));
    }

    [Fact]
    public void Only_the_falling_scene_plays_a_song_note()
    {
        foreach (var family in OptionMatrixPlan.Families)
        {
            var notes = OptionMatrixPlan.Notes(family);
            Assert.Equal(family.Scene == OptionMatrixPlan.Scene.Falling, OptionMatrixPlan.Playing(family));
            if (family.Scene != OptionMatrixPlan.Scene.Falling) Assert.Empty(notes);
            else
            {
                var note = Assert.Single(notes);
                Assert.Equal(OptionMatrixPlan.Pitch, note.Pitch);
                Assert.True(note.Duration > OptionMatrixPlan.Seconds(family), "the sampled note must still sound at the capture");
            }
        }
    }

    [Fact]
    public void Slugs_are_stable_file_names()
    {
        Assert.Equal("speed-lines", OptionMatrixPlan.Slug("Speed Lines"));
        Assert.Equal("crystaloutline", OptionMatrixPlan.Slug("CrystalOutline"));
        Assert.Equal("lightning-storm", OptionMatrixPlan.Slug("Lightning Storm"));
        Assert.Equal("none", OptionMatrixPlan.Slug("None"));
        foreach (var family in OptionMatrixPlan.Families)
        foreach (var value in family.Values)
        {
            var slug = OptionMatrixPlan.Slug(value);
            Assert.False(string.IsNullOrWhiteSpace(slug));
            Assert.All(slug, c => Assert.True(char.IsLetterOrDigit(c) || c == '-'));
            Assert.DoesNotContain("--", slug);
        }
        // One value per family cannot share another's file name.
        Assert.All(OptionMatrixPlan.Families, family =>
            Assert.Equal(family.Values.Count, family.Values.Select(OptionMatrixPlan.Slug).Distinct(StringComparer.Ordinal).Count()));
    }

    [Fact]
    public void Changed_pixels_ignore_alpha_and_the_two_byte_floor()
    {
        byte[] black = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] sameButAlpha = [0, 0, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] smallStep = [2, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] onePixel = [9, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        Assert.Equal(0, OptionMatrixPlan.ChangedPixels(black, sameButAlpha));
        Assert.Equal(0, OptionMatrixPlan.ChangedPixels(black, smallStep));
        Assert.Equal(1, OptionMatrixPlan.ChangedPixels(black, onePixel));
        Assert.Equal(1, OptionMatrixPlan.NonBlackPixels(onePixel));
        Assert.Equal(0, OptionMatrixPlan.NonBlackPixels(black));
        Assert.Equal(0, OptionMatrixPlan.NonBlackPixels(smallStep)); // a 2/255 step is still the black floor
        Assert.Throws<ArgumentException>(() => OptionMatrixPlan.ChangedPixels(black, new byte[4]));
    }
}
