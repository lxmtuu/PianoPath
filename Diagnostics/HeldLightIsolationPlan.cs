namespace PianoPath;

/// <summary>Three independent render factors; low wisps are a fixture override, not a shipping recipe.</summary>
internal static class HeldLightIsolationPlan
{
    internal sealed record Variant(string Id, bool Particles, bool Tint, bool Spill);
    internal sealed record Entry(string Id, HeldParticleControlPlan.Scene Scene, string Load, int WispAmount);
    internal static IReadOnlyList<Variant> Variants { get; } = Array.AsReadOnly(new[]
    {
        new Variant("off-neither",false,false,false), new Variant("off-tint",false,true,false),
        new Variant("off-spill",false,false,true), new Variant("off-both",false,true,true),
        new Variant("on-neither",true,false,false), new Variant("on-tint",true,true,false),
        new Variant("on-spill",true,false,true), new Variant("on-both",true,true,true)
    });
    internal static IEnumerable<Entry> Entries =>
        from family in new[] { "wisps", "flames" }
        from load in family == "wisps" ? new[] { "normal", "low" } : new[] { "normal" }
        from palette in HeldParticleControlPlan.Palettes.Where(p => p.Id is "prism" or "indigo")
        from view in HeldParticleControlPlan.Views
        let scene = new HeldParticleControlPlan.Scene(family,palette,HeldParticleControlPlan.Patterns.Single(p => p.Id=="chord"),view)
        select new Entry($"{family}-{load}-{palette.Id}-{view.Id}",scene,load,load=="low" ? 8 : 32);

    internal readonly record struct Sample(int Pitch, float Age, float Life);
    internal sealed record Row(int Pitch, int Count, int Young, int Middle, int Older,
        double? MinAge, double? MaxAge, double? MeanAge, double? MeanProgress);

    /// <summary>Absolute-age bins [0,.2), [.2,.4), [.4,infinity). Zero-count pitches remain explicit.
    /// Coverage counts packed instances, not on-screen area, occlusion or perceptual visibility.</summary>
    internal static IReadOnlyList<Row> Summarize(IReadOnlyList<int> pitches, IReadOnlyList<Sample> samples)
    {
        if (pitches.Distinct().Count()!=pitches.Count) throw new ArgumentException("Duplicate pitch domain.");
        foreach (var sample in samples)
            if (!pitches.Contains(sample.Pitch) || !float.IsFinite(sample.Age) || !float.IsFinite(sample.Life)
                || sample.Age<0 || sample.Life<=0 || sample.Age>=sample.Life)
                throw new ArgumentException("Invalid live-particle coverage sample.");
        return pitches.Select(p =>
        {
            // Canonical order also makes floating-point mean progress reproducible across the
            // Stage's forward and AR's reverse packing traversal (even below the cap).
            var group=samples.Where(s => s.Pitch==p).OrderBy(s=>s.Age).ThenBy(s=>s.Life).ToArray();
            return new Row(p,group.Length,group.Count(s=>s.Age<.2f),group.Count(s=>s.Age>=.2f && s.Age<.4f),
                group.Count(s=>s.Age>=.4f),group.Length==0 ? null : group.Min(s=>(double)s.Age),
                group.Length==0 ? null : group.Max(s=>(double)s.Age),
                group.Length==0 ? null : group.Average(s=>(double)s.Age),
                group.Length==0 ? null : group.Average(s=>s.Age/(double)s.Life));
        }).ToArray();
    }
}
