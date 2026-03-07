namespace PokerAnalyzer.Application.PreflopAnalysis;

public interface IPreflopHandAnalysisService
{
    Task<PreflopHandAnalysisResultDto?> AnalyzePreflopByHandNumberAsync(long handNumber, CancellationToken ct);
}
