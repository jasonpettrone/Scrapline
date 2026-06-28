using Scrapline.Core.Cars;
using Scrapline.Core.Contracts;
using Xunit;

namespace Scrapline.Core.Tests.Contracts;

public class RaceConfigTests
{
    [Fact]
    public void Default_is_drivable_on_the_arena()
    {
        var config = RaceConfig.Default;

        Assert.Equal("arena", config.TrackId);
        Assert.True(config.PlayerCar.IsWellFormed);
    }

    [Fact]
    public void Carries_a_custom_car()
    {
        var tank = CarStats.Default with { MaxSpeed = 400f, Acceleration = 600f };

        var config = new RaceConfig { PlayerCar = tank };

        Assert.Equal(tank, config.PlayerCar);
    }
}
