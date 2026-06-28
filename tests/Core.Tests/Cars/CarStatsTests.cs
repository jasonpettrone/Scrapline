using Scrapline.Core.Cars;
using Xunit;

namespace Scrapline.Core.Tests.Cars;

public class CarStatsTests
{
    [Fact]
    public void Default_is_well_formed()
    {
        Assert.True(CarStats.Default.IsWellFormed);
    }

    [Fact]
    public void Records_support_tuning_a_single_stat_in_isolation()
    {
        // The data-driven payoff: a "faster" car is just the default with one field changed,
        // and nothing else drifts.
        var fast = CarStats.Default with { MaxSpeed = 1000f };

        Assert.Equal(1000f, fast.MaxSpeed);
        Assert.Equal(CarStats.Default.Acceleration, fast.Acceleration);
        Assert.True(fast.IsWellFormed);
    }

    [Theory]
    [InlineData(0f)]      // no speed
    [InlineData(-100f)]   // negative speed
    public void Non_positive_max_speed_is_not_well_formed(float maxSpeed)
    {
        Assert.False((CarStats.Default with { MaxSpeed = maxSpeed }).IsWellFormed);
    }

    [Theory]
    [InlineData(0f)]      // can't reverse
    [InlineData(1.5f)]    // reverse faster than forward — nonsense
    public void Reverse_factor_outside_zero_to_one_is_not_well_formed(float factor)
    {
        Assert.False((CarStats.Default with { ReverseSpeedFactor = factor }).IsWellFormed);
    }
}
