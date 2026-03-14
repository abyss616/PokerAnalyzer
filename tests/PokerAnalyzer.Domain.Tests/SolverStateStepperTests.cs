using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverStateStepperTests
{
    [Fact]
    public void Step_Fold_UpdatesFoldStatus_AndAdvancesToNextActivePlayer()
    {
        var state = CreateFacingBetState();

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Fold));

        Assert.True(next.Players.Single(p => p.PlayerId == state.ActingPlayerId).IsFolded);
        Assert.Equal(state.Players[2].PlayerId, next.ActingPlayerId);
    }

    [Fact]
    public void Step_Check_OnCheckedToState_AdvancesActingPlayer()
    {
        var state = CreateCheckedToState();

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Check));

        Assert.Equal(state.Players[1].PlayerId, next.ActingPlayerId);
        Assert.Equal(state.Pot, next.Pot);
    }

    [Fact]
    public void Step_Call_UpdatesPotStackAndContributions()
    {
        var state = CreateFacingBetState();

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Call, new ChipAmount(10)));
        var acted = next.Players.Single(p => p.PlayerId == state.ActingPlayerId);

        Assert.Equal(80, acted.Stack.Value);
        Assert.Equal(20, acted.CurrentStreetContribution.Value);
        Assert.Equal(20, acted.TotalContribution.Value);
        Assert.Equal(40, next.Pot.Value);
        Assert.Equal(state.Players[2].PlayerId, next.ActingPlayerId);
    }
    [Fact]
    public void Step_Bet_UpdatesBettingFields_AndAdvancesActingPlayer()
    {
        var state = CreateCheckedToState();

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Bet, new ChipAmount(25)));
        var acted = next.Players.Single(p => p.PlayerId == state.ActingPlayerId);

        Assert.Equal(25, next.CurrentBetSize.Value);
        Assert.Equal(25, next.LastRaiseSize.Value);
        Assert.Equal(state.RaisesThisStreet + 1, next.RaisesThisStreet);
        Assert.Equal(75, acted.Stack.Value);
        Assert.Equal(state.Players[1].PlayerId, next.ActingPlayerId);
    }

    [Fact]
    public void Step_Raise_UpdatesBettingFields_AndReopensAction()
    {
        var state = CreateFacingBetState();

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Raise, new ChipAmount(30)));

        Assert.Equal(30, next.CurrentBetSize.Value);
        Assert.Equal(10, next.LastRaiseSize.Value);
        Assert.Equal(state.RaisesThisStreet + 1, next.RaisesThisStreet);
        Assert.Equal(state.Players[1].PlayerId, next.ActingPlayerId);
    }

    [Fact]
    public void Step_AllIn_UsesCurrentDomainSemantics_AsAggressiveJam()
    {
        var p1 = Player(0, Position.SB, stack: 90, street: 10, total: 10);
        var p2 = Player(1, Position.BB, stack: 10, street: 20, total: 20);
        var state = CreateState(p2.PlayerId, [p1, p2], pot: 30, currentBetSize: 20, lastRaiseSize: 10, raisesThisStreet: 1);

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.AllIn));

        Assert.Equal(40, next.Pot.Value);
        Assert.Equal(p1.PlayerId, next.ActingPlayerId);
        Assert.Equal(30, next.CurrentBetSize.Value);
        Assert.Equal(20, next.ToCall.Value);
        Assert.True(next.Players.Single(p => p.PlayerId == p2.PlayerId).IsAllIn);
    }

    [Fact]
    public void Step_BettingRoundCompletion_ResetsStreetBettingFields()
    {
        var p1 = Player(0, Position.SB, stack: 80, street: 20, total: 20);
        var p2 = Player(1, Position.BB, stack: 80, street: 20, total: 20);

        var state = CreateState(
            p2.PlayerId,
            [p1, p2],
            pot: 40,
            currentBetSize: 20,
            lastRaiseSize: 10,
            raisesThisStreet: 1,
            actionHistory:
            [
                new SolverActionEntry(p1.PlayerId, ActionType.PostSmallBlind, new ChipAmount(10)),
        new SolverActionEntry(p2.PlayerId, ActionType.PostBigBlind, new ChipAmount(20)),
        new SolverActionEntry(p1.PlayerId, ActionType.Call, new ChipAmount(20))
            ]);

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Check));

        Assert.Equal(0, next.CurrentBetSize.Value);
        Assert.Equal(0, next.RaisesThisStreet);
        Assert.All(next.Players, p => Assert.Equal(0, p.CurrentStreetContribution.Value));
        Assert.Empty(next.GenerateLegalActions());
    }


    [Fact]
    public void Step_WithPlayersProvidedOutOfOrder_UsesSeatIndexedTraversal()
    {
        var p1 = Player(0, Position.SB, stack: 90, street: 10, total: 10);
        var p2 = Player(1, Position.BB, stack: 80, street: 20, total: 20);
        var p3 = Player(2, Position.BTN, stack: 100, street: 0, total: 0);

        var state = CreateState(
            p1.PlayerId,
            [p3, p1, p2],
            pot: 30,
            currentBetSize: 20,
            lastRaiseSize: 10,
            raisesThisStreet: 1,
            actionHistory:
            [
                new SolverActionEntry(p1.PlayerId, ActionType.PostSmallBlind, new ChipAmount(10)),
                new SolverActionEntry(p2.PlayerId, ActionType.PostBigBlind, new ChipAmount(20))
            ]);

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Call, new ChipAmount(20)));

        Assert.Equal(p3.PlayerId, next.ActingPlayerId);
    }

    [Fact]
    public void Step_ResultingState_RemainsValid()
    {
        var state = CreateFacingBetState();

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Call, new ChipAmount(10)));

        var ex = Record.Exception(() =>
            _ = new SolverHandState(
                next.Config,
                next.Street,
                next.ButtonSeatIndex,
                next.ActingPlayerId,
                next.Pot,
                next.CurrentBetSize,
                next.LastRaiseSize,
                next.RaisesThisStreet,
                next.Players,
                next.ActionHistory,
                next.BoardCards,
                next.DeadCards,
                next.PrivateCardsByPlayer));
        Assert.Null(ex);
    }

    [Fact]
    public void Step_EquivalentInputs_AreDeterministic()
    {
        var stateA = CreateFacingBetState();
        var stateB = CreateState(
            stateA.ActingPlayerId,
            stateA.Players,
            stateA.Pot.Value,
            stateA.CurrentBetSize.Value,
            stateA.LastRaiseSize.Value,
            stateA.RaisesThisStreet,
            stateA.ActionHistory);

        var nextA = SolverStateStepper.Step(stateA, new LegalAction(ActionType.Call, new ChipAmount(10)));
        var nextB = SolverStateStepper.Step(stateB, new LegalAction(ActionType.Call, new ChipAmount(10)));

        Assert.Equal(nextA.ActionHistorySignature, nextB.ActionHistorySignature);
        Assert.Equal(nextA.Pot, nextB.Pot);
        Assert.Equal(nextA.CurrentBetSize, nextB.CurrentBetSize);
        Assert.Equal(nextA.LastRaiseSize, nextB.LastRaiseSize);
        Assert.Equal(nextA.RaisesThisStreet, nextB.RaisesThisStreet);
        Assert.Equal(nextA.ActingPlayerId, nextB.ActingPlayerId);
        Assert.Equal(nextA.Street, nextB.Street);
        Assert.Equal(nextA.ToCall, nextB.ToCall);
        Assert.Equal(nextA.Players, nextB.Players);
        Assert.Equal(nextA.ActionHistory, nextB.ActionHistory);
    }
    [Fact]
    public void Step_IllegalAction_ThrowsUsefulMessage()
    {
        var state = CreateFacingBetState();

        var ex = Assert.Throws<InvalidOperationException>(() => SolverStateStepper.Step(state, new LegalAction(ActionType.Check)));

        Assert.Contains("not legal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Step_ApplyingGeneratedBetAction_Succeeds()
    {
        var state = CreateCheckedToState();
        var bet = state.GenerateLegalActions().Single(a => a.ActionType == ActionType.Bet);

        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, bet));

        Assert.Null(ex);
    }

    [Fact]
    public void Step_ApplyingGeneratedBetAction_WhenPriorContributionIsNonZero_Succeeds()
    {
        var p1 = Player(0, Position.SB, stack: 90, street: 10, total: 10);
        var p2 = Player(1, Position.BB, stack: 90, street: 10, total: 10);
        var state = CreateState(
            actingPlayerId: p1.PlayerId,
            players: [p1, p2],
            pot: 20,
            currentBetSize: 10,
            lastRaiseSize: 10,
            raisesThisStreet: 1);

        var bet = state.GenerateLegalActions().Single(a => a.ActionType == ActionType.Bet);
        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, bet));

        Assert.True(bet.Amount > p1.CurrentStreetContribution);
        Assert.Null(ex);
    }

    [Fact]
    public void Step_CheckedToGeneratedActions_AreExecutable()
    {
        var state = CreateCheckedToState();
        var actions = state.GenerateLegalActions();
        var exceptions = actions.Select(action => Record.Exception(() => _ = SolverStateStepper.Step(state, action))).ToArray();

        Assert.Contains(actions, action => action.ActionType == ActionType.Check);
        Assert.Contains(actions, action => action.ActionType == ActionType.Bet && action.Amount is not null);
        Assert.All(exceptions, ex => Assert.Null(ex));
    }
    [Fact]
    public void Step_ApplyingGeneratedRaiseAction_Succeeds()
    {
        var state = CreateFacingBetState();
        var raise = state.GenerateLegalActions().Single(a => a.ActionType == ActionType.Raise);

        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, raise));

        Assert.Null(ex);
    }

    [Fact]
    public void Step_ApplyingGeneratedRaiseAction_TargetExceedsPriorContribution()
    {
        var state = CreateFacingBetState();
        var acting = state.Players.Single(p => p.PlayerId == state.ActingPlayerId);
        var raise = state.GenerateLegalActions().Single(a => a.ActionType == ActionType.Raise);

        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, raise));

        Assert.True(raise.Amount > acting.CurrentStreetContribution);
        Assert.Null(ex);
    }

    [Fact]
    public void Step_UnopenedPreflopGeneratedActions_AreExecutable()
    {
        var sb = Player(0, Position.SB, stack: 99, street: 1, total: 1);
        var bb = Player(1, Position.BB, stack: 98, street: 2, total: 2);
        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 3,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2))
            ]);

        var actions = state.GenerateLegalActions();
        var exceptions = actions.Select(action => Record.Exception(() => _ = SolverStateStepper.Step(state, action))).ToArray();

        Assert.Contains(actions, action => action.ActionType == ActionType.Fold);
        Assert.Contains(actions, action => action.ActionType == ActionType.Call);
        Assert.Contains(actions, action => action.ActionType == ActionType.Raise);
        Assert.All(exceptions, ex => Assert.Null(ex));
    }

    [Fact]
    public void Step_FacingLimpPreflopGeneratedActions_AreExecutable()
    {
        var sb = Player(0, Position.SB, stack: 98, street: 2, total: 2);
        var bb = Player(1, Position.BB, stack: 98, street: 2, total: 2);
        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(bb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var actions = state.GenerateLegalActions();
        var exceptions = actions.Select(action => Record.Exception(() => _ = SolverStateStepper.Step(state, action))).ToArray();

        Assert.Equal(
            [
                new LegalAction(ActionType.Fold),
                new LegalAction(ActionType.Call, new ChipAmount(2)),
                new LegalAction(ActionType.Raise, new ChipAmount(11)),
                new LegalAction(ActionType.Raise, new ChipAmount(18))
            ],
            actions);
        Assert.All(exceptions, ex => Assert.Null(ex));
    }






    [Fact]
    public void Step_NonBigBlindFacingLimp_CheckRemainsIllegal()
    {
        var sb = Player(0, Position.SB, stack: 98, street: 2, total: 2);
        var bb = Player(1, Position.BB, stack: 98, street: 2, total: 2);
        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(bb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, new LegalAction(ActionType.Check)));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Step_BigBlindOptionVsLimp_CheckCompletesPreflopRound()
    {
        var sb = Player(0, Position.SB, stack: 98, street: 2, total: 2);
        var bb = Player(1, Position.BB, stack: 98, street: 2, total: 2);
        var state = CreateState(
            actingPlayerId: bb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Check));

        Assert.Equal(0, next.CurrentBetSize.Value);
        Assert.Equal(0, next.RaisesThisStreet);
        Assert.All(next.Players, p => Assert.Equal(0, p.CurrentStreetContribution.Value));
        Assert.Empty(next.GenerateLegalActions());
    }

    [Fact]
    public void Step_BigBlindOptionVsLimp_CheckDoesNotCreateInvalidExtraPreflopAction()
    {
        var sb = Player(0, Position.SB, stack: 98, street: 2, total: 2);
        var bb = Player(1, Position.BB, stack: 98, street: 2, total: 2);
        var state = CreateState(
            actingPlayerId: bb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var next = SolverStateStepper.Step(state, new LegalAction(ActionType.Check));
        var extraActionAttempt = Record.Exception(() => SolverStateStepper.Step(next, new LegalAction(ActionType.Raise, new ChipAmount(11))));

        Assert.IsType<InvalidOperationException>(extraActionAttempt);
        Assert.Contains("not legal", extraActionAttempt!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Step_BigBlindOptionVsLimp_RaiseIsExecutable()
    {
        var sb = Player(0, Position.SB, stack: 98, street: 2, total: 2);
        var bb = Player(1, Position.BB, stack: 98, street: 2, total: 2);
        var state = CreateState(
            actingPlayerId: bb.PlayerId,
            players: [sb, bb],
            pot: 4,
            currentBetSize: 2,
            lastRaiseSize: 2,
            raisesThisStreet: 0,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(2))
            ]);

        var raise = state.GenerateLegalActions().Single(a => a.ActionType == ActionType.Raise && a.Amount == new ChipAmount(11));
        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, raise));

        Assert.Null(ex);
    }

    [Fact]
    public void Step_ApplyingGeneratedBetAction_WithPriorContribution100_Succeeds()
    {
        var p1 = Player(0, Position.SB, stack: 100, street: 100, total: 100);
        var p2 = Player(1, Position.BB, stack: 100, street: 100, total: 100);
        var state = CreateState(
            actingPlayerId: p1.PlayerId,
            players: [p1, p2],
            pot: 200,
            currentBetSize: 100,
            lastRaiseSize: 50,
            raisesThisStreet: 1);

        var provider = new FixedSizeProvider(
            betSizes: [new ChipAmount(100), new ChipAmount(150)],
            raiseSizes: Array.Empty<ChipAmount>());

        var bet = state.GenerateLegalActions(provider).Single(a => a.ActionType == ActionType.Bet);
        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, bet));

        Assert.True(bet.Amount > p1.CurrentStreetContribution);
        Assert.Null(ex);
    }

    [Fact]
    public void Step_ApplyingGeneratedRaiseAction_WithPriorContribution100_Succeeds()
    {
        var p1 = Player(0, Position.SB, stack: 100, street: 100, total: 100);
        var p2 = Player(1, Position.BB, stack: 60, street: 140, total: 140);
        var state = CreateState(
            actingPlayerId: p1.PlayerId,
            players: [p1, p2],
            pot: 240,
            currentBetSize: 140,
            lastRaiseSize: 20,
            raisesThisStreet: 1);

        var provider = new FixedSizeProvider(
            betSizes: Array.Empty<ChipAmount>(),
            raiseSizes: [new ChipAmount(100), new ChipAmount(160)]);

        var raise = state.GenerateLegalActions(provider).Single(a => a.ActionType == ActionType.Raise);
        var ex = Record.Exception(() => _ = SolverStateStepper.Step(state, raise));

        Assert.True(raise.Amount > p1.CurrentStreetContribution);
        Assert.Null(ex);
    }

    private SolverHandState CreateFacingBetState()
    {
        var p1 = Player(0, Position.SB, stack: 90, street: 10, total: 10);
        var p2 = Player(1, Position.BB, stack: 80, street: 20, total: 20);
        var p3 = Player(2, Position.BTN, stack: 100, street: 0, total: 0);

        return CreateState(
            p1.PlayerId,
            [p1, p2, p3],
            pot: 30,
            currentBetSize: 20,
            lastRaiseSize: 10,
            raisesThisStreet: 1,
            actionHistory:
            [
                new SolverActionEntry(p1.PlayerId, ActionType.PostSmallBlind, new ChipAmount(10)),
            new SolverActionEntry(p2.PlayerId, ActionType.PostBigBlind, new ChipAmount(20))
            ]);
    }

    private static SolverHandState CreateCheckedToState()
    {
        var p1 = Player(0, Position.SB, stack: 100, street: 0, total: 0);
        var p2 = Player(1, Position.BB, stack: 100, street: 0, total: 0);
        return CreateState(p1.PlayerId, [p1, p2], pot: 0, currentBetSize: 0, lastRaiseSize: 10, raisesThisStreet: 0);
    }

    private static SolverPlayerState Player(int seat, Position pos, long stack, long street, long total)
        => new(PlayerId.New(), seat, pos, new ChipAmount(stack), new ChipAmount(street), new ChipAmount(total), false, false);



    private sealed class FixedSizeProvider(IReadOnlyList<ChipAmount> betSizes, IReadOnlyList<ChipAmount> raiseSizes) : IBetSizeSetProvider
    {
        public IReadOnlyList<ChipAmount> GetBetSizes(SolverHandState state) => betSizes;

        public IReadOnlyList<ChipAmount> GetRaiseSizes(SolverHandState state) => raiseSizes;
    }

    private static SolverHandState CreateState(
        PlayerId actingPlayerId,
        IReadOnlyList<SolverPlayerState> players,
        long pot,
        long currentBetSize,
        long lastRaiseSize,
        int raisesThisStreet,
        IReadOnlyList<SolverActionEntry>? actionHistory = null)
        => new(
            config: new GameConfig(6, new ChipAmount(5), new ChipAmount(10), ChipAmount.Zero, new ChipAmount(100)),
            street: Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: actingPlayerId,
            pot: new ChipAmount(pot),
            currentBetSize: new ChipAmount(currentBetSize),
            lastRaiseSize: new ChipAmount(lastRaiseSize),
            raisesThisStreet: raisesThisStreet,
            players: players,
            actionHistory: actionHistory,
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: null);
}
