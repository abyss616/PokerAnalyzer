using PokerAnalyzer.Application.PreflopSolver;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class EveryoneFoldsUtilityTests
{
    [Fact]
    public void ComputeEveryoneFoldsUtility_WithZeroRake_HasZeroSum()
    {
        var contributed = new decimal[] { 1m, 2m, 3m };
        var folded = new[] { true, false, true };

        var utility = TerminalUtilities.ComputeEveryoneFoldsUtility(contributed, folded, rake: 0m);

        Assert.Equal(new decimal[] { -1m, 4m, -3m }, utility);
        Assert.Equal(0m, utility.Sum());
    }

    [Fact]
    public void ComputeEveryoneFoldsUtility_WithPositiveRake_HasNegativeRakeSum()
    {
        var contributed = new decimal[] { 1m, 2m, 3m };
        var folded = new[] { true, false, true };

        var utility = TerminalUtilities.ComputeEveryoneFoldsUtility(contributed, folded, rake: 0.5m);

        Assert.Equal(new decimal[] { -1m, 3.5m, -3m }, utility);
        Assert.Equal(-0.5m, utility.Sum());
    }
}
