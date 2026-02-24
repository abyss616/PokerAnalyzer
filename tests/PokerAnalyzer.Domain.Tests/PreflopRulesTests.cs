using PokerAnalyzer.Domain.PreflopTree;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class PreflopRulesTests
{
    [Fact]
    public void CreateInitialState_HeadsUp_PostsBlindsAndSetsActingIndex()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 2, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        Assert.Equal(1, state.ContribBb[0]);
        Assert.Equal(2, state.ContribBb[1]);
        Assert.Equal(99, state.StackBb[0]);
        Assert.Equal(98, state.StackBb[1]);
        Assert.Equal(3, state.PotBb);
        Assert.Equal(0, state.ActingIndex);
    }

    [Fact]
    public void CreateInitialState_ThreeMax_FirstToActWrapsToButton()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 3, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        Assert.Equal(1, state.ContribBb[1]);
        Assert.Equal(2, state.ContribBb[2]);
        Assert.Equal(0, state.ActingIndex);
    }

    [Fact]
    public void CreateInitialState_FourMax_FirstToActIsUtg()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        Assert.Equal(3, state.ActingIndex);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void CreateInitialState_FiveOrSixMax_FirstToActIsIndexThree(int playerCount)
    {
        var state = PreflopRules.CreateInitialState(playerCount, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        Assert.Equal(3, state.ActingIndex);
    }

    [Fact]
    public void GetNextActingIndex_SkipsFoldedPlayers()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);
        state.InHand[0] = false;
        state.ActingIndex = 3;

        var next = PreflopRules.GetNextActingIndex(state);

        Assert.Equal(1, next);
    }
}
