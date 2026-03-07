using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Domain.Tests;

public class SolverChanceSamplerTests
{
    private readonly SolverChanceSampler _sut = new();

    [Fact]
    public void Sample_MissingPrivateCards_DealsWithoutDuplicates()
    {
        var players = CreatePlayers(3);
        var state = CreateState(players, players[0].PlayerId, Street.Preflop);

        var sampled = _sut.Sample(state, new Random(11));

        Assert.Equal(3, sampled.PrivateCardsByPlayer.Count);
        AssertAllCardsUnique(sampled);
    }

    [Fact]
    public void Sample_DoesNotCollideWithDeadBoardOrExistingPrivateCards()
    {
        var players = CreatePlayers(3);
        var knownHole = HoleCards.Parse("AsAh");

        var state = CreateState(
            players,
            players[1].PlayerId,
            Street.Preflop,
            boardCards: [Card.Parse("Kd"), Card.Parse("Qd"), Card.Parse("Jd")],
            deadCards: [Card.Parse("2c"), Card.Parse("3c")],
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards> { [players[0].PlayerId] = knownHole });

        var sampled = _sut.Sample(state, new Random(22));

        Assert.Equal(knownHole, sampled.PrivateCardsByPlayer[players[0].PlayerId]);
        AssertAllCardsUnique(sampled);
    }

    [Fact]
    public void Sample_FlopChance_AddsExactlyThreeCardsAndAdvancesStreet()
    {
        var players = CreatePlayers(2);
        var state = CreateAwaitingBoardState(players, Street.Preflop, boardCards: []);

        var sampled = _sut.Sample(state, new Random(5));

        Assert.Equal(3, sampled.BoardCards.Count);
        Assert.Equal(Street.Flop, sampled.Street);
        AssertAllCardsUnique(sampled);
    }

    [Fact]
    public void Sample_TurnChance_AddsExactlyOneCardAndAdvancesStreet()
    {
        var players = CreatePlayers(2);
        var state = CreateAwaitingBoardState(players, Street.Flop, boardCards: [Card.Parse("2h"), Card.Parse("7d"), Card.Parse("Ks")]);

        var sampled = _sut.Sample(state, new Random(7));

        Assert.Equal(4, sampled.BoardCards.Count);
        Assert.Equal(Street.Turn, sampled.Street);
        AssertAllCardsUnique(sampled);
    }

    [Fact]
    public void Sample_RiverChance_AddsExactlyOneCardAndAdvancesStreet()
    {
        var players = CreatePlayers(2);
        var state = CreateAwaitingBoardState(players, Street.Turn, boardCards: [Card.Parse("2h"), Card.Parse("7d"), Card.Parse("Ks"), Card.Parse("Tc")]);

        var sampled = _sut.Sample(state, new Random(13));

        Assert.Equal(5, sampled.BoardCards.Count);
        Assert.Equal(Street.River, sampled.Street);
        AssertAllCardsUnique(sampled);
    }

    [Fact]
    public void Sample_SeededRng_IsDeterministic()
    {
        var players = CreatePlayers(2);
        var state = CreateState(players, players[0].PlayerId, Street.Preflop);

        var a = _sut.Sample(state, new Random(99));
        var b = _sut.Sample(state, new Random(99));

        Assert.Equal(
            a.PrivateCardsByPlayer.OrderBy(kvp => kvp.Key.Value).Select(kvp => kvp.Value.ToString()).ToArray(),
            b.PrivateCardsByPlayer.OrderBy(kvp => kvp.Key.Value).Select(kvp => kvp.Value.ToString()).ToArray());
        Assert.Equal(a.BoardCards.Select(c => c.ToString()).ToArray(), b.BoardCards.Select(c => c.ToString()).ToArray());
    }

    [Fact]
    public void IsChanceNode_WithNoMissingPrivateCardsAndNoBoardTransition_ReturnsFalse()
    {
        var players = CreatePlayers(2);
        var privateCards = new Dictionary<PlayerId, HoleCards>
        {
            [players[0].PlayerId] = HoleCards.Parse("AsKh"),
            [players[1].PlayerId] = HoleCards.Parse("QdJs")
        };

        var state = CreateState(players, players[0].PlayerId, Street.Preflop, privateCardsByPlayer: privateCards);

        Assert.False(_sut.IsChanceNode(state));
    }

    [Fact]
    public void Sample_PreservesAlreadyAssignedPrivateCards_AndFillsMissing()
    {
        var players = CreatePlayers(3);
        var knownHole = HoleCards.Parse("AsKh");
        var privateCards = new Dictionary<PlayerId, HoleCards> { [players[1].PlayerId] = knownHole };
        var state = CreateState(players, players[0].PlayerId, Street.Preflop, privateCardsByPlayer: privateCards);

        var sampled = _sut.Sample(state, new Random(3));

        Assert.Equal(knownHole, sampled.PrivateCardsByPlayer[players[1].PlayerId]);
        Assert.Equal(3, sampled.PrivateCardsByPlayer.Count);
        AssertAllCardsUnique(sampled);
    }

    private static SolverHandState CreateAwaitingBoardState(IReadOnlyList<SolverPlayerState> players, Street street, IReadOnlyList<Card> boardCards)
    {
        var privateCards = new Dictionary<PlayerId, HoleCards>
        {
            [players[0].PlayerId] = HoleCards.Parse("AsKh"),
            [players[1].PlayerId] = HoleCards.Parse("QdJs")
        };

        var resetPlayers = players.Select(p => p with { CurrentStreetContribution = ChipAmount.Zero }).ToArray();

        return CreateState(
            resetPlayers,
            resetPlayers[0].PlayerId,
            street,
            currentBetSize: ChipAmount.Zero,
            raisesThisStreet: 0,
            boardCards: boardCards,
            privateCardsByPlayer: privateCards);
    }

    private static SolverHandState CreateState(
        IReadOnlyList<SolverPlayerState> players,
        PlayerId actingPlayerId,
        Street street,
        ChipAmount? currentBetSize = null,
        int raisesThisStreet = 1,
        IReadOnlyList<Card>? boardCards = null,
        IReadOnlyList<Card>? deadCards = null,
        IReadOnlyDictionary<PlayerId, HoleCards>? privateCardsByPlayer = null)
        => new(
            config: new GameConfig(6, new ChipAmount(5), new ChipAmount(10), ChipAmount.Zero, new ChipAmount(100)),
            street: street,
            buttonSeatIndex: 1,
            actingPlayerId: actingPlayerId,
            pot: new ChipAmount(players.Sum(p => p.TotalContribution.Value)),
            currentBetSize: currentBetSize ?? new ChipAmount(10),
            lastRaiseSize: new ChipAmount(10),
            raisesThisStreet: raisesThisStreet,
            players: players,
            actionHistory: null,
            boardCards: boardCards,
            deadCards: deadCards,
            privateCardsByPlayer: privateCardsByPlayer);

    private static SolverPlayerState[] CreatePlayers(int count)
        => Enumerable.Range(0, count)
            .Select(i => new SolverPlayerState(PlayerId.New(), i, i == 0 ? Position.SB : Position.BB, new ChipAmount(100), ChipAmount.Zero, ChipAmount.Zero, false, false))
            .ToArray();

    private static void AssertAllCardsUnique(SolverHandState state)
    {
        var cards = new List<Card>();
        cards.AddRange(state.DeadCards);
        cards.AddRange(state.BoardCards);
        cards.AddRange(state.PrivateCardsByPlayer.Values.SelectMany(h => new[] { h.First, h.Second }));

        Assert.Equal(cards.Count, cards.Distinct().Count());
    }
}
