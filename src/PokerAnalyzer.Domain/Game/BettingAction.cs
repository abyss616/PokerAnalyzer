namespace PokerAnalyzer.Domain.Game;

/// <summary>
/// A betting action as recorded in the hand history.
/// For Bet/Raise/AllIn, Amount means "total contribution for this street after the action" (i.e., to-amount).
/// For Call, Amount is ignored (state determines call amount).
/// For Check/Fold, Amount must be zero.
/// </summary>
public readonly record struct BettingAction(
    Street Street,
    PlayerId ActorId,
    ActionType Type,
    ChipAmount Amount
);
