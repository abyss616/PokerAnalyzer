namespace PokerAnalyzer.Domain.Game;

public enum ActionType : byte
{
    Fold = 0,
    Check = 1,
    Call = 2,
    Bet = 3,
    Raise = 4,
    AllIn = 5,
    PostBigBlind = 6,
    PostSmallBlind = 7,
    SitOut = 8,
    DealFlop = 9,
    DealTurn = 10,
    DealRiver = 11
}
