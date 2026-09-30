using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace PianoPath;

/// <summary>
/// A score read from MusicXML: the notes in absolute seconds plus what notation knows and MIDI does not —
/// which staff each note belongs to, the time signature, the title and the composer.
///
/// <para>
/// The staff is the reason this importer exists: a piano score puts the right hand on staff 1 and the left
/// hand on staff 2 (or each hand in its own <c>part</c>), so the hand split is read rather than guessed.
/// <see cref="HandSplitPitch"/> is that reading, and <see cref="SplitFromStaves"/> says whether it was
/// found — when the hands overlap it is not invented, and the note colours keep the value the user chose.
/// </para>
/// </summary>
internal sealed class MusicXmlScore
{
    public required List<NoteEvent> Notes { get; init; }
    /// <summary>Absolute time (seconds) of every metronome beat, from the start to just past the last note.</summary>
    public required IReadOnlyList<double> BeatTimes { get; init; }
    public required int BeatsPerBar { get; init; }
    /// <summary>Part names from <c>part-list</c>, keyed by the track index the notes carry.</summary>
    public required IReadOnlyDictionary<int, string> TrackNames { get; init; }
    /// <summary>The split the staves describe, or null when they describe none (one hand, or overlapping hands).</summary>
    public int? HandSplitPitch { get; init; }
    /// <summary>True when <see cref="HandSplitPitch"/> came from the staves rather than from a guess.</summary>
    public bool SplitFromStaves => HandSplitPitch is not null;
    public int MeasureCount { get; init; }
    public string Title { get; init; } = "";
    public string Composer { get; init; } = "";

    /// <summary>The score as the rest of the application knows a song; the player does not need the notation.</summary>
    internal MidiSong ToSong() => new() { Notes = Notes, BeatTimes = BeatTimes, BeatsPerBar = BeatsPerBar, TrackNames = TrackNames };
}

/// <summary>
/// Reads MusicXML, compressed (<c>.mxl</c>) or plain (<c>.musicxml</c>, <c>.xml</c>).
///
/// <para>
/// Only the part of the format a piano-roll player needs is read: parts, measures, divisions, time and key
/// context, directions that carry a tempo, backups and forwards (which move the cursor inside a measure),
/// chord members (which share the previous note's onset) and per-note staff. Everything else in the file is
/// ignored on purpose — a score is allowed to carry engraving detail this application does not draw.
/// </para>
///
/// <para>
/// Times are seconds: MusicXML counts in <em>divisions per quarter note</em>, so every duration is divided
/// by the divisions in force and multiplied by the seconds a quarter lasts at the tempo in force. A tempo
/// change therefore moves the notes after it, and the metronome grid is built measure by measure from the
/// time signature and that tempo.
/// </para>
/// </summary>
internal static class MusicXmlReader
{
    /// <summary>Semitone of each written step, before the <c>alter</c> of the note is added.</summary>
    private static readonly Dictionary<string, int> StepSemitones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C"] = 0, ["D"] = 2, ["E"] = 4, ["F"] = 5, ["G"] = 7, ["A"] = 9, ["B"] = 11
    };

    internal const double DefaultTempo = 120;
    /// <summary>A hand needs at least this share of the notes for the staves to mean a split.</summary>
    internal const double MinimumHandShare = .10;

    /// <summary>Reads a score from disk: <c>.mxl</c> is a zip, everything else is XML text.</summary>
    internal static MusicXmlScore ReadScore(string path)
    {
        var text = path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase) ? ReadCompressed(path) : File.ReadAllText(path);
        return Parse(text, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>The score document inside a compressed MusicXML file, following <c>META-INF/container.xml</c>.</summary>
    internal static string ReadCompressed(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = FindScoreEntry(archive) ?? throw new InvalidDataException(Loc.T("This .mxl file does not contain a MusicXML score."));
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The entry a compressed score points at through its container, or the only XML entry it holds. Public
    /// to the checks so a hand-built zip is read through exactly the path a real file takes.
    /// </summary>
    internal static ZipArchiveEntry? FindScoreEntry(ZipArchive archive)
    {
        var container = archive.GetEntry("META-INF/container.xml");
        if (container is not null)
        {
            using var stream = container.Open();
            var document = XDocument.Load(stream);
            var path = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var pointed = archive.GetEntry(path!) ?? archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase));
                if (pointed is not null) return pointed;
            }
        }
        return archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase)
            || (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Parses MusicXML text. <paramref name="fallbackTitle"/> names the score when the file does not.</summary>
    internal static MusicXmlScore Parse(string xml, string fallbackTitle = "")
    {
        XDocument document;
        try { document = XDocument.Parse(xml, LoadOptions.None); }
        catch (Exception ex) { throw new InvalidDataException(Loc.F("This file is not MusicXML: {0}", ex.Message), ex); }
        var root = document.Root ?? throw new InvalidDataException(Loc.T("This file is not MusicXML: it has no root element."));
        if (!string.Equals(root.Name.LocalName, "score-partwise", StringComparison.Ordinal))
            throw new InvalidDataException(string.Equals(root.Name.LocalName, "score-timewise", StringComparison.Ordinal)
                ? Loc.T("Time-wise MusicXML is not supported. Save the score as part-wise MusicXML and open it again.")
                : Loc.F("This file is not a MusicXML score (found <{0}>).", root.Name.LocalName));

        var partNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in root.Elements().Where(element => element.Name.LocalName == "part-list").Elements())
        {
            if (part.Name.LocalName != "score-part") continue;
            var id = part.Attribute("id")?.Value ?? "";
            if (id.Length > 0) partNames[id] = Text(part.Element(part.Name.Namespace + "part-name"));
        }

        var notes = new List<NoteEvent>();
        var beatTimes = new List<double>();
        var trackNames = new Dictionary<int, string>();
        var hands = new List<int>();                     // 0 = right, 1 = left; used only for the split
        var beatsPerBar = 4;
        var measures = 0;
        var track = 0;

        foreach (var part in root.Elements().Where(element => element.Name.LocalName == "part"))
        {
            var id = part.Attribute("id")?.Value ?? "";
            trackNames[track] = partNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : Loc.F("Part {0}", track + 1);
            var measureList = part.Elements().Where(element => element.Name.LocalName == "measure").ToList();
            measures = Math.Max(measures, measureList.Count);
            var twoStaves = measureList.SelectMany(measure => measure.Descendants())
                .Any(element => element.Name.LocalName == "staves" && Text(element).Trim() is "2" or "3" or "4");
            var divisions = 1;
            var beats = 4;
            var beatType = 4;
            var tempo = DefaultTempo;
            var partSeconds = 0.0; // where this measure starts, so the beat grid can be laid down measure by measure

            foreach (var measure in measureList)
            {
                // A part's own timeline: the cursor counts divisions from the start of the part, so parts stay
                // in step with each other and `backup`/`forward` move it as the notation says.
                var cursor = 0;
                var lastStart = 0; // onset of the last note that was not a chord member
                foreach (var element in measure.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "attributes":
                            divisions = Math.Max(1, Int(element.Element(element.Name.Namespace + "divisions"), divisions));
                            var time = element.Element(element.Name.Namespace + "time");
                            if (time is not null)
                            {
                                beats = Math.Max(1, Int(time.Element(time.Name.Namespace + "beats"), beats));
                                beatType = Math.Max(1, Int(time.Element(time.Name.Namespace + "beat-type"), beatType));
                            }
                            break;
                        case "direction":
                            tempo = DirectionTempo(element, tempo);
                            break;
                        case "sound":
                            tempo = Math.Max(.5, Double(element.Attribute("tempo"), tempo));
                            break;
                        case "backup":
                            cursor = Math.Max(0, cursor - Int(element.Element(element.Name.Namespace + "duration"), 0));
                            break;
                        case "forward":
                            cursor += Math.Max(0, Int(element.Element(element.Name.Namespace + "duration"), 0));
                            break;
                        case "note":
                            var isChord = element.Element(element.Name.Namespace + "chord") is not null;
                            var isGrace = element.Element(element.Name.Namespace + "grace") is not null;
                            var duration = Math.Max(0, Int(element.Element(element.Name.Namespace + "duration"), 0));
                            var staff = Math.Max(1, Int(element.Element(element.Name.Namespace + "staff"), 1));
                            var start = isChord ? lastStart : cursor; // a chord member shares the onset of the note it is stacked on
                            if (!isGrace && element.Element(element.Name.Namespace + "rest") is null && Pitch(element) is { } pitch && duration > 0)
                            {
                                // The measure's own start plus the offset inside it: a cursor is measured from
                                // the beginning of the part, so the second measure does not restart at zero.
                                notes.Add(new NoteEvent
                                {
                                    Pitch = pitch, Track = track,
                                    Start = partSeconds + Seconds(start, divisions, tempo),
                                    Duration = Seconds(duration, divisions, tempo)
                                });
                                hands.Add(twoStaves ? (staff >= 2 ? 1 : 0) : (track == 0 ? 0 : 1));
                            }
                            if (!isChord) { cursor = start + duration; lastStart = start; }
                            break;
                    }
                }
                // One beat per beat-type unit of the time signature, at the tempo in force for this measure:
                // the grid follows a tempo or a time-signature change instead of assuming one tempo throughout.
                if (track == 0)
                {
                    // One beat per beat-type unit of a simple meter and one per group of three of a compound one,
                    // which is what the sheet beams and counts its rests by; the tempo in force here is the one
                    // the grid is laid down at, so the metronome follows a tempo or signature change.
                    var (feltBeats, unitsPerBeat) = Meter.Of(beats, beatType);
                    var beatSeconds = (60 / tempo) * Meter.BeatInQuarters(unitsPerBeat, beatType);
                    for (var beat = 0; beat < feltBeats; beat++) beatTimes.Add(partSeconds + beat * beatSeconds);
                }
                // The next measure starts where this one's cursor ended; the grid already has every beat of
                // it, so nothing has to be patched up here.
                partSeconds += Seconds(cursor, divisions, tempo);
            }
            if (track == 0) beatsPerBar = Meter.Of(beats, beatType).Beats;
            track++;
        }

        if (notes.Count == 0) throw new InvalidDataException(Loc.T("No notes were found in this MusicXML file."));
        // Stable order: by start, then by pitch (low to high) so chords and simultaneous parts read the same
        // way every time — the note timeline helpers rely on it.
        var order = Enumerable.Range(0, notes.Count).OrderBy(index => notes[index].Start).ThenBy(index => notes[index].Pitch).ToList();
        var sortedNotes = order.Select(index => notes[index]).ToList();
        var sortedHands = order.Select(index => hands[index]).ToList();

        // The grid runs to just past the last note so the metronome and the sheet layer always have a beat
        // to stand on, even when the score ends on an off-beat.
        var last = sortedNotes.Max(note => note.End);
        var quarter = 60 / DefaultTempo;
        var gridStep = beatTimes.Count > 1 ? Math.Max(.05, beatTimes[^1] - beatTimes[^2]) : quarter;
        while (beatTimes.Count > 0 && beatTimes[^1] <= last) beatTimes.Add(beatTimes[^1] + gridStep);
        if (beatTimes.Count == 0) for (var time = 0.0; time <= last + quarter; time += quarter) beatTimes.Add(time);

        var handSplit = HandSplitFromStaves(sortedHands, sortedNotes);
        return new MusicXmlScore
        {
            Notes = sortedNotes,
            BeatTimes = beatTimes,
            BeatsPerBar = beatsPerBar,
            TrackNames = trackNames,
            HandSplitPitch = handSplit,
            MeasureCount = measures,
            Title = Text(root.Descendants().FirstOrDefault(element => element.Name.LocalName == "work-title")) is { Length: > 0 } work ? work : fallbackTitle,
            Composer = Text(root.Descendants().FirstOrDefault(element => element.Name.LocalName == "creator" && element.Attribute("type")?.Value == "composer"))
        };
    }

    /// <summary>
    /// The split the staves describe: every left-hand note has to sit below every right-hand note, and each
    /// hand has to carry a share of the notes; the split is the gap between the two hands. Null when the
    /// score does not say so, because a split that was guessed is worse than none — the user's own value or
    /// <see cref="HandSplit.Infer"/> stays in charge then.
    /// </summary>
    internal static int? HandSplitFromStaves(IReadOnlyList<int> hands, IReadOnlyList<NoteEvent> notes)
    {
        if (hands.Count != notes.Count || notes.Count == 0) return null;
        int? leftTop = null, rightBottom = null;
        var left = 0; var right = 0;
        for (var index = 0; index < notes.Count; index++)
        {
            if (hands[index] == 0) { right++; rightBottom = rightBottom is { } low ? Math.Min(low, notes[index].Pitch) : notes[index].Pitch; }
            else { left++; leftTop = leftTop is { } high ? Math.Max(high, notes[index].Pitch) : notes[index].Pitch; }
        }
        if (left == 0 || right == 0) return null;
        if (left < notes.Count * MinimumHandShare || right < notes.Count * MinimumHandShare) return null;
        if (leftTop is not { } top || rightBottom is not { } bottom || top >= bottom) return null;
        return (top + bottom) / 2;
    }

    /// <summary>Seconds from divisions at a tempo: divisions are per quarter note, so this is exact.</summary>
    private static double Seconds(int divisions, int perQuarter, double tempo) => divisions / (double)Math.Max(1, perQuarter) * (60 / tempo);

    private static double DirectionTempo(XElement direction, double current)
    {
        var sound = direction.Elements().FirstOrDefault(element => element.Name.LocalName == "sound");
        if (sound is not null && Double(sound.Attribute("tempo"), current) > 0) return Double(sound.Attribute("tempo"), current);
        foreach (var perMinute in direction.Descendants().Where(element => element.Name.LocalName == "per-minute"))
            if (double.TryParse(Text(perMinute), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;
        return current;
    }

    private static int? Pitch(XElement note)
    {
        var pitch = note.Element(note.Name.Namespace + "pitch");
        if (pitch is null) return null;
        var step = Text(pitch.Element(pitch.Name.Namespace + "step")).Trim().ToUpperInvariant();
        if (!StepSemitones.TryGetValue(step, out var semitone)) return null;
        var octave = Int(pitch.Element(pitch.Name.Namespace + "octave"), 4);
        var alter = (int)Math.Round(Double(pitch.Element(pitch.Name.Namespace + "alter"), 0));
        var value = 12 * (octave + 1) + semitone + alter;
        return value is >= 0 and <= 127 ? value : null;
    }

    private static string Text(XElement? element) => element?.Value?.Trim() ?? "";
    private static int Int(XElement? element, int fallback) =>
        int.TryParse(Text(element), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static double Double(XAttribute? attribute, double fallback) =>
        double.TryParse(attribute?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static double Double(XElement? element, double fallback) =>
        double.TryParse(Text(element), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
