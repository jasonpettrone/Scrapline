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
        // Kept under the BoostMaxSpeed default so the one-field change stays well-formed.
        var fast = CarStats.Default with { MaxSpeed = 800f };

        Assert.Equal(800f, fast.MaxSpeed);
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

    [Fact]
    public void Non_positive_mass_is_not_well_formed()
    {
        Assert.False((CarStats.Default with { Mass = 0f }).IsWellFormed);
    }

    [Fact]
    public void Drift_grip_above_normal_grip_is_not_well_formed()
    {
        // Drifting must loosen, never tighten, the car: DriftGrip <= Grip.
        Assert.False((CarStats.Default with { DriftGrip = 99f }).IsWellFormed);
    }

    [Fact]
    public void Turn_multiplier_below_one_is_not_well_formed()
    {
        // A drift that slows the turn would be backwards.
        Assert.False((CarStats.Default with { DriftTurnMultiplier = 0.9f }).IsWellFormed);
    }

    [Fact]
    public void Boost_max_speed_not_above_base_max_speed_is_not_well_formed()
    {
        Assert.False((CarStats.Default with { BoostMaxSpeed = CarStats.Default.MaxSpeed }).IsWellFormed);
    }

    [Theory]
    [InlineData(0f)]       // no capacity
    [InlineData(-50f)]     // negative capacity
    public void Non_positive_boost_capacity_is_not_well_formed(float capacity)
    {
        Assert.False((CarStats.Default with { BoostCapacity = capacity }).IsWellFormed);
    }

    [Fact]
    public void Negative_boost_regen_is_not_well_formed()
    {
        // Passive trickle can be zero (a car with no passive gen) but never negative.
        Assert.False((CarStats.Default with { BoostRegenRate = -1f }).IsWellFormed);
        Assert.True((CarStats.Default with { BoostRegenRate = 0f }).IsWellFormed);
    }
}
