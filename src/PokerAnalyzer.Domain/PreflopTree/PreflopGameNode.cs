namespace PokerAnalyzer.Domain.PreflopTree;

public enum PreflopNodeType
{
    Decision,
    Terminal
}

public sealed class PreflopGameNode
{
    public PreflopNodeType Type { get; init; }

    public PreflopPublicState State { get; init; } = default!;

    public int? ActingPlayerIndex { get; init; }

    public List<(PreflopAction Action, PreflopGameNode Child)> Children { get; } = new();

    public string TerminalReason { get; init; } = string.Empty;
}
