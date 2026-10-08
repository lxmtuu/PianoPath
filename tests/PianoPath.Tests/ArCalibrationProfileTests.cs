using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The catalogue of camera views: the ids a calibration file may name, the name each one shows, and the rule that an id
/// the app does not know falls back to the view it has always used rather than to nothing.
/// </summary>
public class ArCalibrationProfileTests
{
    [Fact]
    public void The_catalogue_holds_the_default_view_first()
    {
        Assert.Equal(ArCalibrationProfiles.LegacyId, ArCalibrationProfiles.All[0].Id);
        Assert.Equal(ArCalibrationProfiles.Legacy, ArCalibrationProfiles.All[0]);
        // The examples the overlay was asked to start with: a camera about 45° down and one looking straight down.
        Assert.Contains(ArCalibrationProfiles.All, profile => profile.Id == "tilt45");
        Assert.Contains(ArCalibrationProfiles.All, profile => profile.Id == "overhead90");
    }

    [Fact]
    public void Every_id_is_different_and_lower_case_so_a_file_and_the_settings_agree()
    {
        var ids = ArCalibrationProfiles.All.Select(profile => profile.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(id, id.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("Default")]
    [InlineData("tilt45")]
    [InlineData("TILT45")]
    [InlineData("overhead90")]
    public void An_id_names_its_view_without_regard_to_case(string id)
    {
        var profile = ArCalibrationProfiles.ById(id);
        Assert.NotNull(profile);
        Assert.True(ArCalibrationProfiles.IsKnown(id));
        Assert.Equal(profile!.Id, ArCalibrationProfiles.Normalize(id).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("some-future-view")]
    [InlineData("tilt-45")]
    [InlineData("90")]
    public void Anything_that_is_not_a_view_the_app_knows_is_the_default_view(string? id)
    {
        Assert.Null(ArCalibrationProfiles.ById(id));
        Assert.False(ArCalibrationProfiles.IsKnown(id));
        Assert.Same(ArCalibrationProfiles.Legacy, ArCalibrationProfiles.Normalize(id));
    }

    [Theory]
    [InlineData("Default", "Mặc định")]
    [InlineData("45°", "45°")]
    [InlineData("90° overhead", "90° từ trên xuống")]
    public void A_view_name_is_a_key_of_both_string_tables_so_the_picker_speaks_both_languages(string name, string vietnamese)
    {
        var profile = ArCalibrationProfiles.All.Single(profile => profile.Name == name);
        Assert.Equal(name, profile.Name);
        Assert.True(StringsEnglish.Table.ContainsKey(name), $"{name} should be a key of the English inventory");
        Assert.Equal(vietnamese, StringsVietnamese.Table[name]);
    }
}
