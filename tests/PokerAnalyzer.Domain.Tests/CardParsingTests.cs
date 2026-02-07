using PokerAnalyzer.Domain.Cards;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class CardParsingTests
{
    [Fact]
    public void Parse_As_ShouldWork()
    {
        var c = Card.Parse("As");
        Assert.Equal(Rank.Ace, c.Rank);
        Assert.Equal(Suit.Spades, c.Suit);
        Assert.Equal("As", c.ToString());
    }

    [Fact]
    public void HoleCards_Parse_ShouldWork()
    {
        var hc = HoleCards.Parse("AsKh");
        Assert.NotEqual(hc.First, hc.Second);
        Assert.Equal("AsKh", hc.ToString());
    }
}
