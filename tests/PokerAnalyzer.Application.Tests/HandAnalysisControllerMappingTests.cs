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
    }

    [Fact]
    public void MapToDomainHand_Throws_WhenBlindActorDoesNotMatchComputedSeats()
    {
        var hand = BuildHand(dealerSeat: 1, sbSeat: 4, bbSeat: 3);
        var session = new HandHistorySession { SmallBlind = 0.5m, BigBlind = 1m };

        var ex = Assert.Throws<TargetInvocationException>(() => InvokeMapToDomainHand(hand, session));
        var baseEx = Assert.IsType<InvalidOperationException>(ex.InnerException);

        Assert.Contains("dealerSeat=1", baseEx.Message);
        Assert.Contains("computedSbSeat=2", baseEx.Message);
        Assert.Contains("actionIndex=0", baseEx.Message);
    }

    private static PokerAnalyzer.Domain.HandHistory.Hand InvokeMapToDomainHand(Hand hand, HandHistorySession session)
    {
        var method = typeof(HandAnalysisController).GetMethod("MapToDomainHand", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("MapToDomainHand was not found.");

        return (PokerAnalyzer.Domain.HandHistory.Hand)method.Invoke(null, [hand, session])!;
    }

    private static Hand BuildHand(int dealerSeat, int sbSeat, int bbSeat)
    {
        var hand = new Hand();
        hand.Players.AddRange(
        [
            new HandPlayer { Name = "P1", Seat = 1, StackStart = 100, Dealer = dealerSeat == 1, IsHero = true },
            new HandPlayer { Name = "P2", Seat = 2, StackStart = 100, Dealer = dealerSeat == 2 },
            new HandPlayer { Name = "P3", Seat = 3, StackStart = 100, Dealer = dealerSeat == 3 },
            new HandPlayer { Name = "P4", Seat = 4, StackStart = 100, Dealer = dealerSeat == 4 }
        ]);

        hand.Actions.Add(new HandAction { ActionIndex = 0, Street = Street.Preflop, Player = $"P{sbSeat}", Type = ActionType.PostSmallBlind, Amount = 0.5m });
        hand.Actions.Add(new HandAction { ActionIndex = 1, Street = Street.Preflop, Player = $"P{bbSeat}", Type = ActionType.PostBigBlind, Amount = 1m });

        return hand;
    }
}
