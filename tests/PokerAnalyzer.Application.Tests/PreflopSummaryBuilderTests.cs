using System.Reflection;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class PreflopSummaryBuilderTests
{
    [Fact]
    public void BuildPreflopSummary_BlindOnlyHand_ComputesTotalsAndSeats()
    {
        var hand = CreateBaseHand();
        hand.Actions.Add(new HandAction { ActionIndex = 0, Street = Street.Preflop, Player = "P2", Type = ActionType.PostSmallBlind, Amount = 0.5m });
        hand.Actions.Add(new HandAction { ActionIndex = 1, Street = Street.Preflop, Player = "P3", Type = ActionType.PostBigBlind, Amount = 1m });
        hand.Actions.Add(new HandAction { ActionIndex = 2, Street = Street.Preflop, Player = "P4", Type = ActionType.Fold });
        hand.Actions.Add(new HandAction { ActionIndex = 3, Street = Street.Preflop, Player = "P1", Type = ActionType.Fold });
        hand.Actions.Add(new HandAction { ActionIndex = 4, Street = Street.Preflop, Player = "P2", Type = ActionType.Fold });

        var summary = InvokeBuildPreflopSummary(hand);

        Assert.Equal(6, summary.Players.Count);
        Assert.Equal(0.5m, summary.Players.Single(p => p.Seat == 2).TotalPutIn);
        Assert.Equal(1m, summary.Players.Single(p => p.Seat == 3).TotalPutIn);
        Assert.True(summary.Players.Single(p => p.Seat == 2).FoldedPreflop);
        Assert.Equal("Post SB €0.50", summary.Players.Single(p => p.Seat == 2).Actions[0].Display);
    }

    [Fact]
    public void BuildPreflopSummary_LimpPot_ComputesContributions()
    {
        var hand = CreateBaseHand();
        hand.Actions.Add(new HandAction { ActionIndex = 0, Street = Street.Preflop, Player = "P2", Type = ActionType.PostSmallBlind, Amount = 0.5m });
        hand.Actions.Add(new HandAction { ActionIndex = 1, Street = Street.Preflop, Player = "P3", Type = ActionType.PostBigBlind, Amount = 1m });
        hand.Actions.Add(new HandAction { ActionIndex = 2, Street = Street.Preflop, Player = "P4", Type = ActionType.Call, Amount = 1m });
        hand.Actions.Add(new HandAction { ActionIndex = 3, Street = Street.Preflop, Player = "P1", Type = ActionType.Call, Amount = 1m });
        hand.Actions.Add(new HandAction { ActionIndex = 4, Street = Street.Preflop, Player = "P2", Type = ActionType.Call, Amount = 0.5m });
        hand.Actions.Add(new HandAction { ActionIndex = 5, Street = Street.Preflop, Player = "P3", Type = ActionType.Check });

        var summary = InvokeBuildPreflopSummary(hand);

        Assert.Equal(1m, summary.Players.Single(p => p.Seat == 1).TotalPutIn);
        Assert.Equal(1m, summary.Players.Single(p => p.Seat == 2).TotalPutIn);
        Assert.Equal(1m, summary.Players.Single(p => p.Seat == 3).TotalPutIn);
        Assert.Equal(1m, summary.Players.Single(p => p.Seat == 4).TotalPutIn);
    }

    [Fact]
    public void BuildPreflopSummary_OpenThreeBetAndJam_TracksActionSequenceAndTotals()
    {
        var hand = CreateBaseHand();
        hand.Actions.Add(new HandAction { ActionIndex = 0, Street = Street.Preflop, Player = "P2", Type = ActionType.PostSmallBlind, Amount = 0.5m });
        hand.Actions.Add(new HandAction { ActionIndex = 1, Street = Street.Preflop, Player = "P3", Type = ActionType.PostBigBlind, Amount = 1m });
        hand.Actions.Add(new HandAction { ActionIndex = 2, Street = Street.Preflop, Player = "P4", Type = ActionType.Raise, Amount = 3m, ToAmount = 3m });
        hand.Actions.Add(new HandAction { ActionIndex = 3, Street = Street.Preflop, Player = "P1", Type = ActionType.Raise, Amount = 10m, ToAmount = 10m });
        hand.Actions.Add(new HandAction { ActionIndex = 4, Street = Street.Preflop, Player = "P2", Type = ActionType.Fold });
        hand.Actions.Add(new HandAction { ActionIndex = 5, Street = Street.Preflop, Player = "P3", Type = ActionType.Fold });
        hand.Actions.Add(new HandAction { ActionIndex = 6, Street = Street.Preflop, Player = "P4", Type = ActionType.AllIn, Amount = 40m });

        var summary = InvokeBuildPreflopSummary(hand);
        var utg = summary.Players.Single(p => p.Seat == 4);

        Assert.Equal(43m, utg.TotalPutIn);
        Assert.Equal(10m, summary.Players.Single(p => p.Seat == 1).TotalPutIn);
        Assert.Equal("Raise to €3.00", utg.Actions[0].Display);
        Assert.Equal("All-in €40.00", utg.Actions[1].Display);
    }

    private static HandAnalysisController.PreflopSummary InvokeBuildPreflopSummary(Hand hand)
    {
        var method = typeof(HandAnalysisController).GetMethod("BuildPreflopSummary", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("BuildPreflopSummary was not found.");

        return (HandAnalysisController.PreflopSummary)method.Invoke(null, [hand])!;
    }

    private static Hand CreateBaseHand()
    {
        var hand = new Hand { GameCode = 12345 };

        for (var seat = 1; seat <= 4; seat++)
        {
            hand.Players.Add(new HandPlayer
            {
                Name = $"P{seat}",
                Seat = seat,
                StackStart = 100m,
                PlayerPosition = seat switch
                {
                    1 => HandPlayer.Position.BTN,
                    2 => HandPlayer.Position.SB,
                    3 => HandPlayer.Position.BB,
                    _ => HandPlayer.Position.UTG
                }
            });
        }

        return hand;
    }
}
