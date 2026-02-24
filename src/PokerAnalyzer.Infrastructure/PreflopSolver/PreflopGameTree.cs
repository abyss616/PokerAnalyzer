using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopNode(
    PreflopInfoSetKey InfoSet,
    PreflopNodeState State,
    IReadOnlyList<ActionType> LegalActions,
    IReadOnlyDictionary<ActionType, decimal> RaiseToByActionBb);

public sealed class PreflopGameTreeBuilder
{
    public IReadOnlyList<PreflopNode> Build(PreflopSolverConfig config)
    {
        var sizing = config.Sizing ?? RaiseSizingAbstraction.Default;
        var positions = GetTablePositions(config.PlayerCount);
        var firstActor = config.PlayerCount == 2 ? Position.BTN : positions[0];

        var root = new BuildState(
            positions,
            firstActor,
            new Dictionary<Position, decimal> { [Position.SB] = 0.5m, [Position.BB] = 1m },
            new HashSet<Position>(),
            [],
            1m,
            0m,
            config.EffectiveStackBb);

        var nodes = new Dictionary<PreflopInfoSetKey, PreflopNode>();
        Expand(root, config.PlayerCount, sizing, nodes);
        return nodes.Values.ToList();
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

    private static void Expand(BuildState state, int playerCount, RaiseSizingAbstraction sizing, IDictionary<PreflopInfoSetKey, PreflopNode> nodes)
    {
        if (state.IsTerminal)
            return;

        var actor = state.NextActor;
        var toCall = Math.Max(0m, state.BetToCall - state.Contrib.GetValueOrDefault(actor));
        var legal = toCall > 0 ? new List<ActionType> { ActionType.Fold, ActionType.Call, ActionType.Raise } : new List<ActionType> { ActionType.Check, ActionType.Raise };
        if (state.EffectiveStackBb <= sizing.JamThresholdStackBb || state.RaiseCount >= 3)
            legal.Add(ActionType.AllIn);

        var raiseTo = new Dictionary<ActionType, decimal>();
        if (legal.Contains(ActionType.Raise))
        {
            var size = state.RaiseCount switch
            {
                0 => sizing.OpenSizesBb.First(),
                1 => sizing.ThreeBetSizesBb.First(),
                _ => sizing.FourBetSizesBb.First()
            };
            raiseTo[ActionType.Raise] = Math.Min(size, state.EffectiveStackBb);
        }

        var key = new PreflopInfoSetKey(playerCount, actor, PreflopHistorySignature.Build(state.Actions), (int)Math.Round(toCall), (int)Math.Round(state.LastRaiseBb), (int)Math.Round(state.EffectiveStackBb));
        if (!nodes.ContainsKey(key))
        {
            var villainCommit = state.Contrib.Where(c => c.Key != actor).DefaultIfEmpty(new KeyValuePair<Position, decimal>(Position.BB, 0m)).Max(c => c.Value);
            nodes[key] = new PreflopNode(key,
                new PreflopNodeState(key, state.Contrib.Values.Sum(), toCall, state.Contrib.GetValueOrDefault(actor), villainCommit, state.EffectiveStackBb),
                legal,
                raiseTo);
        }

        foreach (var action in legal)
        {
            var next = state.Apply(action, raiseTo.GetValueOrDefault(ActionType.Raise, 0m));
            Expand(next, playerCount, sizing, nodes);
        }
    }

    private sealed record BuildState(
        IReadOnlyList<Position> Order,
        Position NextActor,
        Dictionary<Position, decimal> Contrib,
        HashSet<Position> Folded,
        List<ActionType> Actions,
        decimal BetToCall,
        decimal LastRaiseBb,
        decimal EffectiveStackBb,
        int RaiseCount = 0,
        int ConsecutiveCallsOrChecks = 0)
    {
        public bool IsTerminal => Folded.Count >= Order.Count - 1 || RaiseCount >= 4 || ConsecutiveCallsOrChecks >= ActiveCount;
        private int ActiveCount => Order.Count - Folded.Count;

        public BuildState Apply(ActionType action, decimal raiseTo)
        {
            var contrib = new Dictionary<Position, decimal>(Contrib);
            var folded = new HashSet<Position>(Folded);
            var actions = new List<ActionType>(Actions) { action };
            var nextBetToCall = BetToCall;
            var nextLastRaise = LastRaiseBb;
            var nextRaiseCount = RaiseCount;
            var nextClosed = ConsecutiveCallsOrChecks + 1;

            if (action == ActionType.Fold)
                folded.Add(NextActor);
            else if (action == ActionType.Call)
                contrib[NextActor] = nextBetToCall;
            else if (action == ActionType.Raise)
            {
                contrib[NextActor] = raiseTo;
                nextLastRaise = Math.Max(0m, raiseTo - nextBetToCall);
                nextBetToCall = raiseTo;
                nextRaiseCount++;
                nextClosed = 1;
            }
            else if (action == ActionType.AllIn)
            {
                contrib[NextActor] = EffectiveStackBb;
                nextLastRaise = Math.Max(0m, EffectiveStackBb - nextBetToCall);
                nextBetToCall = EffectiveStackBb;
                nextRaiseCount++;
                nextClosed = 1;
            }

            var nextActor = NextPosition(Order, NextActor, folded);
            return this with
            {
                Contrib = contrib,
                Folded = folded,
                Actions = actions,
                NextActor = nextActor,
                BetToCall = nextBetToCall,
                LastRaiseBb = nextLastRaise,
                RaiseCount = nextRaiseCount,
                ConsecutiveCallsOrChecks = nextClosed
            };
        }

        private static Position NextPosition(IReadOnlyList<Position> order, Position current, HashSet<Position> folded)
        {
            var idx = FindIndex(order, current);
            for (var i = 1; i <= order.Count; i++)
            {
                var pos = order[(idx + i) % order.Count];
                if (!folded.Contains(pos))
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

            return -1;
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
