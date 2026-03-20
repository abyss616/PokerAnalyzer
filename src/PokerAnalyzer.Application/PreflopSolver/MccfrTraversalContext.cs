using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

/// <summary>
/// Immutable reach bookkeeping for one recursive external-sampling MCCFR traversal.
/// </summary>
internal readonly record struct MccfrTraversalContext
{
    public MccfrTraversalContext(
        PlayerId traverserPlayerId,
        double traverserPolicyReach,
        double opponentPolicyReach,
        double externalSamplingReach)
    {
        TraverserPlayerId = traverserPlayerId;
        TraverserPolicyReach = traverserPolicyReach;
        OpponentPolicyReach = opponentPolicyReach;
        ExternalSamplingReach = externalSamplingReach;
    }

    public PlayerId TraverserPlayerId { get; init; }

    /// <summary>
    /// Product of the traverser's own strategy probabilities along the current path.
    /// Chance outcomes and opponent actions do not affect this term.
    /// </summary>
    public double TraverserPolicyReach { get; init; }

    /// <summary>
    /// Product of every non-traverser player strategy probability along the current path.
    /// Traverser actions and chance outcomes do not affect this term.
    /// </summary>
    public double OpponentPolicyReach { get; init; }

    /// <summary>
    /// Probability that the external sampler would have sampled the realized chance outcomes
    /// and opponent actions seen on the current path.
    /// </summary>
    public double ExternalSamplingReach { get; init; }

    public static MccfrTraversalContext CreateRoot(PlayerId traverserPlayerId)
        => new(traverserPlayerId, traverserPolicyReach: 1d, opponentPolicyReach: 1d, externalSamplingReach: 1d);

    public MccfrTraversalContext AdvanceTraverser(double actionProbability)
        => this with { TraverserPolicyReach = TraverserPolicyReach * actionProbability };

    public MccfrTraversalContext AdvanceOpponent(double actionProbability)
        => this with
        {
            OpponentPolicyReach = OpponentPolicyReach * actionProbability,
            ExternalSamplingReach = ExternalSamplingReach * actionProbability
        };

    public MccfrTraversalContext AdvanceChance(double outcomeProbability)
        => this with { ExternalSamplingReach = ExternalSamplingReach * outcomeProbability };
}
