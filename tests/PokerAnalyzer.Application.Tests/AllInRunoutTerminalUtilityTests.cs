using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class AllInRunoutTerminalUtilityTests
{
    private const decimal Epsilon = 0.000000001m;

    [Fact]
    public void AllInRunout_WinnerTakesAll_NoRake()
    {
        var contributed = new decimal[] { 5m, 5m, 0m };
        var folded = new[] { false, false, true };
        var holeCards = new HoleCards?[]
        {
            HoleCards.Parse("AhAd"),
            HoleCards.Parse("KcKd"),
            null
        };
        var board = Cards("2c", "7d", "9h", "Js", "3c");

        var utility = TerminalUtilities.ComputeAllInRunoutUtility(contributed, folded, holeCards, board, rake: 0m);

        Assert.Equal(new decimal[] { 5m, -5m, 0m }, utility);
        Assert.InRange(Math.Abs(utility.Sum() - 0m), 0m, Epsilon);
    }

    [Fact]
    public void AllInRunout_SplitPot_Tie_NoRake()
    {
        var contributed = new decimal[] { 5m, 5m, 0m };
        var folded = new[] { false, false, true };
        var holeCards = new HoleCards?[]
        {
            HoleCards.Parse("AsKd"),
            HoleCards.Parse("AcKh"),
            null
        };
        var board = Cards("Ah", "Ks", "Qd", "Jc", "Td");

        var utility = TerminalUtilities.ComputeAllInRunoutUtility(contributed, folded, holeCards, board, rake: 0m);

        Assert.Equal(new decimal[] { 0m, 0m, 0m }, utility);
        Assert.InRange(Math.Abs(utility.Sum() - 0m), 0m, Epsilon);
    }

    [Fact]
    public void AllInRunout_FoldedPlayerExcluded()
    {
        var contributed = new decimal[] { 6m, 6m, 0m };
        var folded = new[] { false, false, true };
        var holeCards = new HoleCards?[]
        {
            HoleCards.Parse("QhQs"),
            HoleCards.Parse("JdJh"),
            HoleCards.Parse("AsAc")
        };
        var board = Cards("2c", "7d", "9h", "Ts", "3c");

        var utility = TerminalUtilities.ComputeAllInRunoutUtility(contributed, folded, holeCards, board, rake: 0m);

        Assert.Equal(new decimal[] { 6m, -6m, 0m }, utility);
        Assert.Equal(0m, utility[2]);
        Assert.InRange(Math.Abs(utility.Sum() - 0m), 0m, Epsilon);
    }

    [Fact]
    public void AllInRunout_SidePotDetected_Throws()
    {
        var contributed = new decimal[] { 5m, 5m };
        var folded = new[] { false, false };
        var holeCards = new HoleCards?[] { HoleCards.Parse("AhAd"), HoleCards.Parse("KcKd") };
        var board = Cards("2c", "7d", "9h", "Js", "3c");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TerminalUtilities.ComputeAllInRunoutUtility(
                contributed,
                folded,
                holeCards,
                board,
                rake: 0m,
                hasMultiplePotsOrSidePots: true));

        Assert.Equal(
            "Side pot or multi-pot showdown detected: all-in runout utility supports a single pot only.",
            ex.Message);
    }

    private static Card[] Cards(params string[] cards) => cards.Select(Card.Parse).ToArray();
}
