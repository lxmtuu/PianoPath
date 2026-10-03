using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PianoPath;

/// <summary>One SFZ region: a sample plus the key/velocity window and playback parameters that pick it.</summary>
internal sealed class SfzRegion
{
    public required WavData Sample { get; init; }
    /// <summary>The sample path the region came from, for diagnostics and round-robin identity checks.</summary>
    public required string SamplePath { get; init; }
    public int LoKey = 0, HiKey = 127, LoVel = 0, HiVel = 127;
    /// <summary>MIDI note the sample was recorded at (<c>pitch_keycenter</c>); playback transposes from here.</summary>
    public int Root = 60;
    public int Transpose; public double TuneCents;
    public double VolumeDb, Pan; // pan is -100 (left) .. 100 (right)
    /// <summary>How much velocity drives loudness, 0..1 (<c>amp_veltrack</c> / 100; the spec default tracks fully).</summary>
    public double AmpVeltrack = 1;
    public double AttackSeconds = .001, DecaySeconds = .001, SustainLevel = 1;
    /// <summary>A piano-friendly default: the SFZ spec's instant release clicks on sets that omit <c>ampeg_release</c>.</summary>
    public double ReleaseSeconds = .3;
    /// <summary>True for <c>trigger=release</c> regions — damper-fall noise played when a note is let go.</summary>
    public bool TriggerRelease;
    /// <summary>dB of release-sample attenuation per second the note was held (<c>rt_decay</c>).</summary>
    public double RtDecayDb;
    /// <summary>Round-robin: this region answers when the per-key counter is at <see cref="SeqPosition"/> of <see cref="SeqLength"/>.</summary>
    public int SeqLength = 1, SeqPosition = 1;
    public bool Loop; public int LoopStart, LoopEnd;
    /// <summary>Frames skipped at the start of the sample (<c>offset</c>).</summary>
    public int Offset;
}

/// <summary>A parsed SFZ instrument: the flattened region list and the control-section keyboard shift.</summary>
internal sealed class SfzInstrumentData
{
    public required string Name { get; init; }
    public required IReadOnlyList<SfzRegion> Regions { get; init; }
    /// <summary>ARIA <c>note_offset</c>: added to incoming MIDI notes before regions are matched.</summary>
    public int NoteOffset { get; init; }
}

/// <summary>
/// Reads SFZ instrument definitions — the open sample-mapping format the big free piano libraries ship
/// in (Ivy Audio's Steinway, Salamander, …). The parser covers the subset those sets rely on:
/// <c>&lt;control&gt;/&lt;global&gt;/&lt;master&gt;/&lt;group&gt;/&lt;region&gt;</c> headers with opcode
/// inheritance, <c>#define</c> substitution and <c>#include</c>, <c>default_path</c>, key/velocity
/// windows, velocity layers, round-robins, release samples with <c>rt_decay</c>, loops, tuning and the
/// amp envelope. Unknown opcodes are ignored on purpose: ARIA-flavoured files are full of player-side
/// extensions (curves, CC labels) that do not change which sample sounds for which key. Sample audio is
/// decoded through <see cref="WavReader"/> and shared when several regions reference one file.
/// </summary>
internal static class SfzReader
{
    public static SfzInstrumentData Read(string path)
    {
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var defines = new List<KeyValuePair<string, string>>();
        var text = new StringBuilder();
        Preprocess(Path.GetFullPath(path), defines, text, depth: 0);
        var tokens = Tokenize(text.ToString());

        var control = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var global = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var master = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var group = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var region = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scope = control; var inRegion = false;
        var regions = new List<SfzRegion>();
        var cache = new Dictionary<string, WavData>(StringComparer.OrdinalIgnoreCase);

        void FinishRegion()
        {
            if (!inRegion) return;
            inRegion = false;
            var merged = new Dictionary<string, string>(global, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in master) merged[pair.Key] = pair.Value;
            foreach (var pair in group) merged[pair.Key] = pair.Value;
            foreach (var pair in region) merged[pair.Key] = pair.Value;
            regions.Add(BuildRegion(merged, control, baseDirectory, cache));
            region.Clear();
        }

        foreach (var (isHeader, key, value) in tokens)
        {
            if (isHeader)
            {
                FinishRegion();
                switch (key)
                {
                    case "control": case "curve": case "effect": scope = control; break;
                    case "global": global.Clear(); master.Clear(); group.Clear(); scope = global; break;
                    case "master": master.Clear(); group.Clear(); scope = master; break;
                    case "group": group.Clear(); scope = group; break;
                    case "region": inRegion = true; scope = region; break;
                    default: scope = region; break; // unknown header: swallow its opcodes harmlessly
                }
            }
            else scope[key] = value;
        }
        FinishRegion();
        if (regions.Count == 0) throw new InvalidDataException("This SFZ file defines no playable regions.");
        return new SfzInstrumentData
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Regions = regions,
            NoteOffset = control.TryGetValue("note_offset", out var offset) ? ParseInt(offset) : 0
        };
    }

    /// <summary>Resolves comments, <c>#define</c> and <c>#include</c> into one flat opcode stream.</summary>
    private static void Preprocess(string path, List<KeyValuePair<string, string>> defines, StringBuilder output, int depth)
    {
        if (depth > 16) throw new InvalidDataException("SFZ #include files nest too deeply (circular include?).");
        var directory = Path.GetDirectoryName(path) ?? ".";
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#define", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    defines.RemoveAll(pair => pair.Key == parts[1]);
                    defines.Add(new KeyValuePair<string, string>(parts[1], parts[2]));
                    // Longest name first, so $SUS_PED is never half-replaced by an earlier $SUS.
                    defines.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
                }
                continue;
            }
            if (line.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
            {
                var first = line.IndexOf('"'); var last = line.LastIndexOf('"');
                if (first >= 0 && last > first)
                {
                    var include = line[(first + 1)..last].Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    Preprocess(Path.Combine(directory, include), defines, output, depth + 1);
                }
                continue;
            }
            foreach (var pair in defines) line = line.Replace(pair.Key, pair.Value);
            output.Append(line).Append('\n');
        }
    }

    /// <summary>
    /// Splits the stream into headers and <c>opcode=value</c> pairs. Values may contain spaces (sample
    /// paths usually do), so a value runs until the next word that is itself a header or an opcode.
    /// </summary>
    private static List<(bool IsHeader, string Key, string Value)> Tokenize(string text)
    {
        var tokens = new List<(bool, string, string)>();
        var pendingKey = ""; var pendingValue = new StringBuilder();
        void FlushPending()
        {
            if (pendingKey.Length == 0) return;
            tokens.Add((false, pendingKey, pendingValue.ToString().Trim()));
            pendingKey = ""; pendingValue.Clear();
        }
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith('<') && word.EndsWith('>'))
            {
                FlushPending();
                tokens.Add((true, word[1..^1].ToLowerInvariant(), ""));
                continue;
            }
            var equals = word.IndexOf('=');
            if (equals > 0)
            {
                FlushPending();
                pendingKey = word[..equals].ToLowerInvariant();
                pendingValue.Append(word[(equals + 1)..]);
            }
            else if (pendingKey.Length > 0) pendingValue.Append(' ').Append(word);
        }
        FlushPending();
        return tokens;
    }

    private static SfzRegion BuildRegion(Dictionary<string, string> opcodes, Dictionary<string, string> control, string baseDirectory, Dictionary<string, WavData> cache)
    {
        if (!opcodes.TryGetValue("sample", out var sample) || sample.Length == 0)
            throw new InvalidDataException("An SFZ region has no sample= opcode.");
        var defaultPath = control.TryGetValue("default_path", out var dp) ? dp : "";
        var relative = (defaultPath + sample).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relative));
        if (!cache.TryGetValue(fullPath, out var audio))
        {
            if (!File.Exists(fullPath)) throw new FileNotFoundException($"SFZ sample not found: {relative}", fullPath);
            cache[fullPath] = audio = WavReader.Read(fullPath);
        }
        var region = new SfzRegion { Sample = audio, SamplePath = fullPath };
        // key= sets the window and the root in one stroke; explicit opcodes afterwards refine it.
        if (opcodes.TryGetValue("key", out var key)) { var k = ParseNote(key); region.LoKey = region.HiKey = region.Root = k; }
        if (opcodes.TryGetValue("lokey", out var v)) region.LoKey = ParseNote(v);
        if (opcodes.TryGetValue("hikey", out v)) region.HiKey = ParseNote(v);
        if (opcodes.TryGetValue("pitch_keycenter", out v)) region.Root = ParseNote(v);
        if (opcodes.TryGetValue("lovel", out v)) region.LoVel = ParseInt(v);
        if (opcodes.TryGetValue("hivel", out v)) region.HiVel = ParseInt(v);
        if (opcodes.TryGetValue("transpose", out v)) region.Transpose = ParseInt(v);
        if (opcodes.TryGetValue("tune", out v)) region.TuneCents = ParseDouble(v);
        if (opcodes.TryGetValue("volume", out v)) region.VolumeDb = ParseDouble(v);
        if (opcodes.TryGetValue("pan", out v)) region.Pan = Math.Clamp(ParseDouble(v), -100, 100);
        if (opcodes.TryGetValue("amp_veltrack", out v)) region.AmpVeltrack = Math.Clamp(ParseDouble(v) / 100, -1, 1);
        if (opcodes.TryGetValue("ampeg_attack", out v)) region.AttackSeconds = Math.Clamp(ParseDouble(v), 0, 20);
        if (opcodes.TryGetValue("ampeg_decay", out v)) region.DecaySeconds = Math.Clamp(ParseDouble(v), 0, 20);
        if (opcodes.TryGetValue("ampeg_sustain", out v)) region.SustainLevel = Math.Clamp(ParseDouble(v) / 100, 0, 1);
        if (opcodes.TryGetValue("ampeg_release", out v)) region.ReleaseSeconds = Math.Clamp(ParseDouble(v), .015, 20);
        if (opcodes.TryGetValue("trigger", out v)) region.TriggerRelease = v is "release" or "release_key";
        if (opcodes.TryGetValue("rt_decay", out v)) region.RtDecayDb = Math.Max(0, ParseDouble(v));
        if (opcodes.TryGetValue("seq_length", out v)) region.SeqLength = Math.Max(1, ParseInt(v));
        if (opcodes.TryGetValue("seq_position", out v)) region.SeqPosition = Math.Max(1, ParseInt(v));
        if (opcodes.TryGetValue("loop_mode", out v)) region.Loop = v is "loop_continuous" or "loop_sustain";
        if (opcodes.TryGetValue("loop_start", out v)) region.LoopStart = ParseInt(v);
        if (opcodes.TryGetValue("loop_end", out v)) region.LoopEnd = ParseInt(v);
        if (opcodes.TryGetValue("offset", out v)) region.Offset = Math.Max(0, ParseInt(v));
        if (region.LoopEnd <= region.LoopStart) region.Loop = false;
        if (region.LoopEnd >= audio.Frames) region.LoopEnd = Math.Max(0, audio.Frames - 1);
        return region;
    }

    private static int ParseInt(string value) => (int)Math.Round(ParseDouble(value));
    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    /// <summary>MIDI note numbers or names: 60, c4, C#4, Db5, a0 — sample sets use both spellings.</summary>
    private static int ParseNote(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return Math.Clamp(number, 0, 127);
        var text = value.Trim().ToLowerInvariant();
        if (text.Length < 2) return 60;
        var semitone = text[0] switch { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, 'b' => 11, _ => -1 };
        if (semitone < 0) return 60;
        var index = 1;
        if (index < text.Length && (text[index] == '#' || text[index] == 's')) { semitone++; index++; }
        else if (index < text.Length && text[index] == 'b') { semitone--; index++; }
        if (!int.TryParse(text[index..], NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var octave)) return 60;
        return Math.Clamp((octave + 1) * 12 + semitone, 0, 127);
    }
}
