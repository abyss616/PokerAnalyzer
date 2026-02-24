using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.PreflopTree;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopNode(
    PreflopInfoSetKey InfoSet,
    PreflopNodeState State,
    IReadOnlyList<ActionType> LegalActions,
    IReadOnlyDictionary<ActionType, decimal> RaiseToByActionBb);

public sealed class PreflopGameTreeBuilder
{
    private readonly int _playerCount;
    private readonly decimal _effectiveStackBb;
    private readonly decimal _smallBlindBb;
    private readonly decimal _bigBlindBb;
    private readonly PreflopSizingConfig _sizing;
    private readonly PreflopTreeBuildConfig _buildConfig;
    private readonly Func<PreflopPublicState, StateKey> _stateKeyBuilder;

    public PreflopGameTreeBuilder(int playerCount, decimal effectiveStackBb, decimal smallBlindBb, decimal bigBlindBb, RakeConfig rake, PreflopSizingConfig sizing)
    {
        if (playerCount is < 2 or > 6)
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Supported player count is 2-6.");
        _playerCount = playerCount;
        _effectiveStackBb = effectiveStackBb;
        _smallBlindBb = smallBlindBb;
        _bigBlindBb = bigBlindBb;
        _sizing = sizing;
        _buildConfig = new PreflopTreeBuildConfig();
        _stateKeyBuilder = state => StateKeyBuilder.Build(state, Street.Preflop);
    }

    /// <summary>
    /// Initializes a game-tree builder with explicit tree-build configuration and state-key strategy.
    /// </summary>
    public PreflopGameTreeBuilder(
        int playerCount,
        decimal effectiveStackBb,
        decimal smallBlindBb,
        decimal bigBlindBb,
        RakeConfig rake,
        PreflopSizingConfig sizing,
        PreflopTreeBuildConfig buildConfig,
        Func<PreflopPublicState, StateKey>? stateKeyBuilder = null)
        : this(playerCount, effectiveStackBb, smallBlindBb, bigBlindBb, rake, sizing)
    {
        _buildConfig = buildConfig;
        _stateKeyBuilder = stateKeyBuilder ?? (state => StateKeyBuilder.Build(state, Street.Preflop));
    }

    /// <summary>
    /// Builds a memoized preflop game tree from the generated initial state.
    /// </summary>
    public PreflopGameTree BuildTree()
    {
        var positions = GetTablePositions(_playerCount);
        var rootState = CreateInitialState(positions);
        return Build(rootState, _buildConfig);
    }

    public PreflopGameTree Build(PreflopPublicState root, PreflopTreeBuildConfig config)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(config);

        var memo = new Dictionary<StateKey, PreflopGameTreeNode>();
        var domainSizing = ToDomainSizingConfig(config.EffectiveRaiseSizing);
        var rootNode = BuildNode(root, depth: 0);
        return new PreflopGameTree(rootNode, memo.Count);

        PreflopGameTreeNode BuildNode(PreflopPublicState state, int depth)
        {
            if (depth >= config.MaxDepth)
            {
                return new PreflopGameTreeNode
                {
                    State = state,
                    IsTerminal = true,
                    Children = new Dictionary<PreflopAction, PreflopGameTreeNode>(),
                    Depth = depth
                };
            }

            var street = Street.Preflop;
            if ((config.PreflopOnly && street != Street.Preflop) || PreflopRules.IsTerminal(state, out _))
            {
                return new PreflopGameTreeNode
                {
                    State = state,
                    IsTerminal = true,
                    Children = new Dictionary<PreflopAction, PreflopGameTreeNode>(),
                    Depth = depth
                };
            }

            var key = _stateKeyBuilder(state);
            if (memo.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var node = new PreflopGameTreeNode
            {
                State = state,
                IsTerminal = false,
                Children = new Dictionary<PreflopAction, PreflopGameTreeNode>(),
                Depth = depth
            };

            memo[key] = node;

            var actions = PreflopRules.GetLegalActions(state, domainSizing);
            actions = FilterRaiseActions(actions, state, config.EffectiveRaiseSizing)
                .OrderBy(action => action, PreflopActionComparer.Instance)
                .ToList();

            foreach (var action in actions)
            {
                var next = PreflopRules.ApplyAction(state, action);

#if DEBUG
                if (_stateKeyBuilder(next) == key)
                {
                    throw new InvalidOperationException("State transition did not progress; refusing to recurse to avoid infinite loops.");
                }
#endif

                var child = BuildNode(next, depth + 1);
                node.Children[action] = child;
            }

            if (node.Children.Count == 0)
            {
                node.IsTerminal = true;
            }

            return node;
        }
    }

    public IReadOnlyList<PreflopNode> Build()
    {
        var positions = GetTablePositions(_playerCount);
        var contrib = positions.ToDictionary(p => p, _ => 0m);
        contrib[Position.BB] = _bigBlindBb;
        if (positions.Contains(Position.SB))
            contrib[Position.SB] = _smallBlindBb;

        var root = new BuildState(positions, positions[0], contrib, [], new HashSet<Position>(), _bigBlindBb, 0, new HashSet<Position>(positions.Skip(1)));
        var nodes = new Dictionary<PreflopInfoSetKey, PreflopNode>();
        Expand(root, nodes);
        return nodes.Values.ToList();
    }

    private PreflopPublicState CreateInitialState(IReadOnlyList<Position> positions)
    {
        var sbIndex = FindIndex(positions, Position.SB);
        var bbIndex = FindIndex(positions, Position.BB);
        var contrib = new int[positions.Count];

        if (bbIndex >= 0)
            contrib[bbIndex] = (int)Math.Round(_bigBlindBb);

        if (sbIndex >= 0)
            contrib[sbIndex] = (int)Math.Round(_smallBlindBb);

        return new PreflopPublicState
        {
            PlayerCount = positions.Count,
            ActingIndex = 0,
            InHand = Enumerable.Repeat(true, positions.Count).ToArray(),
            ContribBb = contrib,
            StackBb = Enumerable.Repeat((int)Math.Round(_effectiveStackBb), positions.Count).ToArray(),
            CurrentToCallBb = (int)Math.Round(_bigBlindBb),
            LastRaiseToBb = (int)Math.Round(_bigBlindBb),
            RaisesCount = 0,
            PotBb = contrib.Sum(),
            LastAggressorIndex = bbIndex,
            LastActionWasRaiseByIndex = bbIndex,
            BettingClosed = false
        };
    }

    private static int FindIndex(IReadOnlyList<Position> positions, Position target)
    {
        for (var i = 0; i < positions.Count; i++)
        {
            if (positions[i] == target)
                return i;
        }

        return -1;
    }

    private static PokerAnalyzer.Domain.PreflopTree.PreflopSizingConfig ToDomainSizingConfig(RaiseSizingAbstraction abstraction)
    {
        return new PokerAnalyzer.Domain.PreflopTree.PreflopSizingConfig
        {
            OpenRaiseToBb = abstraction.OpenSizesBb.Select(x => (int)Math.Round(x)).Distinct().Order().ToArray(),
            ThreeBetToBb = abstraction.ThreeBetSizesBb.Select(x => (int)Math.Round(x)).Distinct().Order().ToArray(),
            FourBetToBb = abstraction.FourBetSizesBb.Select(x => (int)Math.Round(x)).Distinct().Order().ToArray(),
            AllowAllInAlways = true
        };
    }

    private static IEnumerable<PreflopAction> FilterRaiseActions(
        IEnumerable<PreflopAction> actions,
        PreflopPublicState state,
        RaiseSizingAbstraction abstraction)
    {
        var raisesCount = state.RaisesCount;
        var allowed = raisesCount switch
        {
            <= 1 => abstraction.OpenSizesBb,
            2 => abstraction.ThreeBetSizesBb,
            _ => abstraction.FourBetSizesBb
        };

        var allowedRaiseTo = allowed
            .Select(x => (int)Math.Round(x))
            .ToHashSet();

        foreach (var action in actions)
        {
            if (action.Type != PreflopActionType.RaiseTo)
            {
                yield return action;
                continue;
            }

            if (allowedRaiseTo.Contains(action.RaiseToBb))
            {
                yield return action;
            }
        }
    }

    public static IReadOnlyList<Position> GetTablePositions(int playerCount) => playerCount switch
    {
        2 => [Position.BTN, Position.BB],
        3 => [Position.BTN, Position.SB, Position.BB],
        4 => [Position.UTG, Position.BTN, Position.SB, Position.BB],
        5 => [Position.UTG, Position.CO, Position.BTN, Position.SB, Position.BB],
        6 => [Position.UTG, Position.HJ, Position.CO, Position.BTN, Position.SB, Position.BB],
        _ => throw new ArgumentOutOfRangeException(nameof(playerCount), "Supported player count is 2-6.")
    };

    private void Expand(BuildState state, IDictionary<PreflopInfoSetKey, PreflopNode> nodes)
    {
        if (state.IsTerminal(_effectiveStackBb))
            return;

        var actor = state.NextActor;
        var toCall = Math.Max(0m, state.BetToCall - state.Contrib.GetValueOrDefault(actor));
        var legal = toCall > 0 ? new List<ActionType> { ActionType.Fold, ActionType.Call } : [ActionType.Check];

        var raiseTo = BuildRaiseTo(state);
        if (raiseTo > state.BetToCall)
            legal.Add(ActionType.Raise);

        if ((_sizing.AllowExplicitJam || _effectiveStackBb <= _sizing.JamThresholdStackBb) && state.Contrib.GetValueOrDefault(actor) < _effectiveStackBb)
            legal.Add(ActionType.AllIn);

        var key = new PreflopInfoSetKey(_playerCount, actor, PreflopHistorySignature.Build(state.Actions), (int)Math.Round(toCall), (int)Math.Round(_effectiveStackBb));
        if (!nodes.ContainsKey(key))
        {
            var villainCommit = state.Contrib.Where(c => c.Key != actor).Max(c => c.Value);
            nodes[key] = new PreflopNode(
                key,
                new PreflopNodeState(key, state.Contrib.Values.Sum(), toCall, state.Contrib.GetValueOrDefault(actor), villainCommit, _effectiveStackBb),
                legal,
                new Dictionary<ActionType, decimal> { [ActionType.Raise] = raiseTo });
        }

        foreach (var action in legal)
            Expand(state.Apply(action, raiseTo, _effectiveStackBb), nodes);
    }

    private decimal BuildRaiseTo(BuildState state)
    {
        var current = state.BetToCall;
        var raw = state.RaiseCount switch
        {
            0 => _sizing.OpenSizesBb.First(),
            1 => current * _sizing.ThreeBetSizeMultipliers.First(),
            _ => current * _sizing.FourBetSizeMultipliers.First()
        };

        raw = Math.Max(current + _bigBlindBb, raw);
        return Math.Min(raw, _effectiveStackBb);
    }

    private sealed record BuildState(
        IReadOnlyList<Position> Order,
        Position NextActor,
        Dictionary<Position, decimal> Contrib,
        List<ActionType> Actions,
        HashSet<Position> Folded,
        decimal BetToCall,
        int RaiseCount,
        HashSet<Position> PendingResponses)
    {
        public bool IsTerminal(decimal effectiveStackBb)
            => Folded.Count >= Order.Count - 1 || PendingResponses.Count == 0 || Contrib.Values.Any(v => v >= effectiveStackBb);

        public BuildState Apply(ActionType action, decimal raiseTo, decimal effectiveStackBb)
        {
            var contrib = new Dictionary<Position, decimal>(Contrib);
            var actions = new List<ActionType>(Actions) { action };
            var folded = new HashSet<Position>(Folded);
            var pending = new HashSet<Position>(PendingResponses);
            pending.Remove(NextActor);
            var nextBetToCall = BetToCall;
            var nextRaiseCount = RaiseCount;

            if (action == ActionType.Fold)
                folded.Add(NextActor);
            else if (action == ActionType.Call)
                contrib[NextActor] = nextBetToCall;
            else if (action == ActionType.Raise)
            {
                contrib[NextActor] = raiseTo;
                nextBetToCall = raiseTo;
                nextRaiseCount++;
                pending = new HashSet<Position>(Order.Where(p => p != NextActor && !folded.Contains(p)));
            }
            else if (action == ActionType.AllIn)
            {
                contrib[NextActor] = effectiveStackBb;
                nextBetToCall = effectiveStackBb;
                nextRaiseCount++;
                pending = new HashSet<Position>(Order.Where(p => p != NextActor && !folded.Contains(p)));
            }

            var nextActor = NextPosition(Order, NextActor, folded, pending);
            return this with
            {
                Contrib = contrib,
                Actions = actions,
                Folded = folded,
                BetToCall = nextBetToCall,
                RaiseCount = nextRaiseCount,
                PendingResponses = pending,
                NextActor = nextActor
            };
        }

        private static Position NextPosition(IReadOnlyList<Position> order, Position current, HashSet<Position> folded, HashSet<Position> pending)
        {
            var idx = FindIndex(order, current);
            for (var i = 1; i <= order.Count; i++)
            {
                var pos = order[(idx + i) % order.Count];
                if (!folded.Contains(pos) && (pending.Count == 0 || pending.Contains(pos)))
                    return pos;
            }

            return current;
        }
        private static int FindIndex(IReadOnlyList<Position> order, Position current)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] == current)
                    return i;
            }

            return 0;
        }
    }
}

public static class PreflopHistorySignature
{
    public static string Build(IReadOnlyList<ActionType> actions)
    {
        var raises = actions.Count(a => a is ActionType.Raise or ActionType.AllIn or ActionType.Bet);
        var calls = actions.Count(a => a == ActionType.Call);
        return raises switch
        {
            0 when calls == 0 => "UNOPENED",
            0 => "LIMPED",
            1 when calls == 0 => "OPEN",
            1 => "OPEN_CALL",
            2 when calls <= 1 => "OPEN_3BET",
            2 => "OPEN_3BET_CALL",
            3 => "OPEN_3BET_4BET",
            _ => "OPEN_3BET_4BET_PLUS"
        };
    }
}
