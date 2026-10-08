using Xunit;

namespace PianoPath.Tests;

public class ArQualityTests
{
    [Fact]
    public void The_default_tier_is_standard()
    {
        Assert.Equal("Standard", ArQualityTiers.DefaultQuality);
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(ArQualityTiers.DefaultQuality));
    }

    [Fact]
    public void TierOf_knows_the_three_names_and_defaults_to_standard()
    {
        Assert.Equal(ArQualityTier.Lite, ArQualityTiers.TierOf("Lite"));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf("Standard"));
        Assert.Equal(ArQualityTier.Ultra, ArQualityTiers.TierOf("Ultra"));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(null));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf(""));
        Assert.Equal(ArQualityTier.Standard, ArQualityTiers.TierOf("Ultra+"));
    }

    [Fact]
    public void Normalize_returns_one_of_the_three_known_names()
    {
        string[] all = ArQualityTiers.All;
        bool foundLite = false, foundStandard = false, foundUltra = false;
        foreach (var name in all)
        {
            if (name == "Lite") foundLite = true;
            if (name == "Standard") foundStandard = true;
            if (name == "Ultra") foundUltra = true;
        }
        Assert.True(foundLite && foundStandard && foundUltra);
        Assert.Equal("Standard", ArQualityTiers.Normalize("garbage"));
        Assert.Equal("Lite", ArQualityTiers.NameOf(ArQualityTier.Lite));
        Assert.Equal("Ultra", ArQualityTiers.NameOf(ArQualityTier.Ultra));
    }

    [Fact]
    public void Clamp_keeps_the_quality_in_range()
    {
        var settings = new PianoVisualSettings { ArQuality = "nonsense" };
        settings.Clamp();
        Assert.Equal("Standard", settings.ArQuality);
        settings.ArQuality = "Lite"; settings.Clamp();
        Assert.Equal("Lite", settings.ArQuality);
    }

    [Fact]
    public void Applying_a_look_never_changes_the_quality_tier()
    {
        var target = new PianoVisualSettings { ArQuality = "Lite" };
        var look = new PianoVisualSettings { ArQuality = "Ultra" };
        target.CopyFrom(look);
        Assert.Equal("Lite", target.ArQuality);
    }
}
