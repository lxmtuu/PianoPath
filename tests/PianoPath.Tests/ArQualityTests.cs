using Xunit;

namespace PianoPath.Tests;

/// <summary>AR-S3: the three quality tier names and how they round-trip through storage.</summary>
public class ArQualityTests
{
    [Fact]
    public void The_default_tier_is_standard_not_lite()
    {
        Assert.Equal(ArQualityTiers.StandardName, ArQualityTiers.DefaultQuality);
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(ArQualityTiers.DefaultQuality));
    }

    [Fact]
    public void TierOf_reads_known_names_and_returns_standard_for_everything_else()
    {
        Assert.Equal(ArQualityTier.Lite, ArQualityTiers.TierOf(ArQualityTiers.LiteName));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(ArQualityTiers.StandardName));
        Assert.Equal(ArQualityTier.Ultra, ArQualityTiers.TierOf(ArQualityTiers.UltraName));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(null));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(""));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf("Ultra+"));
    }

    [Fact]
    public void Normalize_writes_one_of_the_three_known_names()
    {
        Assert.Contains(ArQualityTiers.Normalize("lite"), ArQualityTiers.All);
        Assert.Contains(ArQualityTiers.Normalize(null), ArQualityTiers.All);
        Assert.Equal(ArQualityTiers.StandardName, ArQualityTiers.Normalize("garbage"));
        Assert.Equal(ArQualityTiers.LiteName, ArQualityTiers.NameOf(ArQualityTier.Lite));
        Assert.Equal(ArQualityTiers.UltraName, ArQualityTiers.NameOf(ArQualityTier.Ultra));
    }

    [Fact]
    public void Clamping_keeps_the_quality_in_range_for_a_settings_object()
    {
        var settings = new PianoVisualSettings { ArQuality = "nonsense" };
        settings.Clamp();
        Assert.Equal(ArQualityTiers.StandardName, settings.ArQuality);
        settings.ArQuality = ArQualityTiers.LiteName; settings.Clamp();
        Assert.Equal(ArQualityTiers.LiteName, settings.ArQuality);
    }

    [Fact]
    public void Applying_a_look_never_changes_the_quality_tier()
    {
        var target = new PianoVisualSettings { ArQuality = ArQualityTiers.LiteName };
        var look = new PianoVisualSettings { ArQuality = ArQualityTiers.UltraName };
        target.CopyFrom(look);
        Assert.Equal(ArQualityTiers.LiteName, target.ArQuality);
    }}
