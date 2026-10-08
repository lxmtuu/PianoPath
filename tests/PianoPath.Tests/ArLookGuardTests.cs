using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// AR-S3: the AR look guard (docs/AR-OVERLAY-ROADMAP.md §14.12). A note style with no AR variant — currently
/// Gem and Crystal Outline — keeps its flat fallback instead of claiming the facet look the flat-quad
/// projection cannot give it. Pure: what is ported is a fact about the code.
/// </summary>
public class ArLookGuardTests
{
    [Fact]
    public void A_ported_style_is_passed_through_unchanged()
    {
        Assert.True(ArLookGuard.IsPortedToAr("Neon"));
        Assert.True(ArLookGuard.IsPortedToAr("Solid"));
        Assert.Equal("Neon", ArLookGuard.StyleForAr("Neon"));
        Assert.Equal("Fire", ArLookGuard.StyleForAr("Fire"));
    }

    [Fact]
    public void Gem_and_CrystalOutline_keep_the_flat_neon_fallback()
    {
        Assert.False(ArLookGuard.IsPortedToAr("Gem"));
        Assert.False(ArLookGuard.IsPortedToAr("CrystalOutline"));
        Assert.Equal(ArLookGuard.ArFallbackStyle, ArLookGuard.StyleForAr("Gem"));
        Assert.Equal(ArLookGuard.ArFallbackStyle, ArLookGuard.StyleForAr("CrystalOutline"));
        Assert.Equal("Neon", ArLookGuard.StyleForAr(null));
        Assert.Equal("Neon", ArLookGuard.StyleForAr("Unknown"));
    }

    [Fact]
    public void Style_index_round_trips_to_the_fallback_for_unported_styles()
    {
        var neonIndex = Array.IndexOf(ArLookGuard.NoteStyles, "Neon");
        var gemIndex = Array.IndexOf(ArLookGuard.NoteStyles, "Gem");
        var crystalIndex = Array.IndexOf(ArLookGuard.NoteStyles, "CrystalOutline");
        Assert.Equal(neonIndex, ArLookGuard.StyleIndexForAr(gemIndex));
        Assert.Equal(neonIndex, ArLookGuard.StyleIndexForAr(crystalIndex));
        var solidIndex = Array.IndexOf(ArLookGuard.NoteStyles, "Solid");
        Assert.Equal(solidIndex, ArLookGuard.StyleIndexForAr(solidIndex));
        Assert.Equal(neonIndex, ArLookGuard.StyleIndexForAr(999));
    }
}
