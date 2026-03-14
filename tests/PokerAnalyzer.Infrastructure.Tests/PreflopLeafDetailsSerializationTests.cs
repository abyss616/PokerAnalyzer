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
  "heroHand": "AsKh",
  "strategy": [],
  "actionDiagnostics": [],
  "actionValueSupport": "Regret diagnostics",
  "bestActionMargin": 12.3,
  "separationScore": 1.44,
  "handComparisons": [],
  "solveMetadata": {
    "strategySource": "LiveSolved",
    "iterationsCompleted": 300,
    "elapsedMilliseconds": 25,
    "solveMode": "Fresh",
    "heroHand": "AsKh",
    "leafEvaluationDetails": {
      "heroHand": "AsKh",
      "usedEquityEvaluator": true,
      "usedFallbackEvaluator": false,
      "evaluatorType": "AbstractedHeadsUp",
      "abstractionSource": "WeightedBlindsBTNUnopened",
      "actualActiveOpponentCount": 2,
      "abstractedOpponentCount": 1,
      "syntheticDefenderLabel": "SyntheticBlindDefender",
      "nodeFamily": "FacingRaise",
      "heroPosition": "BTN",
      "villainPosition": "BB",
      "isHeadsUp": true,
      "rangeDescription": "FacingRaise",
      "rangeDetail": "table-range percentile=0.18 source=table-default",
      "foldProbability": 0.51,
      "continueProbability": 0.49,
      "rootActionType": "Raise",
      "immediateWinComponent": 0.765,
      "continueComponent": 0.111,
      "continueBranchUtility": 0.227,
      "filteredCombos": 121,
      "heroEquity": 0.571,
      "heroUtility": 0.142,
      "equityVsRangePercentile": 0.68,
      "handClass": "Offsuit broadway",
      "blockerSummary": "A/K blockers",
      "rationaleSummary": "summary",
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
        Assert.Equal("AbstractedHeadsUp", result.SolveMetadata.LeafEvaluationDetails!.EvaluatorType);
        Assert.Equal("Raise", result.SolveMetadata.LeafEvaluationDetails.RootActionType);
        Assert.Equal(0.765, result.SolveMetadata.LeafEvaluationDetails.ImmediateWinComponent);
        Assert.Equal(0.571, result.SolveMetadata.LeafEvaluationDetails.HeroEquity);
    }
}


