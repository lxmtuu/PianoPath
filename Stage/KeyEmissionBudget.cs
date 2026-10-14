namespace PianoPath;

/// <summary>Fractional birth credit for one key/emitter. Whole births are consumed even when the
/// particle pool is full: capacity exhaustion drops work, it never stores a future catch-up burst.
/// Disabled emitters start a new epoch; dt=0 freezes valid credit. Does not integrate particles.</summary>
internal static class KeyEmissionBudget
{
    internal static int Take(ref double credit, double ratePerSecond, double seconds, int available, bool enabled = true)
    {
        if (!enabled || !double.IsFinite(ratePerSecond) || ratePerSecond <= 0)
        {
            credit = 0;
            return 0;
        }
        credit = double.IsFinite(credit) ? Math.Clamp(credit, 0, Math.BitDecrement(1d)) : 0;
        if (!double.IsFinite(seconds) || seconds <= 0) return 0;
        var total = credit + ratePerSecond * seconds;
        if (!double.IsFinite(total)) { credit = 0; return 0; }
        // A tiny arithmetic tolerance makes exact integer boundaries reproducible with double dt.
        // This is not a frame-rate/motion determinism claim: integration and random draw order are separate.
        var whole = Math.Floor(total + 1e-9);
        credit = Math.Clamp(total - whole, 0, Math.BitDecrement(1d));
        return (int)Math.Min(Math.Max(available, 0), whole);
    }
}
