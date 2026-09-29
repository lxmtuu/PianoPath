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

/// <summary>Reads Standard MIDI Files (format 0 and 1) into absolute-time note events.</summary>
internal static class MidiReader
{
    private const int PercussionChannel = 9;
    private const int MaxBeats = 250_000;

    public static List<NoteEvent> Read(string path) => ReadSong(path).Notes;

    public static MidiSong ReadSong(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (!ChunkId(reader).Equals("MThd", StringComparison.Ordinal)) throw new InvalidDataException("Missing MIDI header.");
        var headerLength = Read32(reader);
        if (headerLength < 6 || headerLength > 1024) throw new InvalidDataException("The MIDI header has an invalid length.");
        var format = Read16(reader); var tracks = Read16(reader); var division = Read16(reader);
        if (format == 2) throw new InvalidDataException("MIDI format 2 sequences are not supported yet.");
        if (division == 0 || (division & 0x8000) != 0) throw new InvalidDataException("Unsupported MIDI time division (SMPTE timing is not supported).");
        if (headerLength > 6) reader.ReadBytes(headerLength - 6);

        var raw = new List<(long start, long end, int pitch, int velocity, int track)>();
        var tempos = new SortedDictionary<long, int> { [0] = 500000 };
        var trackNames = new Dictionary<int, string>();
        int timeSignatureNumerator = 4, timeSignatureDenominator = 4; var timeSignatureFound = false;
        long lastTick = 0; var track = 0;
        while (track < tracks && stream.Position + 8 <= stream.Length)
        {
            var id = ChunkId(reader); var chunkLength = Read32(reader);
            if (chunkLength < 0) throw new InvalidDataException("Invalid MIDI track chunk.");
            var end = Math.Min(stream.Length, stream.Position + chunkLength);
            if (!id.Equals("MTrk", StringComparison.Ordinal)) { stream.Position = end; continue; }
            long tick = 0; var running = 0; var active = new Dictionary<(int channel, int pitch), Queue<(long tick, int velocity)>>();
            while (stream.Position < end)
            {
                tick += ReadVar(reader); var status = reader.ReadByte();
                if (status < 0x80) { stream.Position--; if (running == 0) throw new InvalidDataException("Invalid MIDI running status."); status = (byte)running; }
                else if (status < 0xF0) running = status;
                else running = 0;
                if (status == 0xFF)
                {
                    var type = reader.ReadByte(); var length = ReadVar(reader);
                    if (length < 0 || stream.Position + length > end) throw new InvalidDataException("A MIDI meta event extends past its track.");
                    if (type == 0x51 && length == 3) { var b = reader.ReadBytes(3); var micros = (b[0] << 16) | (b[1] << 8) | b[2]; if (micros > 0) tempos[tick] = micros; }
                    else if (type == 0x58 && length >= 2 && !timeSignatureFound)
                    {
                        var b = reader.ReadBytes((int)length); timeSignatureNumerator = Math.Max(1, (int)b[0]); timeSignatureDenominator = 1 << Math.Clamp((int)b[1], 0, 6); timeSignatureFound = true;
                    }
                    else if (type == 0x03 && length > 0 && !trackNames.ContainsKey(track))
                    {
                        var name = Encoding.Latin1.GetString(reader.ReadBytes((int)length)).Trim('\0', ' ');
                        if (name.Length > 0) trackNames[track] = name.Length > 40 ? name[..40] : name;
                    }
                    else stream.Position += length;
                    continue;
                }
                if (status is 0xF0 or 0xF7) { var length = ReadVar(reader); if (length < 0 || stream.Position + length > end) throw new InvalidDataException("A MIDI system exclusive event extends past its track."); stream.Position += length; continue; }
                if (status >= 0xF0) { stream.Position += status switch { 0xF1 or 0xF3 => 1, 0xF2 => 2, _ => 0 }; continue; }
                var kind = status & 0xF0; var channel = status & 15; int pitch = reader.ReadByte(); int velocity = kind is 0xC0 or 0xD0 ? 0 : reader.ReadByte();
                if (channel == PercussionChannel) continue; // General MIDI drums are not piano notes.
                if (kind == 0x90 && velocity > 0) { var key = (channel, pitch); if (!active.TryGetValue(key, out var queue)) active[key] = queue = new Queue<(long, int)>(); queue.Enqueue((tick, velocity)); }
                else if (kind == 0x80 || kind == 0x90 && velocity == 0)
                {
                    var key = (channel, pitch); if (active.TryGetValue(key, out var queue) && queue.Count > 0) { var on = queue.Dequeue(); raw.Add((on.tick, Math.Max(on.tick + 1, tick), pitch, on.velocity, track)); }
                }
            }
            // Notes that never received a Note Off end with the track instead of disappearing.
            foreach (var (key, queue) in active) while (queue.Count > 0) { var on = queue.Dequeue(); raw.Add((on.tick, Math.Max(on.tick + 1, tick), key.pitch, on.velocity, track)); }
            lastTick = Math.Max(lastTick, tick);
            stream.Position = end; track++;
        }

        // Tempo map as cumulative seconds so every tick lookup is a binary search instead of a scan.
        var points = tempos.ToArray();
        var tempoTicks = new long[points.Length]; var tempoMicros = new int[points.Length]; var tempoSeconds = new double[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            tempoTicks[i] = points[i].Key; tempoMicros[i] = points[i].Value;
            tempoSeconds[i] = i == 0 ? 0 : tempoSeconds[i - 1] + (tempoTicks[i] - tempoTicks[i - 1]) * (double)tempoMicros[i - 1] / division / 1_000_000;
        }
        double Seconds(long target)
        {
            var index = Array.BinarySearch(tempoTicks, target); if (index < 0) index = ~index - 1; if (index < 0) index = 0;
            return tempoSeconds[index] + (target - tempoTicks[index]) * (double)tempoMicros[index] / division / 1_000_000;
        }

        var notes = raw.Select(n => new NoteEvent { Pitch = n.pitch, Start = Seconds(n.start), Duration = Math.Max(.06, Seconds(n.end) - Seconds(n.start)), Velocity = n.velocity, Track = n.track }).OrderBy(n => n.Start).ToList();
        foreach (var n in raw) lastTick = Math.Max(lastTick, n.end);
        var beatTicks = Math.Max(1, division * 4 / timeSignatureDenominator);
        var beatCount = (int)Math.Min(MaxBeats, lastTick / beatTicks + 2);
        var beats = new List<double>(beatCount);
        for (var beat = 0; beat < beatCount; beat++) beats.Add(Seconds((long)beat * beatTicks));
        return new MidiSong { Notes = notes, BeatTimes = beats, BeatsPerBar = timeSignatureNumerator, TrackNames = trackNames };
    }

    private static string ChunkId(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) throw new InvalidDataException("The MIDI file is truncated.");
        return Encoding.ASCII.GetString(bytes);
    }
    private static long ReadVar(BinaryReader reader)
    {
        long value = 0; byte current; var bytes = 0;
        do
        {
            current = reader.ReadByte(); value = (value << 7) | (uint)(current & 127);
            if (++bytes > 4) throw new InvalidDataException("Invalid MIDI variable-length quantity.");
        } while ((current & 128) != 0);
        return value;
    }
    private static int Read16(BinaryReader reader) => (reader.ReadByte() << 8) | reader.ReadByte();
    private static int Read32(BinaryReader reader) => (reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
}
