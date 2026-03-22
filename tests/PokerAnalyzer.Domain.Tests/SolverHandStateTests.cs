using System.Collections.ObjectModel;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverHandStateTests
{



    [Fact]
    public void Validate_ValidState_ReturnsValidResult()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(95), new ChipAmount(5), new ChipAmount(5), false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);

        var state = CreateState(
            actingPlayerId: p1.PlayerId,
            players: [p1, p2],
            pot: new ChipAmount(15),
            currentBetSize: new ChipAmount(10),
            actionHistory:
            [
                new SolverActionEntry(p1.PlayerId, ActionType.PostSmallBlind, new ChipAmount(5)),
                new SolverActionEntry(p2.PlayerId, ActionType.PostBigBlind, new ChipAmount(10))
            ]);

        var result = state.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }






    [Fact]
    public void Constructor_PotMismatch_ShouldThrowWithExpectedAmounts()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(95), new ChipAmount(5), new ChipAmount(5), false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(22),
                currentBetSize: new ChipAmount(10)));

        Assert.Contains("pot is 22", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contributions are 15", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Constructor_ActingPlayerMissing_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: PlayerId.New(),
                players: [p1, p2],
                pot: ChipAmount.Zero,
                currentBetSize: ChipAmount.Zero));

        Assert.Contains("not seated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }




    [Fact]
    public void Constructor_DuplicateCardsAcrossBoardAndPrivate_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);
        var aceSpades = Card.Parse("As");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: ChipAmount.Zero,
                currentBetSize: ChipAmount.Zero,
                boardCards: [aceSpades],
                privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
                {
                    [p1.PlayerId] = new HoleCards(aceSpades, Card.Parse("Kd"))
                }));

        Assert.Contains("duplicate card", ex.Message, StringComparison.OrdinalIgnoreCase);
    }










    private static SolverHandState CreateState(
        PlayerId actingPlayerId,
        IEnumerable<SolverPlayerState> players,
        ChipAmount pot,
        ChipAmount currentBetSize,
        IEnumerable<SolverActionEntry>? actionHistory = null,
        IEnumerable<Card>? boardCards = null,
        IReadOnlyDictionary<PlayerId, HoleCards>? privateCardsByPlayer = null)
        => new(
            config: new GameConfig(6, new ChipAmount(5), new ChipAmount(10), ChipAmount.Zero, new ChipAmount(100)),
            street: Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: actingPlayerId,
            pot: pot,
            currentBetSize: currentBetSize,
            lastRaiseSize: new ChipAmount(10),
            raisesThisStreet: 1,
            players: players,
            actionHistory: actionHistory,
            boardCards: boardCards,
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: privateCardsByPlayer);
}
