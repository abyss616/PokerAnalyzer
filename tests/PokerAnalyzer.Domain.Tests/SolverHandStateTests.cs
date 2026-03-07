using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverHandStateTests
{
    [Fact]
    public void Constructor_ValidPreflopState_WithMultiplePlayers_ShouldSucceed()
    {
        var p1 = new SolverPlayerState(new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111")), 0, Position.SB, new ChipAmount(95), new ChipAmount(5), new ChipAmount(5), false, false);
        var p2 = new SolverPlayerState(new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222")), 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);
        var p3 = new SolverPlayerState(new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333")), 2, Position.BTN, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var state = CreateState(
            actingPlayerId: p3.PlayerId,
            players: [p1, p2, p3],
            pot: new ChipAmount(15),
            currentBetSize: new ChipAmount(10));

        Assert.Equal(Street.Preflop, state.Street);
        Assert.Equal(3, state.Players.Count);
        Assert.Equal(2, state.ButtonSeatIndex);
        Assert.Equal(15, state.Pot.Value);
        Assert.Equal(10, state.ToCall.Value);
    }

    [Fact]
    public void Constructor_PotMatchesSummedContributions_ShouldPassValidation()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(50), new ChipAmount(10), new ChipAmount(30), false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(50), new ChipAmount(10), new ChipAmount(20), false, false);

        var state = CreateState(
            actingPlayerId: p1.PlayerId,
            players: [p1, p2],
            pot: new ChipAmount(50),
            currentBetSize: new ChipAmount(10));

        Assert.Equal(50, state.Pot.Value);
    }

    [Fact]
    public void Constructor_NegativeStack_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(-1), ChipAmount.Zero, ChipAmount.Zero, false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p2.PlayerId,
                players: [p1, p2],
                pot: ChipAmount.Zero,
                currentBetSize: ChipAmount.Zero));

        Assert.Contains("negative stack", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ActingPlayerFolded_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, true, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: ChipAmount.Zero,
                currentBetSize: ChipAmount.Zero));

        Assert.Contains("folded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_SameActionInputs_ShouldProduceDeterministicSignature()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);

        var actions = new[]
        {
            new SolverActionEntry(p1.PlayerId, ActionType.PostSmallBlind, new ChipAmount(5)),
            new SolverActionEntry(p2.PlayerId, ActionType.PostBigBlind, new ChipAmount(10)),
            new SolverActionEntry(p1.PlayerId, ActionType.Call, new ChipAmount(10))
        };

        var state1 = CreateState(
            actingPlayerId: p2.PlayerId,
            players: [p1, p2],
            pot: new ChipAmount(20),
            currentBetSize: new ChipAmount(10),
            actionHistory: actions);

        var state2 = CreateState(
            actingPlayerId: p2.PlayerId,
            players: [p1, p2],
            pot: new ChipAmount(20),
            currentBetSize: new ChipAmount(10),
            actionHistory: actions);

        Assert.Equal(state1.ActionHistorySignature, state2.ActionHistorySignature);
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
    public void Constructor_NegativeCurrentStreetContribution_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(100), new ChipAmount(-1), ChipAmount.Zero, false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p2.PlayerId,
                players: [p1, p2],
                pot: ChipAmount.Zero,
                currentBetSize: ChipAmount.Zero));

        Assert.Contains("negative current-street contribution", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Constructor_ActingPlayerAllIn_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, ChipAmount.Zero, ChipAmount.Zero, new ChipAmount(100), false, true);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(100),
                currentBetSize: ChipAmount.Zero));

        Assert.Contains("all-in", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Constructor_ActingPlayerAllIn_WithNoActionablePlayers_ShouldBeValid()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, ChipAmount.Zero, ChipAmount.Zero, new ChipAmount(100), false, true);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, ChipAmount.Zero, ChipAmount.Zero, new ChipAmount(100), false, true);

        var ex = Record.Exception(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(200),
                currentBetSize: ChipAmount.Zero));

        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_CurrentBetSizeNotMatchingPlayerContributions_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(20),
                currentBetSize: new ChipAmount(20)));

        Assert.Contains("current bet size mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ActionHistoryCheckFacingBet_ShouldThrow()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(90), new ChipAmount(10), new ChipAmount(10), false, false);

        var actions = new[]
        {
            new SolverActionEntry(p1.PlayerId, ActionType.PostSmallBlind, new ChipAmount(5)),
            new SolverActionEntry(p2.PlayerId, ActionType.PostBigBlind, new ChipAmount(10)),
            new SolverActionEntry(p1.PlayerId, ActionType.Check, ChipAmount.Zero)
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(20),
                currentBetSize: new ChipAmount(10),
                actionHistory: actions));

        Assert.Contains("checked while facing", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Constructor_EquivalentInvalidStates_ShouldThrowDeterministicMessage()
    {
        var p1 = new SolverPlayerState(PlayerId.New(), 0, Position.SB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);
        var p2 = new SolverPlayerState(PlayerId.New(), 1, Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false);

        var ex1 = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(1),
                currentBetSize: ChipAmount.Zero));

        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            CreateState(
                actingPlayerId: p1.PlayerId,
                players: [p1, p2],
                pot: new ChipAmount(1),
                currentBetSize: ChipAmount.Zero));

        Assert.Equal(ex1.Message, ex2.Message);
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
