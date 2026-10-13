namespace PianoPath;

/// <summary>One removable chunk of the AR look, in the exact sentence order roadmap §14.8 names it.</summary>
internal enum ArBudgetCut
{
    /// <summary>Particles (the sparks/wisps/flame family and the transient flashes they travel with) go first.</summary>
    Particles = 0,
    /// <summary>Traveling pulses: the shockwave/ripple rings the stage lifts over the keys.</summary>
    Pulses = 1,
    /// <summary>Projected note lights: the key-light spill that pools around a pressed key.</summary>
    NoteLights = 2,
    /// <summary>Light bars: the standing glow columns over lit keys (beam quads).</summary>
    LightBars = 3,
    /// <summary>
    /// Note style: the calibrated 3D note bodies collapse to the plain flat path. The notes themselves stay — that
    /// is why "style" is a cut of its own and never a family of its own.
    /// </summary>
    NoteStyle = 4,
    /// <summary>Impact glow last: a frame this sparse keeps only the strike flash before the floor.</summary>
    ImpactLights = 5,
}

/// <summary>
/// The deterministic AR drop rule of roadmap §14.8, plugged into the adaptive-quality machinery that already watches
/// GPU frame time (<see cref="GpuAdaptiveQualityController"/>): while the renderer runs over its budget (Full →
/// BloomReduced → BloomMinimal → AmbientReduced → AmbientMinimal), the AR overlay drops one more front of the cut
/// table per step, in the roadmap's sentence order — particles → pulses → note lights → light bars → note style →
/// impact glow — and restores them again as the pressure passes, exactly like bloom and ambience return.
///
/// <para>
/// Everything here is pure and reversible: no setting changes, no hysteresis state, no frame-time sampling of its
/// own (the controller keeps all of that); the table just answers "how much may this frame draw?". The three things
/// that are the overlay — keys, notes, keypresses (<see cref="ArElementCatalog.NeverDropped"/>) — are not in the
/// table, so no adaptive state can ever drop them.
/// </para>
/// </summary>
internal static class ArQualityBudget
{
    /// <summary>Six cuts in the exact §14.8 sentence order; the head of this array is what under-budget frames lose.</summary>
    internal static readonly ArBudgetCut[] CutOrder =
        [ArBudgetCut.Particles, ArBudgetCut.Pulses, ArBudgetCut.NoteLights, ArBudgetCut.LightBars, ArBudgetCut.NoteStyle, ArBudgetCut.ImpactLights];

    /// <summary>
    /// How far down the table an adaptive tier has fallen. Bloom steps cost the first cuts; ambience steps the
    /// rest, so at the last tier only keys, notes and keypresses are left — the floor the roadmap forbids touching.
    /// </summary>
    internal static int CutCountFor(GpuAdaptiveQualityTier tier) => tier switch
    {
        GpuAdaptiveQualityTier.BloomReduced => 1,
        GpuAdaptiveQualityTier.BloomMinimal => 2,
        GpuAdaptiveQualityTier.AmbientReduced => 3,
        GpuAdaptiveQualityTier.AmbientMinimal => CutOrder.Length,
        _ => 0,
    };

    /// <summary>Whether this element-chunk is already gone at the given adaptive tier.</summary>
    internal static bool IsCut(GpuAdaptiveQualityTier tier, ArBudgetCut cut) => (int)cut < CutCountFor(tier);

    /// <summary>
    /// <see cref="ArElementCatalog.CanDraw(ArQualityTier, ArElement)">ArElementCatalog.CanDraw</see> through the
    /// budget lens: a frame may draw an element only when the chosen tier allows it AND the current adaptive step
    /// has not cut it yet. Fully deterministic for tests; the real-time part lives in the controller.
    /// </summary>
    internal static bool CanDraw(ArQualityTier tier, GpuAdaptiveQualityTier adaptiveTier, ArElement element) =>
        ArElementCatalog.CanDraw(tier, element) && (element switch
        {
            ArElement.Particles => !IsCut(adaptiveTier, ArBudgetCut.Particles),
            ArElement.Pulses => !IsCut(adaptiveTier, ArBudgetCut.Pulses),
            ArElement.ImpactLights => !IsCut(adaptiveTier, ArBudgetCut.ImpactLights),
            _ => true,
        });
}
