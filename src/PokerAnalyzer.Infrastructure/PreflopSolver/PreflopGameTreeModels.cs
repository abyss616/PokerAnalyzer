using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.PreflopTree;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

/// <summary>
/// Represents a preflop game tree with a single root node and optional node-count metadata.
/// </summary>
public sealed record PreflopGameTree(PreflopGameTreeNode Root, int? NodeCount = null);

/// <summary>
/// Represents a node in the preflop game tree.
/// </summary>
public sealed class PreflopGameTreeNode
{
    /// <summary>
    /// Public game state snapshot represented by this node.
    /// </summary>
    public required PreflopPublicState State { get; init; }

    /// <summary>
    /// Indicates whether this node is terminal.
    /// </summary>
    public bool IsTerminal { get; set; }

    /// <summary>
    /// Child nodes by action.
    /// </summary>
    public Dictionary<PreflopAction, PreflopGameTreeNode> Children { get; init; } = new();

    /// <summary>
    /// Optional depth of this node from root.
    /// </summary>
    public int? Depth { get; init; }
}

/// <summary>
/// Builder configuration for constructing preflop game trees.
/// </summary>
public sealed record PreflopTreeBuildConfig(
    int MaxDepth = 24,
    RaiseSizingAbstraction? RaiseSizing = null,
    bool PreflopOnly = true)
{
    /// <summary>
    /// Effective raise sizing abstraction used for tree expansion.
    /// </summary>
    public RaiseSizingAbstraction EffectiveRaiseSizing => RaiseSizing ?? RaiseSizingAbstraction.Default;
}

/// <summary>
/// Stable memoization key for preflop states.
/// </summary>
public readonly record struct StateKey(
    Street Street,
    int ActingPlayer,
    ulong PlayersInHandMask,
    string ContributionsSignature,
    int CurrentBetBb,
    int ToCallBb,
    int LastRaiseSizeBb);

/// <summary>
/// Builds <see cref="StateKey"/> values from state snapshots.
/// </summary>
public static class StateKeyBuilder
{
    /// <summary>
    /// Creates a stable key for a preflop public state.
    /// </summary>
    public static StateKey Build(PreflopPublicState state, Street street = Street.Preflop)
    {
        ArgumentNullException.ThrowIfNull(state);

        var mask = BuildInHandMask(state.InHand);
        var contributions = string.Join(',', state.ContribBb);
        var currentBet = state.ContribBb.Length == 0 ? 0 : state.ContribBb.Max();

        return new StateKey(
            street,
            state.ActingIndex,
            mask,
            contributions,
            currentBet,
            state.CurrentToCallBb,
            state.LastRaiseToBb);
    }

    private static ulong BuildInHandMask(IReadOnlyList<bool> inHand)
    {
        ulong mask = 0;
        var max = Math.Min(inHand.Count, 64);

        for (var i = 0; i < max; i++)
        {
            if (inHand[i])
                mask |= 1UL << i;
        }

        return mask;
    }
}

/// <summary>
/// Deterministic comparer for preflop actions.
/// </summary>
public sealed class PreflopActionComparer : IComparer<PreflopAction>
{
    public static PreflopActionComparer Instance { get; } = new();

    private PreflopActionComparer()
    {
    }

    public int Compare(PreflopAction x, PreflopAction y)
    {
        var typeComparison = x.Type.CompareTo(y.Type);
        return typeComparison != 0 ? typeComparison : x.RaiseToBb.CompareTo(y.RaiseToBb);
    }
}
