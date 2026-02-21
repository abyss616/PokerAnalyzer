using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public sealed record PreflopNode(
    string NodeId,
    Position Actor,
    Position Villain,
    PreflopNodeState State,
    IReadOnlyList<ActionType> LegalActions,
    IReadOnlyDictionary<ActionType, decimal> RaiseToByActionBb,
    string Stage);

public static class PreflopGameTree
{
    public static IReadOnlyList<PreflopNode> Build(PreflopSolverConfig config)
    {
        var nodes = new List<PreflopNode>();
        nodes.Add(BuildOpenNode(Position.BTN, config));
        nodes.Add(BuildOpenNode(Position.SB, config));
        nodes.Add(BuildFacingOpenNode(Position.BB, Position.BTN, config, isSbVsBtn: false));
        nodes.Add(BuildFacingOpenNode(Position.SB, Position.BTN, config, isSbVsBtn: true));
        nodes.Add(BuildFacing3BetNode(Position.BTN, Position.BB, config));
        nodes.Add(BuildFacing4BetNode(Position.BTN, Position.BB, config));
        nodes.Add(BuildSbLimpNode(config));
        nodes.Add(BuildBbVsSbLimpNode(config));
        return nodes;
    }

    private static PreflopNode BuildOpenNode(Position actor, PreflopSolverConfig config)
    {
        var openSize = actor == Position.SB ? 3.0m : 2.5m;
        var legal = actor == Position.SB
            ? new[] { ActionType.Fold, ActionType.Call, ActionType.Raise }
            : new[] { ActionType.Fold, ActionType.Raise };

        return new PreflopNode(
            $"OPEN_{actor}", actor, Position.BB,
            new PreflopNodeState($"OPEN_{actor}", actor, Position.BB, 1.5m, actor == Position.SB ? 0.5m : 1m, actor == Position.SB ? 0.5m : 0m, 1m, actor == Position.SB, false, false, config.EffectiveStackBb),
            legal,
            new Dictionary<ActionType, decimal> { [ActionType.Raise] = openSize },
            "Unopened");
    }

    private static PreflopNode BuildFacingOpenNode(Position actor, Position opener, PreflopSolverConfig config, bool isSbVsBtn)
    {
        var threeBet = isSbVsBtn ? 10.5m : actor == Position.BB ? 10m : 9m;
        return new PreflopNode(
            $"VS_OPEN_{actor}_vs_{opener}", actor, opener,
            new PreflopNodeState($"VS_OPEN_{actor}_vs_{opener}", actor, opener, 4.0m, 2.5m, actor == Position.BB ? 1m : 0.5m, 2.5m, false, false, false, config.EffectiveStackBb),
            new[] { ActionType.Fold, ActionType.Call, ActionType.Raise, ActionType.AllIn },
            new Dictionary<ActionType, decimal> { [ActionType.Raise] = threeBet },
            "FacingOpen");
    }

    private static PreflopNode BuildFacing3BetNode(Position actor, Position villain, PreflopSolverConfig config)
    {
        var allowJam = config.EffectiveStackBb <= 30m || 22m >= config.EffectiveStackBb * 0.6m;
        var legal = allowJam
            ? new[] { ActionType.Fold, ActionType.Call, ActionType.Raise, ActionType.AllIn }
            : new[] { ActionType.Fold, ActionType.Call, ActionType.Raise };

        return new PreflopNode(
            $"VS_3BET_{actor}_vs_{villain}", actor, villain,
            new PreflopNodeState($"VS_3BET_{actor}_vs_{villain}", actor, villain, 14m, 7.5m, 2.5m, 10m, false, true, false, config.EffectiveStackBb),
            legal,
            new Dictionary<ActionType, decimal> { [ActionType.Raise] = 22m },
            "Facing3Bet");
    }

    private static PreflopNode BuildFacing4BetNode(Position actor, Position villain, PreflopSolverConfig config)
    {
        var legal = new[] { ActionType.Fold, ActionType.Call, ActionType.AllIn };
        return new PreflopNode(
            $"VS_4BET_{actor}_vs_{villain}", actor, villain,
            new PreflopNodeState($"VS_4BET_{actor}_vs_{villain}", actor, villain, 35m, 12m, 10m, 22m, false, false, true, config.EffectiveStackBb),
            legal,
            new Dictionary<ActionType, decimal>(),
            "Facing4Bet");
    }

    private static PreflopNode BuildSbLimpNode(PreflopSolverConfig config)
        => new(
            "SB_LIMP_FIRST_IN", Position.SB, Position.BB,
            new PreflopNodeState("SB_LIMP_FIRST_IN", Position.SB, Position.BB, 1.5m, 0.5m, 0.5m, 1m, true, false, false, config.EffectiveStackBb),
            new[] { ActionType.Call, ActionType.Raise },
            new Dictionary<ActionType, decimal> { [ActionType.Raise] = 3m },
            "Unopened");

    private static PreflopNode BuildBbVsSbLimpNode(PreflopSolverConfig config)
        => new(
            "BB_VS_SB_LIMP", Position.BB, Position.SB,
            new PreflopNodeState("BB_VS_SB_LIMP", Position.BB, Position.SB, 2m, 0m, 1m, 1m, true, false, false, config.EffectiveStackBb),
            new[] { ActionType.Check, ActionType.Raise },
            new Dictionary<ActionType, decimal> { [ActionType.Raise] = 4.5m },
            "Limped");
}
