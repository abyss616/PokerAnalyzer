namespace PokerAnalyzer.Web.Models;

public sealed record LeakageStats
{
    public string PlayerName { get; init; } = string.Empty;
    public decimal? FoldBbVsSteal { get; init; }
}
