using System.Text.Json;
using PokerAnalyzer.Web.Services;
using Xunit;

namespace PokerAnalyzer.Infrastructure.Tests;

public sealed class PreflopLeafDetailsSerializationTests
{
    [Fact]
    public void PreflopHandAnalysisResult_DeserializesLeafEvaluationDetails()
    {
        const string json = """
{
  "isSupported": true,
  "unsupportedReason": null,
  "canonicalKey": "preflop/v2/VS_OPEN/BTN/eff=100",
  "solverKey": "v2/VS_OPEN/BTN/eff=100",
  "street": "Preflop",
  "actingPosition": "BTN",
  "facingPosition": "BB",
  "historySignature": "VS_OPEN",
  "potBb": 3.5,
  "toCallBb": 1.5,
  "effectiveStackBb": 100,
  "raiseDepth": 1,
  "sizingBucketSummary": "open=2.5",
  "legalActions": [],
  "recommendations": [],
  "summaryRecommendation": "",
  "hasStrategy": true,
  "isFallbackStrategy": false,
  "isUniformStrategy": false,
  "strategyStatus": "Solved",
  "strategyExplanation": null,
  "strategy": [],
  "solveMetadata": {
    "strategySource": "LiveSolved",
    "iterationsCompleted": 300,
    "elapsedMilliseconds": 25,
    "solveMode": "Fresh",
    "leafEvaluationDetails": {
      "usedEquityEvaluator": true,
      "usedFallbackEvaluator": false,
      "evaluatorType": "EquityBased",
      "nodeFamily": "FacingRaise",
      "heroPosition": "BTN",
      "villainPosition": "BB",
      "isHeadsUp": true,
      "rangeDescription": "FacingRaise",
      "rangeDetail": "table-range percentile=0.18",
      "filteredCombos": 121,
      "heroEquity": 0.571,
      "heroUtility": 0.142,
      "fallbackReason": null,
      "displaySummary": "Level-2 equity leaf"
    }
  },
  "trace": {
    "solverKey": "v2/VS_OPEN/BTN/eff=100",
    "historySignature": "VS_OPEN",
    "raiseDepth": 1,
    "toCallBb": 1.5,
    "currentBetBb": 2.5,
    "potBb": 3.5,
    "effectiveStackBb": 100,
    "openSizeBucket": "2.5",
    "isoSizeBucket": "3.5",
    "threeBetBucket": "9",
    "squeezeBucket": "10",
    "fourBetBucket": "22",
    "jamThreshold": 18,
    "rawActionHistory": []
  }
}
""";

        var result = JsonSerializer.Deserialize<ApiClient.PreflopHandAnalysisResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.NotNull(result!.SolveMetadata.LeafEvaluationDetails);
        Assert.Equal("EquityBased", result.SolveMetadata.LeafEvaluationDetails!.EvaluatorType);
        Assert.Equal(0.571, result.SolveMetadata.LeafEvaluationDetails.HeroEquity);
    }
}
