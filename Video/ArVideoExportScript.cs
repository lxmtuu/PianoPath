namespace PianoPath;

/// <summary>
/// Maps a loaded song onto the tick grid of an offline take (roadmap §14.10 AR‑S7, export UI): every note
/// becomes a press on the tick that contains its start and a release on the tick that contains its end,
/// so the piano the take mixes says what the falling notes show. A release always follows a press by at
/// least one tick, and when one tick carries both, the releases fire first — a re-pressed key survives the
/// re-press instead of the release that happened to share its tick.
/// </summary>
internal static class ArVideoExportScript
{
    /// <summary>
    /// Builds the per-tick event list for a take of <paramref name="frames"/> ticks at
    /// <paramref name="frameRate"/> fps. Notes are (start, end) in take seconds; anything outside the take
    /// window is dropped, anything partially inside is clipped to the take.
    /// </summary>
    internal static Dictionary<int, ArVideoNoteEvent[]> MapNotes(IEnumerable<(double Start, double End, int Pitch, int Velocity)>? notes, int frames, int frameRate)
    {
        var releases = new Dictionary<int, List<ArVideoNoteEvent>>();
        var presses = new Dictionary<int, List<ArVideoNoteEvent>>();
        if (notes is not null)
            foreach (var note in notes)
            {
                if (note.End <= 0 || note.Start >= frames / (double)frameRate) continue;
                var onTick = Math.Clamp((int)Math.Floor(Math.Max(0, note.Start) * frameRate), 0, frames - 1);
                var offTick = Math.Clamp((int)Math.Floor(note.End * frameRate), onTick + 1, frames);
                if (!presses.TryGetValue(onTick, out var onList)) presses[onTick] = onList = [];
                onList.Add(new ArVideoNoteEvent(note.Pitch, Math.Clamp(note.Velocity, 1, 127), On: true));
                if (offTick < frames)
                {
                    if (!releases.TryGetValue(offTick, out var offList)) releases[offTick] = offList = [];
                    offList.Add(new ArVideoNoteEvent(note.Pitch, 0, On: false));
                }
            }
        var script = new Dictionary<int, ArVideoNoteEvent[]>();
        foreach (var tick in presses.Keys.Union(releases.Keys))
        {
            releases.TryGetValue(tick, out var offs);
            presses.TryGetValue(tick, out var ons);
            var events = new List<ArVideoNoteEvent>((offs?.Count ?? 0) + (ons?.Count ?? 0));
            if (offs is not null) events.AddRange(offs);   // a release first: the re-press on the same tick wins
            if (ons is not null) events.AddRange(ons);
            script[tick] = events.ToArray();
        }
        return script;
    }

    /// <summary>The per-tick lookup the engine's audio hook wants; an unscripted tick fires nothing.</summary>
    internal static IReadOnlyList<ArVideoNoteEvent> EventsForTick(IReadOnlyDictionary<int, ArVideoNoteEvent[]> script, int tick) =>
        script.TryGetValue(tick, out var events) ? events : [];
}
