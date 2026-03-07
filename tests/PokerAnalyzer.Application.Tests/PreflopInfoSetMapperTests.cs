using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopInfoSetMapperTests
{
    [Fact]
    public void MapInfoSetKey_UsesCanonicalOffsuitHand_RegardlessOfCardOrder()
    {
        var actingPlayerId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var state1 = CreateHeadsUpStateWithHoleCards(actingPlayerId, HoleCards.Parse("AsKh"));
        var state2 = CreateHeadsUpStateWithHoleCards(actingPlayerId, HoleCards.Parse("KhAs"));
        var mapper = new PreflopInfoSetMapper();

        var key1 = mapper.MapInfoSetKey(state1, actingPlayerId);
        var key2 = mapper.MapInfoSetKey(state2, actingPlayerId);

        Assert.Contains("hero=AKo", key1);
        Assert.Contains("hero=AKo", key2);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void MapInfoSetKey_UsesCanonicalSuitedAndPairRepresentations()
    {
        var actingPlayerId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var suitedState = CreateHeadsUpStateWithHoleCards(actingPlayerId, HoleCards.Parse("AhKh"));
        var pairState = CreateHeadsUpStateWithHoleCards(actingPlayerId, HoleCards.Parse("7s7d"));
        var mapper = new PreflopInfoSetMapper();

        var suitedKey = mapper.MapInfoSetKey(suitedState, actingPlayerId);
        var pairKey = mapper.MapInfoSetKey(pairState, actingPlayerId);

        Assert.Contains("hero=AKs", suitedKey);
        Assert.Contains("hero=77", pairKey);
    }

    private static SolverHandState CreateHeadsUpStateWithHoleCards(PlayerId actingPlayerId, HoleCards holeCards)
    {
        var otherPlayerId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var config = new GameConfig(MaxPlayers: 2, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(actingPlayerId, SeatIndex: 0, Position.SB, Stack: new ChipAmount(99), CurrentStreetContribution: new ChipAmount(1), TotalContribution: new ChipAmount(1), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(otherPlayerId, SeatIndex: 1, Position.BB, Stack: new ChipAmount(98), CurrentStreetContribution: new ChipAmount(2), TotalContribution: new ChipAmount(2), IsFolded: false, IsAllIn: false)
        };

        return new SolverHandState(
            config,
            street: Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: actingPlayerId,
            pot: new ChipAmount(3),
            currentBetSize: new ChipAmount(2),
            lastRaiseSize: new ChipAmount(2),
            raisesThisStreet: 0,
            players,
            actionHistory: [],
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [actingPlayerId] = holeCards
            });
    }
}
