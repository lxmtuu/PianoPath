namespace PianoPath;

/// <summary>
/// What the audio pipeline needs from an instrument synthesiser, so the same float32 path — sympathetic
/// string resonance, hall reverb, output sink and the recording tap — plays whichever sample engine is
/// loaded. Implemented by <see cref="SoundFontSynthesizer"/> (SoundFont 2, the bundled Yamaha grand) and
/// <see cref="SfzSynthesizer"/> (SFZ instruments such as the Steinway sample sets). Both engines share the
/// continuous damper model: CC 64 half-pedalling, repedal catch and the damper level that drives the
/// resonance bank behave identically whichever format the piano came from.
/// </summary>
internal interface IPianoSynthesizer
{
    /// <summary>A short human-readable instrument name for the Settings page and the window chrome.</summary>
    string Name { get; }
    /// <summary>Sounding voices right now; verification uses it to watch releases and pedal holds.</summary>
    int ActiveVoiceCount { get; }
    /// <summary>Highest sustain-pedal position across channels, 0..1 — feeds the string-resonance stage.</summary>
    double DamperLevel { get; }
    void NoteOn(int channel, int pitch, int velocity);
    void NoteOff(int channel, int pitch);
    /// <summary>Raw MIDI: note on/off and the controllers (CC 64/66/67 pedals, CC 120/123 all-off).</summary>
    void ProcessMidi(int channel, int command, int data1, int data2);
    void AllNotesOff(int? channel = null);
    /// <summary>Renders one block of interleaved stereo float32 at the engine sample rate.</summary>
    void Render(float[] interleaved, int frames);
}
