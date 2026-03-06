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
