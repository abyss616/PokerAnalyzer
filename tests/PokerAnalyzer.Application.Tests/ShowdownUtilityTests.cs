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



}
