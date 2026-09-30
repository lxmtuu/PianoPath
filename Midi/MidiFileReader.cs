using System.IO;
using System.Text;

namespace PianoPath;

/// <summary>One note of a loaded song, in absolute seconds. Playback and scoring flags are mutated while practising.</summary>
internal sealed class NoteEvent
{
    public int Pitch { get; init; }
    public double Start { get; init; }
    public double Duration { get; init; }
    public double End => Start + Duration;
    public int Velocity { get; init; } = 90;
    public int Track { get; init; }
    public bool Played { get; set; }
    public bool Missed { get; set; }
    public double Timing { get; set; }
}

/// <summary>A parsed Standard MIDI File: notes sorted by start time plus the metronome grid derived from its tempo map.</summary>
internal sealed class MidiSong
{
    public required List<NoteEvent> Notes { get; init; }
    /// <summary>Absolute time (seconds) of every metronome beat from the start of the file to just past its last note.</summary>
    public required IReadOnlyList<double> BeatTimes { get; init; }
    /// <summary>Beats per bar from the first time signature (4 when the file has none).</summary>
    public required int BeatsPerBar { get; init; }
    /// <summary>Optional track names (meta event 0x03) keyed by track index.</summary>
    public required IReadOnlyDictionary<int, string> TrackNames { get; init; }
}

/// <summary>Helpers for lists of notes that are sorted by <see cref="NoteEvent.Start"/>.</summary>
internal static class NoteTimeline
{
    /// <summary>Index of the first note whose start is at or after <paramref name="start"/>; <c>notes.Count</c> when there is none.</summary>
    public static int FirstIndexAtOrAfter(IReadOnlyList<NoteEvent> notes, double start)
    {
        int low = 0, high = notes.Count;
        while (low < high)
        {
            var mid = (low + high) >> 1;
            if (notes[mid].Start < start) low = mid + 1; else high = mid;
        }
        return low;
    }

    /// <summary>Index of the first value at or after <paramref name="value"/> in a sorted list of times.</summary>
    public static int FirstIndexAtOrAfter(IReadOnlyList<double> times, double value)
    {
        int low = 0, high = times.Count;
        while (low < high)
        {
            var mid = (low + high) >> 1;
            if (times[mid] < value) low = mid + 1; else high = mid;
        }
        return low;
    }
}

/// <summary>
/// Reads Standard MIDI Files — formats 0, 1 and 2, with a PPQ or an SMPTE time division — into absolute-time
/// note events.
///
/// <para>
/// The three formats disagree about what a track means. Formats 0 and 1 are one arrangement spread over tracks,
/// so their tempo events share a map and their notes share a timeline. Format 2 is a set of independent
/// sequences, so the patterns are laid end to end: where one ends, the next begins, each on its own tempo map.
/// </para>
///
/// <para>
/// The time division disagrees about what a tick means. A PPQ division counts ticks per quarter note and the
/// tempo map scales them into seconds. An SMPTE division names ticks per second — its high byte is a negative
/// frame rate (29 standing for 30 drop frame) and its low byte the ticks in a frame — so its ticks are already
/// absolute time and tempo events only say how long a beat lasts for the metronome.
/// </para>
/// </summary>
internal static class MidiReader
{
    private const int PercussionChannel = 9;
    private const int MaxBeats = 250_000;

    /// <summary>One MTrk chunk as it was written: notes and tempo changes in ticks, on that track's own clock.</summary>
    private sealed class Track
    {
        public int Index { get; init; }
        public List<(long Start, long End, int Pitch, int Velocity)> Notes { get; } = [];
        public SortedDictionary<long, int> Tempos { get; } = [];
        public long EndTick { get; set; }
    }

    /// <summary>
    /// Turns ticks into seconds for one timeline. A PPQ clock walks the tempo map — a change re-scales every tick
    /// after it — while an SMPTE clock is absolute and ignores tempo for note times.
    /// </summary>
    private sealed class Clock
    {
        private readonly int _division;
        private readonly double _ticksPerSecond;
        private readonly long[] _ticks;
        private readonly int[] _micros;
        private readonly double[] _seconds;

        public Clock(SortedDictionary<long, int> tempos, int division, double ticksPerSecond)
        {
            _division = Math.Max(1, division); _ticksPerSecond = ticksPerSecond;
            var points = tempos.ToArray();
            _ticks = new long[points.Length]; _micros = new int[points.Length]; _seconds = new double[points.Length];
            for (var index = 0; index < points.Length; index++)
            {
                _ticks[index] = points[index].Key; _micros[index] = points[index].Value;
                // The cumulative table both answers tick lookups and places the tempo changes in time.
                _seconds[index] = index == 0 ? 0
                    : _seconds[index - 1] + (_ticksPerSecond > 0
                        ? (_ticks[index] - _ticks[index - 1]) / _ticksPerSecond
                        : (_ticks[index] - _ticks[index - 1]) * (double)_micros[index - 1] / _division / 1_000_000);
            }
        }

        /// <summary>The seconds a tick falls on.</summary>
        public double Seconds(long tick)
        {
            if (_ticksPerSecond > 0) return tick / _ticksPerSecond;
            var index = LastAtOrBefore(_ticks, tick);
            return _seconds[index] + (tick - _ticks[index]) * (double)_micros[index] / _division / 1_000_000;
        }

        /// <summary>How long the quarter note in force lasts at a moment of this timeline.</summary>
        public double QuarterSeconds(double seconds)
        {
            if (_micros.Length == 0) return .5;
            return Math.Max(.02, _micros[LastAtOrBefore(_seconds, seconds)] / 1_000_000.0);
        }

        private static int LastAtOrBefore(long[] values, long value)
        {
            var index = Array.BinarySearch(values, value); if (index < 0) index = ~index - 1; return Math.Max(0, index);
        }
        private static int LastAtOrBefore(double[] values, double value)
        {
            var index = Array.BinarySearch(values, value); if (index < 0) index = ~index - 1; return Math.Max(0, index);
        }
    }

    public static List<NoteEvent> Read(string path) => ReadSong(path).Notes;

    public static MidiSong ReadSong(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (!ChunkId(reader).Equals("MThd", StringComparison.Ordinal)) throw new InvalidDataException(Loc.T("Missing MIDI header."));
        var headerLength = Read32(reader);
        if (headerLength < 6 || headerLength > 1024) throw new InvalidDataException(Loc.T("The MIDI header has an invalid length."));
        var format = Read16(reader); var declaredTracks = Read16(reader); var division = Read16(reader);
        if (format > 2) throw new InvalidDataException(Loc.T("The MIDI file declares a format this reader does not know."));
        if (headerLength > 6) reader.ReadBytes(headerLength - 6);

        // The time base. A PPQ division counts ticks per quarter note; an SMPTE division counts ticks per second,
        // which is why its high byte is read as a signed frame rate and its low byte as ticks within a frame.
        var smpte = (division & 0x8000) != 0;
        var ticksPerSecond = 0.0;
        if (smpte)
        {
            var frames = -((short)division >> 8);
            var ticksPerFrame = division & 0xFF;
            var framesPerSecond = frames switch { 24 => 24.0, 25 => 25.0, 29 => 30000.0 / 1001, 30 => 30.0, _ => 0 };
            if (framesPerSecond == 0 || ticksPerFrame == 0) throw new InvalidDataException(Loc.T("The MIDI file declares a time division this reader cannot use."));
            ticksPerSecond = framesPerSecond * ticksPerFrame;
        }
        else if (division <= 0) throw new InvalidDataException(Loc.T("The MIDI file declares a time division this reader cannot use."));

        var tracks = new List<Track>();
        var trackNames = new Dictionary<int, string>();
        int timeSignatureNumerator = 4, timeSignatureDenominator = 4; var timeSignatureFound = false;
        while (tracks.Count < declaredTracks && stream.Position + 8 <= stream.Length)
        {
            var id = ChunkId(reader); var chunkLength = Read32(reader);
            if (chunkLength < 0) throw new InvalidDataException(Loc.T("Invalid MIDI track chunk."));
            var end = Math.Min(stream.Length, stream.Position + chunkLength);
            if (!id.Equals("MTrk", StringComparison.Ordinal)) { stream.Position = end; continue; }
            var track = new Track { Index = tracks.Count }; track.Tempos[0] = 500000;
            long tick = 0; var running = 0; var active = new Dictionary<(int channel, int pitch), Queue<(long tick, int velocity)>>();
            while (stream.Position < end)
            {
                tick += ReadVar(reader); var status = reader.ReadByte();
                if (status < 0x80) { stream.Position--; if (running == 0) throw new InvalidDataException(Loc.T("Invalid MIDI running status.")); status = (byte)running; }
                else if (status < 0xF0) running = status;
                else running = 0;
                if (status == 0xFF)
                {
                    var type = reader.ReadByte(); var length = ReadVar(reader);
                    if (length < 0 || stream.Position + length > end) throw new InvalidDataException(Loc.T("A MIDI meta event extends past its track."));
                    if (type == 0x51 && length == 3) { var b = reader.ReadBytes(3); var micros = (b[0] << 16) | (b[1] << 8) | b[2]; if (micros > 0) track.Tempos[tick] = micros; }
                    else if (type == 0x58 && length >= 2 && !timeSignatureFound)
                    {
                        var b = reader.ReadBytes((int)length); timeSignatureNumerator = Math.Max(1, (int)b[0]); timeSignatureDenominator = 1 << Math.Clamp((int)b[1], 0, 6); timeSignatureFound = true;
                    }
                    else if (type == 0x03 && length > 0 && !trackNames.ContainsKey(track.Index))
                    {
                        var name = Encoding.Latin1.GetString(reader.ReadBytes((int)length)).Trim('\0', ' ');
                        if (name.Length > 0) trackNames[track.Index] = name.Length > 40 ? name[..40] : name;
                    }
                    else stream.Position += length;
                    continue;
                }
                if (status is 0xF0 or 0xF7) { var length = ReadVar(reader); if (length < 0 || stream.Position + length > end) throw new InvalidDataException(Loc.T("A MIDI system exclusive event extends past its track.")); stream.Position += length; continue; }
                if (status >= 0xF0) { stream.Position += status switch { 0xF1 or 0xF3 => 1, 0xF2 => 2, _ => 0 }; continue; }
                var kind = status & 0xF0; var channel = status & 15; int pitch = reader.ReadByte(); int velocity = kind is 0xC0 or 0xD0 ? 0 : reader.ReadByte();
                if (channel == PercussionChannel) continue; // General MIDI drums are not piano notes.
                if (kind == 0x90 && velocity > 0) { var key = (channel, pitch); if (!active.TryGetValue(key, out var queue)) active[key] = queue = new Queue<(long, int)>(); queue.Enqueue((tick, velocity)); }
                else if (kind == 0x80 || kind == 0x90 && velocity == 0)
                {
                    var key = (channel, pitch); if (active.TryGetValue(key, out var queue) && queue.Count > 0) { var on = queue.Dequeue(); track.Notes.Add((on.tick, Math.Max(on.tick + 1, tick), pitch, on.velocity)); }
                }
            }
            // Notes that never received a Note Off end with the track instead of disappearing.
            foreach (var (key, queue) in active) while (queue.Count > 0) { var on = queue.Dequeue(); track.Notes.Add((on.tick, Math.Max(on.tick + 1, tick), key.pitch, on.velocity)); }
            track.EndTick = Math.Max(tick, track.Notes.Count == 0 ? 0 : track.Notes.Max(note => note.End));
            tracks.Add(track);
            stream.Position = end;
        }

        // Format 2 holds independent sequences rather than one arrangement, so the patterns are laid end to end,
        // each on its own tempo map. Formats 0 and 1 are one arrangement: their tempo events share one map (a
        // tempo written into a playing track applies to the whole piece) and their notes share one timeline.
        var notes = new List<NoteEvent>();
        var beats = new List<double>();
        var (feltBeats, unitsPerBeat) = Meter.Of(timeSignatureNumerator, timeSignatureDenominator);
        var beatQuarters = Meter.BeatInQuarters(unitsPerBeat, timeSignatureDenominator);
        if (format == 2)
        {
            var offset = 0.0;
            foreach (var track in tracks)
            {
                var clock = new Clock(track.Tempos, division, ticksPerSecond);
                AddTrack(notes, track, clock, offset);
                AddGrid(beats, track.EndTick, clock, offset, smpte, division, beatQuarters, pattern: true);
                offset += clock.Seconds(track.EndTick);
            }
        }
        else
        {
            var tempos = new SortedDictionary<long, int>();
            foreach (var track in tracks) foreach (var (at, micros) in track.Tempos) tempos[at] = micros;
            var clock = new Clock(tempos, division, ticksPerSecond);
            foreach (var track in tracks) AddTrack(notes, track, clock, 0);
            // One grid for the whole arrangement, reaching two beats past the last note wherever that is.
            AddGrid(beats, tracks.Count == 0 ? 0 : tracks.Max(track => track.EndTick), clock, 0, smpte, division, beatQuarters, pattern: false);
        }
        notes.Sort((left, right) => left.Start.CompareTo(right.Start));
        // The bar holds the felt beats, not the written numerator: 6/8 holds two, so the bar lines the sheet
        // draws every `BeatsPerBar` grid entries land once a bar, and in the right place.
        return new MidiSong { Notes = notes, BeatTimes = beats, BeatsPerBar = feltBeats, TrackNames = trackNames };
    }

    /// <summary>Puts one track's notes onto the timeline, shifted by <paramref name="offset"/> seconds.</summary>
    private static void AddTrack(List<NoteEvent> notes, Track track, Clock clock, double offset)
    {
        foreach (var (start, end, pitch, velocity) in track.Notes)
        {
            var from = clock.Seconds(start); var to = clock.Seconds(end);
            notes.Add(new NoteEvent { Pitch = pitch, Start = offset + from, Duration = Math.Max(.06, to - from), Velocity = velocity, Track = track.Index });
        }
    }

    /// <summary>
    /// Writes one beat grid, shifted by <paramref name="offset"/> and reaching <paramref name="endTick"/>. A PPQ
    /// grid is the tick grid of the time signature, while an SMPTE file has no tick grid for beats to sit on: its
    /// grid comes from the tempo map, one beat per quarter note in force.
    ///
    /// <para>
    /// <paramref name="pattern"/> is true for a format 2 sequence, whose grid has to stop at its own end so that
    /// the next pattern continues it instead of writing beats backwards over it. One arrangement's grid runs two
    /// beats past its last note, which is what keeps the metronome going to the end of the piece.
    /// </para>
    /// </summary>
    /// <summary>
    /// Lays one beat after another over the whole file. <paramref name="beatQuarters"/> is how long a felt beat
    /// is in quarter notes — one for a simple meter's beat-type, one and a half for the dotted quarter a 6/8 bar
    /// is felt in — so a compound meter gives the sheet the grouping a score writes and not one beat per eighth.
    /// </summary>
    private static void AddGrid(List<double> beats, long endTick, Clock clock, double offset, bool smpte, int division, double beatQuarters, bool pattern)
    {
        if (!smpte)
        {
            var beatTicks = Math.Max(1, (long)Math.Round(division * beatQuarters));
            var count = pattern
                ? (endTick + beatTicks - 1) / beatTicks                  // a pattern's grid covers the pattern, no more
                : endTick / beatTicks + 2;                               // an arrangement keeps the metronome past its last note
            for (long beat = 0; beat < Math.Min(MaxBeats, count); beat++) beats.Add(offset + clock.Seconds(beat * beatTicks));
            return;
        }
        var trackEnd = offset + clock.Seconds(endTick);
        var time = offset;
        while (pattern ? time < trackEnd - 1e-9 : time <= trackEnd + 1e-6)
        {
            if (beats.Count >= MaxBeats) break;
            beats.Add(time);
            time += clock.QuarterSeconds(time - offset) * beatQuarters;
        }
    }

    private static string ChunkId(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new InvalidDataException(Loc.T("The MIDI file is truncated."));
        return Encoding.ASCII.GetString(bytes);
    }
    private static long ReadVar(BinaryReader reader)
    {
        long value = 0; byte current; var bytes = 0;
        do
        {
            current = reader.ReadByte(); value = (value << 7) | (uint)(current & 127);
            if (++bytes > 4) throw new InvalidDataException(Loc.T("Invalid MIDI variable-length quantity."));
        } while ((current & 128) != 0);
        return value;
    }
    private static int Read16(BinaryReader reader) => (reader.ReadByte() << 8) | reader.ReadByte();
    private static int Read32(BinaryReader reader) => (reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
}
