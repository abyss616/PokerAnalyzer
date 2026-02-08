namespace PokerAnalyzer.Web.Models;

public sealed record LeakageStatsGridModel
{
    public string PlayerName { get; init; } = string.Empty;
    public decimal? FoldBbVsSteal { get; init; }
    public decimal? RfiButton { get; init; }
}
