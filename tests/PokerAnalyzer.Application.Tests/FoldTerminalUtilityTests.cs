using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class FoldTerminalUtilityTests
{
    [Fact]
    public void FoldTerminal_WinnerGetsPot_NoRake()
    {
        var p1 = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var p2 = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var players = new[] { p1, p2 };

        var utility = TerminalUtilities.ComputeFoldTerminalUtility(players, winner: p1, potSize: 100d);

        Assert.Equal(100d, utility[p1]);
        Assert.Equal(0d, utility[p2]);
        Assert.True(Math.Abs(utility.Values.Sum() - 100d) <= 1e-9);
    }

    [Fact]
    public void FoldTerminal_WinnerGetsPot_WithRake()
    {
        var p1 = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var p2 = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var players = new[] { p1, p2 };

        var utility = TerminalUtilities.ComputeFoldTerminalUtility(players, winner: p1, potSize: 100d, rake: 5d);

        Assert.Equal(95d, utility[p1]);
        Assert.Equal(0d, utility[p2]);
        Assert.True(Math.Abs(utility.Values.Sum() - 95d) <= 1e-9);
    }

    [Fact]
    public void FoldTerminal_InvalidWinner_Throws()
    {
        var p1 = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var p2 = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var p3 = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var players = new[] { p1, p2 };

        var ex = Assert.Throws<ArgumentException>(() =>
            TerminalUtilities.ComputeFoldTerminalUtility(players, winner: p3, potSize: 100d));

        Assert.Equal("winner", ex.ParamName);
    }

    [Fact]
    public void FoldTerminal_NegativePot_Throws()
    {
        var p1 = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var players = new[] { p1 };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TerminalUtilities.ComputeFoldTerminalUtility(players, winner: p1, potSize: -1d));

        Assert.Equal("potSize", ex.ParamName);
    }
}
