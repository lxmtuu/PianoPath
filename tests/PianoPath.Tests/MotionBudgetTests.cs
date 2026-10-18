using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// N11: the only pulse that belongs to the note itself has a hard ceiling, and it sits below the
/// three-flashes-per-second line even at the fastest setting.
/// </summary>
public class MotionBudgetTests
{
    [Fact]
    public void The_falling_pulse_stays_below_three_hertz()
    {
        Assert.Equal(.239, MotionBudget.PulseHertz(0), 3);
        Assert.Equal(1.671, MotionBudget.PulseHertz(1), 3);
        Assert.True(MotionBudget.PulseHertz(1) < 3);
    }

    [Fact]
    public void The_pulse_rate_is_clamped_so_a_preset_cannot_outrun_it()
    {
        Assert.Equal(MotionBudget.PulseAngularRate(1), MotionBudget.PulseAngularRate(5), 9);
        Assert.Equal(MotionBudget.PulseAngularRate(0), MotionBudget.PulseAngularRate(-1), 9);
    }
}
