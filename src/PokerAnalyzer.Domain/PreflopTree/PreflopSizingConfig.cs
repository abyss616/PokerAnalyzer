namespace PokerAnalyzer.Domain.PreflopTree;

public sealed class PreflopSizingConfig
{
    public int[] OpenRaiseToBb { get; init; } = [2, 3];

    public int[] ThreeBetToBb { get; init; } = [9, 11];

    public int[] FourBetToBb { get; init; } = [22];

    public bool AllowAllInAlways { get; init; } = true;

    public static PreflopSizingConfig Default { get; } = new();
}
