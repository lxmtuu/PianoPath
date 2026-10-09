namespace PianoPath;

/// <summary>
/// How loud the piano is right now: per-key sounding energy, the three pedals, and how busy the player
/// is. Pure arithmetic (AR-S1 of docs/AR-OVERLAY-ROADMAP.md §14) so the same numbers can drive the AR
/// elements later (colour by activity, impact lights, particles) and every claim about them is checkable
/// without a GPU, a camera or a piano.
///
/// The model follows the KeySight simulation: a strike's velocity becomes activity through a
/// floor/ceiling band, energy decays with a register-dependent die-off (low strings ring longer than
/// high ones), the sustain pedal undamps released strings 0..1 so half-pedalling counts, the sostenuto
/// pedal catches the dampers of the keys that are down when it is pressed, and the soft pedal is
/// recorded but deliberately does nothing. Notes per second is a one-second window of note-ons; total
/// activity is the sum over the keys.
/// </summary>
internal sealed class PianoSimulation
{
    /// <summary>Die-off of a held string at A0 (pitch 21) and at C8 (pitch 108), in seconds.</summary>
    internal const double LowPitchDieOffSeconds = 2.2;
    internal const double HighPitchDieOffSeconds = 0.45;

    /// <summary>How fast a released string dies when the damper falls on it (no pedal).</summary>
    internal const double DamperDieOffSeconds = 0.05;

    private const int PitchCount = 128;
    private readonly double[] _activity = new double[PitchCount];
    private readonly bool[] _pressed = new bool[PitchCount];
    private readonly bool[] _caught = new bool[PitchCount];
    private readonly Queue<double> _pressTimes = new();
    private double _now;

    /// <summary>Velocity floor and ceiling in raw MIDI units 0..127; activity is where a strike lands in between.</summary>
    internal int VelocityFloor { get; set; }

    /// <summary>See <see cref="VelocityFloor"/>.</summary>
    internal int VelocityCeiling { get; set; } = 127;

    /// <summary>Sustain pedal level 0..1 (CC 64); half-pedalling is real, so this is not a boolean.</summary>
    internal double Sustain { get; private set; }

    /// <summary>Sostenuto pedal (CC 66).</summary>
    internal bool Sostenuto { get; private set; }

    /// <summary>Soft pedal (CC 67). Tracked for parity with the synthesizer; deliberately unused by the model.</summary>
    internal bool Soft { get; private set; }

    /// <summary>A strike: the key goes down and the string takes on at most the strike's energy.</summary>
    internal void NoteOn(int pitch, double velocity)
    {
        if (pitch < 0 || pitch >= PitchCount || double.IsNaN(velocity) || velocity <= 0) return; // a zero-velocity note-on is an off
        _pressed[pitch] = true;
        var energy = EnergyFor(velocity);
        if (energy > _activity[pitch]) _activity[pitch] = energy; // a soft re-strike never quiets a ringing string
        _pressTimes.Enqueue(_now);
    }

    /// <summary>The key comes up; whether the damper falls depends on the pedals.</summary>
    internal void NoteOff(int pitch)
    {
        if (pitch < 0 || pitch >= PitchCount) return;
        _pressed[pitch] = false;
    }

    /// <summary>Sustain pedal level 0..1. Out-of-range and NaN values clamp to a real pedal position.</summary>
    internal void SetSustain(double level) => Sustain = double.IsNaN(level) ? 0 : Math.Clamp(level, 0, 1);

    /// <summary>Sostenuto catches the dampers of the keys that are down at this moment, and lets them go when it lifts.</summary>
    internal void SetSostenuto(bool down)
    {
        if (Sostenuto == down) return; // repeated same-state calls (other pedals toggling) catch nothing new
        Sostenuto = down;
        if (down)
        {
            for (var pitch = 0; pitch < PitchCount; pitch++) if (_pressed[pitch]) _caught[pitch] = true;
        }
        else Array.Clear(_caught);
    }

    /// <summary>Soft pedal state; recorded and ignored, like KeySight's unused-by-default soft pedal.</summary>
    internal void SetSoft(bool down) => Soft = down;

    /// <summary>Advances time: the energy decays and the notes-per-second window slides.</summary>
    internal void Advance(double seconds)
    {
        if (!(seconds > 0) || double.IsNaN(seconds)) return; // zero, negative and NaN steps change nothing
        _now += seconds;
        PrunePresses();
        for (var pitch = 0; pitch < PitchCount; pitch++)
        {
            var energy = _activity[pitch];
            if (energy <= 0) continue;
            _activity[pitch] = energy * Math.Exp(-seconds / DieOffSeconds(pitch));
        }
    }

    /// <summary>How loudly this key sounds right now, 0..1.</summary>
    internal double Activity(int pitch) => pitch < 0 || pitch >= PitchCount ? 0 : _activity[pitch];

    /// <summary>Where a strike of this raw velocity (0..127) lands in the floor/ceiling band, 0..1 — the input every graph of the redesigned overlay walks.</summary>
    internal double ActivityForVelocity(double velocity) => EnergyFor(velocity);

    /// <summary>Whether the player's finger is on this key.</summary>
    internal bool IsPressed(int pitch) => pitch >= 0 && pitch < PitchCount && _pressed[pitch];

    /// <summary>Note-ons in the last second of simulation time.</summary>
    internal double NotesPerSecond
    {
        get
        {
            PrunePresses();
            return _pressTimes.Count;
        }
    }

    /// <summary>The sum of every key's activity; more than 1 whenever several keys ring together.</summary>
    internal double TotalActivity
    {
        get
        {
            var total = 0.0;
            for (var pitch = 0; pitch < PitchCount; pitch++) total += _activity[pitch];
            return total;
        }
    }

    /// <summary>Forgets the notes but keeps the pedals and the velocity band.</summary>
    internal void Reset()
    {
        Array.Clear(_activity);
        Array.Clear(_pressed);
        Array.Clear(_caught);
        _pressTimes.Clear();
    }

    /// <summary>Where a strike lands in the velocity band, 0..1; a degenerate band is a step.</summary>
    private double EnergyFor(double velocity)
    {
        if (VelocityCeiling <= VelocityFloor) return velocity > VelocityFloor ? 1 : 0;
        return Math.Clamp((velocity - VelocityFloor) / (VelocityCeiling - VelocityFloor), 0, 1);
    }

    /// <summary>
    /// Seconds for this string's energy to fall by a factor of e: the natural register-dependent decay
    /// while the damper is off the string (held, or caught by sostenuto), and the damper's fast choke
    /// otherwise - lifted away in proportion to how far the sustain pedal is down.
    /// </summary>
    private double DieOffSeconds(int pitch)
    {
        var natural = NaturalDieOffSeconds(pitch);
        if (_pressed[pitch] || (_caught[pitch] && Sostenuto)) return natural;
        return DamperDieOffSeconds + (natural - DamperDieOffSeconds) * Sustain;
    }

    /// <summary>Natural die-off, linear from <see cref="LowPitchDieOffSeconds"/> at A0 to <see cref="HighPitchDieOffSeconds"/> at C8.</summary>
    internal static double NaturalDieOffSeconds(int pitch)
    {
        var t = Math.Clamp((pitch - 21) / 87.0, 0, 1);
        return LowPitchDieOffSeconds + (HighPitchDieOffSeconds - LowPitchDieOffSeconds) * t;
    }

    private void PrunePresses()
    {
        while (_pressTimes.Count > 0 && _pressTimes.Peek() <= _now - 1) _pressTimes.Dequeue();
    }
}
