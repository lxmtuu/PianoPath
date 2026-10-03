using System.IO;
using System.IO.Compression;
using System.Text;
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
    /// <summary>Beat-grid indices at which MusicXML measures begin; empty means use the fixed MIDI meter.</summary>
    public IReadOnlyList<int> DownbeatIndices { get; init; } = [];
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
    internal MidiSong ToSong() => new()
    {
        Notes = Notes,
        BeatTimes = BeatTimes,
        BeatsPerBar = BeatsPerBar,
        DownbeatIndices = DownbeatIndices,
        TrackNames = TrackNames
    };
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
/// Notes are first placed on a score-wide timeline measured in quarter notes. Durations are converted using
/// the divisions in force at their own position; tempo marks from any part are then integrated into one
/// quarter-note-per-minute map. This keeps simultaneous parts aligned and handles tempo changes within a bar.
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
    /// <summary>Maximum decompressed/input MusicXML size, in bytes and decoded characters.</summary>
    internal const long MaxXmlBytes = 32L * 1024 * 1024;
    internal const int MaxXmlCharacters = 8 * 1024 * 1024;
    internal const int MaxBeats = 250_000;
    /// <summary>A hand needs at least this share of the notes for the staves to mean a split.</summary>
    internal const double MinimumHandShare = .10;

    private readonly record struct MeasureInfo(double StartQuarter, double LengthQuarter, int Beats, int BeatType);
    private readonly record struct RawNote(double StartQuarter, double DurationQuarter, int Pitch, int Track, int Hand);
    private readonly record struct TempoEvent(double Quarter, double QuarterBpm, int Track, int Order);

    /// <summary>Reads a score from disk, refusing files that exceed the import size limit before parsing them.</summary>
    internal static MusicXmlScore ReadScore(string path)
    {
        if (path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
            return Parse(ReadCompressed(path), Path.GetFileNameWithoutExtension(path));

        using var stream = File.OpenRead(path);
        if (stream.Length > MaxXmlBytes) throw TooLarge();
        return Parse(ReadBoundedText(stream), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>The score document inside a compressed MusicXML file, following <c>META-INF/container.xml</c>.</summary>
    internal static string ReadCompressed(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = FindScoreEntry(archive) ?? throw new InvalidDataException(Loc.T("This .mxl file does not contain a MusicXML score."));
        if (entry.Length > MaxXmlBytes) throw TooLarge();
        using var stream = entry.Open();
        return ReadBoundedText(stream);
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
            if (container.Length > MaxXmlBytes) throw TooLarge();
            using var stream = container.Open();
            var document = XDocument.Parse(ReadBoundedText(stream), LoadOptions.None);
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
        if (xml.Length > MaxXmlCharacters) throw TooLarge();
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

        var parts = root.Elements().Where(element => element.Name.LocalName == "part").ToList();
        var measureLists = parts.Select(part => part.Elements().Where(element => element.Name.LocalName == "measure").ToList()).ToList();
        var measureCount = measureLists.Count == 0 ? 0 : measureLists.Max(list => list.Count);
        var primaryMeasures = measureLists.FirstOrDefault(list => list.Count > 0) ?? [];
        var measureMap = BuildMeasureMap(primaryMeasures);
        while (measureMap.Count < measureCount)
        {
            var last = measureMap.Count > 0 ? measureMap[^1] : new MeasureInfo(0, 4, 4, 4);
            var length = last.LengthQuarter > 0 ? last.LengthQuarter : last.Beats * 4.0 / Math.Max(1, last.BeatType);
            measureMap.Add(new MeasureInfo(last.StartQuarter + length, length, last.Beats, last.BeatType));
        }

        var partTrackNames = new Dictionary<int, string>();
        var rawNotes = new List<RawNote>();
        var tempoEvents = new List<TempoEvent>();
        var tempoOrder = 0;
        var track = 0;
        foreach (var part in parts)
        {
            var id = part.Attribute("id")?.Value ?? "";
            partTrackNames[track] = partNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : Loc.F("Part {0}", track + 1);
            var measures = measureLists[track];
            var twoStaves = measures.SelectMany(measure => measure.Descendants())
                .Any(element => element.Name.LocalName == "staves" && Text(element).Trim() is "2" or "3" or "4");
            var divisions = 1;

            for (var measureIndex = 0; measureIndex < measures.Count; measureIndex++)
            {
                var measureStart = measureMap[measureIndex].StartQuarter;
                var cursor = 0.0;                 // measure-local quarter-note position; divisions may change at any point
                var lastStart = 0.0;              // onset of the last note that was not a chord member
                foreach (var element in measures[measureIndex].Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "attributes":
                            divisions = Math.Max(1, Int(element.Element(element.Name.Namespace + "divisions"), divisions));
                            break;
                        case "direction":
                            if (DirectionTempo(element) is { } directionTempo)
                            {
                                var offset = Int(element.Element(element.Name.Namespace + "offset"), 0) / (double)divisions;
                                tempoEvents.Add(new TempoEvent(measureStart + Math.Max(0, cursor + offset), directionTempo, track, tempoOrder++));
                            }
                            break;
                        case "sound":
                            if (TryTempo(element.Attribute("tempo"), out var soundTempo))
                                tempoEvents.Add(new TempoEvent(measureStart + cursor, soundTempo, track, tempoOrder++));
                            break;
                        case "backup":
                            cursor = Math.Max(0, cursor - DurationQuarters(element, divisions));
                            break;
                        case "forward":
                            cursor += DurationQuarters(element, divisions);
                            break;
                        case "note":
                            var isChord = element.Element(element.Name.Namespace + "chord") is not null;
                            var isGrace = element.Element(element.Name.Namespace + "grace") is not null;
                            var duration = DurationQuarters(element, divisions);
                            var staff = Math.Max(1, Int(element.Element(element.Name.Namespace + "staff"), 1));
                            var start = isChord ? lastStart : cursor;
                            if (!isGrace && element.Element(element.Name.Namespace + "rest") is null && Pitch(element) is { } pitch && duration > 0)
                                rawNotes.Add(new RawNote(measureStart + start, duration, pitch, track,
                                    twoStaves ? (staff >= 2 ? 1 : 0) : (track == 0 ? 0 : 1)));
                            if (!isChord) { cursor = start + duration; lastStart = start; }
                            break;
                    }
                }
            }
            track++;
        }

        if (rawNotes.Count == 0) throw new InvalidDataException(Loc.T("No notes were found in this MusicXML file."));
        var tempoMap = new TempoTimeline(tempoEvents);
        var converted = rawNotes.Select(raw =>
        {
            var start = tempoMap.SecondsAt(raw.StartQuarter);
            var end = tempoMap.SecondsAt(raw.StartQuarter + raw.DurationQuarter);
            return (Note: new NoteEvent { Pitch = raw.Pitch, Track = raw.Track, Start = start, Duration = end - start }, raw.Hand);
        }).OrderBy(item => item.Note.Start).ThenBy(item => item.Note.Pitch).ToList();
        var notes = converted.Select(item => item.Note).ToList();
        var hands = converted.Select(item => item.Hand).ToList();

        var quarterBeatTimes = new List<double>();
        var downbeatIndices = new List<int>();
        var beatsPerBar = 4;
        var extensionStep = 1.0;
        for (var measureIndex = 0; measureIndex < measureMap.Count; measureIndex++)
        {
            var measure = measureMap[measureIndex];
            var (feltBeats, unitsPerBeat) = Meter.Of(measure.Beats, measure.BeatType);
            var beatQuarter = Meter.BeatInQuarters(unitsPerBeat, measure.BeatType);
            if (measureIndex == 0) { beatsPerBar = feltBeats; extensionStep = beatQuarter; }
            if (measureIndex == measureMap.Count - 1) extensionStep = beatQuarter;
            downbeatIndices.Add(quarterBeatTimes.Count);
            var count = 0;
            for (var beat = 0; beat < feltBeats && beat * beatQuarter < measure.LengthQuarter - 1e-9; beat++)
            {
                if (quarterBeatTimes.Count >= MaxBeats) throw TooManyBeats();
                quarterBeatTimes.Add(measure.StartQuarter + beat * beatQuarter);
                count++;
            }
            if (count == 0)
            {
                if (quarterBeatTimes.Count >= MaxBeats) throw TooManyBeats();
                quarterBeatTimes.Add(measure.StartQuarter);
            }
        }
        if (quarterBeatTimes.Count == 0) quarterBeatTimes.Add(0);

        var lastNoteQuarter = rawNotes.Max(note => note.StartQuarter + note.DurationQuarter);
        var lastBeatQuarter = quarterBeatTimes[^1];
        while (lastBeatQuarter <= lastNoteQuarter + 1e-9)
        {
            if (quarterBeatTimes.Count >= MaxBeats) throw TooManyBeats();
            lastBeatQuarter += Math.Max(.05, extensionStep);
            quarterBeatTimes.Add(lastBeatQuarter);
        }
        var beatTimes = quarterBeatTimes.Select(tempoMap.SecondsAt).ToList();

        return new MusicXmlScore
        {
            Notes = notes,
            BeatTimes = beatTimes,
            BeatsPerBar = beatsPerBar,
            DownbeatIndices = downbeatIndices,
            TrackNames = partTrackNames,
            HandSplitPitch = HandSplitFromStaves(hands, notes),
            MeasureCount = measureCount,
            Title = Text(root.Descendants().FirstOrDefault(element => element.Name.LocalName == "work-title")) is { Length: > 0 } work ? work : fallbackTitle,
            Composer = Text(root.Descendants().FirstOrDefault(element => element.Name.LocalName == "creator" && element.Attribute("type")?.Value == "composer"))
        };
    }

    /// <summary>
    /// Reads the measure starts and meter from the first part. The furthest cursor position accounts for
    /// multiple voices using backup/forward; a normal bar is at least its written meter length, while an
    /// implicit pickup uses the duration actually present.
    /// </summary>
    private static List<MeasureInfo> BuildMeasureMap(IReadOnlyList<XElement> measures)
    {
        var result = new List<MeasureInfo>(measures.Count);
        var divisions = 1;
        var beats = 4;
        var beatType = 4;
        var measureStart = 0.0;
        foreach (var measure in measures)
        {
            var cursor = 0.0;
            var furthest = 0.0;
            var lastStart = 0.0;
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
                    case "backup":
                        cursor = Math.Max(0, cursor - DurationQuarters(element, divisions));
                        break;
                    case "forward":
                        cursor += DurationQuarters(element, divisions);
                        furthest = Math.Max(furthest, cursor);
                        break;
                    case "note":
                        var chord = element.Element(element.Name.Namespace + "chord") is not null;
                        var duration = DurationQuarters(element, divisions);
                        var start = chord ? lastStart : cursor;
                        furthest = Math.Max(furthest, start + duration);
                        if (!chord) { cursor = start + duration; lastStart = start; }
                        break;
                }
            }
            var nominalLength = beats * 4.0 / Math.Max(1, beatType);
            var implicitMeasure = string.Equals(measure.Attribute("implicit")?.Value, "yes", StringComparison.OrdinalIgnoreCase);
            var length = implicitMeasure ? furthest : Math.Max(nominalLength, furthest);
            if (length <= 1e-9) length = nominalLength;
            result.Add(new MeasureInfo(measureStart, length, beats, beatType));
            measureStart += length;
        }
        return result;
    }

    /// <summary>Converts the current divisions-based duration to quarter notes.</summary>
    private static double DurationQuarters(XElement element, int divisions) =>
        Math.Max(0, Int(element.Element(element.Name.Namespace + "duration"), 0)) / (double)Math.Max(1, divisions);

    /// <summary>Reads the quarter-note tempo of a direction, including the written beat unit and its dots.</summary>
    private static double? DirectionTempo(XElement direction)
    {
        var sound = direction.Elements().FirstOrDefault(element => element.Name.LocalName == "sound");
        if (sound is not null && TryTempo(sound.Attribute("tempo"), out var soundTempo)) return soundTempo;

        var metronome = direction.Descendants().FirstOrDefault(element => element.Name.LocalName == "metronome");
        if (metronome is null) return null;
        var perMinute = metronome.Elements().FirstOrDefault(element => element.Name.LocalName == "per-minute");
        var beatUnit = metronome.Elements().FirstOrDefault(element => element.Name.LocalName == "beat-unit");
        if (perMinute is null || beatUnit is null
            || !double.TryParse(Text(perMinute), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0
            || BeatUnitQuarters(Text(beatUnit)) is not { } quarterLength)
            return null;

        var dots = metronome.Elements().Count(element => element.Name.LocalName == "beat-unit-dot");
        var dottedLength = quarterLength * (2 - Math.Pow(.5, dots));
        return Math.Max(.5, value * dottedLength);
    }

    private static double? BeatUnitQuarters(string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "maxima" => 32,
        "long" => 16,
        "breve" => 8,
        "whole" => 4,
        "half" => 2,
        "quarter" => 1,
        "eighth" => .5,
        "16th" => .25,
        "32nd" => .125,
        "64th" => .0625,
        "128th" => .03125,
        "256th" => .015625,
        _ => null
    };

    private static bool TryTempo(XAttribute? attribute, out double tempo)
    {
        if (double.TryParse(attribute?.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            tempo = Math.Max(.5, value);
            return true;
        }
        tempo = 0;
        return false;
    }

    /// <summary>Integrates the tempo events into seconds so notes and beats share one score-wide clock.</summary>
    private sealed class TempoTimeline
    {
        private readonly double[] _quarters;
        private readonly double[] _tempos;
        private readonly double[] _seconds;

        internal TempoTimeline(IEnumerable<TempoEvent> events)
        {
            var points = new List<(double Quarter, double Tempo)> { (0, DefaultTempo) };
            foreach (var tempoEvent in events.Where(item => item.Quarter >= 0)
                         .OrderBy(item => item.Quarter).ThenBy(item => item.Track).ThenBy(item => item.Order))
            {
                var quarter = tempoEvent.Quarter;
                if (quarter <= points[^1].Quarter + 1e-9)
                {
                    if (Math.Abs(quarter - points[^1].Quarter) <= 1e-9)
                        points[^1] = (points[^1].Quarter, tempoEvent.QuarterBpm);
                    continue;
                }
                points.Add((quarter, tempoEvent.QuarterBpm));
            }

            _quarters = points.Select(point => point.Quarter).ToArray();
            _tempos = points.Select(point => point.Tempo).ToArray();
            _seconds = new double[points.Count];
            for (var index = 1; index < points.Count; index++)
                _seconds[index] = _seconds[index - 1] + (_quarters[index] - _quarters[index - 1]) * 60 / _tempos[index - 1];
        }

        internal double SecondsAt(double quarter)
        {
            int low = 0, high = _quarters.Length;
            while (low < high)
            {
                var middle = (low + high) >> 1;
                if (_quarters[middle] <= quarter + 1e-9) low = middle + 1; else high = middle;
            }
            var index = Math.Max(0, low - 1);
            return _seconds[index] + (quarter - _quarters[index]) * 60 / _tempos[index];
        }
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
    private static double Double(XElement? element, double fallback) =>
        double.TryParse(Text(element), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static string ReadBoundedText(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var text = new StringBuilder();
        var buffer = new char[8192];
        var total = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxXmlCharacters) throw TooLarge();
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    private static InvalidDataException TooLarge() => new(Loc.T("This MusicXML file is too large to open safely."));
    private static InvalidDataException TooManyBeats() => new(Loc.T("This MusicXML score has too many beats to display safely."));
}
