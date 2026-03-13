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
            Array.Empty<ApiClient.PreflopStrategyItem>(),
            new ApiClient.PreflopSolveMetadata(
                "LiveSolved",
                300,
                20,
                "Fresh",
                new ApiClient.PreflopLeafEvaluationDetails(
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
                    null,
                    "Level-2 equity leaf")),
            new ApiClient.PreflopTrace("k", "h", 1, 1, 1, 1, 1, "o", "i", "t", "s", "f", 18, Array.Empty<ApiClient.PreflopTraceAction>()));

        var field = typeof(HandHistoryUpload).GetField("_preflopAnalysis", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(cut.Instance, analysis);
        cut.Render();

        Assert.Contains("Level-2 leaf evaluation", cut.Markup);
        Assert.Contains("Equity-based leaf utility computed from weighted opponent range", cut.Markup);
        Assert.Contains("0.571", cut.Markup);
    }
}
