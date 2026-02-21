using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public class PreflopSolverTests
{
    [Fact]
    public void UnopenedBtn_AA_RaisesMoreThan72o()
    {
        var solver = new PreflopSolverService();
        var node = new PreflopNodeState("root", Position.BTN, Position.SB, 100m, 1.5m, true);

        var aa = solver.QueryStrategy(node, HoleCards.Parse("AsAh"));
        var sevenTwo = solver.QueryStrategy(node, HoleCards.Parse("7c2d"));

        var aaRaise = aa.Frequencies.Where(kv => kv.Key.Contains("raise")).Sum(kv => kv.Value);
        var weakRaise = sevenTwo.Frequencies.Where(kv => kv.Key.Contains("raise")).Sum(kv => kv.Value);
        Assert.True(aaRaise >= weakRaise);
    }

    [Fact]
    public void SbLimpNode_BbHasIsoForStrongHands()
    {
        var tree = new PreflopGameTreeBuilder().BuildDefaultSubgame(Position.SB, Position.BB, 100m);
        var limpNode = tree.Children["limp"];
        Assert.Contains("iso_4.5", limpNode.Actions);
    }

    [Fact]
    public void HuSymmetryOrdering_IsConsistent()
    {
        var solver = new PreflopSolverService();
        var sbNode = new PreflopNodeState("root", Position.SB, Position.BB, 100m, 1.5m, false);
        var bbNode = new PreflopNodeState("root", Position.BB, Position.SB, 100m, 1.5m, true);

        var sbStrong = solver.QueryStrategy(sbNode, HoleCards.Parse("AsKs")).EstimatedEv;
        var sbWeak = solver.QueryStrategy(sbNode, HoleCards.Parse("7c2d")).EstimatedEv;
        var bbStrong = solver.QueryStrategy(bbNode, HoleCards.Parse("AsKs")).EstimatedEv;
        var bbWeak = solver.QueryStrategy(bbNode, HoleCards.Parse("7c2d")).EstimatedEv;

        Assert.True(sbStrong >= sbWeak);
        Assert.True(bbStrong >= bbWeak);
    }
}
