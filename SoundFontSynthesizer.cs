using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PianoPath;

internal sealed record SoundFontPreset(int Bank, int Program, string Name, IReadOnlyList<SoundFontRegion> Regions);

internal sealed class SoundFontRegion
{
    public required int[] LeftSamples { get; init; }
    public int[]? RightSamples { get; init; }
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
        public readonly Dictionary<int, int> Values = [];
        public (int Low, int High) Keys = (0, 127);
        public (int Low, int High) Velocities = (0, 127);
        public bool HasKeys, HasVelocities;
        public int Get(int op, int fallback = 0) => Values.TryGetValue(op, out var value) ? value : fallback;
        public void Add(int op, int value) => Values[op] = Values.GetValueOrDefault(op) + value;
        public void Merge(GeneratorSet other)
        {
            foreach (var pair in other.Values) Add(pair.Key, pair.Value);
            if (other.HasKeys) { Keys = HasKeys ? (Math.Max(Keys.Low, other.Keys.Low), Math.Min(Keys.High, other.Keys.High)) : other.Keys; HasKeys = true; }
            if (other.HasVelocities) { Velocities = HasVelocities ? (Math.Max(Velocities.Low, other.Velocities.Low), Math.Min(Velocities.High, other.Velocities.High)) : other.Velocities; HasVelocities = true; }
        }
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
            var pGlobal = new GeneratorSet();
            foreach (var zone in pZones) if (!zone.Values.ContainsKey(41)) { pGlobal.Merge(zone); break; }
            var regions = new List<SoundFontRegion>();
            foreach (var pZone in pZones)
            {
                if (!pZone.Values.TryGetValue(41, out var instrumentIndex)) continue;
                var presetGenerators = new GeneratorSet(); presetGenerators.Merge(pGlobal); presetGenerators.Merge(pZone);
                if (instrumentIndex < 0 || instrumentIndex >= instrumentHeaders.Count - 1) continue;
                var instrument = instrumentHeaders[instrumentIndex]; var instrumentEnd = instrumentHeaders[instrumentIndex + 1];
                var iZones = ReadZones(instrumentBags, instrumentGens, instrument.Bag, instrumentEnd.Bag);
                var iGlobal = new GeneratorSet();
                foreach (var zone in iZones) if (!zone.Values.ContainsKey(53)) { iGlobal.Merge(zone); break; }
                foreach (var iZone in iZones)
                {
                    if (!iZone.Values.TryGetValue(53, out var sampleIndex) || sampleIndex < 0 || sampleIndex >= samples.Count - 1) continue;
                    var combined = new GeneratorSet(); combined.Merge(presetGenerators); combined.Merge(iGlobal); combined.Merge(iZone);
                    var range = samples[sampleIndex];
                    var start = range.Start + combined.Get(0) + combined.Get(4) * 32768;
                    var end = range.End + combined.Get(1) + combined.Get(12) * 32768;
                    var loopStart = range.LoopStart + combined.Get(2) + combined.Get(45) * 32768;
                    var loopEnd = range.LoopEnd + combined.Get(3) + combined.Get(50) * 32768;
                    if (start < 0 || end > sampleCount - 46 || end - start < 8 || range.Rate is < 4000 or > 192000) continue;
                    start = Math.Clamp(start, 0, sampleCount - 1); end = Math.Clamp(end, start + 8, sampleCount - 46);
                    loopStart = Math.Clamp(loopStart, start, end - 1); loopEnd = Math.Clamp(loopEnd, loopStart + 1, end);
                    var sampleMode = combined.Get(54) & 3;
                    int[]? rightSamples = null;
                    if ((range.Type & 0x7fff) is 2 or 4 && range.Link >= 0 && range.Link < samples.Count - 1)
                    {
                        var right = samples[range.Link];
                        if (right.End <= sampleCount - 46 && right.End > right.Start && right.Start >= 0) rightSamples = [right.Start, right.End, right.LoopStart, right.LoopEnd];
                    }
                    var rootKey = combined.Get(58, range.Root); if (rootKey is < 0 or > 127) rootKey = range.Root;
                    var keyRange = combined.HasKeys ? combined.Keys : (0, 127); var velocityRange = combined.HasVelocities ? combined.Velocities : (0, 127);
                    if (keyRange.Item1 > keyRange.Item2 || velocityRange.Item1 > velocityRange.Item2) continue;
                    var sustainCb = Math.Clamp(combined.Get(37), 0, 1440);
                    regions.Add(new SoundFontRegion
                    {
                        LeftSamples = [start, end, loopStart, loopEnd], RightSamples = rightSamples,
                        Start = start, End = end, LoopStart = loopStart, LoopEnd = loopEnd, SampleRate = range.Rate, RootKey = rootKey,
                        KeyLow = Math.Clamp(keyRange.Item1, 0, 127), KeyHigh = Math.Clamp(keyRange.Item2, 0, 127),
                        VelocityLow = Math.Clamp(velocityRange.Item1, 0, 127), VelocityHigh = Math.Clamp(velocityRange.Item2, 0, 127),
                        SampleMode = sampleMode, FineTuneCents = range.Correction + combined.Get(51) * 100 + combined.Get(52),
                        ScaleTuning = combined.Get(56, 100), Pan = Math.Clamp(combined.Get(17), -500, 500),
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
            var zone = new GeneratorSet(); for (var g = start; g < end; g++) zone.Merge(generators[g]); result.Add(zone);
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
    private const int VoiceLimit = 96;
    private sealed class Voice
    {
        public required int Channel, Pitch;
        public required SoundFontRegion Region;
        public required double Position, Step, Gain, PanLeft, PanRight;
        public required double Attack, Decay, Sustain, Release;
        public required bool Loop;
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
                if (data1 == 0) _bank[channel] = data2 << 7;
                else if (data1 == 32) _bank[channel] = (_bank[channel] & 0x3f80) | data2;
                else if (data1 == 64) SetSustain(channel, data2 >= 64);
                else if (data1 == 66) SetSostenuto(channel, data2 >= 64);
                else if (data1 == 67) _soft[channel] = data2 >= 64;
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
            if (_voices.Count >= VoiceLimit) _voices.RemoveAt(0);
            var preset = _channelPresets[channel & 15]; if (preset is null) return;
            foreach (var region in preset.Regions)
            {
                if (pitch < region.KeyLow || pitch > region.KeyHigh || velocity < region.VelocityLow || velocity > region.VelocityHigh) continue;
                var semitones = (pitch - region.RootKey) * region.ScaleTuning / 100.0 + region.FineTuneCents / 100.0;
                var step = region.SampleRate / (double)_sampleRate * Math.Pow(2, semitones / 12.0);
                var pan = region.Pan / 500.0; var velocityGain = Math.Pow(velocity / 127.0, 1.75) * region.Attenuation;
                _voices.Add(new Voice
                {
                    Channel = channel & 15, Pitch = pitch, Region = region, Position = region.Start, Step = step, Gain = velocityGain,
                    PanLeft = Math.Sqrt((1 - pan) * .5), PanRight = Math.Sqrt((1 + pan) * .5),
                    Attack = Math.Clamp(region.AttackSeconds, .001, 20), Decay = Math.Clamp(region.DecaySeconds, .001, 20),
                    Sustain = Math.Clamp(region.SustainLevel, .015, 1), Release = Math.Clamp(region.ReleaseSeconds, .015, 8),
                    Loop = (region.SampleMode & 1) != 0
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
    public void Render(short[] interleaved, int frames)
    {
        lock (_gate)
        {
            Array.Clear(interleaved, 0, frames * 2);
            var left = new double[frames]; var right = new double[frames];
            foreach (var voice in _voices.ToArray())
            {
                var r = voice.Region; var envelope = 0.0;
                var soft = _soft[voice.Channel]; var softGain = soft ? .72 : 1.0;
                var filterAlpha = soft ? 1 - Math.Exp(-2 * Math.PI * 4200 / _sampleRate) : 1.0;
                for (var i = 0; i < frames; i++)
                {
                    if (voice.Loop && (voice.KeyHeld || voice.SustainHeld || voice.SostenutoHeld) && voice.Position >= r.LoopEnd && r.LoopEnd > r.LoopStart) voice.Position = r.LoopStart + (voice.Position - r.LoopEnd) % (r.LoopEnd - r.LoopStart);
                    if (voice.Position >= r.End || voice.Position < r.Start) break;
                    var sampleIndex = (int)voice.Position; var fraction = voice.Position - sampleIndex;
                    var sampleL = Interpolate(_font, sampleIndex, fraction);
                    var sampleR = sampleL;
                    if (r.RightSamples is { } stereo && _font.SampleCount > 0)
                    {
                        var rightPosition = stereo[0] + (voice.Position - r.Start); var rightIndex = Math.Clamp((int)rightPosition, stereo[0], stereo[1] - 1);
                        sampleR = Interpolate(_font, rightIndex, rightPosition - rightIndex);
                    }
                    if (soft)
                    {
                        voice.FilterLeft += filterAlpha * (sampleL - voice.FilterLeft);
                        voice.FilterRight += filterAlpha * (sampleR - voice.FilterRight);
                        sampleL = voice.FilterLeft; sampleR = voice.FilterRight;
                    }
                    envelope = voice.Envelope(); var gain = envelope * voice.Gain * softGain;
                    left[i] += sampleL * gain * voice.PanLeft; right[i] += sampleR * gain * voice.PanRight;
                    voice.Position += voice.Step; voice.Age += 1.0 / _sampleRate; if (voice.ReleaseAge >= 0) voice.ReleaseAge += 1.0 / _sampleRate;
                }
                if (envelope < .001 || voice.Position >= r.End || voice.ReleaseAge >= 0 && voice.ReleaseAge > voice.Release * 1.1) _voices.Remove(voice);
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
