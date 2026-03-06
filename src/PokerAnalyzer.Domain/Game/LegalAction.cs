namespace PokerAnalyzer.Domain.Game;

public sealed record LegalAction(ActionType ActionType, ChipAmount? Amount = null);
