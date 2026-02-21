using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Application.PreflopSolver;

public enum TerminalType { Fold, AllIn, FlopCall }

public sealed class PreflopNode
{
    public required string Id { get; init; }
    public required int ActingPlayer { get; init; } // 0 hero,1 villain
    public required IReadOnlyList<string> Actions { get; init; }
    public required Dictionary<string, PreflopNode> Children { get; init; }
    public TerminalType? TerminalType { get; init; }
    public decimal PotBb { get; init; }
    public decimal EffectiveStackBb { get; init; }
    public bool HeroInPosition { get; init; }
    public Position HeroPosition { get; init; }
    public Position VillainPosition { get; init; }
}

public sealed class PreflopGameTreeBuilder
{
    public PreflopNode BuildDefaultSubgame(Position heroPos, Position villainPos, decimal stackBb)
    {
        // compact 2-player abstraction used per node lookup within a 6-max config.
        var root = new PreflopNode
        {
            Id = $"root:{heroPos}v{villainPos}",
            ActingPlayer = 0,
            Actions = heroPos == Position.SB ? ["limp", "raise_3.0", "fold"] : ["raise_2.5", "fold"],
            Children = new(),
            PotBb = 1.5m,
            EffectiveStackBb = stackBb,
            HeroInPosition = heroPos > villainPos,
            HeroPosition = heroPos,
            VillainPosition = villainPos
        };

        var foldTerminal = Terminal("t:fold", TerminalType.Fold, 1.5m, stackBb, heroPos, villainPos);
        var openFlop = Terminal("t:open_called", TerminalType.FlopCall, 5.5m, stackBb - 2.5m, heroPos, villainPos);
        var threeBetNode = new PreflopNode
        {
            Id = "n:facing_3bet",
            ActingPlayer = 0,
            Actions = ["fold", "call", "fourbet_22", "jam"],
            Children = new(),
            PotBb = 12.5m,
            EffectiveStackBb = stackBb - 10m,
            HeroInPosition = true,
            HeroPosition = heroPos,
            VillainPosition = villainPos
        };
        threeBetNode.Children["fold"] = Terminal("t:villain_wins_3bet", TerminalType.Fold, threeBetNode.PotBb, stackBb, heroPos, villainPos);
        threeBetNode.Children["call"] = Terminal("t:3bet_called", TerminalType.FlopCall, 20.5m, stackBb - 10m, heroPos, villainPos);
        threeBetNode.Children["fourbet_22"] = new PreflopNode
        {
            Id = "n:facing_4bet",
            ActingPlayer = 1,
            Actions = ["fold", "call", "jam"],
            Children = new()
            {
                ["fold"] = Terminal("t:fold_to_4bet", TerminalType.Fold, 34m, stackBb, heroPos, villainPos),
                ["call"] = Terminal("t:4bet_called", TerminalType.FlopCall, 44m, stackBb - 22m, heroPos, villainPos),
                ["jam"] = Terminal("t:4bet_jam", TerminalType.AllIn, stackBb * 2m + 1.5m, 0m, heroPos, villainPos)
            },
            PotBb = 34m,
            EffectiveStackBb = stackBb - 22m,
            HeroInPosition = true,
            HeroPosition = heroPos,
            VillainPosition = villainPos
        };
        threeBetNode.Children["jam"] = Terminal("t:3bet_jam", TerminalType.AllIn, stackBb * 2m + 1.5m, 0m, heroPos, villainPos);

        var facingOpen = new PreflopNode
        {
            Id = "n:facing_open",
            ActingPlayer = 1,
            Actions = ["fold", "call", heroPos == Position.BTN && villainPos == Position.SB ? "threebet_10.5" : "threebet_10"],
            Children = new()
            {
                ["fold"] = Terminal("t:fold_vs_open", TerminalType.Fold, 4m, stackBb, heroPos, villainPos),
                ["call"] = openFlop,
                [heroPos == Position.BTN && villainPos == Position.SB ? "threebet_10.5" : "threebet_10"] = threeBetNode
            },
            PotBb = 4m,
            EffectiveStackBb = stackBb - 2.5m,
            HeroInPosition = heroPos == Position.BTN,
            HeroPosition = heroPos,
            VillainPosition = villainPos
        };

        root.Children["fold"] = foldTerminal;
        if (root.Actions.Contains("raise_2.5")) root.Children["raise_2.5"] = facingOpen;
        if (root.Actions.Contains("raise_3.0")) root.Children["raise_3.0"] = facingOpen;

        if (root.Actions.Contains("limp"))
        {
            root.Children["limp"] = new PreflopNode
            {
                Id = "n:sb_limp_bb",
                ActingPlayer = 1,
                Actions = ["check", "iso_4.5"],
                Children = new()
                {
                    ["check"] = Terminal("t:limp_checked", TerminalType.FlopCall, 2m, stackBb - 1m, heroPos, villainPos),
                    ["iso_4.5"] = new PreflopNode
                    {
                        Id = "n:sb_vs_iso",
                        ActingPlayer = 0,
                        Actions = ["fold", "call", "threebet_10"],
                        Children = new()
                        {
                            ["fold"] = Terminal("t:sb_fold_iso", TerminalType.Fold, 5.5m, stackBb, heroPos, villainPos),
                            ["call"] = Terminal("t:sb_call_iso", TerminalType.FlopCall, 9m, stackBb - 4.5m, heroPos, villainPos),
                            ["threebet_10"] = Terminal("t:sb_3bet_iso", TerminalType.AllIn, stackBb * 2m + 1.5m, 0m, heroPos, villainPos)
                        },
                        PotBb = 5.5m,
                        EffectiveStackBb = stackBb - 4.5m,
                        HeroInPosition = false,
                        HeroPosition = heroPos,
                        VillainPosition = villainPos
                    }
                },
                PotBb = 2m,
                EffectiveStackBb = stackBb - 1m,
                HeroInPosition = false,
                HeroPosition = heroPos,
                VillainPosition = villainPos
            };
        }

        return root;
    }

    private static PreflopNode Terminal(string id, TerminalType type, decimal potBb, decimal stackBb, Position heroPos, Position villainPos)
        => new()
        {
            Id = id,
            ActingPlayer = -1,
            Actions = [],
            Children = new(),
            TerminalType = type,
            PotBb = potBb,
            EffectiveStackBb = stackBb,
            HeroInPosition = heroPos > villainPos,
            HeroPosition = heroPos,
            VillainPosition = villainPos
        };
}
