using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PianoPath;

internal sealed record SoundFontPreset(int Bank, int Program, string Name, IReadOnlyList<SoundFontRegion> Regions);

internal sealed class SoundFontRegion
{
    public required int[] LeftSamples { get; init; }
    /// <summary>Linked partner sample (start, end, loop start, loop end) used only when the SoundFont omits the partner's own zone.</summary>
    public int[]? RightSamples { get; init; }
    /// <summary>True when this zone holds the right channel of a pair, so the linked sample feeds the left output instead.</summary>
    public bool StereoSwapped { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
    public required int LoopStart { get; init; }
    public required int LoopEnd { get; init; }
    public required int SampleRate { get; init; }
    public required int RootKey { get; init; }
    public required int KeyLow { get; init; }
    public required int KeyHigh { get; init; }
    public required int VelocityLow { get; init; }
    public required int VelocityHigh { get; init; }
    public required int SampleMode { get; init; }
    public required int FineTuneCents { get; init; }
    public required int ScaleTuning { get; init; }
    public required int Pan { get; init; }
    public required double Attenuation { get; init; }
    public required double AttackSeconds { get; init; }
    public required double DecaySeconds { get; init; }
    public required double SustainLevel { get; init; }
    public required double ReleaseSeconds { get; init; }
}

internal sealed class SoundFontData
{
    public required string Name { get; init; }
    public required byte[] FileData { get; init; }
    public required int SampleDataOffset { get; init; }
    public required int SampleCount { get; init; }
    public required IReadOnlyList<SoundFontPreset> Presets { get; init; }

    public short ReadSample(int index)
    {
        if ((uint)index >= (uint)SampleCount) return 0;
        return BinaryPrimitives.ReadInt16LittleEndian(FileData.AsSpan(SampleDataOffset + index * 2, 2));
    }
}

/// <summary>Reads the RIFF/pdta/sdta portion of uncompressed SoundFont 2 files.</summary>
internal static class SoundFontReader
{
    private readonly record struct Chunk(int Offset, int Length);
    private readonly record struct PresetHeader(string Name, int Program, int Bank, int Bag);
    private readonly record struct InstrumentHeader(string Name, int Bag);
    private readonly record struct Bag(int Generator);
    private readonly record struct SampleHeader(string Name, int Start, int End, int LoopStart, int LoopEnd, int Rate, int Root, int Correction, int Link, int Type);
    private sealed class GeneratorSet
    {
        /// <summary>Generators that only make sense inside an instrument zone; the SF2 specification says presets must not contribute them.</summary>
        private static readonly HashSet<int> InstrumentOnly = [0, 1, 2, 3, 4, 12, 41, 45, 46, 47, 50, 53, 54, 57, 58];
        /// <summary>Specification defaults for generators whose neutral value is not zero, used when a preset offsets a generator the instrument left unset.</summary>
        private static readonly Dictionary<int, int> Defaults = new() { [8] = 13500, [33] = -12000, [34] = -12000, [35] = -12000, [36] = -12000, [38] = -12000, [25] = -12000, [26] = -12000, [27] = -12000, [28] = -12000, [30] = -12000, [56] = 100, [58] = -1 };
        public readonly Dictionary<int, int> Values = [];
        public (int Low, int High) Keys = (0, 127);
        public (int Low, int High) Velocities = (0, 127);
        public bool HasKeys, HasVelocities;
        public int Get(int op, int fallback = 0) => Values.TryGetValue(op, out var value) ? value : fallback;
        /// <summary>Local zone semantics: a generator in <paramref name="other"/> replaces the same generator here (global → local within one level).</summary>
        public void Override(GeneratorSet other)
        {
            foreach (var pair in other.Values) Values[pair.Key] = pair.Value;
            if (other.HasKeys) { Keys = other.Keys; HasKeys = true; }
            if (other.HasVelocities) { Velocities = other.Velocities; HasVelocities = true; }
        }
        /// <summary>Preset-level semantics: values are added to the instrument result (or to the generator default) and ranges are intersected.</summary>
        public void AddPresetLevel(GeneratorSet preset)
        {
            foreach (var pair in preset.Values)
            {
                if (InstrumentOnly.Contains(pair.Key)) continue;
                var current = Values.TryGetValue(pair.Key, out var existing) ? existing : Defaults.GetValueOrDefault(pair.Key);
                Values[pair.Key] = current + pair.Value;
            }
            if (preset.HasKeys) { Keys = HasKeys ? (Math.Max(Keys.Low, preset.Keys.Low), Math.Min(Keys.High, preset.Keys.High)) : preset.Keys; HasKeys = true; }
            if (preset.HasVelocities) { Velocities = HasVelocities ? (Math.Max(Velocities.Low, preset.Velocities.Low), Math.Min(Velocities.High, preset.Velocities.High)) : preset.Velocities; HasVelocities = true; }
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> exists but is still the ~134-byte Git LFS pointer text file
    /// instead of the real SoundFont, which is what a clone without <c>git lfs pull</c> (or a CI checkout
    /// with <c>lfs: false</c>) leaves behind. A missing file is not a pointer and returns false.
    /// </summary>
    internal static bool IsLfsPointer(string path)
    {
        if (!File.Exists(path)) return false;
        var length = new FileInfo(path).Length;
        if (length == 0) return false;
        if (length >= 1024 * 1024) return false; // a real bank is far larger than any pointer file
        var head = new byte[(int)Math.Min(48, length)];
        using var stream = File.OpenRead(path);
        var read = stream.Read(head);
        return Encoding.ASCII.GetString(head, 0, read).StartsWith("version https://git-lfs.github.com/spec", StringComparison.Ordinal);
    }

    public static SoundFontData Read(string path)
    {
        var file = File.ReadAllBytes(path);
        var span = file.AsSpan();
        if (span.Length < 12 || Ascii(span[..4]) != "RIFF" || Ascii(span.Slice(8, 4)) != "sfbk") throw new InvalidDataException("This is not an uncompressed SoundFont 2 (.sf2) file.");
        var riffEnd = Math.Min(span.Length, checked((int)U32(span, 4) + 8));
        if (riffEnd < 12) throw new InvalidDataException("The SoundFont header is truncated.");
        var root = Children(span, 12, riffEnd);
        var info = List(span, root, "INFO"); var sdta = List(span, root, "sdta"); var pdta = List(span, root, "pdta");
        var name = ReadInfoString(span, Children(span, info.Offset, info.Offset + info.Length), "INAM");
        var sampleChunk = Find(Children(span, sdta.Offset, sdta.Offset + sdta.Length), "smpl");
        if (sampleChunk.Length < 96 || sampleChunk.Length % 2 != 0) throw new InvalidDataException("The SoundFont has no usable 16-bit sample data.");
        var sampleCount = sampleChunk.Length / 2;

        var pdtaChunks = Children(span, pdta.Offset, pdta.Offset + pdta.Length);
        var presetHeaders = ReadPresetHeaders(span, Find(pdtaChunks, "phdr"));
        var presetBags = ReadBags(span, Find(pdtaChunks, "pbag"));
        var presetGens = ReadGenerators(span, Find(pdtaChunks, "pgen"));
        var instrumentHeaders = ReadInstrumentHeaders(span, Find(pdtaChunks, "inst"));
        var instrumentBags = ReadBags(span, Find(pdtaChunks, "ibag"));
        var instrumentGens = ReadGenerators(span, Find(pdtaChunks, "igen"));
        var samples = ReadSampleHeaders(span, Find(pdtaChunks, "shdr"));
        var presets = new List<SoundFontPreset>();

        for (var p = 0; p + 1 < presetHeaders.Count; p++)
        {
            var header = presetHeaders[p]; var headerEnd = presetHeaders[p + 1];
            var pZones = ReadZones(presetBags, presetGens, header.Bag, headerEnd.Bag);
            // Only a leading zone without an instrument generator is a global zone (SF2 §7.3); later ones are ignored.
            var pGlobal = pZones.Count > 0 && !pZones[0].Values.ContainsKey(41) ? pZones[0] : new GeneratorSet();
            var regions = new List<SoundFontRegion>();
            foreach (var pZone in pZones)
            {
                if (!pZone.Values.TryGetValue(41, out var instrumentIndex)) continue;
                var presetGenerators = new GeneratorSet(); presetGenerators.Override(pGlobal); presetGenerators.Override(pZone);
                if (instrumentIndex < 0 || instrumentIndex >= instrumentHeaders.Count - 1) continue;
                var instrument = instrumentHeaders[instrumentIndex]; var instrumentEnd = instrumentHeaders[instrumentIndex + 1];
                var iZones = ReadZones(instrumentBags, instrumentGens, instrument.Bag, instrumentEnd.Bag);
                var iGlobal = iZones.Count > 0 && !iZones[0].Values.ContainsKey(53) ? iZones[0] : new GeneratorSet();
                // Samples that already have their own zone in this instrument must not be mixed in a second time through sample links.
                var zoneSamples = new HashSet<int>();
                foreach (var zone in iZones) if (zone.Values.TryGetValue(53, out var used)) zoneSamples.Add(used);
                foreach (var iZone in iZones)
                {
                    if (!iZone.Values.TryGetValue(53, out var sampleIndex) || sampleIndex < 0 || sampleIndex >= samples.Count - 1) continue;
                    // Instrument: local generators replace global ones. Preset: values are relative offsets added on top.
                    var combined = new GeneratorSet(); combined.Override(iGlobal); combined.Override(iZone); combined.AddPresetLevel(presetGenerators);
                    var range = samples[sampleIndex];
                    var start = range.Start + combined.Get(0) + combined.Get(4) * 32768;
                    var end = range.End + combined.Get(1) + combined.Get(12) * 32768;
                    var loopStart = range.LoopStart + combined.Get(2) + combined.Get(45) * 32768;
                    var loopEnd = range.LoopEnd + combined.Get(3) + combined.Get(50) * 32768;
                    if (start < 0 || end > sampleCount - 46 || end - start < 8 || range.Rate is < 4000 or > 192000) continue;
                    start = Math.Clamp(start, 0, sampleCount - 1); end = Math.Clamp(end, start + 8, sampleCount - 46);
                    loopStart = Math.Clamp(loopStart, start, end - 1); loopEnd = Math.Clamp(loopEnd, loopStart + 1, end);
                    var sampleMode = combined.Get(54) & 3;
                    // A stereo pair is normally two mono zones (left + right) that are panned apart, so each zone plays on its own.
                    // Only when the partner sample has no zone of its own do we render both channels from this one zone.
                    int[]? rightSamples = null; var stereoSwapped = false; var sampleType = range.Type & 0x7fff;
                    if (sampleType is 2 or 4 && range.Link >= 0 && range.Link < samples.Count - 1 && !zoneSamples.Contains(range.Link))
                    {
                        var partner = samples[range.Link];
                        if (partner.End <= sampleCount - 46 && partner.End > partner.Start && partner.Start >= 0)
                        {
                            rightSamples = [partner.Start, partner.End, partner.LoopStart, partner.LoopEnd];
                            stereoSwapped = sampleType == 2;
                        }
                    }
                    var rootKey = combined.Get(58, range.Root); if (rootKey is < 0 or > 127) rootKey = range.Root;
                    var keyRange = combined.HasKeys ? combined.Keys : (0, 127); var velocityRange = combined.HasVelocities ? combined.Velocities : (0, 127);
                    if (keyRange.Item1 > keyRange.Item2 || velocityRange.Item1 > velocityRange.Item2) continue;
                    var sustainCb = Math.Clamp(combined.Get(37), 0, 1440);
                    regions.Add(new SoundFontRegion
                    {
                        LeftSamples = [start, end, loopStart, loopEnd], RightSamples = rightSamples, StereoSwapped = stereoSwapped,
                        Start = start, End = end, LoopStart = loopStart, LoopEnd = loopEnd, SampleRate = range.Rate, RootKey = rootKey,
                        KeyLow = Math.Clamp(keyRange.Item1, 0, 127), KeyHigh = Math.Clamp(keyRange.Item2, 0, 127),
                        VelocityLow = Math.Clamp(velocityRange.Item1, 0, 127), VelocityHigh = Math.Clamp(velocityRange.Item2, 0, 127),
                        SampleMode = sampleMode, FineTuneCents = range.Correction + combined.Get(51) * 100 + combined.Get(52),
                        ScaleTuning = combined.Get(56, 100), Pan = rightSamples is null ? Math.Clamp(combined.Get(17), -500, 500) : 0,
                        Attenuation = Math.Pow(10, -Math.Clamp(combined.Get(48), 0, 1440) / 200.0),
                        AttackSeconds = Timecents(combined.Get(34, -12000)), DecaySeconds = Timecents(combined.Get(36, -12000)),
                        SustainLevel = Math.Pow(10, -sustainCb / 200.0), ReleaseSeconds = Math.Clamp(Timecents(combined.Get(38, -7200)), .015, 8)
                    });
                }
            }
            if (regions.Count > 0) presets.Add(new SoundFontPreset(header.Bank, header.Program, header.Name, regions));
        }
        if (presets.Count == 0) throw new InvalidDataException("The SoundFont contains no usable preset/sample zones.");
        return new SoundFontData { Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name, FileData = file, SampleDataOffset = sampleChunk.Offset, SampleCount = sampleCount, Presets = presets };
    }

    private static double Timecents(int amount) => Math.Clamp(Math.Pow(2, Math.Clamp(amount, -12000, 8000) / 1200.0), .001, 20);
    private static string ReadInfoString(ReadOnlySpan<byte> file, IReadOnlyDictionary<string, Chunk> chunks, string id) => chunks.TryGetValue(id, out var chunk) ? Encoding.ASCII.GetString(file.Slice(chunk.Offset, chunk.Length)).TrimEnd('\0', ' ', '\r', '\n') : string.Empty;
    private static List<PresetHeader> ReadPresetHeaders(ReadOnlySpan<byte> file, Chunk chunk)
    {
        RequireStride(chunk, 38, "phdr"); var result = new List<PresetHeader>();
        for (var o = chunk.Offset; o < chunk.Offset + chunk.Length; o += 38) result.Add(new PresetHeader(ReadName(file.Slice(o, 20)), U16(file, o + 20), U16(file, o + 22), U16(file, o + 24)));
        return result;
    }
    private static List<InstrumentHeader> ReadInstrumentHeaders(ReadOnlySpan<byte> file, Chunk chunk)
    {
        RequireStride(chunk, 22, "inst"); var result = new List<InstrumentHeader>();
        for (var o = chunk.Offset; o < chunk.Offset + chunk.Length; o += 22) result.Add(new InstrumentHeader(ReadName(file.Slice(o, 20)), U16(file, o + 20)));
        return result;
    }
    private static List<Bag> ReadBags(ReadOnlySpan<byte> file, Chunk chunk)
    {
        RequireStride(chunk, 4, "bag"); var result = new List<Bag>();
        for (var o = chunk.Offset; o < chunk.Offset + chunk.Length; o += 4) result.Add(new Bag(U16(file, o)));
        return result;
    }
    private static List<GeneratorSet> ReadGenerators(ReadOnlySpan<byte> file, Chunk chunk)
    {
        RequireStride(chunk, 4, "gen"); var result = new List<GeneratorSet>();
        for (var o = chunk.Offset; o < chunk.Offset + chunk.Length; o += 4)
        {
            var op = U16(file, o); var amount = U16(file, o + 2); var set = new GeneratorSet();
            if (op == 43) { set.Keys = (amount & 255, amount >> 8); set.HasKeys = true; }
            else if (op == 44) { set.Velocities = (amount & 255, amount >> 8); set.HasVelocities = true; }
            else if (op is not 0 and not 41 and not 53) set.Values[op] = unchecked((short)amount);
            else set.Values[op] = amount;
            result.Add(set);
        }
        return result;
    }
    private static List<SampleHeader> ReadSampleHeaders(ReadOnlySpan<byte> file, Chunk chunk)
    {
        RequireStride(chunk, 46, "shdr"); var result = new List<SampleHeader>();
        for (var o = chunk.Offset; o < chunk.Offset + chunk.Length; o += 46) result.Add(new SampleHeader(ReadName(file.Slice(o, 20)), checked((int)U32(file, o + 20)), checked((int)U32(file, o + 24)), checked((int)U32(file, o + 28)), checked((int)U32(file, o + 32)), checked((int)U32(file, o + 36)), file[o + 40], unchecked((sbyte)file[o + 41]), U16(file, o + 42), U16(file, o + 44)));
        return result;
    }
    private static List<GeneratorSet> ReadZones(IReadOnlyList<Bag> bags, IReadOnlyList<GeneratorSet> generators, int first, int afterLast)
    {
        if (first < 0 || afterLast < first || afterLast >= bags.Count) throw new InvalidDataException("SoundFont zone table contains an invalid bag index.");
        var result = new List<GeneratorSet>();
        for (var i = first; i < afterLast; i++)
        {
            var start = bags[i].Generator; var end = bags[i + 1].Generator;
            if (start < 0 || end < start || end > generators.Count) throw new InvalidDataException("SoundFont generator table contains an invalid zone.");
            var zone = new GeneratorSet(); for (var g = start; g < end; g++) zone.Override(generators[g]); result.Add(zone);
        }
        return result;
    }
    private static Dictionary<string, Chunk> Children(ReadOnlySpan<byte> file, int start, int end)
    {
        var chunks = new Dictionary<string, Chunk>(); var p = start;
        while (p + 8 <= end)
        {
            var id = Ascii(file.Slice(p, 4)); var size = checked((int)U32(file, p + 4)); var data = p + 8;
            if (size < 0 || data + size > end) throw new InvalidDataException($"SoundFont chunk '{id}' extends past the file boundary.");
            if (id == "LIST")
            {
                if (size < 4) throw new InvalidDataException("A SoundFont LIST chunk is missing its type.");
                var listType = Ascii(file.Slice(data, 4)); chunks[listType] = new Chunk(data + 4, size - 4);
            }
            else chunks[id] = new Chunk(data, size);
            p = data + size + (size & 1);
        }
        if (p != end) throw new InvalidDataException("SoundFont chunk alignment is invalid.");
        return chunks;
    }
    private static Chunk List(ReadOnlySpan<byte> file, IReadOnlyDictionary<string, Chunk> chunks, string id)
    {
        if (!chunks.TryGetValue(id, out var chunk)) throw new InvalidDataException($"SoundFont is missing the {id} list.");
        _ = Children(file, chunk.Offset, chunk.Offset + chunk.Length); return chunk;
    }
    private static Chunk Find(IReadOnlyDictionary<string, Chunk> chunks, string id) => chunks.TryGetValue(id, out var chunk) ? chunk : throw new InvalidDataException($"SoundFont is missing the '{id}' chunk.");
    private static void RequireStride(Chunk chunk, int size, string name) { if (chunk.Length < size * 2 || chunk.Length % size != 0) throw new InvalidDataException($"SoundFont {name} table has an invalid size."); }
    private static string Ascii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes);
    private static string ReadName(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    private static ushort U16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(o, 2));
    private static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(o, 4));
}

/// <summary>Small polyphonic SF2 voice renderer. The synthesizer is only advanced while a SoundFont is loaded.</summary>
internal sealed class SoundFontSynthesizer
{
    private readonly object _gate = new();
    private readonly SoundFontData _font;
    private readonly int _sampleRate;
    private readonly SoundFontPreset?[] _channelPresets = new SoundFontPreset?[16];
    private readonly int[] _bank = new int[16];
    private readonly bool[] _sustain = new bool[16];
    private readonly bool[] _sostenuto = new bool[16];
    private readonly bool[] _soft = new bool[16];
    private readonly List<Voice> _voices = [];
    private double[] _mixLeft = new double[512];
    private double[] _mixRight = new double[512];
    private const int VoiceLimit = 96;
    private sealed class Voice
    {
        public required int Channel, Pitch;
        public required SoundFontRegion Region;
        public required double Position, Step, Gain, PanLeft, PanRight;
        public required double Attack, Decay, Sustain, Release;
        public required bool Loop;
        /// <summary>SF2 sample mode 3: loop only while the note is held, then play the remainder of the sample.</summary>
        public bool LoopUntilRelease;
        public double Age, ReleaseAge = -1, ReleaseLevel;
        public bool KeyHeld = true, SustainHeld, SostenutoHeld;
        public double FilterLeft, FilterRight;
        public double Envelope()
        {
            if (ReleaseAge >= 0) return ReleaseLevel * Math.Exp(-ReleaseAge * 7.0 / Release);
            if (Attack > 0 && Age < Attack) return Age / Attack;
            var decayAge = Age - Attack;
            if (Decay > 0 && decayAge < Decay) return 1 + (Sustain - 1) * decayAge / Decay;
            return Sustain;
        }
        public void ReleaseKey() { if (ReleaseAge >= 0) return; ReleaseLevel = Math.Max(.01, Envelope()); ReleaseAge = 0; }
    }

    public SoundFontSynthesizer(SoundFontData font, int sampleRate = 44100)
    {
        _font = font; _sampleRate = sampleRate;
        var defaultPreset = font.Presets.FirstOrDefault(x => x.Bank == 0 && x.Program == 0) ?? font.Presets[0];
        for (var i = 0; i < 16; i++) _channelPresets[i] = defaultPreset;
    }
    public SoundFontData Font => _font;
    public int ActiveVoiceCount { get { lock (_gate) return _voices.Count; } }
    public void SelectPreset(int bank, int program)
    {
        lock (_gate)
        {
            var preset = _font.Presets.FirstOrDefault(x => x.Bank == bank && x.Program == program)
                ?? _font.Presets.FirstOrDefault(x => x.Program == program) ?? _font.Presets[0];
            for (var c = 0; c < 16; c++) _channelPresets[c] = preset;
        }
    }
    public void ProcessMidi(int channel, int command, int data1, int data2)
    {
        channel &= 15; data1 &= 127; data2 &= 127;
        switch (command & 0xF0)
        {
            case 0x80: NoteOff(channel, data1); break;
            case 0x90: if (data2 == 0) NoteOff(channel, data1); else NoteOn(channel, data1, data2); break;
            case 0xB0:
                lock (_gate)
                {
                    if (data1 == 0) _bank[channel] = data2 << 7;
                    else if (data1 == 32) _bank[channel] = (_bank[channel] & 0x3f80) | data2;
                    else if (data1 == 67) _soft[channel] = data2 >= 64;
                }
                if (data1 == 64) SetSustain(channel, data2 >= 64);
                else if (data1 == 66) SetSostenuto(channel, data2 >= 64);
                else if (data1 == 120 || data1 == 123) AllNotesOff(channel);
                break;
            case 0xC0:
                lock (_gate) { _channelPresets[channel] = _font.Presets.FirstOrDefault(x => x.Bank == _bank[channel] && x.Program == data1) ?? _font.Presets.FirstOrDefault(x => x.Program == data1) ?? _font.Presets[0]; }
                break;
        }
    }
    public void NoteOn(int channel, int pitch, int velocity)
    {
        if (velocity <= 0) { NoteOff(channel, pitch); return; }
        lock (_gate)
        {
            var preset = _channelPresets[channel & 15]; if (preset is null) return;
            foreach (var region in preset.Regions)
            {
                if (pitch < region.KeyLow || pitch > region.KeyHigh || velocity < region.VelocityLow || velocity > region.VelocityHigh) continue;
                // Layered presets add one voice per matching zone, so steal per voice rather than once per Note On.
                if (_voices.Count >= VoiceLimit) StealVoice();
                var semitones = (pitch - region.RootKey) * region.ScaleTuning / 100.0 + region.FineTuneCents / 100.0;
                var step = region.SampleRate / (double)_sampleRate * Math.Pow(2, semitones / 12.0);
                var pan = region.Pan / 500.0; var velocityGain = Math.Pow(velocity / 127.0, 1.75) * region.Attenuation;
                _voices.Add(new Voice
                {
                    Channel = channel & 15, Pitch = pitch, Region = region, Position = region.Start, Step = step, Gain = velocityGain,
                    PanLeft = Math.Sqrt((1 - pan) * .5), PanRight = Math.Sqrt((1 + pan) * .5),
                    Attack = Math.Clamp(region.AttackSeconds, .001, 20), Decay = Math.Clamp(region.DecaySeconds, .001, 20),
                    Sustain = Math.Clamp(region.SustainLevel, .015, 1), Release = Math.Clamp(region.ReleaseSeconds, .015, 8),
                    Loop = (region.SampleMode & 1) != 0, LoopUntilRelease = region.SampleMode == 3
                });
            }
        }
    }
    public void NoteOff(int channel, int pitch)
    {
        lock (_gate)
        {
            foreach (var voice in _voices.Where(v => v.Channel == (channel & 15) && v.Pitch == pitch && v.KeyHeld))
            {
                voice.KeyHeld = false;
                if (_sustain[voice.Channel]) voice.SustainHeld = true;
                if (!voice.SustainHeld && !voice.SostenutoHeld) voice.ReleaseKey();
            }
        }
    }
    private void SetSustain(int channel, bool down)
    {
        lock (_gate)
        {
            if (_sustain[channel] == down) return; _sustain[channel] = down;
            if (!down) foreach (var voice in _voices.Where(v => v.Channel == channel && v.SustainHeld))
            {
                voice.SustainHeld = false;
                if (!voice.KeyHeld && !voice.SostenutoHeld) voice.ReleaseKey();
            }
        }
    }
    private void SetSostenuto(int channel, bool down)
    {
        lock (_gate)
        {
            if (_sostenuto[channel] == down) return;
            _sostenuto[channel] = down;
            if (down)
            {
                foreach (var voice in _voices.Where(v => v.Channel == channel && v.KeyHeld)) voice.SostenutoHeld = true;
            }
            else foreach (var voice in _voices.Where(v => v.Channel == channel && v.SostenutoHeld))
            {
                voice.SostenutoHeld = false;
                if (!voice.KeyHeld && !voice.SustainHeld) voice.ReleaseKey();
            }
        }
    }
    public void AllNotesOff(int? channel = null)
    {
        lock (_gate) foreach (var voice in _voices.Where(v => channel is null || v.Channel == channel))
        {
            voice.KeyHeld = false; voice.SustainHeld = false; voice.SostenutoHeld = false; voice.ReleaseKey();
        }
    }
    /// <summary>Drops the oldest voice that is already releasing, or the oldest voice overall, so a Note On can be honoured.</summary>
    private void StealVoice()
    {
        for (var i = 0; i < _voices.Count; i++) if (_voices[i].ReleaseAge >= 0) { _voices.RemoveAt(i); return; }
        _voices.RemoveAt(0);
    }
    public void Render(short[] interleaved, int frames)
    {
        lock (_gate)
        {
            Array.Clear(interleaved, 0, frames * 2);
            // Mixing buffers are reused between callbacks so the audio thread does not allocate.
            if (_mixLeft.Length < frames) { _mixLeft = new double[frames]; _mixRight = new double[frames]; }
            var left = _mixLeft; var right = _mixRight;
            Array.Clear(left, 0, frames); Array.Clear(right, 0, frames);
            var sampleSeconds = 1.0 / _sampleRate;
            for (var v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                var r = voice.Region; var envelope = 0.0;
                var soft = _soft[voice.Channel]; var softGain = soft ? .72 : 1.0;
                var filterAlpha = soft ? 1 - Math.Exp(-2 * Math.PI * 4200 / _sampleRate) : 1.0;
                // Mode 1 keeps looping through the release tail; mode 3 leaves the loop once the note is released.
                var looping = voice.Loop && r.LoopEnd > r.LoopStart && (!voice.LoopUntilRelease || voice.ReleaseAge < 0);
                var stereo = r.RightSamples;
                for (var i = 0; i < frames; i++)
                {
                    if (looping && voice.Position >= r.LoopEnd) voice.Position = r.LoopStart + (voice.Position - r.LoopEnd) % (r.LoopEnd - r.LoopStart);
                    if (voice.Position >= r.End || voice.Position < r.Start) break;
                    var sampleIndex = (int)voice.Position; var fraction = voice.Position - sampleIndex;
                    var own = Interpolate(_font, sampleIndex, fraction);
                    var sampleL = own; var sampleR = own;
                    if (stereo is not null)
                    {
                        var partnerPosition = stereo[0] + (voice.Position - r.Start); var partnerIndex = Math.Clamp((int)partnerPosition, stereo[0], stereo[1] - 1);
                        var partner = Interpolate(_font, partnerIndex, partnerPosition - partnerIndex);
                        if (r.StereoSwapped) sampleL = partner; else sampleR = partner;
                    }
                    if (soft)
                    {
                        voice.FilterLeft += filterAlpha * (sampleL - voice.FilterLeft);
                        voice.FilterRight += filterAlpha * (sampleR - voice.FilterRight);
                        sampleL = voice.FilterLeft; sampleR = voice.FilterRight;
                    }
                    envelope = voice.Envelope(); var gain = envelope * voice.Gain * softGain;
                    left[i] += sampleL * gain * voice.PanLeft; right[i] += sampleR * gain * voice.PanRight;
                    voice.Position += voice.Step; voice.Age += sampleSeconds; if (voice.ReleaseAge >= 0) voice.ReleaseAge += sampleSeconds;
                }
                if (envelope < .001 || voice.Position >= r.End || voice.ReleaseAge >= 0 && voice.ReleaseAge > voice.Release * 1.1) _voices.RemoveAt(v);
            }
            for (var i = 0; i < frames; i++)
            {
                interleaved[i * 2] = ToPcm(left[i]); interleaved[i * 2 + 1] = ToPcm(right[i]);
            }
        }
    }
    private static double Interpolate(SoundFontData font, int index, double fraction)
    {
        if ((uint)index >= (uint)font.SampleCount) return 0;
        var a = font.ReadSample(index) / 32768.0; var b = index + 1 < font.SampleCount ? font.ReadSample(index + 1) / 32768.0 : a;
        return a + (b - a) * fraction;
    }
    private static short ToPcm(double sample) => (short)Math.Clamp(Math.Round(Math.Tanh(sample * 1.45) * short.MaxValue), short.MinValue, short.MaxValue);
}
