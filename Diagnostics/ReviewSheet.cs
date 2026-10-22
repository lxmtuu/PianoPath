using System.IO;

namespace PianoPath;

/// <summary>
/// Lays the pictures a review wrote out in one grid with a caption under each, so the evidence can be committed
/// to the repository instead of living in a two-day artifact on a runner.
///
/// <para>
/// The sheet is composed here rather than through WPF drawing, so the layout, the box downscale, the label font
/// and the compositing are plain arithmetic over a byte array. That buys three things the checks need: it runs on
/// Linux with no window, no graphics device and no installed font; the same tiles always produce the same bytes;
/// and a reviewer can read the layout from the numbers instead of opening the picture. The last one is what makes
/// it safe for CI to commit the sheets — a picture that differs between two runs would turn every build into a
/// diff nobody can review.
/// </para>
///
/// <para>
/// Tiles are BGRA, like every review PNG. A tile is handed over either premultiplied (the software stage) or
/// straight (the Direct3D readback and <see cref="ReviewPng.Decode"/>), which is why <see cref="Canvas.Blit"/>
/// takes a flag: averaging straight alpha would drag the colour of a transparent pixel into the picture next to
/// it, and a review sheet is exactly the place where that shows up as a halo around the art being judged.
/// </para>
/// </summary>
internal static class ReviewSheet
{
    /// <summary>Space between the page edge and the grid, in pixels.</summary>
    internal const int Padding = 12;
    /// <summary>Space between two cells, in pixels.</summary>
    internal const int Gap = 10;
    /// <summary>Width of the frame drawn round one picture, in pixels.</summary>
    internal const int Border = 1;
    /// <summary>Height of the caption strip under one picture, in pixels.</summary>
    internal const int CaptionHeight = 20;
    /// <summary>Height of the title strip above the grid, in pixels.</summary>
    internal const int TitleHeight = 28;

    /// <summary>The grid a sheet comes to: its pixel size, the cell size, and how the cells are laid out.</summary>
    internal readonly record struct Layout(int Width, int Height, int TileWidth, int TileHeight, int Columns, int Rows);

    private static readonly (byte B, byte G, byte R) Page = (0x1B, 0x14, 0x10);
    private static readonly (byte B, byte G, byte R) Cell = (0x2B, 0x21, 0x1A);
    private static readonly (byte B, byte G, byte R) Frame = (0x43, 0x32, 0x26);
    private static readonly (byte B, byte G, byte R) Ink = (0xE6, 0xD6, 0xC9);
    private static readonly (byte B, byte G, byte R) Heading = (0xF2, 0xE4, 0xD2);

    /// <summary>Cells of one glyph, left to right and top to bottom.</summary>
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    /// <summary>Distance one character takes, including the column that separates it from the next.</summary>
    private const int GlyphAdvance = GlyphWidth + 1;

    /// <summary>
    /// A five-by-seven bitmap font, written as the picture it is: one string per glyph, seven rows of five cells
    /// separated by '|', '#' for ink.
    ///
    /// <para>
    /// The captions are drawn from this rather than from a system font because a contact sheet that CI commits
    /// has to be byte-identical between two runs, and rasterised text is not. Only the characters a caption can
    /// hold are here, in capitals; anything else falls back to '?' instead of leaving a blank where a word
    /// should be. This has to stay above <see cref="Font"/>: static fields are initialised in the order they are
    /// written, and the font is built from this table.
    /// </para>
    /// </summary>
    private static readonly (char Key, string Pattern)[] Patterns =
    [
        ('A', ".###.|#...#|#...#|#####|#...#|#...#|#...#"),
        ('B', "####.|#...#|#...#|####.|#...#|#...#|####."),
        ('C', ".####|#....|#....|#....|#....|#....|.####"),
        ('D', "####.|#...#|#...#|#...#|#...#|#...#|####."),
        ('E', "#####|#....|#....|####.|#....|#....|#####"),
        ('F', "#####|#....|#....|####.|#....|#....|#...."),
        ('G', ".####|#....|#....|#..##|#...#|#...#|.###."),
        ('H', "#...#|#...#|#...#|#####|#...#|#...#|#...#"),
        ('I', "#####|..#..|..#..|..#..|..#..|..#..|#####"),
        ('J', "..###|...#.|...#.|...#.|...#.|#..#.|.##.."),
        ('K', "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#"),
        ('L', "#....|#....|#....|#....|#....|#....|#####"),
        ('M', "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#"),
        ('N', "#...#|##..#|#.#.#|#..##|#...#|#...#|#...#"),
        ('O', ".###.|#...#|#...#|#...#|#...#|#...#|.###."),
        ('P', "####.|#...#|#...#|####.|#....|#....|#...."),
        ('Q', ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#"),
        ('R', "####.|#...#|#...#|####.|#.#..|#..#.|#...#"),
        ('S', ".####|#....|#....|.###.|....#|....#|####."),
        ('T', "#####|..#..|..#..|..#..|..#..|..#..|..#.."),
        ('U', "#...#|#...#|#...#|#...#|#...#|#...#|.###."),
        ('V', "#...#|#...#|#...#|#...#|#...#|.#.#.|..#.."),
        ('W', "#...#|#...#|#...#|#.#.#|#.#.#|##.##|#...#"),
        ('X', "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#"),
        ('Y', "#...#|#...#|.#.#.|..#..|..#..|..#..|..#.."),
        ('Z', "#####|....#|...#.|..#..|.#...|#....|#####"),
        ('0', ".###.|#...#|#..##|#.#.#|##..#|#...#|.###."),
        ('1', "..#..|.##..|..#..|..#..|..#..|..#..|.###."),
        ('2', ".###.|#...#|....#|...#.|..#..|.#...|#####"),
        ('3', "#####|...#.|..#..|...#.|....#|#...#|.###."),
        ('4', "...#.|..##.|.#.#.|#..#.|#####|...#.|...#."),
        ('5', "#####|#....|####.|....#|....#|#...#|.###."),
        ('6', "..##.|.#...|#....|####.|#...#|#...#|.###."),
        ('7', "#####|....#|...#.|..#..|.#...|.#...|.#..."),
        ('8', ".###.|#...#|#...#|.###.|#...#|#...#|.###."),
        ('9', ".###.|#...#|#...#|.####|....#|...#.|.##.."),
        ('-', ".....|.....|.....|.###.|.....|.....|....."),
        ('=', ".....|.....|#####|.....|#####|.....|....."),
        ('.', ".....|.....|.....|.....|.....|.##..|.##.."),
        ('/', "....#|....#|...#.|..#..|.#...|#....|#...."),
        (':', ".....|.##..|.##..|.....|.##..|.##..|....."),
        ('(', "...#.|..#..|.#...|.#...|.#...|..#..|...#."),
        (')', ".#...|..#..|...#.|...#.|...#.|..#..|.#..."),
        ('%', "##..#|##.#.|...#.|..#..|.#...|#.##.|#.##."),
        ('+', ".....|..#..|..#..|#####|..#..|..#..|....."),
        (',', ".....|.....|.....|.....|.##..|.##..|.#..."),
        ('_', ".....|.....|.....|.....|.....|.....|#####"),
        ('#', ".#.#.|.#.#.|#####|.#.#.|#####|.#.#.|.#.#."),
        ('?', ".###.|#...#|....#|...#.|..#..|.....|..#.."),
        (' ', ".....|.....|.....|.....|.....|.....|....."),
    ];

    /// <summary>The parsed font. Must stay below <see cref="Patterns"/>: it is built from it.</summary>
    private static readonly IReadOnlyDictionary<char, string[]> Font = BuildFont();

    /// <summary>
    /// The size a sheet of <paramref name="count"/> tiles comes to, without drawing it: the columns decide the
    /// width, and the rows those columns leave decide the height.
    /// </summary>
    internal static Layout Measure(int count, int tileWidth, int tileHeight, int columns)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "A sheet needs at least one tile.");
        if (tileWidth < 1) throw new ArgumentOutOfRangeException(nameof(tileWidth), tileWidth, "A tile needs a width.");
        if (tileHeight < 1) throw new ArgumentOutOfRangeException(nameof(tileHeight), tileHeight, "A tile needs a height.");
        var across = Math.Clamp(columns, 1, count);
        var rows = (count + across - 1) / across;
        var cellWidth = tileWidth + 2 * Border;
        var cellHeight = tileHeight + 2 * Border + CaptionHeight;
        return new Layout(
            Padding * 2 + across * cellWidth + (across - 1) * Gap,
            Padding * 2 + TitleHeight + rows * cellHeight + (rows - 1) * Gap,
            tileWidth, tileHeight, across, rows);
    }

    /// <summary>
    /// One sheet being drawn. Cells are filled by index, row by row and left to right, so a review can post each
    /// picture as it captures it and never has to hold a whole run of frames to make one grid.
    /// </summary>
    internal sealed class Canvas
    {
        private readonly byte[] _pixels;
        private readonly bool[] _filled;
        private readonly string _title;

        internal Canvas(int count, int tileWidth, int tileHeight, int columns, string title)
        {
            Sheet = Measure(count, tileWidth, tileHeight, columns);
            _pixels = new byte[Sheet.Width * Sheet.Height * 4];
            _filled = new bool[count];
            _title = title ?? string.Empty;
            Fill(_pixels, Sheet.Width, 0, 0, Sheet.Width, Sheet.Height, Page);
            if (_title.Length == 0) return;
            var scale = ScaleFor(_title, Sheet.Width - 2 * Padding);
            DrawText(_pixels, Sheet.Width, Padding, Padding + (TitleHeight - GlyphHeight * scale) / 2,
                _title, scale, Heading);
        }

        /// <summary>The grid this canvas draws, and the pixel size <see cref="Finish"/> returns.</summary>
        internal Layout Sheet { get; }

        /// <summary>How many cells the sheet has.</summary>
        internal int Count => _filled.Length;

        /// <summary>
        /// Draws one picture into the cell at <paramref name="index"/>, downscaled to the cell, and writes
        /// <paramref name="caption"/> under it. <paramref name="premultiplied"/> names the buffer being handed
        /// over, not the one that will be written: the sheet is always premultiplied.
        /// </summary>
        internal void Blit(int index, string caption, byte[] pixels, int width, int height, bool premultiplied)
        {
            if (index < 0 || index >= _filled.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The sheet '{_title}' has {_filled.Length} cells.");
            if (_filled[index])
                throw new InvalidDataException($"Cell {index} of the sheet '{_title}' was drawn twice.");
            if (pixels.Length != width * height * 4)
                throw new InvalidDataException(
                    $"A tile of the sheet '{_title}' holds {pixels.Length} bytes; it should be {width}×{height} BGRA.");
            if (width < Sheet.TileWidth || height < Sheet.TileHeight)
                throw new InvalidDataException(
                    $"A tile of the sheet '{_title}' is {width}×{height}, smaller than the {Sheet.TileWidth}×{Sheet.TileHeight} cell it fills.");

            var cellWidth = Sheet.TileWidth + 2 * Border;
            var cellHeight = Sheet.TileHeight + 2 * Border + CaptionHeight;
            var x = Padding + (index % Sheet.Columns) * (cellWidth + Gap);
            var y = Padding + TitleHeight + (index / Sheet.Columns) * (cellHeight + Gap);

            Fill(_pixels, Sheet.Width, x, y, cellWidth, Border, Frame);
            Fill(_pixels, Sheet.Width, x, y + cellHeight - Border, cellWidth, Border, Frame);
            Fill(_pixels, Sheet.Width, x, y, Border, cellHeight, Frame);
            Fill(_pixels, Sheet.Width, x + cellWidth - Border, y, Border, cellHeight, Frame);

            Downscale(x + Border, y + Border, pixels, width, height, premultiplied);

            var captionY = y + Border + Sheet.TileHeight;
            Fill(_pixels, Sheet.Width, x + Border, captionY, Sheet.TileWidth, CaptionHeight, Cell);
            if (caption.Length > 0)
            {
                var scale = ScaleFor(caption, Sheet.TileWidth - 8);
                DrawText(_pixels, Sheet.Width, x + Border + 4, captionY + (CaptionHeight - GlyphHeight * scale) / 2,
                    caption, scale, Ink);
            }
            // Only now is the cell done: an empty caption draws nothing but the picture still landed, and a cell
            // that returned early here would look to Finish exactly like one that was never drawn.
            _filled[index] = true;
        }

        /// <summary>
        /// The finished sheet, as premultiplied BGRA for <c>PixelFormats.Pbgra32</c>. A cell that was never
        /// drawn is an error rather than a hole: an empty cell reads as "the option drew nothing", which is the
        /// exact finding the review exists to report, so it must not appear by accident.
        /// </summary>
        internal byte[] Finish()
        {
            var missing = Array.IndexOf(_filled, false);
            if (missing >= 0)
                throw new InvalidDataException(
                    $"Cell {missing} of the sheet '{_title}' was never drawn; every capture on a sheet has to reach it.");
            return _pixels;
        }

        /// <summary>
        /// Averages each cell's block of source pixels and puts the result over the page. A block is never empty:
        /// the source is at least as large as the cell, so every step covers at least one pixel.
        /// </summary>
        private void Downscale(int destX, int destY, byte[] source, int width, int height, bool premultiplied)
        {
            for (var ty = 0; ty < Sheet.TileHeight; ty++)
            {
                var y0 = ty * height / Sheet.TileHeight;
                var y1 = Math.Max(y0 + 1, (ty + 1) * height / Sheet.TileHeight);
                for (var tx = 0; tx < Sheet.TileWidth; tx++)
                {
                    var x0 = tx * width / Sheet.TileWidth;
                    var x1 = Math.Max(x0 + 1, (tx + 1) * width / Sheet.TileWidth);
                    long blue = 0, green = 0, red = 0, alpha = 0;
                    var samples = 0;
                    for (var sy = y0; sy < y1; sy++)
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var at = (sy * width + sx) * 4;
                        var a = source[at + 3];
                        if (premultiplied)
                        {
                            blue += source[at];
                            green += source[at + 1];
                            red += source[at + 2];
                        }
                        else
                        {
                            // Straight alpha is multiplied by its own coverage first, so a transparent pixel
                            // contributes nothing to the colour instead of its unlit RGB.
                            blue += (source[at] * a + 127) / 255;
                            green += (source[at + 1] * a + 127) / 255;
                            red += (source[at + 2] * a + 127) / 255;
                        }
                        alpha += a;
                        samples++;
                    }
                    var half = samples / 2;
                    Over(destX + tx, destY + ty,
                        (byte)((blue + half) / samples), (byte)((green + half) / samples),
                        (byte)((red + half) / samples), (byte)((alpha + half) / samples));
                }
            }
        }

        /// <summary>Puts one premultiplied pixel over what is already on the page.</summary>
        private void Over(int x, int y, byte blue, byte green, byte red, byte alpha)
        {
            var at = (y * Sheet.Width + x) * 4;
            var keep = 255 - alpha;
            _pixels[at] = Blend(_pixels[at], blue, keep);
            _pixels[at + 1] = Blend(_pixels[at + 1], green, keep);
            _pixels[at + 2] = Blend(_pixels[at + 2], red, keep);
            _pixels[at + 3] = Blend(_pixels[at + 3], alpha, keep);
        }

        private static byte Blend(byte under, byte over, int keep) =>
            (byte)Math.Min(255, over + (under * keep + 127) / 255);
    }

    /// <summary>Fills a rectangle with an opaque colour, clipped to the page.</summary>
    private static void Fill(byte[] target, int width, int x, int y, int w, int h, (byte B, byte G, byte R) colour)
    {
        var height = target.Length / (width * 4);
        var left = Math.Max(0, x);
        var right = Math.Min(width, x + w);
        var top = Math.Max(0, y);
        var bottom = Math.Min(height, y + h);
        for (var row = top; row < bottom; row++)
        for (var column = left; column < right; column++)
        {
            var at = (row * width + column) * 4;
            target[at] = colour.B;
            target[at + 1] = colour.G;
            target[at + 2] = colour.R;
            target[at + 3] = 255;
        }
    }

    /// <summary>Draws a line of text at a scale; every pixel is either ink or left alone, so text never blends.</summary>
    private static void DrawText(byte[] target, int targetWidth, int x, int y, string text, int scale,
        (byte B, byte G, byte R) colour)
    {
        var cursor = x;
        foreach (var character in text)
        {
            var glyph = Glyph(character);
            if (glyph is not null)
                for (var row = 0; row < GlyphHeight; row++)
                for (var column = 0; column < GlyphWidth; column++)
                    if (glyph[row][column] == '#')
                        Fill(target, targetWidth, cursor + column * scale, y + row * scale, scale, scale, colour);
            cursor += GlyphAdvance * scale;
        }
    }

    /// <summary>The width a line takes at a scale; the last character is not followed by its gap.</summary>
    private static int TextWidth(string text, int scale) =>
        text.Length == 0 ? 0 : text.Length * GlyphAdvance * scale - scale;

    /// <summary>
    /// The size a caption is drawn at: two when it fits, one when it does not. A long caption shrinks to fit its
    /// own strip instead of running into the cell beside it.
    /// </summary>
    private static int ScaleFor(string text, int available) => TextWidth(text, 2) <= available ? 2 : 1;

    /// <summary>One character's rows, or the fallback, so a caption never loses a letter without saying so.</summary>
    private static string[]? Glyph(char character)
    {
        if (Font.TryGetValue(char.ToUpperInvariant(character), out var rows)) return rows;
        return Font.TryGetValue('?', out var fallback) ? fallback : null;
    }

    private static Dictionary<char, string[]> BuildFont()
    {
        var font = new Dictionary<char, string[]>(Patterns.Length);
        foreach (var (key, pattern) in Patterns)
        {
            var rows = pattern.Split('|');
            if (rows.Length != GlyphHeight || Array.Exists(rows, row => row.Length != GlyphWidth))
                throw new InvalidOperationException(
                    $"The bitmap glyph '{key}' is not {GlyphHeight} rows of {GlyphWidth} cells.");
            font.Add(key, rows);
        }
        return font;
    }
}
