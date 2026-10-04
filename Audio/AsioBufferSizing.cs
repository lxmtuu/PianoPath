namespace PianoPath;

/// <summary>
/// Chooses an ASIO callback size from the driver's advertised range instead of blindly taking its usually
/// conservative preferred size. ASIO reports granularity -1 for sizes that double from minSize, a positive
/// granularity for minSize + n*granularity, and zero for a fixed-size driver.
/// </summary>
internal static class AsioBufferSizing
{
    internal static int SelectFrames(int minimum, int maximum, int preferred, int granularity, int targetFrames)
    {
        if (minimum <= 0) minimum = preferred > 0 ? preferred : 32;
        if (maximum < minimum) maximum = minimum;
        targetFrames = Math.Clamp(targetFrames, minimum, maximum);

        if (granularity == -1)
        {
            var candidate = minimum;
            while (candidate <= maximum / 2 && candidate <= targetFrames / 2)
                candidate *= 2;
            return candidate;
        }

        if (granularity > 0)
        {
            var availableSteps = (maximum - minimum) / granularity;
            var targetSteps = Math.Max(0, (targetFrames - minimum) / granularity);
            return minimum + Math.Min(availableSteps, targetSteps) * granularity;
        }

        // By contract a zero granularity means a fixed size (min == max). Some legacy drivers return an
        // inconsistent range; in that case honor their preferred size rather than asking for an unknown one.
        if (minimum == maximum) return minimum;
        return preferred >= minimum && preferred <= maximum ? preferred : minimum;
    }
}
