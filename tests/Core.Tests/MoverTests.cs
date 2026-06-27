using System.Numerics;
using Scrapline.Core;
using Xunit;

namespace Scrapline.Core.Tests;

public class MoverTests
{
    [Fact]
    public void No_input_means_no_movement()
    {
        var start = new Vector2(10, 20);
        Assert.Equal(start, Mover.Step(start, Vector2.Zero, speed: 100f, deltaSeconds: 0.016f));
    }

    [Fact]
    public void Distance_scales_with_speed_and_delta()
    {
        // 100 px/sec for half a second along +X = 50 px.
        var result = Mover.Step(Vector2.Zero, new Vector2(1, 0), speed: 100f, deltaSeconds: 0.5f);

        Assert.Equal(50f, result.X, 3);
        Assert.Equal(0f, result.Y, 3);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    public void Cardinal_input_moves_at_full_speed(float ix, float iy)
    {
        var result = Mover.Step(Vector2.Zero, new Vector2(ix, iy), speed: 100f, deltaSeconds: 1f);

        Assert.Equal(100f, result.Length(), 3);
    }

    [Fact]
    public void Diagonal_is_not_faster_than_cardinal()
    {
        var result = Mover.Step(Vector2.Zero, new Vector2(1, 1), speed: 100f, deltaSeconds: 1f);

        // Un-normalized this would be ~141 px; the normalization rule caps it at 100.
        Assert.Equal(100f, result.Length(), 3);
    }
}
