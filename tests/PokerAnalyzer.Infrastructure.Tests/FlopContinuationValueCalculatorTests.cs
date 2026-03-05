using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Engines;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class FlopContinuationValueCalculatorTests
{
    private readonly FlopContinuationValueCalculator _sut = new();

    [Fact]
    public async Task Determinism_WithSeed_Multiway()
    {
        var (state, players) = BuildThreeWayEndOfPreflop();

        var a = await _sut.ComputeAsync(players, state, flopsToSample: 2_000, seed: 77, CancellationToken.None);
        var b = await _sut.ComputeAsync(players, state, flopsToSample: 2_000, seed: 77, CancellationToken.None);

        Assert.Equal("FlopRollout", a.Method);
        Assert.Equal(2_000, a.FlopsSampled);
        Assert.Equal(77, a.SeedUsed);
        Assert.Equal(a.ChipEv.Count, b.ChipEv.Count);
        foreach (var (player, value) in a.ChipEv)
            Assert.Equal(value, b.ChipEv[player]);
    }

    [Fact]
    public async Task ZeroSum_EvSumsToZero()
    {
        var (state, players) = BuildThreeWayEndOfPreflop();

        var result = await _sut.ComputeAsync(players, state, flopsToSample: 2_000, seed: 77, CancellationToken.None);

        var sum = result.ChipEv.Values.Sum();
        Assert.True(Math.Abs(sum) <= 0.0001m, $"sum was {sum}");
    }

    [Fact]
    public async Task Sanity_Bounds()
    {
        var (state, players) = BuildThreeWayEndOfPreflop();

        var result = await _sut.ComputeAsync(players, state, flopsToSample: 2_000, seed: 77, CancellationToken.None);

        var maxStackBehind = state.ActivePlayers.Max(p => state.Stacks[p].Value);
        var upper = state.Pot.Value + maxStackBehind;
        var lower = -maxStackBehind;

        foreach (var value in result.ChipEv.Values)
            Assert.InRange(value, lower, upper);
    }

    [Fact]
    public async Task DuplicateCards_ThrowsClearMessage()
    {
        var (state, players) = BuildThreeWayEndOfPreflop();
        var dupPlayers = new List<(PlayerId PlayerId, HoleCards Cards)>
        {
            (players[0].PlayerId, players[0].Cards),
            (players[1].PlayerId, HoleCards.Parse("AsKd"))
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ComputeAsync(dupPlayers, state, 100, 1, CancellationToken.None));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (HandState State, List<(PlayerId PlayerId, HoleCards Cards)> Players) BuildThreeWayEndOfPreflop()
    {
        var p1 = PlayerId.New();
        var p2 = PlayerId.New();
        var p3 = PlayerId.New();

        var seats = new List<PlayerSeat>
        {
            new(p1, "SB", 1, Position.SB, new ChipAmount(200)),
            new(p2, "BB", 2, Position.BB, new ChipAmount(200)),
            new(p3, "BTN", 3, Position.BTN, new ChipAmount(200))
        };

        var state = HandState.CreateNewHand(seats, new ChipAmount(5), new ChipAmount(10), Street.Preflop, new Board());
        state = state.Apply(new BettingAction(Street.Preflop, p3, ActionType.Raise, new ChipAmount(30)));
        state = state.Apply(new BettingAction(Street.Preflop, p1, ActionType.Call, ChipAmount.Zero));
        state = state.Apply(new BettingAction(Street.Preflop, p2, ActionType.Call, ChipAmount.Zero));

        var players = new List<(PlayerId PlayerId, HoleCards Cards)>
        {
            (p1, HoleCards.Parse("AsKd")),
            (p2, HoleCards.Parse("QhQc")),
            (p3, HoleCards.Parse("9s9d"))
        };

        return (state, players);
    }
}
