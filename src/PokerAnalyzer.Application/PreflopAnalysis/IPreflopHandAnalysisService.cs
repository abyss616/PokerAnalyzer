namespace PokerAnalyzer.Application.PreflopAnalysis;

public interface IPreflopHandAnalysisService
{
    Task<PreflopHandAnalysisResultDto?> AnalyzePreflopByHandNumberAsync(long handNumber, CancellationToken ct);
    Task<PreflopNodeQueryResultDto?> QueryPreflopNodeByHandNumberAsync(long handNumber, CancellationToken ct);
    Task<PreflopNodeQueryResultDto> QueryPreflopNodeAsync(PreflopNodeQueryRequestDto request, CancellationToken ct);
}
