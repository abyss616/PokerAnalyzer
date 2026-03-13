using System.Net.Http;
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Web.Components.Pages;
using PokerAnalyzer.Web.Services;
using Radzen;
using Xunit;

namespace PokerAnalyzer.Web.Tests;

public sealed class HandHistoryUploadTests : TestContext
{
    [Fact]
    public void RendersLevel2LeafEvaluationSection_WhenMetadataIsPresent()
    {
        Services.AddSingleton(new ApiClient(new HttpClient { BaseAddress = new Uri("http://localhost") }));
        Services.AddScoped<TooltipService>(_ => null!);

        var cut = RenderComponent<HandHistoryUpload>();

        var analysis = new ApiClient.PreflopHandAnalysisResult(
            true,
            null,
            "preflop/v2/VS_OPEN/BTN/eff=100",
            "v2/VS_OPEN/BTN/eff=100",
            "Preflop",
            "BTN",
            "BB",
            "VS_OPEN",
            3.5m,
            1.5m,
            100m,
            1,
            "open=2.5",
            Array.Empty<ApiClient.PreflopLegalAction>(),
            Array.Empty<ApiClient.PreflopRecommendationItem>(),
            "Mix",
            true,
            false,
            false,
            "Solved",
            null,
            "AsKh",
            Array.Empty<ApiClient.PreflopStrategyItem>(),
            Array.Empty<ApiClient.PreflopActionDiagnostic>(),
            "Regret diagnostics",
            12m,
            1.23m,
            new[]
            {
                new ApiClient.PreflopHandComparison("J9o", 90m, 10m, 0m, 0.52, 0.04, "FacingRaise", "EquityBased", "Regret diagnostics", 5m, 0.9m, Array.Empty<ApiClient.PreflopActionDiagnostic>()),
                new ApiClient.PreflopHandComparison("Q4o", 89m, 11m, 0m, 0.519, 0.038, "FacingRaise", "EquityBased", "Regret diagnostics", 4m, 0.8m, Array.Empty<ApiClient.PreflopActionDiagnostic>())
            },
            new ApiClient.PreflopSolveMetadata(
                "LiveSolved",
                300,
                20,
                "Fresh",
                new ApiClient.PreflopLeafEvaluationDetails(
                    "AsKh",
                    true,
                    false,
                    "EquityBased",
                    "FacingRaise",
                    "BTN",
                    "BB",
                    true,
                    "FacingRaise",
                    "table-range percentile=0.18",
                    121,
                    0.571,
                    0.142,
                    0.68,
                    "Offsuit broadway",
                    "A/K blockers",
                    "rationale",
                    null,
                    "Level-2 equity leaf"),
                "AsKh"),
            new ApiClient.PreflopTrace("k", "h", 1, 1, 1, 1, 1, "o", "i", "t", "s", "f", 18, Array.Empty<ApiClient.PreflopTraceAction>()));

        var field = typeof(HandHistoryUpload).GetField("_preflopAnalysis", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(cut.Instance, analysis);
        cut.Render();

        Assert.Contains("Level-2 leaf evaluation", cut.Markup);
        Assert.Contains("Equity-based leaf utility computed from weighted opponent range", cut.Markup);
        Assert.Contains("0.571", cut.Markup);
        Assert.Contains("Why are J9 and Q4 both ~90% raise?", cut.Markup);
    }
}
