using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The contact sheet is the one part of a review that CI commits, so it cannot be drawn: these checks run on
/// Linux with no window, no graphics device and no installed font, and they pin the layout, the box downscale,
/// the two alpha formats a review hands over, and the byte-for-byte determinism the commit depends on.
/// </summary>
public class ReviewSheetTests
{
    private const int TileWidth = 40, TileHeight = 20;

    /// <summary>One flat colour, at the size the sheet will downscale from.</summary>
    private static byte[] Solid(int width, int height, byte blue, byte green, byte red, byte alpha = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var at = 0; at < pixels.Length; at += 4)
        {
            pixels[at] = blue;
            pixels[at + 1] = green;
            pixels[at + 2] = red;
            pixels[at + 3] = alpha;
        }
        return pixels;
    }

    /// <summary>The BGRA of one pixel, so a check states the colour it expects rather than four indexes.</summary>
    private static (int Blue, int Green, int Red, int Alpha) At(byte[] pixels, int width, int x, int y)
    {
        var at = (y * width + x) * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
    }

    /// <summary>Where the first cell starts: the page margin, then the title strip.</summary>
    private static int CellX => ReviewSheet.Padding + ReviewSheet.Border;
    private static int CellY => ReviewSheet.Padding + ReviewSheet.TitleHeight + ReviewSheet.Border;

    [Fact]
    public void The_grid_is_the_cells_plus_the_gaps_between_them()
    {
        var sheet = ReviewSheet.Measure(21, 320, 180, 4);

        Assert.Equal(4, sheet.Columns);
        // Twenty-one cells over four columns is six rows; the last row carries one picture and three gaps.
        Assert.Equal(6, sheet.Rows);
        // A cell is the picture plus its one-pixel frame on each side.
        Assert.Equal(2 * ReviewSheet.Padding + 4 * 322 + 3 * ReviewSheet.Gap, sheet.Width);
        // A cell is the picture, its frame and the caption strip under it.
        Assert.Equal(2 * ReviewSheet.Padding + ReviewSheet.TitleHeight + 6 * 202 + 5 * ReviewSheet.Gap, sheet.Height);
        Assert.Equal(1342, sheet.Width);
        Assert.Equal(1314, sheet.Height);
    }

    [Fact]
    public void A_sheet_asks_for_no_more_columns_than_it_has_pictures()
    {
        var sheet = ReviewSheet.Measure(3, TileWidth, TileHeight, 8);

        Assert.Equal(3, sheet.Columns);
        Assert.Equal(1, sheet.Rows);
        Assert.Equal(2 * ReviewSheet.Padding + 3 * (TileWidth + 2) + 2 * ReviewSheet.Gap, sheet.Width);
    }

    [Fact]
    public void One_pose_of_the_ar_slice_fills_a_whole_sheet()
    {
        // The plan is the authority on how many pictures one pose writes: every family contributes its off
        // state once, and every row adds one picture on top of it.
        static int Captures(IReadOnlyList<(string Family, string Value)> rows) =>
            rows.Select(row => OptionMatrixPlan.PoseFamily(row.Family)).Distinct().Count() + rows.Count;

        var perPose = Captures(OptionMatrixPlan.PoseSelections) + Captures(OptionMatrixPlan.PoseSliceSelections);

        Assert.Equal(21, perPose);
        Assert.Equal(63, perPose * 3);
        var sheet = ReviewSheet.Measure(perPose, 320, 180, 4);
        Assert.Equal(6, sheet.Rows);
        Assert.Equal(4 * 322 + 3 * ReviewSheet.Gap + 2 * ReviewSheet.Padding, sheet.Width);
    }

    [Fact]
    public void A_flat_tile_reaches_the_page_unchanged()
    {
        var sheet = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty, captionScale: 1);
        sheet.Blit(0, "GEM", Solid(TileWidth * 2, TileHeight * 2, 10, 20, 30), TileWidth * 2, TileHeight * 2,
            premultiplied: false);

        var pixels = sheet.Finish();
        Assert.Equal((10, 20, 30, 255), At(pixels, sheet.Sheet.Width, CellX, CellY));
        Assert.Equal((10, 20, 30, 255),
            At(pixels, sheet.Sheet.Width, CellX + TileWidth - 1, CellY + TileHeight - 1));
    }

    [Fact]
    public void A_tile_shrinks_by_averaging_its_blocks_not_by_dropping_pixels()
    {
        // Left half blue, right half red, at twice the cell. The seam is one pixel off the block boundary, so
        // exactly one cell column has to be half of each: a downscale that samples instead of averaging picks
        // one of the two colours and the seam moves.
        var width = TileWidth * 2;
        var height = TileHeight * 2;
        var seam = width / 2 + 1;
        var source = Solid(width, height, 0, 0, 0);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            var blue = x < seam;
            source[at] = blue ? (byte)255 : (byte)0;
            source[at + 1] = 0;
            source[at + 2] = blue ? (byte)0 : (byte)255;
            source[at + 3] = 255;
        }

        var sheet = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty);
        sheet.Blit(0, string.Empty, source, width, height, premultiplied: true);

        var pixels = sheet.Finish();
        Assert.Equal((255, 0, 0, 255), At(pixels, sheet.Sheet.Width, CellX, CellY));
        Assert.Equal((0, 0, 255, 255), At(pixels, sheet.Sheet.Width, CellX + TileWidth - 1, CellY + TileHeight - 1));
        for (var ty = 0; ty < TileHeight; ty++)
            Assert.Equal((128, 0, 128, 255), At(pixels, sheet.Sheet.Width, CellX + seam / 2, CellY + ty));
    }

    [Fact]
    public void A_straight_alpha_tile_composes_like_its_premultiplied_form()
    {
        // Half-covered white over nothing: averaged as straight alpha the three unlit pixels would keep their
        // white and the cell would come out three quarters lit instead of one half.
        var width = TileWidth * 2;
        var straight = new byte[width * TileHeight * 2 * 4];
        var premultiplied = new byte[straight.Length];
        for (var at = 0; at < straight.Length; at += 4)
        {
            straight[at] = straight[at + 1] = straight[at + 2] = 255;
            straight[at + 3] = 128;
            premultiplied[at] = premultiplied[at + 1] = premultiplied[at + 2] = 128;
            premultiplied[at + 3] = 128;
        }

        var fromStraight = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty);
        fromStraight.Blit(0, string.Empty, straight, width, TileHeight * 2, premultiplied: false);
        var fromPremultiplied = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty);
        fromPremultiplied.Blit(0, string.Empty, premultiplied, width, TileHeight * 2, premultiplied: true);

        // Compared as bytes rather than through the collection overload: this is the claim itself, not a shape.
        Assert.True(fromPremultiplied.Finish().AsSpan().SequenceEqual(fromStraight.Finish()),
            "the straight-alpha tile composed to different bytes than its premultiplied form");
    }

    [Fact]
    public void A_transparent_tile_leaves_the_page_showing()
    {
        var sheet = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty);
        sheet.Blit(0, string.Empty, Solid(TileWidth, TileHeight, 0, 0, 0, 0), TileWidth, TileHeight,
            premultiplied: true);

        var pixels = sheet.Finish();
        // 0x10141B: the same dark page the review HTML uses.
        Assert.Equal((0x1B, 0x14, 0x10, 255), At(pixels, sheet.Sheet.Width, CellX, CellY));
    }

    [Fact]
    public void A_caption_stays_in_the_strip_under_its_own_picture()
    {
        var sheet = new ReviewSheet.Canvas(2, TileWidth, TileHeight, 2, "N12", captionScale: 1);
        sheet.Blit(0, "GEM", Solid(TileWidth, TileHeight, 40, 40, 40), TileWidth, TileHeight, premultiplied: false);
        sheet.Blit(1, string.Empty, Solid(TileWidth, TileHeight, 40, 40, 40), TileWidth, TileHeight,
            premultiplied: false);

        var pixels = sheet.Finish();
        var width = sheet.Sheet.Width;
        var stripTop = CellY + TileHeight;
        var stripBottom = stripTop + ReviewSheet.CaptionHeight;
        var cellRight = CellX + TileWidth;
        var ink = 0;
        for (var y = 0; y < sheet.Sheet.Height; y++)
        for (var x = 0; x < width; x++)
        {
            // Ink is 0xC9D6E6; the caption strip is the only opaque light colour on the page.
            if (At(pixels, width, x, y) != (0xE6, 0xD6, 0xC9, 255)) continue;
            ink++;
            Assert.InRange(x, CellX, cellRight - 1);
            Assert.InRange(y, stripTop, stripBottom - 1);
        }
        Assert.True(ink > 0, "the caption drew nothing at all");
        // The picture above the strip is untouched: clipping is the strip's job, not the picture's.
        Assert.Equal((40, 40, 40, 255), At(pixels, width, cellRight - 1, stripTop - 1));
    }

    [Fact]
    public void A_caption_that_does_not_fit_is_refused_instead_of_being_cut()
    {
        // Cutting mid-word is how "AMBIENT-ENERGY CONFETTI RAIN" became "CONFETTI RA" on a committed page: the
        // reader cannot tell a shortened value from a different one.
        var sheet = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty, captionScale: 1);

        var tooLong = Assert.Throws<System.IO.InvalidDataException>(() =>
            sheet.Blit(0, "A CAPTION FAR TOO LONG FOR A FORTY PIXEL STRIP", Solid(TileWidth, TileHeight, 0, 0, 0),
                TileWidth, TileHeight, false));
        Assert.Contains("instead of cutting it", tooLong.Message);
    }

    [Fact]
    public void Every_caption_on_a_sheet_is_the_same_size()
    {
        // The page picked by CaptionScaleFor, not the cell: a twelve-character caption beside a
        // twenty-seven-character one used to come out at twice the height.
        var captions = new[] { "GEM", "AMBIENT-LIGHT GRADIENT WAVE" };
        Assert.Equal(1, ReviewSheet.CaptionScaleFor(captions, 176));
        Assert.Equal(2, ReviewSheet.CaptionScaleFor(new[] { "GEM", "RIBBON" }, 176));

        var sheet = new ReviewSheet.Canvas(2, 176, TileHeight, 2, string.Empty,
            ReviewSheet.CaptionScaleFor(captions, 176));
        sheet.Blit(0, captions[0], Solid(176, TileHeight, 0, 0, 0), 176, TileHeight, false);
        sheet.Blit(1, captions[1], Solid(176, TileHeight, 0, 0, 0), 176, TileHeight, false);

        var pixels = sheet.Finish();
        var width = sheet.Sheet.Width;
        var inkRows = new List<(int Column, int Row)>();
        for (var y = 0; y < sheet.Sheet.Height; y++)
        for (var x = 0; x < width; x++)
            if (At(pixels, width, x, y) == (0xE6, 0xD6, 0xC9, 255))
                inkRows.Add((x < ReviewSheet.Padding + 178 ? 0 : 1, y));
        foreach (var column in inkRows.GroupBy(entry => entry.Column))
        {
            var rows = column.Select(entry => entry.Row).Distinct().Count();
            Assert.Equal(7, rows);
        }
    }

    [Fact]
    public void A_cell_that_is_drawn_twice_is_an_error()
    {
        var sheet = new ReviewSheet.Canvas(2, TileWidth, TileHeight, 2, string.Empty);
        sheet.Blit(0, string.Empty, Solid(TileWidth, TileHeight, 0, 0, 0), TileWidth, TileHeight, false);

        var again = Assert.Throws<System.IO.InvalidDataException>(() =>
            sheet.Blit(0, string.Empty, Solid(TileWidth, TileHeight, 0, 0, 0), TileWidth, TileHeight, false));
        Assert.Contains("drawn twice", again.Message);
    }

    [Fact]
    public void A_sheet_with_a_hole_is_never_handed_over()
    {
        var sheet = new ReviewSheet.Canvas(2, TileWidth, TileHeight, 2, "N12");
        sheet.Blit(0, string.Empty, Solid(TileWidth, TileHeight, 0, 0, 0), TileWidth, TileHeight, false);

        var missing = Assert.Throws<System.IO.InvalidDataException>(() => sheet.Finish());
        Assert.Contains("never drawn", missing.Message);
    }

    [Fact]
    public void A_tile_smaller_than_its_cell_is_refused_instead_of_being_stretched()
    {
        var sheet = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty);

        Assert.Throws<System.IO.InvalidDataException>(() =>
            sheet.Blit(0, string.Empty, Solid(TileWidth / 2, TileHeight / 2, 0, 0, 0), TileWidth / 2,
                TileHeight / 2, false));
    }

    [Fact]
    public void Every_glyph_the_captions_use_draws_ink()
    {
        // The font is built the first time the class is touched and refuses a glyph that is not seven rows of
        // five cells, which is how a separator typo failed loudly once already. This walks the characters a
        // caption can hold so a glyph that is the right shape but blank is named here rather than noticed on a
        // committed page.
        foreach (var character in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-=./:()+,_#?")
        {
            var sheet = new ReviewSheet.Canvas(1, TileWidth, TileHeight, 1, string.Empty);
            sheet.Blit(0, character.ToString(), Solid(TileWidth, TileHeight, 0, 0, 0), TileWidth, TileHeight,
                premultiplied: false);
            var pixels = sheet.Finish();
            var ink = 0;
            for (var y = 0; y < sheet.Sheet.Height; y++)
            for (var x = 0; x < sheet.Sheet.Width; x++)
                if (At(pixels, sheet.Sheet.Width, x, y) == (0xE6, 0xD6, 0xC9, 255)) ink++;
            Assert.True(ink > 0, $"the glyph '{character}' drew nothing");
        }
    }

    [Fact]
    public void The_same_tiles_always_compose_to_the_same_bytes()
    {
        static byte[] Compose()
        {
            var sheet = new ReviewSheet.Canvas(3, TileWidth, TileHeight, 2, "N12(D) FRONT", captionScale: 1);
            for (var index = 0; index < 3; index++)
                sheet.Blit(index, $"C{index}", Solid(TileWidth * 2, TileHeight * 2, (byte)(index * 40), 12, 200),
                    TileWidth * 2, TileHeight * 2, premultiplied: false);
            return sheet.Finish();
        }

        Assert.True(Compose().AsSpan().SequenceEqual(Compose()),
            "two runs of the same tiles differed; a sheet that changes between runs cannot be committed");
    }
}
