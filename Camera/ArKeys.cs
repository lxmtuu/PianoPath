namespace PianoPath;

/// <summary>
/// The part of a key that can be seen from above, as rectangles of the keyboard plane (<c>u</c> across the keys, <c>v</c>
/// from the back of the keys to their front): a black key is one rectangle over the back of the key area; a white key is
/// the whole slot at the front plus a stem between its black neighbours at the back.
/// </summary>
/// <remarks>
/// <para>Lighting these rectangles — and nothing else — lights the key you can see: a white key's glow does not creep
/// under the black keys beside it, and a black key's own rectangle is lit when it is pressed. Together the rectangles of
/// a keyboard tile its key area exactly once, which the tests hold to.</para>
/// <para>Drawing is done in the picture, so a white key's stem and front are two quads that meet along an edge.</para>
/// </remarks>
internal static class ArKeys
{
    /// <summary>A rectangle of the keyboard plane.</summary>
    internal readonly record struct Rect(double U0, double U1, double V0, double V1)
    {
        internal double Area => (U1 - U0) * (V1 - V0);
    }

    /// <summary>Appends the visible rectangles of a key; nothing for a pitch the layout does not have.</summary>
    internal static void Rects(KeyboardLayout layout, int pitch, List<Rect> into)
    {
        if (!layout.Contains(pitch)) return;
        const double black = ArCalibration.BlackLengthShare;
        if (KeyboardLayout.IsBlack(pitch))
        {
            var (left, right) = layout.Span(pitch);
            into.Add(new Rect(left, right, 0, black));
            return;
        }
        var (slotLeft, slotRight) = layout.Span(pitch);
        into.Add(new Rect(slotLeft, slotRight, black, 1));
        double stemLeft = slotLeft, stemRight = slotRight;
        if (layout.Contains(pitch - 1) && KeyboardLayout.IsBlack(pitch - 1)) stemLeft = Math.Max(stemLeft, layout.Span(pitch - 1).Right);
        if (layout.Contains(pitch + 1) && KeyboardLayout.IsBlack(pitch + 1)) stemRight = Math.Min(stemRight, layout.Span(pitch + 1).Left);
        if (stemRight > stemLeft + 1e-9) into.Add(new Rect(stemLeft, stemRight, 0, black));
    }

    /// <summary>Appends the patches of a key on the output (see <see cref="Rects"/>); a rectangle that cannot be drawn is left out.</summary>
    internal static void Patches(ArCalibration calibration, ArView view, int pitch, List<ArPatch> into)
    {
        var rects = new List<Rect>(2);
        Rects(calibration.Layout, pitch, rects);
        foreach (var rect in rects)
            if (ArProjection.TryProject(calibration, view, rect.U0, rect.U1, rect.V0, rect.V1, out var patch, out _, out _)) into.Add(patch);
    }
}
