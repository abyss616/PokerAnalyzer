namespace PokerAnalyzer.Domain.PreflopTree;

public enum PreflopActionType
{
    Fold,
    Check,
    Call,
    RaiseTo,
    AllIn
}

public sealed record PreflopAction(PreflopActionType Type, int RaiseToBb = 0);
