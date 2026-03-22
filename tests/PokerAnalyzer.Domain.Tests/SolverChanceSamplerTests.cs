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
    public void IsChanceNode_CompletedPreflopRoundAwaitingFlop_ReturnsTrue()
    {
        var players = CreatePlayers(2);
        var state = CreateAwaitingBoardState(players, Street.Preflop, boardCards: []);

        Assert.True(_sut.IsChanceNode(state));
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
            actionHistory: street == Street.Preflop
                ? BuildCompletedPreflopHistory(resetPlayers)
                : BuildCompletedPostflopHistory(resetPlayers),
            boardCards: boardCards,
            privateCardsByPlayer: privateCards);
    }

    private static IReadOnlyList<SolverActionEntry> BuildCompletedPreflopHistory(IReadOnlyList<SolverPlayerState> players)
    {
        var sb = players.First(p => p.Position == Position.SB);
        var bb = players.First(p => p.Position == Position.BB);

        return
        [
            new SolverActionEntry(sb.PlayerId, ActionType.PostSmallBlind, new ChipAmount(5)),
            new SolverActionEntry(bb.PlayerId, ActionType.PostBigBlind, new ChipAmount(10)),
            new SolverActionEntry(sb.PlayerId, ActionType.Call, new ChipAmount(5)),
            new SolverActionEntry(bb.PlayerId, ActionType.Check, ChipAmount.Zero)
        ];
    }

    private static IReadOnlyList<SolverActionEntry> BuildCompletedPostflopHistory(IReadOnlyList<SolverPlayerState> players)
    {
        var first = players[0];
        var second = players[1];
        return
        [
            new SolverActionEntry(first.PlayerId, ActionType.Check, ChipAmount.Zero),
            new SolverActionEntry(second.PlayerId, ActionType.Check, ChipAmount.Zero)
        ];
    }

    private static SolverHandState CreateState(
        IReadOnlyList<SolverPlayerState> players,
        PlayerId actingPlayerId,
        Street street,
        ChipAmount? pot = null,
        ChipAmount? currentBetSize = null,
        int raisesThisStreet = 1,
        IReadOnlyList<SolverActionEntry>? actionHistory = null,
        IReadOnlyList<Card>? boardCards = null,
        IReadOnlyList<Card>? deadCards = null,
        IReadOnlyDictionary<PlayerId, HoleCards>? privateCardsByPlayer = null)
        => new(
            config: new GameConfig(6, new ChipAmount(5), new ChipAmount(10), ChipAmount.Zero, new ChipAmount(100)),
            street: street,
            buttonSeatIndex: 1,
            actingPlayerId: actingPlayerId,
            pot: pot ?? new ChipAmount(players.Sum(p => p.TotalContribution.Value)),
            currentBetSize: currentBetSize ?? new ChipAmount(10),
            lastRaiseSize: new ChipAmount(10),
            raisesThisStreet: raisesThisStreet,
            players: players,
            actionHistory: actionHistory,
            boardCards: boardCards,
            deadCards: deadCards,
            privateCardsByPlayer: privateCardsByPlayer);

    private static SolverPlayerState[] CreatePlayers(int count)
    => Enumerable.Range(0, count)
        .Select(i =>
        {
            var posted = i switch
            {
                0 => new ChipAmount(5),   // SB
                1 => new ChipAmount(10),  // BB
                _ => ChipAmount.Zero
            };

            return new SolverPlayerState(
                PlayerId.New(),
                i,
                i switch
                {
                    0 => Position.SB,
                    1 => Position.BB,
                    _ => Position.BTN
                },
                new ChipAmount(100 - posted.Value),
                posted,
                posted,
                false,
                false);
        })
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
