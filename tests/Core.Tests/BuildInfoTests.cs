using Scrapline.Core;
using Xunit;

namespace Scrapline.Core.Tests;

public class BuildInfoTests
{
    [Fact]
    public void Greeting_identifies_the_core()
    {
        Assert.Equal("Scrapline Core online.", BuildInfo.Greeting());
    }
}
