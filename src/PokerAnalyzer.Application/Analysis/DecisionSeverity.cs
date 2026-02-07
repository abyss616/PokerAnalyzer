namespace PokerAnalyzer.Application.Analysis;

public enum DecisionSeverity : byte
{
    Unknown = 0,
    Ok = 1,
    Inaccuracy = 2,
    Mistake = 3,
    Blunder = 4
}
