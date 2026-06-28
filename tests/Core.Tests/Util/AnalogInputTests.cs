using Scrapline.Core.Util;
using Xunit;

namespace Scrapline.Core.Tests.Util;

public class AnalogInputTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.1f)]
    [InlineData(-0.2f)]
    public void Inside_the_deadzone_returns_zero(float value)
    {
        Assert.Equal(0f, AnalogInput.ApplyDeadzone(value, 0.2f), 4);
    }

    [Fact]
    public void Full_deflection_stays_full_in_both_directions()
    {
        Assert.Equal(1f, AnalogInput.ApplyDeadzone(1f, 0.2f), 4);
        Assert.Equal(-1f, AnalogInput.ApplyDeadzone(-1f, 0.2f), 4);
    }

    [Fact]
    public void Just_past_the_deadzone_starts_near_zero_not_at_the_threshold()
    {
        // The whole point of rescaling: no discontinuity at the deadzone edge.
        float justPast = AnalogInput.ApplyDeadzone(0.201f, 0.2f);
        Assert.True(justPast > 0f && justPast < 0.01f, $"expected ~0, got {justPast}");
    }

    [Fact]
    public void Midpoint_is_rescaled_across_the_live_range()
    {
        // 0.6 with a 0.2 deadzone -> (0.6 - 0.2) / (1 - 0.2) = 0.5
        Assert.Equal(0.5f, AnalogInput.ApplyDeadzone(0.6f, 0.2f), 4);
    }
}
