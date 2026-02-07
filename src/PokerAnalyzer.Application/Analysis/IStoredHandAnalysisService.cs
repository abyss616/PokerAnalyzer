using PokerAnalyzer.Application.Analysis;

public interface IStoredHandAnalysisService
{
    Task<HandAnalysisResult> AnalyzeHandAsync(Guid handId, CancellationToken ct);
}
