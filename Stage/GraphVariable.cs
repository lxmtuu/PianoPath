namespace PianoPath;

/// <summary>
/// A parameter of a visual element, either a fixed value or a curve over an input that always runs 0..1 — the
/// "graph" of KeySight (AR-S2 of docs/AR-OVERLAY-ROADMAP.md §14). The input is a strike's velocity mapped through
/// the simulation's floor/ceiling band, an effect's progress, a pitch on the keyboard, notes per second, activity,
/// or the sustain level; nothing else. What the value is (a colour share, a note's width, a glow's radius) is the
/// caller's business.
/// </summary>
/// <param name="Graph">False: the value is always <see cref="Max"/>. True: <see cref="Sample"/> walks <see cref="Min"/> to <see cref="Max"/> between <see cref="Start"/> and <see cref="Stop"/>, bending with <see cref="Curve"/>.</param>
/// <param name="Start">Where the walk starts, on the input's 0..1 scale.</param>
/// <param name="Stop">Where the walk ends, on the input's 0..1 scale.</param>
/// <param name="Min">The value at <see cref="Start"/>.</param>
/// <param name="Max">The value at <see cref="Stop"/>, and always the value when <see cref="Graph"/> is false.</param>
/// <param name="Curve">−1 curves the walk so it starts slow and ends fast, +1 the other way round, 0 is straight.</param>
internal readonly record struct GraphVariable(bool Graph = false, double Start = 0, double Stop = 1, double Min = 0, double Max = 1, double Curve = 0)
{
    internal const double CurveMin = -1, CurveMax = 1;

    /// <summary>The fixed value: Max, so a flat parameter does not depend on the other fields.</summary>
    internal static readonly GraphVariable Flat = new();

    /// <summary>The value for an input in 0..1. Flat returns <see cref="Max"/>; a graph clamps outside its window and walks between its ends with the curve.</summary>
    internal double Sample(double input)
    {
        if (!Graph) return Finite(Max, 0);
        if (!double.IsFinite(input)) input = 0;
        var start = Finite(Start, 0); var stop = Finite(Stop, 1);
        if (stop <= start) return Finite(Max, 0); // an empty window is a flat Max, never a divide by zero
        var t = Math.Clamp((input - start) / (stop - start), 0, 1);
        if (t is 0 or 1) return Finite(t == 0 ? Min : Max, 0);
        var curve = Math.Clamp(Finite(Curve, 0), CurveMin, CurveMax);
        // Power curve: -1 starts slow and ends fast, +1 the other way round; 0 is the straight line.
        var bent = curve == 0 ? t : Math.Pow(t, 1 - curve);
        return Finite(Min, 0) + (Finite(Max, 0) - Finite(Min, 0)) * bent;
    }

    /// <summary>The same parameter pulled into sane ranges; a parameter whose numbers make no sense reads as flat Max.</summary>
    internal GraphVariable Normalised() =>
        double.IsFinite(Min) && double.IsFinite(Max) && double.IsFinite(Start) && double.IsFinite(Stop) && double.IsFinite(Curve)
            ? this with { Curve = Math.Clamp(Curve, CurveMin, CurveMax) }
            : Flat with { Max = 1 };

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}
