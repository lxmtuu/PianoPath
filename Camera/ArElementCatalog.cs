namespace PianoPath;

/// <summary>The three quality tiers of the AR overlay (AR-S3 of docs/AR-OVERLAY-ROADMAP.md §14.8). A tier decides which elements of the catalogue may be drawn: <see cref="Lite"/> keeps the calibrated note and keypress geometry but omits AR lighting/transients, <see cref="Standard"/> adds calibrated key lighting, and <see cref="Ultra"/> adds the remaining high-fill effects.</summary>
internal enum ArQualityTier { Lite, Standard, Ultra }

/// <summary>The tier names as stored in settings and shown in the picker.</summary>
internal static class ArQualityTiers
{
    internal const string LiteName = "Lite";
    internal const string StandardName = "Standard";
    internal const string UltraName = "Ultra";
    internal const string DefaultQuality = StandardName;

    internal static readonly string[] All = new[] { LiteName, StandardName, UltraName };

    /// <summary>The stored name for a tier, so a picker caption or a settings file round-trips.</summary>
    internal static string NameOf(ArQualityTier tier) => tier switch
    {
        ArQualityTier.Lite => LiteName,
        ArQualityTier.Ultra => UltraName,
        _ => StandardName
    };

    /// <summary>The tier a stored name means; anything unknown is the default, never Lite (a typo must not take the lights away).</summary>
    internal static ArQualityTier TierOf(string? quality) => quality switch
    {
        LiteName => ArQualityTier.Lite,
        UltraName => ArQualityTier.Ultra,
        _ => ArQualityTier.Standard
    };

    /// <summary>The stored name for anything the app reads: one of <see cref="All"/>.</summary>
    internal static string Normalize(string? quality) => NameOf(TierOf(quality));
}

/// <summary>The two families of AR elements, as KeySight splits them: what is always there (the scene) and what is born with a strike (an effect).</summary>
internal enum ArElementFamily { Scene, Effect }

/// <summary>Every element the redesigned overlay knows (docs/AR-OVERLAY-ROADMAP.md §14.3). Which of them exist today is the catalogue's business; which of them a tier may draw is <see cref="ArElementCatalog"/>'s.</summary>
internal enum ArElement
{
    /// <summary>The key bodies. On the camera picture these are the real keys; the catalogue keeps them so a tier never drops the keyboard itself.</summary>
    PianoKeys,
    /// <summary>The damper strip at the back edge of the keys, lifted by the sustain level.</summary>
    Dampers,
    /// <summary>A faint line at every octave boundary.</summary>
    OctaveLines,
    /// <summary>A gradient along the edges of the key strip. Never in AR: it casts a planar shadow in KeySight's words.</summary>
    LightBars,
    /// <summary>Note counter, pedal state, note names: drawn last, never through the homography.</summary>
    Hud,
    /// <summary>The song's notes, flat quads on the keyboard's plane.</summary>
    NoteObjects,
    /// <summary>The bars of the keys the player presses, rising from each key while it is held.</summary>
    Keypresses,
    /// <summary>The glow a struck key lights up with — the key tint of the overlay today.</summary>
    ImpactLights,
    /// <summary>Light spilling from a sounding note onto the keys beside it.</summary>
    NoteLights,
    /// <summary>A ring at the point of impact.</summary>
    Pulses,
    /// <summary>Camera-projected Live Stage particles, impact/release flashes, hold arcs and keyed light beams.</summary>
    Particles
}

/// <summary>
/// The AR element catalogue: each element's family, the pass it is drawn with, and the lowest quality
/// tier that may draw it. Pure metadata — no GPU, no camera — so the tier arithmetic is tested on any
/// machine and the renderer can ask <see cref="CanDraw"/> before it builds anything.
/// </summary>
internal static class ArElementCatalog
{
    /// <summary>The elements that are never dropped, whatever the tier or the frame budget: without them the overlay is not an overlay.</summary>
    internal static readonly ArElement[] NeverDropped = new[] { ArElement.PianoKeys, ArElement.NoteObjects, ArElement.Keypresses };

    /// <summary>
    /// The order an over-budget frame drops elements in (roadmap §14.8): the expensive fill first,
    /// the key glow last before the floor. A note's style is a property of <see cref="ArElement.NoteObjects"/>,
    /// not an element: dropping style means drawing the note flat, and the note itself stays.
    /// </summary>
    internal static readonly ArElement[] DropOrder = new[]
    {
        ArElement.Particles, ArElement.Pulses, ArElement.LightBars, ArElement.Hud,
        ArElement.NoteLights, ArElement.Dampers, ArElement.OctaveLines, ArElement.ImpactLights
    };

    internal static ArElementFamily FamilyOf(ArElement element) => element switch
    {
        ArElement.PianoKeys or ArElement.Dampers or ArElement.OctaveLines or ArElement.LightBars or ArElement.Hud => ArElementFamily.Scene,
        _ => ArElementFamily.Effect
    };

    /// <summary>The shader pass the element is drawn with today, or the one planned for it in §14.3.</summary>
    internal static string PassOf(ArElement element) => element switch
    {
        ArElement.PianoKeys or ArElement.Dampers or ArElement.OctaveLines => "VsArQuad",
        ArElement.NoteObjects or ArElement.Keypresses or ArElement.ImpactLights or ArElement.Pulses => "VsArQuad (camera-projected 3D geometry)",
        ArElement.NoteLights => "VsArQuad (projected 3D key-light spill)",
        ArElement.Particles => "VsArQuad (camera-projected Live Stage particles and transient geometry)",
        ArElement.LightBars => "VsArQuad (AR-S8)",
        _ => "own pass, after everything, no homography (AR-S8)"
    };

    /// <summary>The lowest tier that may draw the element.</summary>
    internal static ArQualityTier MinTierOf(ArElement element) => element switch
    {
        ArElement.PianoKeys or ArElement.NoteObjects or ArElement.Keypresses => ArQualityTier.Lite,
        ArElement.ImpactLights or ArElement.NoteLights or ArElement.Dampers or ArElement.OctaveLines => ArQualityTier.Standard,
        _ => ArQualityTier.Ultra
    };

    /// <summary>Whether a tier may draw the element.</summary>
    internal static bool CanDraw(ArQualityTier tier, ArElement element) => tier >= MinTierOf(element);

    /// <summary>Every element a tier may draw.</summary>
    internal static IReadOnlySet<ArElement> ForTier(ArQualityTier tier)
    {
        var all = new HashSet<ArElement>();
        foreach (ArElement element in Enum.GetValues<ArElement>()) if (CanDraw(tier, element)) all.Add(element);
        return all;
    }
}
