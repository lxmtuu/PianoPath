namespace PianoPath;

/// <summary>The temporary, renderer-only quality steps used while a real GPU remains over its frame budget.</summary>
internal enum GpuAdaptiveQualityTier
{
    Full,
    BloomReduced,
    BloomMinimal,
    AmbientReduced,
    AmbientMinimal
}

/// <summary>
/// The adaptive renderer may shorten its bloom pyramid and thin peripheral ambience. It never changes
/// note dimensions, pitch/timing, key geometry, persistent settings or preset data.
/// </summary>
internal readonly record struct GpuAdaptiveQualityProfile(int MaxBloomLevels, float AmbientDensity)
{
    internal const int FullBloomLevelCount = 6;

    internal static GpuAdaptiveQualityProfile For(GpuAdaptiveQualityTier tier) => tier switch
    {
        GpuAdaptiveQualityTier.BloomReduced => new(4, 1f),
        GpuAdaptiveQualityTier.BloomMinimal => new(2, 1f),
        GpuAdaptiveQualityTier.AmbientReduced => new(2, .6f),
        GpuAdaptiveQualityTier.AmbientMinimal => new(2, .3f),
        _ => new(FullBloomLevelCount, 1f)
    };
}

/// <summary>
/// A small hysteresis controller driven only by completed, non-blocking D3D11 GPU timestamp samples.
/// Samples are grouped into non-overlapping 60-sample windows (one second at 60 FPS), so a brief spike cannot
/// step quality down. Three hot windows reduce one tier; eight cool windows restore one tier.
/// </summary>
internal sealed class GpuAdaptiveQualityController
{
    internal const int SamplesPerEvaluation = 60;
    internal const int HotWindowsToReduce = 3;
    internal const int CoolWindowsToRestore = 8;
    internal const double OverBudgetRatio = .90;
    internal const double RecoveryRatio = .75;
    internal const double UnlimitedFrameBudgetMs = 8.0;

    private readonly double[] _samples = new double[SamplesPerEvaluation];
    private readonly double[] _ordered = new double[SamplesPerEvaluation];
    private int _sampleCount;
    private int _hotWindows;
    private int _coolWindows;
    private int _tier;
    private int _sampleGeneration;
    private double _frameBudgetMs = UnlimitedFrameBudgetMs;

    internal GpuAdaptiveQualityTier Tier => (GpuAdaptiveQualityTier)Volatile.Read(ref _tier);
    internal GpuAdaptiveQualityProfile Profile => GpuAdaptiveQualityProfile.For(Tier);
    internal int SampleGeneration => Volatile.Read(ref _sampleGeneration);
    internal double FrameBudgetMs => _frameBudgetMs;
    internal double LastP95Milliseconds { get; private set; }

    /// <summary>The current target, derived from the configured GPU frame rate; Unlimited uses the 8 ms product budget.</summary>
    internal static double BudgetForTargetFps(int targetFps) =>
        targetFps > 0 ? 1000.0 / Math.Clamp(targetFps, 1, 1000) : UnlimitedFrameBudgetMs;

    /// <summary>
    /// Uses the slower of the configured cap and the current display refresh read from a VSync swap-chain window.
    /// </summary>
    internal static double BudgetForOutput(int targetFps, bool vsync, bool hasSwapChain, double displayRefreshHz)
    {
        var budget = BudgetForTargetFps(targetFps);
        if (!vsync || !hasSwapChain || !double.IsFinite(displayRefreshHz) || displayRefreshHz <= 0) return budget;
        return Math.Max(budget, 1000.0 / displayRefreshHz);
    }

    /// <summary>Updates the target and discards a partial window so samples from two budgets are never mixed.</summary>
    internal void SetFrameBudgetMs(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) milliseconds = UnlimitedFrameBudgetMs;
        if (Math.Abs(milliseconds - _frameBudgetMs) < .01) return;
        _frameBudgetMs = milliseconds;
        ClearWindow();
        Interlocked.Increment(ref _sampleGeneration);
        _hotWindows = _coolWindows = 0;
    }

    /// <summary>Feeds one valid GPU timestamp. Invalid or unavailable samples are ignored rather than treated as fast.</summary>
    internal void Observe(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) return;
        _samples[_sampleCount++] = milliseconds;
        if (_sampleCount < SamplesPerEvaluation) return;

        Array.Copy(_samples, _ordered, SamplesPerEvaluation);
        Array.Sort(_ordered);
        var rank = (int)Math.Ceiling(SamplesPerEvaluation * .95);
        LastP95Milliseconds = _ordered[Math.Clamp(rank - 1, 0, SamplesPerEvaluation - 1)];
        ClearWindow();
        EvaluateWindow(LastP95Milliseconds);
    }

    /// <summary>Returns to full quality if runtime timestamp collection becomes unavailable.</summary>
    internal void ResetToFull()
    {
        ClearWindow();
        Interlocked.Increment(ref _sampleGeneration);
        _hotWindows = _coolWindows = 0;
        LastP95Milliseconds = 0;
        ChangeTier(0);
    }

    private void EvaluateWindow(double p95)
    {
        if (p95 > _frameBudgetMs * OverBudgetRatio)
        {
            _hotWindows++;
            _coolWindows = 0;
            if (_hotWindows >= HotWindowsToReduce && _tier < (int)GpuAdaptiveQualityTier.AmbientMinimal)
            {
                ChangeTier(_tier + 1);
                _hotWindows = _coolWindows = 0;
            }
        }
        else if (p95 < _frameBudgetMs * RecoveryRatio)
        {
            _coolWindows++;
            _hotWindows = 0;
            if (_coolWindows >= CoolWindowsToRestore && _tier > (int)GpuAdaptiveQualityTier.Full)
            {
                ChangeTier(_tier - 1);
                _hotWindows = _coolWindows = 0;
            }
        }
        else
        {
            _hotWindows = _coolWindows = 0;
        }
    }

    private void ChangeTier(int next)
    {
        next = Math.Clamp(next, (int)GpuAdaptiveQualityTier.Full, (int)GpuAdaptiveQualityTier.AmbientMinimal);
        if (next == _tier) return;
        Volatile.Write(ref _tier, next);
        Interlocked.Increment(ref _sampleGeneration);
    }

    private void ClearWindow()
    {
        _sampleCount = 0;
        Array.Clear(_samples);
        Array.Clear(_ordered);
    }
}
