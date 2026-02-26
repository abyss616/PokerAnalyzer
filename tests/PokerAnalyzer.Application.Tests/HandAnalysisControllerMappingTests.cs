using Xunit;
using System.Reflection;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.Tests;

public sealed class HandAnalysisControllerMappingTests
{
    [Fact]
    public void MapToDomainHand_ComputesBlindPositions_FromDealerAndSeatOrder()
    {
        var hand = BuildHand(dealerSeat: 1, sbSeat: 2, bbSeat: 3);
        var session = new HandHistorySession { SmallBlind = 0.5m, BigBlind = 1m };

        var mapped = InvokeMapToDomainHand(hand, session);

        var sb = mapped.Seats.Single(s => s.Position == Position.SB);
        var bb = mapped.Seats.Single(s => s.Position == Position.BB);

        Assert.Equal(2, sb.SeatNumber);
        Assert.Equal(3, bb.SeatNumber);
        Assert.Equal(sb.Id, mapped.Actions.First(a => a.Type == ActionType.PostSmallBlind).ActorId);
        Assert.Equal(bb.Id, mapped.Actions.First(a => a.Type == ActionType.PostBigBlind).ActorId);

        var utg = mapped.Seats.Single(s => s.Position == Position.UTG);
        Assert.Equal(4, utg.SeatNumber);
        Assert.DoesNotContain(mapped.Seats, s => s.Position == Position.CO);
    }

    [Fact]
    public void MapToDomainHand_UsesUtgAndCo_ForFiveHandedTable()
    {
        var hand = BuildHand(dealerSeat: 1, sbSeat: 2, bbSeat: 3, playerCount: 5);
        var session = new HandHistorySession { SmallBlind = 0.5m, BigBlind = 1m };

        var mapped = InvokeMapToDomainHand(hand, session);

        var utg = mapped.Seats.Single(s => s.Position == Position.UTG);
        var co = mapped.Seats.Single(s => s.Position == Position.CO);

        Assert.Equal(4, utg.SeatNumber);
        Assert.Equal(5, co.SeatNumber);
    }

    [Fact]
    public void MapToDomainHand_Throws_WhenBigBlindActorDoesNotMatchComputedSeats()
    {
        var hand = BuildHand(dealerSeat: 1, sbSeat: 4, bbSeat: 4);
        var session = new HandHistorySession { SmallBlind = 0.5m, BigBlind = 1m };

        var ex = Assert.Throws<TargetInvocationException>(() => InvokeMapToDomainHand(hand, session));
        var baseEx = Assert.IsType<InvalidOperationException>(ex.InnerException);

        Assert.Contains("dealerSeat=1", baseEx.Message);
        Assert.Contains("computedSbSeat=4", baseEx.Message);
        Assert.Contains("computedBbSeat=1", baseEx.Message);
        Assert.Contains("actionIndex=1", baseEx.Message);
    }

    private static PokerAnalyzer.Domain.HandHistory.Hand InvokeMapToDomainHand(Hand hand, HandHistorySession session)
    {
        var method = typeof(HandAnalysisController).GetMethod("MapToDomainHand", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("MapToDomainHand was not found.");

        return (PokerAnalyzer.Domain.HandHistory.Hand)method.Invoke(null, [hand, session])!;
    }

    private static Hand BuildHand(int dealerSeat, int sbSeat, int bbSeat, int playerCount = 4)
    {
        var hand = new Hand();

        for (var seat = 1; seat <= playerCount; seat++)
        {
            hand.Players.Add(new HandPlayer
            {
                Name = $"P{seat}",
                Seat = seat,
                StackStart = 100,
                Dealer = dealerSeat == seat,
                IsHero = seat == 1
            });
        }

        hand.Actions.Add(new HandAction { ActionIndex = 0, Street = Street.Preflop, Player = $"P{sbSeat}", Type = ActionType.PostSmallBlind, Amount = 0.5m });
        hand.Actions.Add(new HandAction { ActionIndex = 1, Street = Street.Preflop, Player = $"P{bbSeat}", Type = ActionType.PostBigBlind, Amount = 1m });

        return hand;
    }
}
