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
    public void MapInfoSetKey_MultiwayState_AddsContinuingPlayerTopologyUntilTrueHeadsUp()
    {
        var actingPlayerId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var state = CreateThreeWayStateWithHoleCards(actingPlayerId, HoleCards.Parse("AsKh"));
        var mapper = new PreflopInfoSetMapper();

        var key = mapper.MapInfoSetKey(state, actingPlayerId);

        Assert.Contains("continuing=3", key, StringComparison.Ordinal);
        Assert.Contains("continuingPositions=BTN,SB,BB", key, StringComparison.Ordinal);
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


    private static SolverHandState CreateThreeHandedStateReducedToHeadsUp(PlayerId actingPlayerId, HoleCards holeCards)
    {
        var sbPlayerId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var bbPlayerId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var config = new GameConfig(MaxPlayers: 3, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(actingPlayerId, SeatIndex: 0, Position.BTN, Stack: new ChipAmount(100), CurrentStreetContribution: ChipAmount.Zero, TotalContribution: ChipAmount.Zero, IsFolded: false, IsAllIn: false),
            new SolverPlayerState(sbPlayerId, SeatIndex: 1, Position.SB, Stack: new ChipAmount(99), CurrentStreetContribution: new ChipAmount(1), TotalContribution: new ChipAmount(1), IsFolded: true, IsAllIn: false),
            new SolverPlayerState(bbPlayerId, SeatIndex: 2, Position.BB, Stack: new ChipAmount(98), CurrentStreetContribution: new ChipAmount(2), TotalContribution: new ChipAmount(2), IsFolded: false, IsAllIn: false)
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
            actionHistory:
            [
                new SolverActionEntry(sbPlayerId, ActionType.PostSmallBlind, new ChipAmount(1)),
                new SolverActionEntry(bbPlayerId, ActionType.PostBigBlind, new ChipAmount(2)),
                new SolverActionEntry(sbPlayerId, ActionType.Fold, ChipAmount.Zero)
            ],
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [actingPlayerId] = holeCards
            });
    }

    private static SolverHandState CreateThreeWayStateWithHoleCards(PlayerId actingPlayerId, HoleCards holeCards)
    {
        var sbPlayerId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var bbPlayerId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var config = new GameConfig(MaxPlayers: 3, SmallBlind: new ChipAmount(1), BigBlind: new ChipAmount(2), Ante: ChipAmount.Zero, StartingStack: new ChipAmount(100));

        var players = new[]
        {
            new SolverPlayerState(actingPlayerId, SeatIndex: 0, Position.BTN, Stack: new ChipAmount(100), CurrentStreetContribution: ChipAmount.Zero, TotalContribution: ChipAmount.Zero, IsFolded: false, IsAllIn: false),
            new SolverPlayerState(sbPlayerId, SeatIndex: 1, Position.SB, Stack: new ChipAmount(99), CurrentStreetContribution: new ChipAmount(1), TotalContribution: new ChipAmount(1), IsFolded: false, IsAllIn: false),
            new SolverPlayerState(bbPlayerId, SeatIndex: 2, Position.BB, Stack: new ChipAmount(98), CurrentStreetContribution: new ChipAmount(2), TotalContribution: new ChipAmount(2), IsFolded: false, IsAllIn: false)
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
