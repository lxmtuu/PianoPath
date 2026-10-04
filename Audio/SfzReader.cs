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
    /// <summary>CC 64 window that fires this region as a one-shot (<c>on_locc64</c>/<c>on_hicc64</c>): pedal-down and pedal-up noises. −1 means not CC-triggered.</summary>
    public int OnLoCc64 = -1, OnHiCc64 = -1;
    /// <summary>Random-selection window (<c>lorand</c>/<c>hirand</c>) so repeated pedal presses alternate takes.</summary>
    public double LoRand, HiRand = 1;
    public bool IsCc64Trigger => OnLoCc64 >= 0;
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
        // Opcodes under headers this parser does not model (<curve>, <effect>, SFZ v2 <sample>…) land
        // here so they can never leak into the next real region or into the control settings.
        var discard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scope = control; var inRegion = false;
        var regions = new List<SfzRegion>();
        var cache = new Dictionary<string, WavData>(StringComparer.OrdinalIgnoreCase);

        void FinishRegion()
        {
            if (!inRegion) return;
            inRegion = false;
            // Each opcode remembers the header level it came from, because key= at a deeper level must
            // beat lokey/hikey/pitch_keycenter inherited from a shallower one (and only then).
            var merged = new Dictionary<string, (string Value, int Level)>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in global) merged[pair.Key] = (pair.Value, 0);
            foreach (var pair in master) merged[pair.Key] = (pair.Value, 1);
            foreach (var pair in group) merged[pair.Key] = (pair.Value, 2);
            foreach (var pair in region) merged[pair.Key] = (pair.Value, 3);
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
                    case "control": scope = control; break;
                    case "global": global.Clear(); master.Clear(); group.Clear(); scope = global; break;
                    case "master": master.Clear(); group.Clear(); scope = master; break;
                    case "group": group.Clear(); scope = group; break;
                    case "region": inRegion = true; scope = region; break;
                    default: discard.Clear(); scope = discard; break;
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
                // The spec requires macro names to start with $; replacing an unprefixed word would
                // rewrite matching letters inside ordinary opcodes, so such a define is ignored.
                if (parts.Length == 3 && parts[1].StartsWith('$'))
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

    private static SfzRegion BuildRegion(Dictionary<string, (string Value, int Level)> opcodes, Dictionary<string, string> control, string baseDirectory, Dictionary<string, WavData> cache)
    {
        string? Opt(string name) => opcodes.TryGetValue(name, out var entry) ? entry.Value : null;
        int LevelOf(string name) => opcodes.TryGetValue(name, out var entry) ? entry.Level : -1;
        if (Opt("sample") is not { Length: > 0 } sample)
            throw new InvalidDataException("An SFZ region has no sample= opcode.");
        var defaultPath = control.TryGetValue("default_path", out var dp) ? dp : "";
        var relative = (defaultPath + sample).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relative));
        if (!cache.TryGetValue(fullPath, out var audio))
        {
            if (!File.Exists(fullPath)) throw new FileNotFoundException($"SFZ sample not found: {relative}", fullPath);
            cache[fullPath] = audio = fullPath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
                ? FlacReader.Read(fullPath)
                : WavReader.Read(fullPath);
        }
        var region = new SfzRegion { Sample = audio, SamplePath = fullPath };
        // key= expands to lokey+hikey+pitch_keycenter, but only beats one of them when it sits at a
        // deeper header level: <region> key=60 overrides a group's window, a region's own lokey wins.
        var keyLevel = LevelOf("key"); var keyNote = keyLevel >= 0 ? ParseNote(Opt("key")!) : 0;
        int PickNote(string name, int fallback)
        {
            var explicitLevel = LevelOf(name);
            if (keyLevel > explicitLevel) return keyNote;
            return explicitLevel >= 0 ? ParseNote(Opt(name)!) : fallback;
        }
        region.LoKey = PickNote("lokey", 0);
        region.HiKey = PickNote("hikey", 127);
        region.Root = PickNote("pitch_keycenter", 60);
        if (Opt("lovel") is { } v) region.LoVel = ParseInt(v);
        if (Opt("hivel") is { } hivel) region.HiVel = ParseInt(hivel);
        if (Opt("transpose") is { } transpose) region.Transpose = ParseInt(transpose);
        if (Opt("tune") is { } tune) region.TuneCents = ParseDouble(tune);
        if (Opt("volume") is { } volume) region.VolumeDb = ParseDouble(volume);
        if (Opt("pan") is { } pan) region.Pan = Math.Clamp(ParseDouble(pan), -100, 100);
        if (Opt("amp_veltrack") is { } veltrack) region.AmpVeltrack = Math.Clamp(ParseDouble(veltrack) / 100, -1, 1);
        if (Opt("ampeg_attack") is { } attack) region.AttackSeconds = Math.Clamp(ParseDouble(attack), 0, 20);
        if (Opt("ampeg_decay") is { } decay) region.DecaySeconds = Math.Clamp(ParseDouble(decay), 0, 20);
        if (Opt("ampeg_sustain") is { } sustain) region.SustainLevel = Math.Clamp(ParseDouble(sustain) / 100, 0, 1);
        if (Opt("ampeg_release") is { } release) region.ReleaseSeconds = Math.Clamp(ParseDouble(release), .015, 20);
        if (Opt("trigger") is { } trigger) region.TriggerRelease = trigger is "release" or "release_key";
        if (Opt("rt_decay") is { } rtDecay) region.RtDecayDb = Math.Max(0, ParseDouble(rtDecay));
        if (Opt("seq_length") is { } seqLength) region.SeqLength = Math.Max(1, ParseInt(seqLength));
        if (Opt("seq_position") is { } seqPosition) region.SeqPosition = Math.Max(1, ParseInt(seqPosition));
        if (Opt("loop_mode") is { } loopMode) region.Loop = loopMode is "loop_continuous" or "loop_sustain";
        if (Opt("loop_start") is { } loopStart) region.LoopStart = ParseInt(loopStart);
        if (Opt("loop_end") is { } loopEnd) region.LoopEnd = ParseInt(loopEnd);
        if (Opt("offset") is { } offset) region.Offset = Math.Max(0, ParseInt(offset));
        var onLo = Opt("on_locc64"); var onHi = Opt("on_hicc64");
        if (onLo is not null || onHi is not null)
        {
            region.OnLoCc64 = Math.Clamp(onLo is null ? 0 : ParseInt(onLo), 0, 127);
            region.OnHiCc64 = Math.Clamp(onHi is null ? 127 : ParseInt(onHi), region.OnLoCc64, 127);
        }
        if (Opt("lorand") is { } loRand) region.LoRand = Math.Clamp(ParseDouble(loRand), 0, 1);
        if (Opt("hirand") is { } hiRand) region.HiRand = Math.Clamp(ParseDouble(hiRand), 0, 1);
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
