using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverTraversalGuardsTests
{
    [Fact]
    public void IsCompletedPreflopState_ReturnsTrue_ForLimpedPotWithBigBlindOptionChecked()
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
                new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(2)),
                new SolverActionEntry(bb.PlayerId, ActionType.Check, ChipAmount.Zero)
            ]);

        Assert.True(SolverTraversalGuards.IsCompletedPreflopState(state));
    }

    [Fact]
    public void IsCompletedPreflopState_ReturnsTrue_ForRaisedAndCalledPreflopState()
    {
        var sb = Player(0, Position.SB, stack: 88, street: 12, total: 12);
        var bb = Player(1, Position.BB, stack: 88, street: 12, total: 12);

        var state = CreateState(
            actingPlayerId: sb.PlayerId,
            players: [sb, bb],
            pot: 24,
            currentBetSize: 12,
            lastRaiseSize: 10,
            raisesThisStreet: 1,
            actionHistory:
            [
                new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sb.PlayerId, ActionType.Raise, new ChipAmount(12)),
                new SolverActionEntry(bb.PlayerId, ActionType.Call, new ChipAmount(12))
            ]);

        Assert.True(SolverTraversalGuards.IsCompletedPreflopState(state));
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
