using Scrapline.Core.Contracts;
using Xunit;

namespace Scrapline.Core.Tests.Contracts;

public class RaceResultTests
{
    [Fact]
    public void A_won_result_carries_all_its_fields()
    {
        var result = new RaceResult
        {
            Placement = 1,
            HpRemaining = 60,
            Outcome = RaceOutcome.Won,
            Takedowns = 3,
            ScrapEarned = 250,
        };

        Assert.Equal(1, result.Placement);
        Assert.Equal(60, result.HpRemaining);
        Assert.Equal(RaceOutcome.Won, result.Outcome);
        Assert.Equal(3, result.Takedowns);
        Assert.Equal(250, result.ScrapEarned);
    }

    [Fact]
    public void Takedowns_and_scrap_default_to_zero()
    {
        var result = new RaceResult { Placement = 2, HpRemaining = 10, Outcome = RaceOutcome.FinishedBehind };

        Assert.Equal(0, result.Takedowns);
        Assert.Equal(0, result.ScrapEarned);
    }

    [Fact]
    public void A_wreck_leaves_zero_hp()
    {
        var result = new RaceResult { Placement = 4, HpRemaining = 0, Outcome = RaceOutcome.Wrecked };

        Assert.Equal(RaceOutcome.Wrecked, result.Outcome);
        Assert.Equal(0, result.HpRemaining);
    }
}
