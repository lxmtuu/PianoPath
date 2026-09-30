namespace PianoPath;

/// <summary>
/// Reads a hand out of a camera frame: where it is, how big it is, and how many fingers it is holding up.
///
/// <para>
/// Everything here is arithmetic on pixels — no model, no download, no new dependency — and it is deliberately
/// small enough to run on the interface thread at the camera's own frame rate. A frame is read on a coarse grid
/// of columns and rows; a pixel counts as skin by the usual video rule in YCbCr (a brightness floor and a window
/// around the chroma a human skin tone lands in), the biggest group of touching skin cells is taken as the hand,
/// and the height of each column of that group is what the fingers are counted from: a run of tall columns is a
/// finger, and a real finger has a valley between it and the next.
/// </para>
///
/// <para>
/// Pure by design, so the checks can call it with a frame they painted themselves and read the answer instead of
/// watching a picture: the skin rule on single colours, the centroid and the box on a painted hand, the finger
/// count on hand-made column heights, and the key a position lands on.
/// </para>
/// </summary>
internal static class HandTracker
{
    /// <summary>Columns and rows a frame is read on: enough for a hand and its fingers, cheap enough per frame.</summary>
    internal const int Columns = 32, Rows = 24;

    /// <summary>A cell counts as skin when this much of what was read in it is skin, so a few stray pixels do not.</summary>
    private const double CellSkinShare = 0.4;

    /// <summary>Below this share of the frame a group is too small to be a hand, or is a face far away.</summary>
    internal const double MinimumCoverage = 0.03;

    /// <summary>The widest and narrowest chroma windows the sensitivity slider asks for, around the rule's own.</summary>
    private const int ChromaSpread = 14;

    /// <summary>
    /// What the tracker saw in one frame. The centre, the width and the height are shares of the frame (0 to 1),
    /// so a caller can place something over the picture without knowing its size; <see cref="Fingers"/> is 0 when
    /// the hand is too small or too flat for the count to mean anything.
    /// </summary>
    internal readonly record struct Reading(bool Found, double CenterX, double CenterY, double Width, double Height, int Fingers, double Coverage)
    {
        /// <summary>What the tracker says when it found nothing worth following; the stage draws nothing for it.</summary>
        internal static readonly Reading Nothing = new(false, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Reads the biggest hand-shaped group of skin out of one frame of packed 32-bit pixels (blue, green, red,
    /// then an unused byte, top row first — the shape the camera reader always hands over).
    /// </summary>
    /// <param name="sensitivity">0 to 100, as the slider shows it: higher accepts more colours as skin.</param>
    internal static Reading Track(byte[] bgra, int width, int height, double sensitivity)
    {
        if (width < 8 || height < 8 || bgra.Length < width * height * 4) return Reading.Nothing;
        var spread = (int)Math.Round(Math.Clamp(sensitivity, 0, 100) / 100 * (ChromaSpread * 2) - ChromaSpread);
        // The frame is read on a grid: a step is chosen so about 240 lines are looked at, whatever the camera's
        // resolution, which keeps the cost the same for a 320p webcam and a 1080p one.
        var step = Math.Max(1, Math.Min(width, height) / 240);
        var counts = new int[Columns * Rows];
        for (var y = 0; y < height; y += step)
        {
            var row = Math.Min(Rows - 1, y * Rows / height);
            for (var x = 0; x < width; x += step)
            {
                var pixel = (y * width + x) * 4;
                if (!IsSkin(bgra[pixel], bgra[pixel + 1], bgra[pixel + 2], spread)) continue;
                counts[row * Columns + Math.Min(Columns - 1, x * Columns / width)]++;
            }
        }
        // A cell is part of the hand when a good share of what was read in it is skin.
        var samplesPerCell = Math.Max(1, (width / step / Columns) * (height / step / Rows));
        var wanted = Math.Max(2, (int)(samplesPerCell * CellSkinShare));
        var on = new bool[counts.Length];
        for (var cell = 0; cell < counts.Length; cell++) on[cell] = counts[cell] >= wanted;
        var blob = LargestGroup(on);
        if (blob.Count == 0) return Reading.Nothing;
        var coverage = blob.Sum(cell => counts[cell]) / (double)(width * height / (step * step));
        if (coverage < MinimumCoverage) return Reading.Nothing;
        // The centre and the box come from the cells, weighted by how much skin each of them really held.
        double weight = 0, x = 0, y = 0;
        int left = Columns, right = -1, top = Rows, bottom = -1;
        foreach (var cell in blob)
        {
            var column = cell % Columns; var row = cell / Columns; var mass = counts[cell];
            weight += mass; x += (column + .5) * mass; y += (row + .5) * mass;
            left = Math.Min(left, column); right = Math.Max(right, column);
            top = Math.Min(top, row); bottom = Math.Max(bottom, row);
        }
        var fingers = CountFingers(ColumnDepths(blob, top, bottom));
        return new Reading(true,
            Math.Clamp(x / weight / Columns, 0, 1), Math.Clamp(y / weight / Rows, 0, 1),
            Math.Clamp((right - left + 1) / (double)Columns, 0, 1), Math.Clamp((bottom - top + 1) / (double)Rows, 0, 1),
            fingers, coverage);
    }

    /// <summary>
    /// The video rule for skin in YCbCr, with a window the sensitivity widens or narrows: a floor on brightness,
    /// then the chroma window a human skin tone sits in under any light the camera copes with at all.
    /// </summary>
    /// <param name="spread">Cells of extra room on each side of the chroma window; negative narrows it.</param>
    internal static bool IsSkin(byte blue, byte green, byte red, int spread)
    {
        var y = (77 * red + 150 * green + 29 * blue) >> 8;
        if (y < 40) return false;
        var cb = ((-43 * red - 85 * green + 128 * blue) >> 8) + 128;
        var cr = ((128 * red - 107 * green - 21 * blue) >> 8) + 128;
        return cb >= 77 - spread && cb <= 127 + spread && cr >= 133 - spread && cr <= 173 + spread;
    }

    /// <summary>
    /// The biggest group of touching cells, four ways (no diagonals), by walking the grid once. Empty when
    /// nothing is on, and the grid is small enough that the walk is nothing next to reading the frame.
    /// </summary>
    private static List<int> LargestGroup(bool[] on)
    {
        var best = new List<int>();
        var seen = new bool[on.Length];
        var queue = new Queue<int>();
        for (var start = 0; start < on.Length; start++)
        {
            if (!on[start] || seen[start]) continue;
            var group = new List<int>();
            queue.Enqueue(start); seen[start] = true;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue(); group.Add(cell);
                var column = cell % Columns; var row = cell / Columns;
                if (column > 0 && on[cell - 1] && !seen[cell - 1]) { seen[cell - 1] = true; queue.Enqueue(cell - 1); }
                if (column < Columns - 1 && on[cell + 1] && !seen[cell + 1]) { seen[cell + 1] = true; queue.Enqueue(cell + 1); }
                if (row > 0 && on[cell - Columns] && !seen[cell - Columns]) { seen[cell - Columns] = true; queue.Enqueue(cell - Columns); }
                if (row < Rows - 1 && on[cell + Columns] && !seen[cell + Columns]) { seen[cell + Columns] = true; queue.Enqueue(cell + Columns); }
            }
            if (group.Count > best.Count) best = group;
        }
        return best;
    }

    /// <summary>
    /// How tall the group stands in each of its columns, counted from the lowest cell it reaches: that profile is
    /// what the fingers are read from, and a fist is a profile of nearly equal heights.
    /// </summary>
    private static int[] ColumnDepths(List<int> blob, int top, int bottom)
    {
        var depths = new int[Columns];
        foreach (var cell in blob) depths[cell % Columns] = Math.Max(depths[cell % Columns], bottom - cell / Columns + 1);
        // Columns outside the group are valleys, not gaps: the count needs them to see where a hand begins.
        var first = Array.FindIndex(depths, depth => depth > 0);
        var last = Array.FindLastIndex(depths, depth => depth > 0);
        if (first < 0) return depths;
        var profile = new int[last - first + 1];
        Array.Copy(depths, first, profile, 0, profile.Length);
        return profile;
    }

    /// <summary>
    /// Counts fingers in a column profile: the profile is how far the hand reaches up in each column it covers,
    /// so a finger is a column that stands clearly above the palm and a valley between two of them is what makes
    /// them two. The two lines are drawn between the shortest and the tallest column — the palm line and the
    /// fingertips — which is what keeps a warmly lit hand and a silhouetted one measuring the same. A profile
    /// with no rise at all is a closed hand or a flat blob, and nothing is counted from one too small for the
    /// shapes to mean anything, so a hand held far away reads as a hand with no finger count rather than a
    /// wrong one.
    /// </summary>
    internal static int CountFingers(int[] depths)
    {
        var tallest = 0;
        var heights = new Dictionary<int, int>();
        foreach (var depth in depths)
        {
            if (depth <= 0) continue;
            tallest = Math.Max(tallest, depth);
            heights[depth] = heights.GetValueOrDefault(depth) + 1;
        }
        if (tallest < 4) return 0;
        // The palm line is the height the hand holds in most of its columns: a hand covers far more columns with
        // its palm than with any single finger, so the commonest height is where the fingers start from.
        var palm = 0; var best = 0;
        foreach (var (height, count) in heights)
        {
            if (count > best || (count == best && height < palm)) { palm = height; best = count; }
        }
        var rise = tallest - palm;
        if (rise < 2) return 0;
        // A finger is a stretch of the hand reaching above the line halfway between the palm and the fingertips,
        // and a stretch at or below that line is the gap between two fingers. One line rather than two means a dip
        // that does not reach halfway down is part of the same finger, which is what a knuckle looks like from a
        // webcam, while a gap that really opens the hand in two is counted as two.
        var line = palm + rise * 0.5;
        var fingers = 0; var inside = false;
        foreach (var depth in depths)
        {
            if (depth > line)
            {
                if (!inside) { fingers++; inside = true; }
            }
            else inside = false;
        }
        return fingers;
    }

    /// <summary>
    /// The key a tracked position is over: the share across the frame turned into one of the keyboard's pitches,
    /// clamped to the keys the stage really draws so a hand at the edge of the picture never points off the end.
    /// </summary>
    internal static int KeyPitch(double centerX, int firstPitch, int keyCount) =>
        firstPitch + (int)Math.Round(Math.Clamp(centerX, 0, 1) * (keyCount - 1));
}
