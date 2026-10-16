namespace PianoPath;

/// <summary>First integration slice: real live events, not an imported MIDI/device test.</summary>
internal static class KeyParticleEnsemblePlan
{
    internal const int Fps=30, Width=640, Height=360, Seed=20261016;
    internal sealed record Recipe(string Id,string Burst,string Held,string Morph,string Release);
    internal sealed record Note(int Pitch,int On,int Off,double Strength);
    internal sealed record Checkpoint(string Id,int Frame);
    internal static IReadOnlyList<Recipe> Recipes { get; }=Array.AsReadOnly(new[] {
        new Recipe("embers-wisps","Embers","Wisps","Shatter","Float Up"),
        new Recipe("splash-flames","Splash","Flames","Melt","Dissolve"),
        new Recipe("fireworks-wisps","Fireworks","Wisps","Bounce","Smoke"),
        new Recipe("confetti-flames","Confetti","Flames","Shatter","Float Up"),
        new Recipe("dust-wisps","Dust","Wisps","Melt","Dissolve") });
    internal static IReadOnlyList<Note> Notes { get; }=Array.AsReadOnly(new[] {new Note(60,0,12,.85)}
        .Concat(new[]{48,55,60,61,64,67,72,73}.Select(p=>new Note(p,18,36,.9))).ToArray());
    internal static IReadOnlyList<Checkpoint> Checkpoints { get; }=Array.AsReadOnly(new[] {
        new Checkpoint("single-attack",2),new Checkpoint("single-held",10),new Checkpoint("chord-attack",20),
        new Checkpoint("chord-held",33),new Checkpoint("release-pedal",38),new Checkpoint("pedal-up",56),new Checkpoint("rest",149) });
    internal static IReadOnlyList<string> Palettes { get; }=Array.AsReadOnly(new[]{"prism","indigo"});
    internal static IReadOnlyList<int> Lights { get; }=Array.AsReadOnly(new[]{0,100});
    internal static IReadOnlyList<string> Variants { get; }=Array.AsReadOnly(new[]{"off","classic","mixed","front-control"});
    internal static bool PedalAt(int frame)
    {if(frame<0)throw new ArgumentOutOfRangeException(nameof(frame));return frame>=24 && frame<54;}
    internal static HashSet<int> PressedAt(int frame)
    {if(frame<0)throw new ArgumentOutOfRangeException(nameof(frame));return Notes.Where(n=>frame>=n.On && frame<n.Off).Select(n=>n.Pitch).ToHashSet();}
    internal static int CaptureCount=>Recipes.Count*Palettes.Count*Lights.Count*2*Checkpoints.Count*Variants.Count;
}
