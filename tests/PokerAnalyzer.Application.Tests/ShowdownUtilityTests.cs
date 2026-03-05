using PokerAnalyzer.Application.PreflopSolver;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class ShowdownUtilityTests
{
    private const decimal Epsilon = 0.000000001m;

    [Fact]
    public void WinnerTakesAll_NoRake()
    {
        var contributed = new decimal[] { 5m, 5m, 0m };
        var folded = new[] { false, false, true };

        var utility = TerminalUtilities.ComputeShowdownUtility(
            contributed,
            folded,
            winnerIndices: new[] { 0 },
            rake: 0m);

        Assert.Equal(new decimal[] { 5m, -5m, 0m }, utility);
        Assert.InRange(Math.Abs(utility.Sum() - 0m), 0m, Epsilon);
    }

    [Fact]
    public void SplitPot_TwoWayTie_NoRake()
    {
        var contributed = new decimal[] { 5m, 5m, 0m };
        var folded = new[] { false, false, true };

        var utility = TerminalUtilities.ComputeShowdownUtility(
            contributed,
            folded,
            winnerIndices: new[] { 0, 1 },
            rake: 0m);

        Assert.Equal(new decimal[] { 0m, 0m, 0m }, utility);
        Assert.InRange(Math.Abs(utility.Sum() - 0m), 0m, Epsilon);
    }

    [Fact]
    public void SplitPot_ThreeWayTie_WithRake()
    {
        var contributed = new decimal[] { 10m, 10m, 10m };
        var folded = new[] { false, false, false };

        var utility = TerminalUtilities.ComputeShowdownUtility(
            contributed,
            folded,
            winnerIndices: new[] { 0, 1, 2 },
            rake: 3m);

        Assert.Equal(new decimal[] { -1m, -1m, -1m }, utility);
        Assert.InRange(Math.Abs(utility.Sum() - (-3m)), 0m, Epsilon);
    }

    [Fact]
    public void FoldedPlayer_NotEligible()
    {
        var contributed = new decimal[] { 5m, 5m, 0m };
        var folded = new[] { false, false, true };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TerminalUtilities.ComputeShowdownUtility(
                contributed,
                folded,
                winnerIndices: new[] { 0, 2 },
                rake: 0m));

        Assert.Equal("Folded players are not eligible to win at showdown.", ex.Message);
    }

    [Fact]
    public void SidePotOrMultiPot_Throws()
    {
        var contributed = new decimal[] { 10m, 5m, 0m };
        var folded = new[] { false, false, true };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TerminalUtilities.ComputeShowdownUtility(
                contributed,
                folded,
                winnerIndices: new[] { 0 },
                rake: 0m));

        Assert.Equal(
            "Side pot or multi-pot showdown detected: non-folded players have unequal contributions.",
            ex.Message);
    }
}
