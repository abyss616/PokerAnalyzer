using System.Runtime.CompilerServices;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.PreflopTree;
using PokerAnalyzer.Infrastructure.PreflopSolver;
using Xunit;
using SolverSizingConfig = PokerAnalyzer.Infrastructure.PreflopSolver.PreflopSizingConfig;

namespace PokerAnalyzer.Application.Tests;

public class PreflopGameTreeBuilderTests
{
    private static readonly RakeConfig Rake = new(0.05m, 1m, true);

    [Fact]
    public void BuildTree_HeadsUpRoot_ExpandsChildren()
    {
        var tree = CreateBuilder().BuildTree();

        Assert.NotEmpty(tree.Root.Children);
    }

    [Fact]
    public void BuildTree_TerminalNodes_DoNotHaveChildren()
    {
        var tree = CreateBuilder().BuildTree();

        foreach (var node in Traverse(tree.Root))
        {
            if (node.IsTerminal)
            {
                Assert.Empty(node.Children);
            }
        }
    }

    [Fact]
    public void BuildTree_MaxDepthOne_ProducesOnlyLeafChildrenUnderRoot()
    {
        var builder = CreateBuilder(new PreflopTreeBuildConfig(MaxDepth: 1));

        var tree = builder.BuildTree();

        Assert.NotEmpty(tree.Root.Children);
        Assert.All(tree.Root.Children.Values, child =>
        {
            Assert.True(child.IsTerminal || child.Children.Count == 0);
            Assert.Empty(child.Children);
        });
    }

    [Fact]
    public void BuildTree_HasNoCyclesInAnyTraversalPath()
    {
        var tree = CreateBuilder().BuildTree();

        AssertNoCycles(tree.Root, new HashSet<PreflopGameTreeNode>(ReferenceEqualityComparer.Instance));
    }

    [Fact]
    public void BuildTree_IsDeterministic_ForRootActionOrdering()
    {
        var first = CreateBuilder().BuildTree();
        var second = CreateBuilder().BuildTree();

        var firstOrder = first.Root.Children.Keys.Select(ToComparableAction).ToArray();
        var secondOrder = second.Root.Children.Keys.Select(ToComparableAction).ToArray();

        Assert.Equal(firstOrder, secondOrder);
    }

    private static PreflopGameTreeBuilder CreateBuilder(PreflopTreeBuildConfig? config = null)
    {
        return config is null
            ? new PreflopGameTreeBuilder(2, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default)
            : new PreflopGameTreeBuilder(2, 100m, 0.5m, 1m, Rake, SolverSizingConfig.Default, config);
    }

    private static IEnumerable<PreflopGameTreeNode> Traverse(PreflopGameTreeNode root)
    {
        var seen = new HashSet<PreflopGameTreeNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<PreflopGameTreeNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            foreach (var child in current.Children.Values)
            {
                stack.Push(child);
            }
        }
    }

    private static void AssertNoCycles(PreflopGameTreeNode node, HashSet<PreflopGameTreeNode> path)
    {
        Assert.True(path.Add(node), "Detected a cycle in a traversal path.");

        foreach (var child in node.Children.Values)
        {
            AssertNoCycles(child, path);
        }

        path.Remove(node);
    }

    private static string ToComparableAction(PreflopAction action)
        => $"{action.Type}:{action.RaiseToBb}";

    private sealed class ReferenceEqualityComparer : IEqualityComparer<PreflopGameTreeNode>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(PreflopGameTreeNode? x, PreflopGameTreeNode? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(PreflopGameTreeNode obj)
            => RuntimeHelpers.GetHashCode(obj);
    }
}
