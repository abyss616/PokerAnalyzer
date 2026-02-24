namespace PokerAnalyzer.Domain.PreflopTree;

public enum PreflopActionType
{
    Fold,
    Check,
    Call
}

public sealed record PreflopAction(PreflopActionType Type, int RaiseToBb = 0);
