namespace PokerAnalyzer.Web.Models;

public sealed class StepLog
{
    public DateTime Time { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
