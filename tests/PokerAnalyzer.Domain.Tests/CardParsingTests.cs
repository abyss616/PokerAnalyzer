using PokerAnalyzer.Domain.Cards;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class CardParsingTests
{
    [Fact]
    public void HoleCards_Parse_ShouldWork()
    {
        var hc = HoleCards.Parse("AsKh");
        Assert.NotEqual(hc.First, hc.Second);
        Assert.Equal("AsKh", hc.ToString());
    }

    [Fact]
    public void HoleCards_Parse_ShouldSupportTenNotation()
    {
        var hc = HoleCards.Parse("Jc10c");
        Assert.NotEqual(hc.First, hc.Second);
        Assert.Equal("JcTc", hc.ToString());
    }
}
