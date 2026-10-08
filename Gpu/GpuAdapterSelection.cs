namespace PianoPath;

/// <summary>
/// Picks the DXGI adapter a diagnostic run asked for by name. Hybrid laptops carry an iGPU and a dGPU
/// and Direct3D hands the process whichever adapter Windows assigned to the executable, so the hardware
/// gate needs a way to say "measure this adapter" instead of hoping the OS setting is right. The matcher
/// is pure — it never touches DXGI — so the selection rules are testable on any machine.
/// </summary>
internal static class GpuAdapterSelection
{
    /// <summary>
    /// Resolves a requested adapter name against the machine's adapter list. The request is matched as a
    /// case-insensitive substring of the adapter description, and it must match exactly one adapter: a
    /// request that matches none or several is a problem to report, never a silent fallback — measuring
    /// the wrong adapter and calling it the requested one is worse than not measuring.
    /// </summary>
    /// <returns>
    /// <c>(-1, null)</c> when nothing was requested (keep the OS default adapter); <c>(index, null)</c>
    /// for the unique match; <c>(-1, problem)</c> with a message naming every candidate otherwise.
    /// </returns>
    internal static (int Index, string? Problem) Select(IReadOnlyList<string> adapterNames, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return (-1, null);
        var matches = new List<int>();
        for (var i = 0; i < adapterNames.Count; i++)
        {
            if (adapterNames[i].Contains(requested.Trim(), StringComparison.OrdinalIgnoreCase)) matches.Add(i);
        }
        if (matches.Count == 1) return (matches[0], null);
        var available = string.Join(" | ", adapterNames);
        return matches.Count == 0
            ? (-1, $"No adapter matches \"{requested.Trim()}\". Adapters on this machine: {available}")
            : (-1, $"\"{requested.Trim()}\" matches {matches.Count} adapters ({string.Join(" | ", matches.Select(index => adapterNames[index]))}); ask for a longer name that identifies exactly one.");
    }
}
