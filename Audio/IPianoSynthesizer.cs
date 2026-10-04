namespace PianoPath;

/// <summary>
/// What the audio pipeline needs from the Salamander Grand SFZ synthesiser, so the same float32 path —
/// sympathetic string resonance, hall reverb, output sink and the recording tap — is shared by playback,
/// MIDI input and recording. Its continuous damper model covers CC 64 half-pedalling and repedal catch; the
/// pedal level also drives the sympathetic-resonance bank.
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
