using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// AR-S1: the piano simulation behind the AR effects (docs/AR-OVERLAY-ROADMAP.md §14, slice AR-S1).
/// Pure arithmetic on purpose: activity from a velocity band, register-dependent die-off, the three
/// pedals, notes per second and the total. No GPU, no camera, no piano.
/// </summary>
public class PianoSimulationTests
{
    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    /// <summary>Strike a key, let go at once, and read the string some time after the damper decision.</summary>
    private static double Released(PianoSimulation sim, int pitch, double sustain, double afterSeconds)
    {
        sim.SetSustain(sustain);
        sim.NoteOn(pitch, 127);
        sim.NoteOff(pitch);
        sim.Advance(afterSeconds);
        return sim.Activity(pitch);
    }

    [Fact]
    public void A_soft_strike_is_quiet_a_hard_strike_is_loud()
    {
        var sim = new PianoSimulation { VelocityFloor = 20, VelocityCeiling = 100 };
        sim.NoteOn(60, 10); Near(0, sim.Activity(60));                  // below the floor: nothing
        sim.NoteOn(60, 20); Near(0, sim.Activity(60));                  // on the floor: still nothing
        sim.NoteOn(60, 60); Near(.5, sim.Activity(60));               // halfway up the band
        sim.NoteOn(60, 100); Near(1, sim.Activity(60));               // at the ceiling: full
        sim.NoteOn(62, 127); Near(1, sim.Activity(62));               // above the ceiling clamps
    }

    [Fact]
    public void A_bass_note_rings_longer_than_a_treble_note()
    {
        var sim = new PianoSimulation();
        Near(2.2, PianoSimulation.NaturalDieOffSeconds(21), 1e-9);    // A0 keeps its energy
        Near(.45, PianoSimulation.NaturalDieOffSeconds(108), 1e-9);   // C8 gives up first
        sim.NoteOn(21, 127);
        sim.NoteOn(108, 127);
        sim.Advance(1);
        Assert.True(sim.Activity(21) > sim.Activity(108) + .3,
            "After the same second the bass string should still be far ahead of the treble string.");
    }

    [Fact]
    public void A_released_note_is_damped_away_in_a_blink_but_the_sustain_pedal_leaves_it_ringing()
    {
        var sim = new PianoSimulation();
        var damped = Released(sim, 60, sustain: 0, afterSeconds: .2);
        var sustained = Released(sim, 62, sustain: 1, afterSeconds: .2);
        Assert.True(damped < .05, $"Without a pedal the damper should choke a released note at once, got {damped}.");
        Assert.True(sustained > .7, $"With the sustain pedal down a released note should still ring, got {sustained}.");
    }

    [Fact]
    public void Half_the_sustain_pedal_is_halfway_between_damped_and_ringing()
    {
        var sim = new PianoSimulation();
        var damped = Released(sim, 60, sustain: 0, afterSeconds: .2);
        var half = Released(sim, 60, sustain: .5, afterSeconds: .2);
        var held = Released(sim, 60, sustain: 1, afterSeconds: .2);
        Assert.True(half > damped && half < held,
            $"Half-pedalling should sit strictly between the damper and the ringing string ({damped:0.000} < {half:0.000} < {held:0.000}).");
    }

    [Fact]
    public void The_sostenuto_pedal_catches_only_the_keys_that_were_down()
    {
        var sim = new PianoSimulation();
        sim.NoteOn(60, 127);
        sim.NoteOn(62, 127);
        sim.SetSostenuto(true);            // catches 60 and 62
        sim.NoteOff(60);
        sim.NoteOff(62);
        sim.NoteOn(64, 127);               // 64 comes after the pedal: not caught
        sim.NoteOff(64);
        sim.Advance(.3);
        Assert.True(sim.Activity(60) > .6 && sim.Activity(62) > .6,
            "The keys that were down when sostenuto caught should still ring.");
        Assert.True(sim.Activity(64) < .05,
            "A key pressed after sostenuto caught should be damped as usual.");
        sim.SetSostenuto(false);
        sim.Advance(.3);
        Assert.True(sim.Activity(60) < .05 && sim.Activity(62) < .05,
            "Lifting sostenuto should drop the caught dampers.");
    }

    [Fact]
    public void A_soft_re_strike_never_makes_a_ringing_string_quieter()
    {
        var sim = new PianoSimulation();
        sim.NoteOn(60, 127);
        var ringing = sim.Activity(60);
        sim.NoteOn(60, 8);                  // a whisper on top of a roar
        Assert.True(sim.Activity(60) >= ringing,
            "Re-striking must take the maximum of the ringing energy and the new strike.");
    }

    [Fact]
    public void Notes_per_second_counts_the_last_second_only()
    {
        var sim = new PianoSimulation();
        sim.NoteOn(60, 100);
        sim.Advance(.4);
        sim.NoteOn(62, 100);
        sim.Advance(.5);
        sim.NoteOn(64, 100);
        sim.Advance(.05);                   // presses at 0, .4 and .9 of a second
        Near(3, sim.NotesPerSecond);
        sim.Advance(.6);                    // only the third press is still inside the second
        Near(1, sim.NotesPerSecond);
        sim.Advance(1);
        Near(0, sim.NotesPerSecond);
    }

    [Fact]
    public void Total_activity_is_the_sum_of_the_keys()
    {
        var sim = new PianoSimulation();
        sim.NoteOn(60, 127);
        sim.NoteOn(64, 127);
        Near(sim.Activity(60) + sim.Activity(64), sim.TotalActivity);
        Assert.True(sim.TotalActivity > 1, "Two full strikes should add up to more than one key's worth.");
    }

    [Fact]
    public void The_soft_pedal_is_recorded_but_changes_nothing()
    {
        var withSoft = new PianoSimulation();
        var without = new PianoSimulation();
        withSoft.SetSoft(true);
        Assert.True(withSoft.Soft, "The soft pedal should be tracked for the pedal display.");
        withSoft.NoteOn(60, 127);
        without.NoteOn(60, 127);
        withSoft.Advance(.2);
        without.Advance(.2);
        withSoft.NoteOff(60);
        without.NoteOff(60);
        withSoft.Advance(.2);
        without.Advance(.2);
        Near(without.Activity(60), withSoft.Activity(60), 1e-12);
    }

    [Fact]
    public void Reset_forgets_the_notes_but_keeps_the_pedals()
    {
        var sim = new PianoSimulation();
        sim.NoteOn(60, 127);
        sim.SetSustain(1);
        sim.Reset();
        Near(0, sim.Activity(60));
        Assert.False(sim.IsPressed(60));
        Near(0, sim.NotesPerSecond);
        Near(1, sim.Sustain);
    }

    [Fact]
    public void Degenerate_presses_and_steps_do_nothing()
    {
        var sim = new PianoSimulation();
        sim.NoteOn(-1, 100);
        sim.NoteOn(128, 100);
        sim.NoteOn(60, 0);
        sim.NoteOn(60, -5);
        sim.NoteOn(60, double.NaN);
        sim.NoteOff(999);
        sim.Advance(0);
        sim.Advance(-1);
        sim.Advance(double.NaN);
        Near(0, sim.Activity(60));
        Near(0, sim.NotesPerSecond);
        Near(0, sim.TotalActivity);
    }
}
