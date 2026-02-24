namespace PokerAnalyzer.Domain.PreflopTree;

public enum PreflopActionType
{
    Fold,
    Call,
    RaiseTo,
    AllIn
}

public sealed record PreflopAction(PreflopActionType Type, int RaiseToBb = 0);
