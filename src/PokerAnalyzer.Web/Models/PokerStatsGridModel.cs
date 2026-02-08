namespace PokerAnalyzer.Web.Models;

public sealed record PokerStatsGridModel
{
    public string Player { get; init; } = string.Empty;
    public int Hands { get; init; }
    public decimal? VPIP { get; init; }
    public decimal? PFR { get; init; }
    public decimal? ThreeBet { get; init; }
    public decimal? Faced3Bet { get; init; }
    public decimal? FoldTo3Bet { get; init; }
    public decimal? FlopWTSD { get; init; }
    public decimal? FlopWSD { get; init; }
    public decimal? FlopCBetOpp { get; init; }
    public decimal? FlopCBet { get; init; }
    public decimal? FoldToFlopCBetOpp { get; init; }
    public decimal? FoldToFlopCBet { get; init; }
    public decimal? FirstFoldToCBet { get; init; }
    public decimal? CallVsCBet { get; init; }
    public decimal? RaiseVsCBet { get; init; }
    public decimal? MultiwayCBet { get; init; }
    public decimal? TurnWTSD { get; init; }
    public decimal? TurnWSD { get; init; }
    public decimal? TurnCBet { get; init; }
    public decimal? TurnFoldToBet { get; init; }
    public decimal? TurnAggression { get; init; }
    public decimal? TurnWTSDCarryover { get; init; }
    public decimal? RiverWTSD { get; init; }
    public decimal? RiverWSD { get; init; }
    public decimal? RiverBetsWhenCheckedTo { get; init; }
    public decimal? RiverFoldToBet { get; init; }
    public decimal? RiverAggression { get; init; }
}
