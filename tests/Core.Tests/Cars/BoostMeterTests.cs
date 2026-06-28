using Scrapline.Core.Cars;
using Xunit;

namespace Scrapline.Core.Tests.Cars;

public class BoostMeterTests
{
    [Fact]
    public void Starts_empty()
    {
        var meter = new BoostMeter(100f);

        Assert.Equal(0f, meter.Current);
        Assert.Equal(0f, meter.Fraction);
        Assert.True(meter.IsEmpty);
    }

    [Fact]
    public void Add_fills_and_reports_fraction()
    {
        var meter = new BoostMeter(100f);

        meter.Add(40f);

        Assert.Equal(40f, meter.Current);
        Assert.Equal(0.4f, meter.Fraction, 4);
        Assert.False(meter.IsEmpty);
    }

    [Fact]
    public void Add_clamps_at_capacity()
    {
        var meter = new BoostMeter(100f);

        meter.Add(80f);
        meter.Add(80f); // would be 160 — clamped

        Assert.Equal(100f, meter.Current);
        Assert.Equal(1f, meter.Fraction, 4);
    }

    [Fact]
    public void Non_positive_add_does_nothing()
    {
        var meter = new BoostMeter(100f);
        meter.Add(50f);

        meter.Add(0f);
        meter.Add(-10f);

        Assert.Equal(50f, meter.Current);
    }

    [Fact]
    public void Drain_reduces_current_and_returns_amount_spent()
    {
        var meter = new BoostMeter(100f);
        meter.Add(100f);

        float drained = meter.TryDrain(0.5f, 50f); // 25 fuel this frame

        Assert.Equal(25f, drained, 4);
        Assert.Equal(75f, meter.Current, 4);
    }

    [Fact]
    public void Drain_never_goes_below_empty_and_returns_only_what_was_left()
    {
        var meter = new BoostMeter(100f);
        meter.Add(10f);

        float drained = meter.TryDrain(1f, 50f); // wants 50, only 10 left

        Assert.Equal(10f, drained, 4);
        Assert.Equal(0f, meter.Current);
        Assert.True(meter.IsEmpty);
    }

    [Fact]
    public void Draining_an_empty_meter_returns_zero()
    {
        var meter = new BoostMeter(100f);

        float drained = meter.TryDrain(1f, 50f);

        Assert.Equal(0f, drained);
        Assert.True(meter.IsEmpty);
    }

    [Fact]
    public void Zero_capacity_meter_reports_zero_fraction_without_dividing_by_zero()
    {
        var meter = new BoostMeter(0f);

        Assert.Equal(0f, meter.Fraction);
        Assert.True(meter.IsEmpty);
    }
}
