using System;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// AR-S3: known material IDs are passed through to the shared AR note shader. This is a code-path fact, not
/// a full parity claim; AR parameter coverage remains Unverified in the typed contract.
/// </summary>
public class ArLookGuardTests
{
    [Fact]
    public void Every_shipped_material_style_is_passed_through_to_the_shared_shader()
    {
        Assert.Equal(ArLookGuard.NoteStyles, ArLookGuard.PortedNoteStyles);
        foreach (var style in ArLookGuard.NoteStyles)
        {
            Assert.True(ArLookGuard.IsPortedToAr(style));
            Assert.Equal(style, ArLookGuard.StyleForAr(style));
        }
    }

    [Fact]
    public void Unknown_or_unported_styles_keep_the_neon_fallback()
    {
        Assert.False(ArLookGuard.IsPortedToAr(null));
        Assert.False(ArLookGuard.IsPortedToAr("FutureMaterial"));
        Assert.Equal(ArLookGuard.ArFallbackStyle, ArLookGuard.StyleForAr(null));
        Assert.Equal("Neon", ArLookGuard.StyleForAr("FutureMaterial"));
    }

    [Fact]
    public void Style_index_preserves_each_material_and_fails_closed_out_of_range()
    {
        for (var index = 0; index < ArLookGuard.NoteStyles.Length; index++)
            Assert.Equal(index, ArLookGuard.StyleIndexForAr(index));
        var neonIndex = Array.IndexOf(ArLookGuard.NoteStyles, "Neon");
        Assert.Equal(neonIndex, ArLookGuard.StyleIndexForAr(-1));
        Assert.Equal(neonIndex, ArLookGuard.StyleIndexForAr(999));
    }
}
