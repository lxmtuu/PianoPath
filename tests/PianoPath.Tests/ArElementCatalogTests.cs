using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// AR-S3: the AR element catalogue (docs/AR-OVERLAY-ROADMAP.md §14.3 and §14.8) — what is always there,
/// which elements each quality tier may draw, and the order an over-budget frame drops them in.
/// </summary>
public class ArElementCatalogTests
{
    private static void SameSet(IReadOnlySet<ArElement> actual, params ArElement[] expected)
    {
        var missing = expected.Where(e => !actual.Contains(e)).ToList();
        var extra = actual.Where(e => !expected.Contains(e)).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"Expected [{string.Join(", ", expected)}]; got [{string.Join(", ", actual)}] (missing {string.Join(", ", missing)}, extra {string.Join(", ", extra)}).");
    }

    [Fact]
    public void Lite_draws_the_keys_the_notes_and_the_bars_of_the_keys_the_player_presses()
    {
        SameSet(ArElementCatalog.ForTier(ArQualityTier.Lite), ArElement.PianoKeys, ArElement.NoteObjects, ArElement.Keypresses);
    }

    [Fact]
    public void Standard_adds_the_key_and_impact_glows_but_keeps_the_calm_stuff_off()
    {
        var standard = ArElementCatalog.ForTier(ArQualityTier.Standard);
        Assert.Contains(ArElement.ImpactLights, standard);
        Assert.Contains(ArElement.NoteLights, standard);
        Assert.Contains(ArElement.Dampers, standard);
        Assert.DoesNotContain(ArElement.Particles, standard);
        Assert.DoesNotContain(ArElement.Pulses, standard);
        Assert.DoesNotContain(ArElement.LightBars, standard);
        // Standard must draw at least everything Lite draws
        foreach (var floor in ArElementCatalog.NeverDropped) Assert.True(standard.Contains(floor));
    }

    [Fact]
    public void Ultra_adds_everything_the_catalogue_knows()
    {
        var ultra = ArElementCatalog.ForTier(ArQualityTier.Ultra);
        foreach (ArElement element in Enum.GetValues<ArElement>()) Assert.Contains(element, ultra);
    }

    [Fact]
    public void The_keys_notes_and_bars_are_never_dropped_in_any_tier()
    {
        foreach (ArQualityTier tier in Enum.GetValues<ArQualityTier>())
        {
            var set = ArElementCatalog.ForTier(tier);
            foreach (var floor in ArElementCatalog.NeverDropped) Assert.True(set.Contains(floor), $"Tier {tier} must never drop {floor}.");
        }
    }

    [Fact]
    public void The_drop_order_puts_the_expensive_fills_first_and_ends_at_the_floor()
    {
        foreach (var floor in ArElementCatalog.NeverDropped) Assert.DoesNotContain(floor, ArElementCatalog.DropOrder);
        Assert.Equal(ArElement.ImpactLights, ArElementCatalog.DropOrder.Last());
        Assert.Equal(ArElement.Particles, ArElementCatalog.DropOrder[0]);
    }

    [Fact]
    public void Scene_and_effect_elements_split_along_KeySight_families()
    {
        Assert.Equal(ArElementFamily.Scene, ArElementCatalog.FamilyOf(ArElement.PianoKeys));
        Assert.Equal(ArElementFamily.Scene, ArElementCatalog.FamilyOf(ArElement.Dampers));
        Assert.Equal(ArElementFamily.Effect, ArElementCatalog.FamilyOf(ArElement.NoteObjects));
        Assert.Equal(ArElementFamily.Effect, ArElementCatalog.FamilyOf(ArElement.Particles));
    }

    [Fact]
    public void Every_element_has_a_pass_name_and_a_tier()
    {
        foreach (ArElement element in Enum.GetValues<ArElement>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ArElementCatalog.PassOf(element)), $"Every element must name the pass it is drawn in ({element}).");
            Assert.True(ArElementCatalog.MinTierOf(element) <= ArQualityTier.Ultra);
            Assert.True(ArElementCatalog.CanDraw(ArQualityTier.Ultra, element));
        }
    }
}
