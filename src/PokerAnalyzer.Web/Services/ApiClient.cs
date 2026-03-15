using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Headers;

namespace PokerAnalyzer.Web.Services;


public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public sealed record UploadHandHistoryResult(Guid SessionId);
    public sealed record PlayerStatPercent(string Name, decimal Percent);
    public sealed record PlayerStatsResult(string Player, int Hands, IReadOnlyList<PlayerStatPercent> Stats);

    public sealed record PreflopHandAnalysisResult(
        bool IsSupported,
        string? UnsupportedReason,
        string CanonicalKey,
        string SolverKey,
        string Street,
        string ActingPosition,
        string? FacingPosition,
        string HistorySignature,
        decimal PotBb,
        decimal ToCallBb,
        decimal EffectiveStackBb,
        int RaiseDepth,
        string SizingBucketSummary,
        IReadOnlyList<PreflopLegalAction> LegalActions,
        IReadOnlyList<PreflopRecommendationItem> Recommendations,
        string SummaryRecommendation,
        bool HasStrategy,
        bool IsFallbackStrategy,
        bool IsUniformStrategy,
        string? StrategyStatus,
        string? StrategyExplanation,
        string? HeroHand,
        IReadOnlyList<PreflopStrategyItem> Strategy,
        IReadOnlyList<PreflopActionDiagnostic> ActionDiagnostics,
        string? ActionValueSupport,
        decimal? BestActionMargin,
        decimal? SeparationScore,
        PreflopSolveMetadata SolveMetadata,
        IReadOnlyList<PreflopActionExplanation> ActionExplanations,
        PreflopTrace Trace);

    public sealed record PreflopLegalAction(string ActionKey, string ActionType, decimal? SizeBb, bool IsFacingAllIn);
    public sealed record PreflopRecommendationItem(string ActionKey, string DisplayLabel, decimal Frequency, bool IsBestAction);
    public sealed record PreflopStrategyItem(string ActionKey, decimal Frequency);
    public sealed record PreflopActionDiagnostic(string ActionKey, decimal Frequency, decimal CurrentPolicyFrequency, double Regret, double PositiveRegret, bool IsBestByFrequency);
    public sealed record PreflopActionExplanation(string ActionKey, PreflopLeafEvaluationDetails? LeafEvaluationDetails);
    public sealed record PreflopSolveMetadata(
        string StrategySource,
        int IterationsCompleted,
        long ElapsedMilliseconds,
        string SolveMode,
        PreflopLeafEvaluationDetails? LeafEvaluationDetails,
        string? HeroHand);

    public sealed record PreflopLeafEvaluationDetails(
        string HeroHand,
        bool UsedEquityEvaluator,
        bool UsedFallbackEvaluator,
        string EvaluatorType,
        string? AbstractionSource,
        int ActualActiveOpponentCount,
        int? AbstractedOpponentCount,
        string? SyntheticDefenderLabel,
        string? NodeFamily,
        string? HeroPosition,
        string? VillainPosition,
        bool IsHeadsUp,
        string? RangeDescription,
        string? RangeDetail,
        double? FoldProbability,
        double? ContinueProbability,
        string? RootActionType,
        double? ImmediateWinComponent,
        double? ContinueComponent,
        double? ContinueBranchUtility,
        int? FilteredCombos,
        double? HeroEquity,
        double? HeroUtility,
        double? EquityVsRangePercentile,
        string? HandClass,
        string? BlockerSummary,
        string? RationaleSummary,
        string? FallbackReason,
        string? DisplaySummary,
        string? RootEvaluatorMode = null,
        int? RootActiveOpponentCount = null,
        int? LeafActiveOpponentCount = null,
        int? SampledTrajectoryDepth = null,
        bool? UsedDirectAbstractionShortcut = null,
        long? TraversalMilliseconds = null,
        long? LeafEvaluationMilliseconds = null,
        string? ActivePopulationProfile = null);
    public sealed record PreflopTrace(
        string SolverKey,
        string HistorySignature,
        int RaiseDepth,
        decimal ToCallBb,
        decimal CurrentBetBb,
        decimal PotBb,
        decimal EffectiveStackBb,
        string OpenSizeBucket,
        string IsoSizeBucket,
        string ThreeBetBucket,
        string SqueezeBucket,
        string FourBetBucket,
        decimal JamThreshold,
        IReadOnlyList<PreflopTraceAction> RawActionHistory);

    public sealed record PreflopTraceAction(Guid PlayerId, string? Position, string ActionType, decimal AmountBb);

    public async Task<UploadHandHistoryResult> UploadHandHistoryXmlAsync(
        IBrowserFile file,
        CancellationToken ct = default)
    {
        if (file is null) throw new ArgumentNullException(nameof(file));
        if (!file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .xml files are supported.");

        // match API limit; keep it consistent with [RequestSizeLimit] on the controller
        const long maxBytes = 20_000_000;

        using var content = new MultipartFormDataContent();

        await using var stream = file.OpenReadStream(maxAllowedSize: maxBytes, cancellationToken: ct);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/xml");

        content.Add(fileContent, name: "file", fileName: file.Name);

        using var resp = await _http.PostAsync("api/hand-histories/upload-xml", content, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Upload failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        var result = await resp.Content.ReadFromJsonAsync<UploadHandHistoryResult>(cancellationToken: ct);
        if (result is null || result.SessionId == Guid.Empty)
            throw new InvalidOperationException("Upload succeeded but response was invalid.");

        return result;
    }

    public async Task<IReadOnlyList<PlayerStatsResult>> GetPlayerStatsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        using var resp = await _http.GetAsync($"api/hand-histories/{sessionId}/player-stats", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Player stats request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        var result = await resp.Content.ReadFromJsonAsync<IReadOnlyList<PlayerStatsResult>>(cancellationToken: ct);
        return result ?? Array.Empty<PlayerStatsResult>();
    }

    public async Task<PreflopHandAnalysisResult?> AnalyzePreflopByHandNumberAsync(
        long handNumber,
        string? populationProfile = null,
        CancellationToken ct = default)
    {
        var url = $"api/preflop-analysis/hand-number/{handNumber}";
        if (!string.IsNullOrWhiteSpace(populationProfile))
            url += $"?populationProfile={Uri.EscapeDataString(populationProfile)}";

        using var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Preflop analysis request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        return await resp.Content.ReadFromJsonAsync<PreflopHandAnalysisResult>(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<PlayerStatsResult>> GetLatestPlayerStatsAsync(
        CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/hand-histories/latest/player-stats", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Latest player stats request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        var result = await resp.Content.ReadFromJsonAsync<IReadOnlyList<PlayerStatsResult>>(cancellationToken: ct);
        return result ?? Array.Empty<PlayerStatsResult>();
    }
}
