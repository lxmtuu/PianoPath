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
        bool foundLite = false, foundStandard = false, foundUltra = false;
        foreach (var name in ArQualityTiers.All)
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
}
