using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PianoPath;

/// <summary>P0 input consumer shared by portable product-reader tests and Windows capture diagnostics.
/// Pedal events are explicit test input, not a claim that MidiSong now contains CC64.</summary>
internal sealed class PrismFlowBaseline
{
    internal sealed record InputNote(double Start, double Duration, int Pitch, int Velocity, int Channel);
    internal sealed record Control(double Time, int Channel, int Controller, int Value);
    internal sealed record Checkpoint(double Time, string Id);
    internal sealed record Document(int SchemaVersion, string Status, double DurationSeconds, int Ppq,
        int TempoMicroseconds, int RenderSeedProposal, InputNote[] Notes, Control[] Controls,
        Checkpoint[] Checkpoints, string MidiSha256);

    internal Document Manifest { get; }
    internal IReadOnlyList<NoteEvent> Notes { get; }

    private PrismFlowBaseline(Document document, IReadOnlyList<NoteEvent> notes)
    { Manifest = document; Notes = notes; }

    internal static PrismFlowBaseline Load(string directory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        var midiPath = Path.Combine(directory, "baseline.mid");
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing Prism Flow document.");
        if (document.SchemaVersion != 1 || document.Status != "input-fixture-not-renderer-evidence"
            || document.Ppq != 480 || document.TempoMicroseconds != 500000
            || !double.IsFinite(document.DurationSeconds) || document.DurationSeconds <= 0
            || document.Notes is not { Length: > 0 } || document.Controls is null
            || document.Checkpoints is not { Length: > 0 })
            throw new InvalidDataException("Unsupported Prism Flow fixture schema or timing.");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(midiPath)));
        if (!string.Equals(hash, document.MidiSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prism Flow MIDI checksum mismatch.");
        foreach (var note in document.Notes)
            if (!ValidTime(note.Start) || !double.IsFinite(note.Duration) || note.Duration <= 0
                || note.Start + note.Duration > document.DurationSeconds
                || note.Pitch is < 21 or > 108 || note.Velocity is < 1 or > 127 || note.Channel is < 0 or > 1)
                throw new InvalidDataException("Invalid Prism Flow note.");
        foreach (var control in document.Controls)
            if (!ValidTime(control.Time) || control.Channel != 0 || control.Controller != 64
                || control.Value is < 0 or > 127)
                throw new InvalidDataException("Only channel-zero CC64 is supported by this P0 consumer.");
        if (document.Checkpoints.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != document.Checkpoints.Length
            || document.Checkpoints.Any(c => !ValidTime(c.Time) || string.IsNullOrWhiteSpace(c.Id)
                || c.Id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')))
            throw new InvalidDataException("Invalid Prism Flow checkpoint.");

        // Exercise the shipping reader, not a second MIDI parser. Track zero is the conductor;
        // the two note channels are deliberately stored on tracks one and two in this fixture.
        var actual = MidiReader.ReadSong(midiPath).Notes;
        var expected = document.Notes.Select(n => new NoteEvent
        { Start = n.Start, Duration = n.Duration, Pitch = n.Pitch, Velocity = n.Velocity, Track = n.Channel + 1 });
        static IOrderedEnumerable<NoteEvent> Sort(IEnumerable<NoteEvent> notes) => notes
            .OrderBy(n => n.Start).ThenBy(n => n.Track).ThenBy(n => n.Pitch).ThenBy(n => n.Duration);
        var pairs = Sort(actual).Zip(Sort(expected)).ToArray();
        if (actual.Count != document.Notes.Length || pairs.Any(p => p.First.Pitch != p.Second.Pitch
            || p.First.Track != p.Second.Track || p.First.Velocity != p.Second.Velocity
            || Math.Abs(p.First.Start - p.Second.Start) > 1e-9 || Math.Abs(p.First.Duration - p.Second.Duration) > 1e-9))
            throw new InvalidDataException("Product MIDI reader disagrees with Prism Flow manifest.");
        return new PrismFlowBaseline(document, actual);

        bool ValidTime(double time) => double.IsFinite(time) && time >= 0 && time <= document.DurationSeconds;
    }

    internal Cursor CreateCursor() => new(this);

    /// <summary>Forward-only deterministic cursor; create a new cursor AND simulation for a replay/seek.
    /// Controls precede strikes at identical timestamps. Callbacks represent explicit fixture events.</summary>
    internal sealed class Cursor
    {
        private readonly (double Time, NoteEvent? Note, Control? Control)[] _events;
        private int _next;
        private double _position = -1;
        internal Cursor(PrismFlowBaseline scene)
        {
            _events = scene.Notes.Select(n => (Time: n.Start, Note: (NoteEvent?)n, Control: (Control?)null))
                .Concat(scene.Manifest.Controls.Select(c => (Time: c.Time, Note: (NoteEvent?)null, Control: (Control?)c)))
                .OrderBy(e => e.Time).ThenBy(e => e.Control is null ? 1 : 0).ToArray();
        }

        internal void AdvanceTo(double time, Action<NoteEvent> strike, Action<bool, double> pedal)
        {
            if (!double.IsFinite(time) || time < 0 || time < _position)
                throw new ArgumentOutOfRangeException(nameof(time), "Create a fresh cursor for backward seeks.");
            _position = time;
            while (_next < _events.Length && _events[_next].Time <= time)
            {
                var item = _events[_next++];
                if (item.Control is { } cc) pedal(cc.Value >= 64, cc.Value / 127.0);
                else strike(item.Note!);
            }
        }
    }
}
