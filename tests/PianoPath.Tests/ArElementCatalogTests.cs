using System.Collections.Generic;
using Xunit;

namespace PianoPath.Tests;

public class ArElementCatalogTests
{
    [Fact]
    public void Lite_draws_the_keys_the_notes_and_the_bars_of_the_keys_the_player_presses()
    {
        var lite = ArElementCatalog.ForTier(ArQualityTier.Lite);
        Assert.True(lite.Contains(ArElement.PianoKeys));
        Assert.True(lite.Contains(ArElement.NoteObjects));
        Assert.True(lite.Contains(ArElement.Keypresses));
        var count = 0; foreach (var _ in lite) count++;
        Assert.Equal(3, count);
    }

    [Fact]
    public void Standard_adds_the_key_and_impact_glows_and_keeps_the_calm_stuff_off()
    {
        var standard = ArElementCatalog.ForTier(ArQualityTier.Standard);
        Assert.True(standard.Contains(ArElement.ImpactLights));
        Assert.True(standard.Contains(ArElement.NoteLights));
        Assert.True(standard.Contains(ArElement.Dampers));
        Assert.False(standard.Contains(ArElement.Particles));
        Assert.False(standard.Contains(ArElement.Pulses));
        Assert.False(standard.Contains(ArElement.LightBars));
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
            foreach (var floor in ArElementCatalog.NeverDropped) Assert.True(set.Contains(floor));
        }
    }

    [Fact]
    public void The_drop_order_puts_the_expensive_fills_first_and_ends_at_the_floor()
    {
        var drop = ArElementCatalog.DropOrder;
        foreach (var floor in ArElementCatalog.NeverDropped)
            foreach (var d in drop) Assert.True(floor != d);
        Assert.Equal(ArElement.Particles, drop[0]);
        Assert.Equal(ArElement.ImpactLights, drop[drop.Length - 1]);
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
            Assert.False(string.IsNullOrWhiteSpace(ArElementCatalog.PassOf(element)));
            Assert.True(ArElementCatalog.MinTierOf(element) <= ArQualityTier.Ultra);
            Assert.True(ArElementCatalog.CanDraw(ArQualityTier.Ultra, element));
        }
    }
}
