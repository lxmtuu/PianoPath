using Xunit;

namespace PianoPath.Tests;

public sealed class LatencyProbeTests
{
    /// <summary>A player's notes: random gaps between a third of a second and a second and a half, starting a second in.</summary>
    internal static List<double> Notes(Random random, int count)
    {
        var notes = new List<double>();
        var time = 1.0;
        for (var i = 0; i < count; i++) { time += .35 + random.NextDouble() * 1.05; notes.Add(time); }
        return notes;
    }

    private static double Gauss(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    /// <summary>
    /// What a camera at <paramref name="fps"/> makes of the playing: a little noise all the time, a pulse of movement 40 ms wide
    /// <paramref name="delay"/> seconds after each note, and (when asked) as many unrelated pulses of movement, 60 % as strong, at random times.
    /// </summary>
    internal static List<MotionSample> Motion(Random random, IReadOnlyList<double> notes, double delay, double fps, int unrelated = 0, bool signal = true)
    {
        var length = Math.Max(25, notes[^1] + 1);
        var stray = Enumerable.Range(0, unrelated).Select(_ => 1 + random.NextDouble() * (length - 2)).ToArray();
        var motion = new List<MotionSample>();
        for (var time = 0.0; time < length; time += 1 / fps)
        {
            var at = time + (random.NextDouble() - .5) * .004;                          // frames do not arrive on the dot
            var energy = .1 + Math.Abs(Gauss(random) * .05);
            if (signal) foreach (var note in notes) energy += Math.Exp(-Math.Pow((at - (note + delay)) / .04, 2) / 2);
            foreach (var time2 in stray) energy += .6 * Math.Exp(-Math.Pow((at - time2) / .04, 2) / 2);
            motion.Add(new MotionSample(at, energy));
        }
        return motion;
    }

    public static TheoryData<double, double> Cases
    {
        get
        {
            var data = new TheoryData<double, double>();
            foreach (var fps in new[] { 30.0, 60.0 })
                foreach (var delay in new[] { 0.0, .03, .083, .15, .25, .35 }) data.Add(fps, delay);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_delay_is_found_within_one_frame_with_or_without_movement_that_has_nothing_to_do_with_the_notes(double fps, double delay)
    {
        for (var seed = 1; seed <= 5; seed++)
            foreach (var unrelated in new[] { 0, 12 })
            {
                var random = new Random(seed * 101 + (int)(delay * 1000) + unrelated);
                var notes = Notes(random, 20);
                var estimate = LatencyProbe.Estimate(Motion(random, notes, delay, fps, unrelated), notes);
                Assert.True(estimate.IsMeasured, $"fps {fps}, delay {delay}, seed {seed}, {unrelated} stray: {estimate}");
                Assert.True(Math.Abs(estimate.DelaySeconds - delay) <= 1 / fps, $"fps {fps}, delay {delay}, seed {seed}, {unrelated} stray: found {estimate.DelaySeconds:0.000}");
                Assert.InRange(estimate.Confidence, LatencyProbe.RequiredConfidence, 1);
                Assert.Equal(20, estimate.Notes);
            }
    }

    [Fact]
    public void Eight_notes_are_enough_and_seven_are_not()
    {
        var random = new Random(5);
        var notes = Notes(random, 8);
        var motion = Motion(random, notes, .1, 60);
        Assert.True(LatencyProbe.Estimate(motion, notes).IsMeasured);
        Assert.Equal(LatencyOutcome.NotEnoughNotes, LatencyProbe.Estimate(motion, notes.Take(7).ToArray()).Outcome);
        Assert.Equal(LatencyOutcome.NotEnoughNotes, LatencyProbe.Estimate(motion, []).Outcome);
    }

    [Fact]
    public void Notes_outside_the_recording_do_not_count_and_a_chord_is_one_note()
    {
        var random = new Random(8);
        var notes = Notes(random, 10);
        var motion = Motion(random, notes, .12, 60);
        // before the picture began and too near its end to have a 400 ms to look at
        var outside = notes.Concat([-3.0, -0.2, motion[^1].Time - .1, motion[^1].Time + 5]).ToArray();
        var estimate = LatencyProbe.Estimate(motion, outside);
        Assert.True(estimate.IsMeasured);
        Assert.Equal(10, estimate.Notes);
        // three keys of a chord a few milliseconds apart are one press
        var chords = notes.SelectMany(n => new[] { n, n + .004, n + .011 }).ToArray();
        var chordEstimate = LatencyProbe.Estimate(motion, chords);
        Assert.Equal(10, chordEstimate.Notes);
        Assert.Equal(estimate.DelaySeconds, chordEstimate.DelaySeconds, 9);
    }

    [Fact]
    public void The_order_of_what_is_given_does_not_matter_and_the_same_input_gives_the_same_answer()
    {
        var random = new Random(11);
        var notes = Notes(random, 20);
        var motion = Motion(random, notes, .2, 60, unrelated: 8);
        var first = LatencyProbe.Estimate(motion, notes);
        Assert.Equal(first, LatencyProbe.Estimate(motion, notes));
        var shuffled = motion.OrderBy(_ => random.Next()).ToList();
        var notesShuffled = notes.OrderBy(_ => random.Next()).ToList();
        Assert.Equal(first, LatencyProbe.Estimate(shuffled, notesShuffled));
    }

    [Fact]
    public void A_picture_that_is_too_short_or_too_sparse_is_said_to_be_so()
    {
        var notes = Enumerable.Range(1, 12).Select(i => i * .5).ToArray();
        Assert.Equal(LatencyOutcome.NotEnoughPicture, LatencyProbe.Estimate([], notes).Outcome);
        var few = Enumerable.Range(0, 9).Select(i => new MotionSample(i, 1)).ToArray();
        Assert.Equal(LatencyOutcome.NotEnoughPicture, LatencyProbe.Estimate(few, notes).Outcome);
        var brief = Enumerable.Range(0, 100).Select(i => new MotionSample(i / 60.0, i % 3)).ToArray();     // under two seconds
        Assert.Equal(LatencyOutcome.NotEnoughPicture, LatencyProbe.Estimate(brief, notes).Outcome);
    }

    [Fact]
    public void A_picture_that_does_not_move_is_no_motion_and_not_a_delay_of_zero()
    {
        var random = new Random(3);
        var notes = Notes(random, 15);
        var still = Enumerable.Range(0, 25 * 60).Select(i => new MotionSample(i / 60.0, .2)).ToArray();
        Assert.Equal(LatencyOutcome.NoMotion, LatencyProbe.Estimate(still, notes).Outcome);
        var dark = Enumerable.Range(0, 25 * 60).Select(i => new MotionSample(i / 60.0, 0)).ToArray();
        Assert.Equal(LatencyOutcome.NoMotion, LatencyProbe.Estimate(dark, notes).Outcome);
    }

    [Fact]
    public void Movement_that_does_not_follow_the_notes_is_almost_never_taken_for_a_delay()
    {
        var measured = 0;
        for (var seed = 0; seed < 20; seed++)
        {
            var random = new Random(900 + seed);
            var notes = Notes(random, 20);
            var estimate = LatencyProbe.Estimate(Motion(random, notes, 0, 60, unrelated: 12, signal: false), notes);
            if (estimate.IsMeasured) measured++;
            else Assert.True(estimate.Outcome == LatencyOutcome.Unclear, $"seed {seed}: {estimate}");
        }
        Assert.True(measured <= 2, $"{measured} of 20 recordings with no link to the notes were given a delay");
    }

    [Fact]
    public void Garbage_never_throws_and_is_never_a_delay()
    {
        var random = new Random(1);
        var notes = new List<double> { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var motion = new List<MotionSample>
        {
            new(double.NaN, 1), new(1, double.NaN), new(double.PositiveInfinity, 1), new(2, double.NegativeInfinity),
        };
        for (var i = 0; i < 2000; i++) motion.Add(new MotionSample(i * .01, random.NextDouble()));
        motion.Add(new MotionSample(5, 3)); motion.Add(new MotionSample(5, 4));                       // the same time twice
        motion.Add(new MotionSample(6, -2));                                                         // less than none
        var estimate = LatencyProbe.Estimate(motion, notes);
        Assert.True(estimate.Outcome != LatencyOutcome.Measured || estimate.DelaySeconds is >= 0 and <= LatencyProbe.MaxDelay);
        Assert.InRange(estimate.Confidence, 0, 1);
        Assert.Equal(LatencyOutcome.NotEnoughPicture, LatencyProbe.Estimate(motion.Where(m => !double.IsFinite(m.Time) || !double.IsFinite(m.Energy)).ToArray(), notes).Outcome);
    }

    [Fact]
    public void Two_delays_that_explain_the_playing_equally_well_make_it_unclear()
    {
        // the first ten notes are followed by movement after 80 ms, the next ten after 300 ms: no single delay explains both
        var random = new Random(21);
        var notes = Notes(random, 20);
        var motion = new List<MotionSample>();
        for (var time = 0.0; time < notes[^1] + 1; time += 1 / 60.0)
        {
            var energy = .1 + Math.Abs(Gauss(random) * .05);
            for (var i = 0; i < notes.Count; i++) energy += Math.Exp(-Math.Pow((time - (notes[i] + (i < 10 ? .08 : .3))) / .04, 2) / 2);
            motion.Add(new MotionSample(time, energy));
        }
        Assert.Equal(LatencyOutcome.Unclear, LatencyProbe.Estimate(motion, notes).Outcome);
    }
}
