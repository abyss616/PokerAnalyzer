using PokerAnalyzer.Domain.PreflopTree;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class PreflopRulesTests
{
    // These tests use blind-denominated integer units where SB=1 and BB=2 ("half-BB" units).
    // PreflopActionType.RaiseTo is always an absolute "raise-to" total contribution in this unit.

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
        Assert.Equal(1, state.LastAggressorIndex);
        Assert.False(state.BettingClosed);
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

    [Fact]
    public void GetLegalActions_UtgFacingBigBlind_ContainsFoldCallRaiseAndAllIn()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        var legal = PreflopRules.GetLegalActions(state, PreflopSizingConfig.Default);

        Assert.Contains(legal, a => a.Type == PreflopActionType.Fold);
        Assert.Contains(legal, a => a.Type == PreflopActionType.Call);
        Assert.Contains(legal, a => a.Type == PreflopActionType.RaiseTo && a.RaiseToBb == 9);
        Assert.Contains(legal, a => a.Type == PreflopActionType.RaiseTo && a.RaiseToBb == 11);
        Assert.Contains(legal, a => a.Type == PreflopActionType.AllIn);
        Assert.DoesNotContain(legal, a => a.Type == PreflopActionType.Check);
    }

    [Fact]
    public void GetLegalActions_SbAfterUtgAndBtnFold_ToCallIsOneWithFoldAndCallOnly()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Fold));
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Fold));

        var toCall = PreflopRules.GetToCall(state, state.ActingIndex);
        var legal = PreflopRules.GetLegalActions(state, PreflopSizingConfig.Default);

        Assert.Equal(1, toCall);
        Assert.Contains(legal, a => a.Type == PreflopActionType.Fold);
        Assert.Contains(legal, a => a.Type == PreflopActionType.Call);
        Assert.DoesNotContain(legal, a => a.Type == PreflopActionType.Check);
    }

    [Fact]
    public void GetLegalActions_BigBlindAfterCalls_CanCheckButCannotCall()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call));
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call));
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call));

        var toCall = PreflopRules.GetToCall(state, state.ActingIndex);
        var legal = PreflopRules.GetLegalActions(state, PreflopSizingConfig.Default);

        Assert.Equal(2, state.ActingIndex);
        Assert.Equal(0, toCall);
        Assert.Contains(legal, a => a.Type == PreflopActionType.Check);
        Assert.DoesNotContain(legal, a => a.Type == PreflopActionType.Call);
    }

    [Fact]
    public void HeadsUp_Limp_DoesNotCloseBeforeBigBlindOption()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 2, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call));

        Assert.False(state.BettingClosed);
        Assert.False(PreflopRules.IsTerminal(state, out _));
        Assert.Equal(1, state.ActingIndex);
        Assert.Equal(0, PreflopRules.GetToCall(state, state.ActingIndex));

        var legal = PreflopRules.GetLegalActions(state, PreflopSizingConfig.Default);
        Assert.Contains(legal, a => a.Type == PreflopActionType.Check);
        Assert.Contains(legal, a => a.Type == PreflopActionType.RaiseTo);
    }

    [Fact]
    public void ApplyAction_CheckWhenFacingBet_ThrowsInvalidOperationException()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        Assert.Throws<InvalidOperationException>(() =>
            PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Check)));
    }

    [Fact]
    public void ApplyAction_Call_UpdatesPotAndAdvancesActingIndex()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        var next = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call));

        Assert.Equal(2, next.ContribBb[3]);
        Assert.Equal(98, next.StackBb[3]);
        Assert.Equal(5, next.PotBb);
        Assert.Equal(0, next.ActingIndex);
    }

    [Fact]
    public void OpenCallClose_BettingClosedAndTerminal()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.RaiseTo, 4)); // UTG opens to 2bb (4 units)
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Fold)); // BTN
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call)); // SB
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call)); // BB

        Assert.True(state.BettingClosed);
        Assert.True(PreflopRules.IsTerminal(state, out var reason));
        Assert.Equal("BettingClosed", reason);
    }

    [Fact]
    public void ThreeBetSpot_ClosesAfterOriginalRaiserCalls()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 4, stackBb: 100, smallBlindBb: 1, bigBlindBb: 2);

        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.RaiseTo, 4)); // UTG opens to 2bb (4 units)
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.RaiseTo, 9)); // BTN
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Fold)); // SB
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Fold)); // BB
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call)); // UTG

        Assert.True(PreflopRules.IsTerminal(state, out var reason));
        Assert.Equal("BettingClosed", reason);
    }

    [Fact]
    public void HeadsUp_AllInThenCall_TerminalAllIn()
    {
        var state = PreflopRules.CreateInitialState(playerCount: 2, stackBb: 10, smallBlindBb: 1, bigBlindBb: 2);

        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.AllIn)); // SB
        state = PreflopRules.ApplyAction(state, new PreflopAction(PreflopActionType.Call)); // BB

        Assert.True(PreflopRules.IsTerminal(state, out var reason));
        Assert.Equal("AllIn", reason);
    }

    [Fact]
    public void IsTerminal_AllRemainingPlayersAllIn_NeverReturnsBettingClosed()
    {
        var state = new PreflopPublicState
        {
            PlayerCount = 3,
            InHand = [true, false, true],
            ContribBb = [10, 0, 10],
            StackBb = [0, 0, 0],
            PotBb = 20,
            CurrentToCallBb = 10,
            BettingClosed = true,
            ActingIndex = 0
        };

        Assert.True(PreflopRules.IsTerminal(state, out var reason));
        Assert.Equal("AllIn", reason);
    }
}
