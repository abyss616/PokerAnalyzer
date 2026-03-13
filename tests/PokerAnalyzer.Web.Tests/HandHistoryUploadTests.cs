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
    public void RendersSingleHandRationaleSection_WhenMetadataIsPresent()
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
                    2,
                    2,
                    null,
                    "FacingRaise",
                    "BTN",
                    "BB",
                    true,
                    "table-range percentile=0.18",
                    "weighted combos",
                    0.571,
                    0.429,
                    121,
                    0.68,
                    0.142,
                    0.85,
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

        Assert.Contains("Why this hand got this strategy", cut.Markup);
        Assert.Contains("Hero hand", cut.Markup);
        Assert.Contains("AsKh", cut.Markup);
        Assert.Contains("Equity-based leaf utility computed from weighted opponent range", cut.Markup);
        Assert.DoesNotContain("Why are J9 and Q4 both ~90% raise?", cut.Markup);
        Assert.DoesNotContain("J9", cut.Markup);
        Assert.DoesNotContain("Q4", cut.Markup);
        Assert.DoesNotContain("90% raise", cut.Markup);
    }
}
