using System.Windows;

namespace PianoPath;

/// <summary>
/// The geometry behind the two pictures on the History page: the fourteen-day bar chart and the ghost of a
/// take. It is arithmetic only — the page turns the numbers into <c>Rectangle</c>s and <c>Ellipse</c>s — so the
/// checks can state where a bar ends and where a graded note lands instead of measuring the chrome.
/// </summary>
internal static class PracticeChart
{
    /// <summary>
    /// How tall a day's bar is: linear in that day's accuracy, with a floor so a day that was practised at all
    /// is visible even when almost every note was missed. A day with no run has no bar.
    /// </summary>
    internal static double BarHeight(double accuracy, double height, double floor) =>
        accuracy <= 0 ? 0 : Math.Max(floor, height * Math.Clamp(accuracy, 0, 100) / 100);

    /// <summary>
    /// The pitch window the ghost is drawn in: the graded notes' own range, padded by a semitone at each end so
    /// dots never sit on the frame, and grown to at least an octave so a take with few notes still has a shape.
    /// </summary>
    internal static (int Low, int High) PitchWindow(IReadOnlyList<PracticePoint> points)
    {
        if (points.Count == 0) return (48, 72);
        var low = points.Min(point => point.Pitch);
        var high = points.Max(point => point.Pitch);
        if (high - low < 12) { var middle = (low + high) / 2; low = middle - 6; high = middle + 6; }
        return (Math.Max(0, low - 1), Math.Min(127, high + 1));
    }

    /// <summary>
    /// Where one graded note is drawn: across by its position in the song and up by its pitch, both relative to
    /// the axes the rows share. A song with no length (a live take) puts everything at the left edge, which is
    /// exactly where a live take happened.
    /// </summary>
    internal static Point Point(double at, int pitch, double songLength, int lowPitch, int highPitch, Rect area)
    {
        var x = area.X + (songLength > .001 ? Math.Clamp(at / songLength, 0, 1) : 0) * Math.Max(0, area.Width - 2 * DotRadius) + DotRadius;
        var span = Math.Max(1, highPitch - lowPitch);
        var y = area.Bottom - DotRadius - Math.Clamp((pitch - lowPitch) / (double)span, 0, 1) * Math.Max(0, area.Height - 2 * DotRadius);
        return new Point(x, y);
    }

    /// <summary>Radius of one graded note in the ghost; small enough that a phrase still reads as a line.</summary>
    internal const double DotRadius = 1.6;

    /// <summary>The line under the ghost rows: hits, misses and the accuracy the pair works out to.</summary>
    internal static string RowCaption(int hits, int misses) =>
        Loc.F("{0} hit · {1} missed · {2:0.#}%", hits, misses, hits + misses == 0 ? 0 : 100.0 * hits / (hits + misses));
}
