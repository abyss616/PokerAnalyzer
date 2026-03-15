using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using Xunit;

namespace PokerAnalyzer.Application.Tests;

public sealed class EquityBasedPreflopLeafEvaluatorTests
{
    [Theory]
    [InlineData("v2/UNOPENED/BTN/eff=100", PreflopNodeFamily.Unopened)]
    [InlineData("v2/LIMP_OPTION/BB/eff=100", PreflopNodeFamily.FacingLimp)]
    [InlineData("v2/VS_OPEN/BTN/eff=100", PreflopNodeFamily.FacingRaise)]
    [InlineData("v2/VS_3BET/BTN/eff=100", PreflopNodeFamily.Facing3Bet)]
    public void Evaluate_UsesSameEvaluatorPathAcrossFamilies(string solverKey, PreflopNodeFamily expectedFamily)
    {
        var fallback = new HeuristicPreflopLeafEvaluator();
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), fallback, samplesPerMatchup: 120);
        var context = CreateHeadsUpContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QdJd"), ActionType.Raise, solverKey);

        var result = evaluator.Evaluate(context);

        Assert.Contains("equity leaf evaluator:", result.Reason);
        Assert.Contains($"family={expectedFamily}", result.Reason);
        Assert.DoesNotContain("fallback", result.Reason);
        Assert.NotNull(result.Details);
        Assert.True(result.Details!.UsedEquityEvaluator);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("TrueHeadsUp", result.Details.EvaluatorType);
        Assert.Equal(expectedFamily.ToString(), result.Details.NodeFamily);
        Assert.Equal("BTN", result.Details.HeroPosition);
        Assert.Equal("BB", result.Details.VillainPosition);
        Assert.NotNull(result.Details.HeroEquity);
        Assert.NotNull(result.Details.HeroUtility);
        Assert.Equal("AKo", result.Details.HeroHand);
        Assert.NotNull(result.Details.HandClass);
        Assert.NotNull(result.Details.RationaleSummary);
    }


    [Fact]
    public void Evaluate_LimpOptionBb_UsesActionSizeSensitiveUtilityAndDiagnostics()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var check = evaluator.Evaluate(CreateLimpOptionBbContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QdJd"), ActionType.Check));
        var raiseFivePointFive = evaluator.Evaluate(CreateLimpOptionBbContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QdJd"), ActionType.Raise, new ChipAmount(550)));
        var raiseNine = evaluator.Evaluate(CreateLimpOptionBbContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QdJd"), ActionType.Raise, new ChipAmount(900)));

        Assert.NotNull(check.Details);
        Assert.NotNull(raiseFivePointFive.Details);
        Assert.NotNull(raiseNine.Details);
        Assert.Equal("FacingLimp", check.Details!.NodeFamily);
        Assert.Equal("BB", check.Details.HeroPosition);
        Assert.Equal("SB", check.Details.VillainPosition);

        Assert.NotEqual(check.Details.HeroUtility, raiseFivePointFive.Details!.HeroUtility);
        Assert.NotEqual(raiseFivePointFive.Details.HeroUtility, raiseNine.Details!.HeroUtility);

        Assert.Equal(0d, check.Details.FoldProbability);
        Assert.Equal(1d, check.Details.ContinueProbability);
        Assert.Equal(0d, check.Details.ImmediateWinComponent);

        Assert.NotNull(raiseFivePointFive.Details.FoldProbability);
        Assert.NotNull(raiseFivePointFive.Details.ContinueProbability);
        Assert.NotNull(raiseFivePointFive.Details.ImmediateWinComponent);
        Assert.NotNull(raiseFivePointFive.Details.ContinueComponent);
        Assert.NotNull(raiseFivePointFive.Details.ContinueBranchUtility);

        Assert.True(raiseNine.Details.FoldProbability > raiseFivePointFive.Details.FoldProbability);
        Assert.True(raiseNine.Details.ContinueProbability < raiseFivePointFive.Details.ContinueProbability);
    }

    [Fact]
    public void Evaluate_AppliesBlockerFiltering_BeforeEquity()
    {
        var provider = new StaticOpponentRangeProvider(
            new WeightedHoleCards(HoleCards.Parse("AsAd"), 1d),
            new WeightedHoleCards(HoleCards.Parse("KdQd"), 1d));

        var evaluator = new EquityBasedPreflopLeafEvaluator(provider, new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateHeadsUpContext(HoleCards.Parse("AsKh"), HoleCards.Parse("QcJc"), ActionType.Raise, "v2/UNOPENED/BTN/eff=100");

        var result = evaluator.Evaluate(context);

        Assert.Contains("filteredCombos=1", result.Reason);
        Assert.DoesNotContain("fallback", result.Reason);
        Assert.NotNull(result.Details);
        Assert.Equal(1, result.Details!.FilteredCombos);
        Assert.Equal("static-test", result.Details.RangeDescription);
        Assert.Equal("static-test", result.Details.RangeDetail);
    }


    [Fact]
    public void Evaluate_UnopenedBtnMultiway_UsesAbstractedHeadsUp()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("AsKh"));

        var result = evaluator.Evaluate(context);

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.True(result.Details.UsedEquityEvaluator);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("WeightedBlindsBTNUnopened", result.Details.AbstractionSource);
        Assert.Equal(2, result.Details.ActualActiveOpponentCount);
        Assert.Equal(1, result.Details.AbstractedOpponentCount);
        Assert.NotNull(result.Details.FoldProbability);
        Assert.NotNull(result.Details.ContinueProbability);
    }

    [Fact]
    public void Evaluate_UnopenedBtnMultiway_SeparatesDifferentHands()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var j9Result = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d")));
        var q4Result = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Qc4d")));

        Assert.NotNull(j9Result.Details);
        Assert.NotNull(q4Result.Details);
        Assert.Equal("AbstractedHeadsUp", j9Result.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", q4Result.Details!.EvaluatorType);
        Assert.NotEqual(j9Result.Details.HeroUtility, q4Result.Details.HeroUtility);
        Assert.NotEqual(j9Result.Details.HeroEquity, q4Result.Details.HeroEquity);
    }



    [Fact]
    public void Evaluate_SupportedAbstractionMode_IsStableAcrossLeafOpponentCounts()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var leafTwoOpponents = evaluator.Evaluate(CreateThreeWayContextWithLeafActiveOpponents("v2/UNOPENED/BTN/eff=100", 2));
        var leafZeroOpponents = evaluator.Evaluate(CreateThreeWayContextWithLeafActiveOpponents("v2/UNOPENED/BTN/eff=100", 0));

        Assert.NotNull(leafTwoOpponents.Details);
        Assert.NotNull(leafZeroOpponents.Details);
        Assert.Equal("AbstractedHeadsUp", leafTwoOpponents.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", leafZeroOpponents.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", leafTwoOpponents.Details.RootEvaluatorMode);
        Assert.Equal("AbstractedHeadsUp", leafZeroOpponents.Details.RootEvaluatorMode);
        Assert.Equal(2, leafTwoOpponents.Details.RootActiveOpponentCount);
        Assert.Equal(2, leafZeroOpponents.Details.RootActiveOpponentCount);
        Assert.Equal(2, leafTwoOpponents.Details.LeafActiveOpponentCount);
        Assert.Equal(0, leafZeroOpponents.Details.LeafActiveOpponentCount);
        Assert.True(leafTwoOpponents.Details.UsedDirectAbstractionShortcut);
        Assert.True(leafZeroOpponents.Details.UsedDirectAbstractionShortcut);
        Assert.Equal(leafTwoOpponents.Details.HeroUtility, leafZeroOpponents.Details.HeroUtility);
    }

    [Fact]
    public void Evaluate_UnopenedBtnRaise_IncludesFoldEquityComponents()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d"), ActionType.Raise));

        Assert.NotNull(result.Details);
        Assert.Equal("Raise", result.Details!.RootActionType);
        Assert.NotNull(result.Details.FoldProbability);
        Assert.NotNull(result.Details.ContinueProbability);
        Assert.True(result.Details.ImmediateWinComponent > 0d);
        Assert.NotNull(result.Details.ContinueBranchUtility);
        Assert.NotNull(result.Details.ContinueComponent);
        Assert.Contains("Action=Raise", result.Details.DisplaySummary);
    }

    [Fact]
    public void Evaluate_UnopenedBtn_ActionUtilitiesDifferAcrossRaiseCallFold()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var raise = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d"), ActionType.Raise));
        var call = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d"), ActionType.Call));
        var fold = evaluator.Evaluate(CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Jc9d"), ActionType.Fold));

        Assert.NotNull(raise.Details);
        Assert.NotNull(call.Details);
        Assert.NotNull(fold.Details);
        Assert.NotEqual(raise.Details!.HeroUtility, call.Details!.HeroUtility);
        Assert.Equal(0d, fold.Details!.HeroUtility);
        Assert.Equal("Raise", raise.Details.RootActionType);
        Assert.Equal("Call", call.Details.RootActionType);
        Assert.Equal("Fold", fold.Details.RootActionType);
    }


    [Fact]
    public void Evaluate_UnopenedBtn_FoldProbabilityChangesByProfile()
    {
        var gtoEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName));

        var microEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.MicroStakesLoosePassiveName));

        var context = CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("KcTd"), ActionType.Raise);
        var gto = gtoEvaluator.Evaluate(context);
        var micro = microEvaluator.Evaluate(context);

        Assert.NotNull(gto.Details?.FoldProbability);
        Assert.NotNull(micro.Details?.FoldProbability);
        Assert.NotEqual(gto.Details!.FoldProbability, micro.Details!.FoldProbability);
        Assert.True(micro.Details.FoldProbability < gto.Details.FoldProbability);
        Assert.Contains("sbPct=0.45", gto.Details.RangeDetail);
        Assert.Contains("bbPct=0.45", gto.Details.RangeDetail);
        Assert.Contains("sbPct=0.52", micro.Details.RangeDetail);
        Assert.Contains("bbPct=0.62", micro.Details.RangeDetail);
    }

    [Fact]
    public void Evaluate_UnopenedBtn_UsesProfileSpecificRangeCompositionInDiagnosticsAndRangeShape()
    {
        var tightEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.TightRegsName));

        var microEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.MicroStakesLoosePassiveName));

        var context = CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("9c7d"), ActionType.Raise);
        var tight = tightEvaluator.Evaluate(context);
        var micro = microEvaluator.Evaluate(context);

        Assert.NotNull(tight.Details);
        Assert.NotNull(micro.Details);
        Assert.Contains("sbPct=0.36", tight.Details!.RangeDetail);
        Assert.Contains("bbPct=0.40", tight.Details.RangeDetail);
        Assert.Contains("table-range percentile=0.36 source=profile-override", tight.Details.RangeDetail);
        Assert.Contains("table-range percentile=0.40 source=profile-override", tight.Details.RangeDetail);
        Assert.Contains("sbPct=0.52", micro.Details!.RangeDetail);
        Assert.Contains("bbPct=0.62", micro.Details.RangeDetail);
        Assert.Contains("table-range percentile=0.52 source=profile-override", micro.Details.RangeDetail);
        Assert.Contains("table-range percentile=0.62 source=profile-override", micro.Details.RangeDetail);
        Assert.NotEqual(tight.Details.FilteredCombos, micro.Details.FilteredCombos);
    }

    [Fact]
    public void Evaluate_UnopenedBtn_MicroStakesPenalizesMarginalOffsuitOpens()
    {
        var gtoEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName));

        var microEvaluator = new EquityBasedPreflopLeafEvaluator(
            new TableDrivenOpponentRangeProvider(),
            new HeuristicPreflopLeafEvaluator(),
            samplesPerMatchup: 120,
            populationProfileProvider: new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.MicroStakesLoosePassiveName));

        var context = CreateThreeWayContext("v2/UNOPENED/BTN/eff=100", HoleCards.Parse("Qc4d"), ActionType.Raise);
        var gto = gtoEvaluator.Evaluate(context);
        var micro = microEvaluator.Evaluate(context);

        Assert.NotNull(gto.Details?.HeroUtility);
        Assert.NotNull(micro.Details?.HeroUtility);
        Assert.True(micro.Details!.HeroUtility < gto.Details!.HeroUtility);
        Assert.Equal(PreflopPopulationProfiles.GtoLikeName, gto.Details.ActivePopulationProfile);
        Assert.Equal(PreflopPopulationProfiles.MicroStakesLoosePassiveName, micro.Details.ActivePopulationProfile);
    }


    [Fact]
    public void Evaluate_BtnFacingLimpMultiway_UsesAbstractedHeadsUp()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateBtnFacingLimpMultiwayContext(ActionType.Call);

        var result = evaluator.Evaluate(context);

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", result.Details.RootEvaluatorMode);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("SyntheticFieldFacingLimp", result.Details.AbstractionSource);
        Assert.Equal("SyntheticLimpFieldDefender", result.Details.SyntheticDefenderLabel);
        Assert.Equal("FacingLimp", result.Details.NodeFamily);
    }

    [Fact]
    public void Evaluate_BtnFacingLimpMultiway_DifferentiatesCallAndRaiseUtilities()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);

        var overlimp = evaluator.Evaluate(CreateBtnFacingLimpMultiwayContext(ActionType.Call));
        var isoFivePointFive = evaluator.Evaluate(CreateBtnFacingLimpMultiwayContext(ActionType.Raise, new ChipAmount(550)));
        var isoNine = evaluator.Evaluate(CreateBtnFacingLimpMultiwayContext(ActionType.Raise, new ChipAmount(900)));

        Assert.NotNull(overlimp.Details);
        Assert.NotNull(isoFivePointFive.Details);
        Assert.NotNull(isoNine.Details);

        Assert.NotEqual(overlimp.Details!.HeroUtility, isoFivePointFive.Details!.HeroUtility);
        Assert.NotEqual(isoFivePointFive.Details.HeroUtility, isoNine.Details!.HeroUtility);

        Assert.Equal(0d, overlimp.Details.FoldProbability);
        Assert.Equal(1d, overlimp.Details.ContinueProbability);
        Assert.Equal(0d, overlimp.Details.ImmediateWinComponent);

        Assert.True(isoFivePointFive.Details.FoldProbability > 0d);
        Assert.True(isoFivePointFive.Details.ContinueProbability < 1d);
        Assert.True(isoNine.Details.FoldProbability > isoFivePointFive.Details.FoldProbability);
        Assert.True(isoNine.Details.ContinueProbability < isoFivePointFive.Details.ContinueProbability);

        Assert.NotNull(isoFivePointFive.Details.RangeDescription);
        Assert.NotNull(isoFivePointFive.Details.RangeDetail);
        Assert.NotNull(isoFivePointFive.Details.ContinueBranchUtility);
    }

    [Fact]
    public void Evaluate_CoFacingLimpMultiway_UsesAbstractedHeadsUp()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateCoFacingLimpMultiwayContext(ActionType.Call));

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", result.Details.RootEvaluatorMode);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("SyntheticFieldFacingLimp", result.Details.AbstractionSource);
        Assert.Equal("SyntheticLimpFieldDefender", result.Details.SyntheticDefenderLabel);
        Assert.Equal("FacingLimp", result.Details.NodeFamily);
        Assert.Equal("CO", result.Details.HeroPosition);
    }

    [Fact]
    public void Evaluate_CoFacingLimpMultiway_DifferentiatesCallAndIsoSizes()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);

        var call = evaluator.Evaluate(CreateCoFacingLimpMultiwayContext(ActionType.Call));
        var isoFivePointFive = evaluator.Evaluate(CreateCoFacingLimpMultiwayContext(ActionType.Raise, new ChipAmount(550)));
        var isoNine = evaluator.Evaluate(CreateCoFacingLimpMultiwayContext(ActionType.Raise, new ChipAmount(900)));

        Assert.NotNull(call.Details);
        Assert.NotNull(isoFivePointFive.Details);
        Assert.NotNull(isoNine.Details);

        Assert.NotEqual(call.Details!.HeroUtility, isoFivePointFive.Details!.HeroUtility);
        Assert.NotEqual(isoFivePointFive.Details.HeroUtility, isoNine.Details!.HeroUtility);

        Assert.Equal(0d, call.Details.FoldProbability);
        Assert.Equal(1d, call.Details.ContinueProbability);
        Assert.Equal(0d, call.Details.ImmediateWinComponent);

        Assert.True(isoFivePointFive.Details.FoldProbability > 0d);
        Assert.True(isoFivePointFive.Details.ContinueProbability < 1d);
        Assert.True(isoNine.Details.FoldProbability > isoFivePointFive.Details.FoldProbability);
        Assert.True(isoNine.Details.ContinueProbability < isoFivePointFive.Details.ContinueProbability);
        Assert.NotNull(isoFivePointFive.Details.ContinueBranchUtility);
        Assert.Contains("Action=Raise", isoFivePointFive.Details.DisplaySummary);
    }

    [Fact]
    public void Evaluate_SbUnopenedMultiway_UsesAbstractedHeadsUp()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Call));

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", result.Details.RootEvaluatorMode);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("SyntheticFieldSbUnopened", result.Details.AbstractionSource);
        Assert.Equal("SyntheticSbUnopenedDefender", result.Details.SyntheticDefenderLabel);
        Assert.Equal("Unopened", result.Details.NodeFamily);
        Assert.Equal("SB", result.Details.HeroPosition);
    }

    [Fact]
    public void Evaluate_SbUnopenedMultiway_DoesNotFallbackAsUnsupported()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(550)));

        Assert.DoesNotContain("equity evaluator fallback", result.Reason);
        Assert.DoesNotContain("unsupported root evaluator mode", result.Reason);
        Assert.NotNull(result.Details);
        Assert.True(result.Details!.UsedEquityEvaluator);
        Assert.False(result.Details.UsedFallbackEvaluator);
    }

    [Fact]
    public void Evaluate_SbUnopenedMultiway_DifferentiatesFoldCompleteAndRaiseSizes()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var fold = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Fold));
        var complete = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Call));
        var raiseFivePointFive = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(550)));
        var raiseNine = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(900)));

        Assert.NotNull(fold.Details);
        Assert.NotNull(complete.Details);
        Assert.NotNull(raiseFivePointFive.Details);
        Assert.NotNull(raiseNine.Details);

        Assert.Equal(0d, fold.Details!.HeroUtility);
        Assert.NotEqual(complete.Details!.HeroUtility, raiseFivePointFive.Details!.HeroUtility);
        Assert.NotEqual(raiseFivePointFive.Details.HeroUtility, raiseNine.Details!.HeroUtility);

        Assert.Equal(0d, complete.Details.FoldProbability);
        Assert.Equal(1d, complete.Details.ContinueProbability);
        Assert.True(raiseFivePointFive.Details.FoldProbability > 0d);
        Assert.True(raiseNine.Details.FoldProbability > raiseFivePointFive.Details.FoldProbability);
    }

    [Fact]
    public void Evaluate_SbUnopenedMultiway_PopulatesDiagnostics()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateSbUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(900)));

        Assert.NotNull(result.Details);
        Assert.Equal("SyntheticFieldSbUnopened", result.Details!.AbstractionSource);
        Assert.Equal("SyntheticSbUnopenedDefender", result.Details.SyntheticDefenderLabel);
        Assert.NotNull(result.Details.RangeDescription);
        Assert.NotNull(result.Details.RangeDetail);
        Assert.NotNull(result.Details.FoldProbability);
        Assert.NotNull(result.Details.ContinueProbability);
        Assert.NotNull(result.Details.ImmediateWinComponent);
        Assert.NotNull(result.Details.ContinueComponent);
        Assert.NotNull(result.Details.ContinueBranchUtility);
        Assert.Contains("Action=Raise", result.Details.DisplaySummary);
    }

    [Fact]
    public void Evaluate_HjUnopenedMultiway_UsesGeneralizedSyntheticAbstraction()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateHjUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(250)));

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.Equal("AbstractedHeadsUp", result.Details.RootEvaluatorMode);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.Equal("SyntheticFieldUnopened", result.Details.AbstractionSource);
        Assert.Equal("SyntheticUnopenedFieldDefender", result.Details.SyntheticDefenderLabel);
        Assert.Equal("Unopened", result.Details.NodeFamily);
        Assert.Equal("HJ", result.Details.HeroPosition);
        Assert.DoesNotContain("unsupported root evaluator mode", result.Reason);
    }

    [Fact]
    public void Evaluate_CoUnopenedMultiway_UsesGeneralizedSyntheticAbstraction()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var result = evaluator.Evaluate(CreateCoUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(250)));

        Assert.NotNull(result.Details);
        Assert.Equal("AbstractedHeadsUp", result.Details!.EvaluatorType);
        Assert.Equal("SyntheticFieldUnopened", result.Details.AbstractionSource);
        Assert.Equal("SyntheticUnopenedFieldDefender", result.Details.SyntheticDefenderLabel);
        Assert.Equal("CO", result.Details.HeroPosition);
        Assert.Equal("Unopened", result.Details.NodeFamily);
        Assert.False(result.Details.UsedFallbackEvaluator);
        Assert.NotNull(result.Details.RangeDescription);
        Assert.NotNull(result.Details.RangeDetail);
        Assert.NotNull(result.Details.FoldProbability);
        Assert.NotNull(result.Details.ContinueProbability);
    }

    [Fact]
    public void Evaluate_CoUnopenedMultiway_DifferentiatesActionUtilitiesAndDiagnostics()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var fold = evaluator.Evaluate(CreateCoUnopenedMultiwayContext(ActionType.Fold));
        var call = evaluator.Evaluate(CreateCoUnopenedMultiwayContext(ActionType.Call));
        var raise = evaluator.Evaluate(CreateCoUnopenedMultiwayContext(ActionType.Raise, new ChipAmount(250)));

        Assert.NotNull(fold.Details);
        Assert.NotNull(call.Details);
        Assert.NotNull(raise.Details);

        Assert.Equal(0d, fold.Details!.HeroUtility);
        Assert.NotEqual(call.Details!.HeroUtility, raise.Details!.HeroUtility);
        Assert.Equal(0d, call.Details.FoldProbability);
        Assert.Equal(1d, call.Details.ContinueProbability);
        Assert.True(raise.Details.FoldProbability > 0d);
        Assert.True(raise.Details.ContinueProbability < 1d);
        Assert.NotNull(raise.Details.ImmediateWinComponent);
        Assert.NotNull(raise.Details.ContinueComponent);
        Assert.NotNull(raise.Details.ContinueBranchUtility);
        Assert.Contains("Action=Raise", raise.Details.DisplaySummary);
    }

    [Fact]
    public void Evaluate_MultiwayContext_FallsBackToHeuristic()
    {
        var evaluator = new EquityBasedPreflopLeafEvaluator(new TableDrivenOpponentRangeProvider(), new HeuristicPreflopLeafEvaluator(), samplesPerMatchup: 120);
        var context = CreateThreeWayContext("v2/VS_OPEN/BTN/eff=100");

        var result = evaluator.Evaluate(context);

        Assert.Contains("equity evaluator fallback", result.Reason);
        Assert.Contains("heuristic preflop", result.Reason);
        Assert.NotNull(result.Details);
        Assert.True(result.Details!.UsedFallbackEvaluator);
        Assert.Equal("HeuristicFallback", result.Details.EvaluatorType);
        Assert.Equal("AKo", result.Details.HeroHand);
        Assert.NotNull(result.Details.FallbackReason);
        Assert.Contains("unsupported root evaluator mode", result.Details.FallbackReason!);
    }

    [Fact]
    public void TableDrivenOpponentRangeProvider_CachesBuiltRanges_ByContext()
    {
        var provider = new TableDrivenOpponentRangeProvider();
        var request = new OpponentRangeRequest(
            Position.BTN,
            Position.BB,
            PreflopNodeFamily.FacingRaise,
            RaiseDepth: 1,
            IsHeadsUp: true,
            SolverKey: "v2/VS_OPEN/BTN/eff=100");

        var first = provider.TryGetRange(request, out var rangeA, out _);
        var second = provider.TryGetRange(request, out var rangeB, out _);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, provider.RangeBuildCount);
        Assert.Equal(1, provider.CachedRangeCount);
        Assert.Same(rangeA.WeightedCombos, rangeB.WeightedCombos);
    }

    [Fact]
    public void DeterministicPreflopEquity_CachesCanonicalMatchups()
    {
        var hero = HoleCards.Parse("AsKh");
        var villain = HoleCards.Parse("QdJd");
        var samples = 96;

        var before = DeterministicPreflopEquity.MatchupComputationCount;
        var heroVsVillain = DeterministicPreflopEquity.CalculateHeadsUpEquity(hero, villain, samples);
        var villainVsHero = DeterministicPreflopEquity.CalculateHeadsUpEquity(villain, hero, samples);
        var after = DeterministicPreflopEquity.MatchupComputationCount;

        Assert.Equal(before + 1, after);
        Assert.InRange(Math.Abs((heroVsVillain + villainVsHero) - 1d), 0d, 0.0000001d);
    }

    private static PreflopLeafEvaluationContext CreateHeadsUpContext(HoleCards heroCards, HoleCards villainCards, ActionType rootAction, string solverKey, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var villainId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var config = new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(villainId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var root = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(200),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 1,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(villainId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [villainId] = villainCards
            });

        return new PreflopLeafEvaluationContext(
            root,
            root,
            heroId,
            Position.BTN,
            heroCards,
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(250) : ChipAmount.Zero),
            solverKey);
    }

    private static PreflopLeafEvaluationContext CreateLimpOptionBbContext(HoleCards heroCards, HoleCards villainCards, ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var villainId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var config = new GameConfig(2, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(villainId, 0, Position.SB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var root = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(200),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(villainId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(heroId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(villainId, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards,
                [villainId] = villainCards
            });

        return new PreflopLeafEvaluationContext(
            root,
            root,
            heroId,
            Position.BB,
            heroCards,
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/LIMP_OPTION/BB/eff=118.5/jam=18");
    }

    private static PreflopLeafEvaluationContext CreateThreeWayContext(string solverKey = "v2/VS_OPEN/BTN/eff=100", HoleCards? heroCards = null, ActionType rootAction = ActionType.Raise)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var v1 = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var v2 = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var config = new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(v1, 1, Position.SB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(v2, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(300),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 1,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(v1, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(v2, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = heroCards ?? HoleCards.Parse("AsKh"),
                [v1] = HoleCards.Parse("QdJd"),
                [v2] = HoleCards.Parse("9c9d")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.BTN,
            heroCards ?? HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? new ChipAmount(250) : ChipAmount.Zero),
            solverKey);
    }



    private static PreflopLeafEvaluationContext CreateBtnFacingLimpMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var limperId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var sbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var bbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var config = new GameConfig(4, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(limperId, 0, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(100), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: heroId,
            pot: new ChipAmount(350),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(limperId, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [limperId] = HoleCards.Parse("QdJd"),
                [sbId] = HoleCards.Parse("9c9d"),
                [bbId] = HoleCards.Parse("8c7c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.BTN,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/LIMP/BTN/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateCoFacingLimpMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var limperId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var btnId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var sbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var bbId = new PlayerId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        var config = new GameConfig(5, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(limperId, 0, Position.HJ, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(btnId, 2, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 3, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 4, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: heroId,
            pot: new ChipAmount(250),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100)),
                new SolverActionEntry(limperId, ActionType.Call, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [limperId] = HoleCards.Parse("QdJd"),
                [btnId] = HoleCards.Parse("9h8h"),
                [sbId] = HoleCards.Parse("7c7d"),
                [bbId] = HoleCards.Parse("8c6c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.CO,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/LIMP/CO/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateHjUnopenedMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var coId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var btnId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var sbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var bbId = new PlayerId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        var config = new GameConfig(5, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.HJ, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(coId, 1, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(btnId, 2, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 3, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 4, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 2,
            actingPlayerId: heroId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [coId] = HoleCards.Parse("QdJd"),
                [btnId] = HoleCards.Parse("9h8h"),
                [sbId] = HoleCards.Parse("7c7d"),
                [bbId] = HoleCards.Parse("8c6c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.HJ,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(250) : ChipAmount.Zero),
            "v2/UNOPENED/HJ/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateCoUnopenedMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var btnId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var sbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var bbId = new PlayerId(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var config = new GameConfig(4, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(heroId, 0, Position.CO, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(btnId, 1, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(sbId, 2, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 3, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 1,
            actingPlayerId: heroId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(sbId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [btnId] = HoleCards.Parse("QdJd"),
                [sbId] = HoleCards.Parse("7c7d"),
                [bbId] = HoleCards.Parse("8c6c")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.CO,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(250) : ChipAmount.Zero),
            "v2/UNOPENED/CO/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateSbUnopenedMultiwayContext(ActionType rootAction, ChipAmount? raiseAmount = null)
    {
        var heroId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var btnId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var bbId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var config = new GameConfig(3, new ChipAmount(50), new ChipAmount(100), ChipAmount.Zero, new ChipAmount(10000));
        var players = new[]
        {
            new SolverPlayerState(btnId, 0, Position.BTN, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false),
            new SolverPlayerState(heroId, 1, Position.SB, new ChipAmount(9950), new ChipAmount(50), new ChipAmount(50), false, false),
            new SolverPlayerState(bbId, 2, Position.BB, new ChipAmount(9900), new ChipAmount(100), new ChipAmount(100), false, false)
        };

        var state = new SolverHandState(
            config,
            Street.Preflop,
            buttonSeatIndex: 0,
            actingPlayerId: heroId,
            pot: new ChipAmount(150),
            currentBetSize: new ChipAmount(100),
            lastRaiseSize: new ChipAmount(100),
            raisesThisStreet: 0,
            players,
            actionHistory: new[]
            {
                new SolverActionEntry(heroId, ActionType.PostSmallBlind, new ChipAmount(50)),
                new SolverActionEntry(bbId, ActionType.PostBigBlind, new ChipAmount(100))
            },
            boardCards: Array.Empty<Card>(),
            deadCards: Array.Empty<Card>(),
            privateCardsByPlayer: new Dictionary<PlayerId, HoleCards>
            {
                [heroId] = HoleCards.Parse("AsKh"),
                [btnId] = HoleCards.Parse("QdJd"),
                [bbId] = HoleCards.Parse("9c9d")
            });

        return new PreflopLeafEvaluationContext(
            state,
            state,
            heroId,
            Position.SB,
            HoleCards.Parse("AsKh"),
            100,
            new LegalAction(rootAction, rootAction == ActionType.Raise ? raiseAmount ?? new ChipAmount(550) : ChipAmount.Zero),
            "v2/UNOPENED_SB/SB/eff=100");
    }

    private static PreflopLeafEvaluationContext CreateThreeWayContextWithLeafActiveOpponents(string solverKey, int leafActiveOpponents)
    {
        var baseline = CreateThreeWayContext(solverKey, HoleCards.Parse("AsKh"));
        var heroId = baseline.HeroPlayerId;

        var updatedPlayers = baseline.LeafState.Players
            .Select(player => player.PlayerId == heroId
                ? player
                : player with { IsFolded = leafActiveOpponents == 0 })
            .ToArray();

        var leaf = baseline.LeafState with { Players = updatedPlayers };
        return baseline with { LeafState = leaf };
    }

    private sealed class StaticOpponentRangeProvider : IOpponentRangeProvider
    {
        private readonly WeightedHoleCards[] _combos;

        public StaticOpponentRangeProvider(params WeightedHoleCards[] combos)
        {
            _combos = combos;
        }

        public bool TryGetRange(OpponentRangeRequest request, out OpponentWeightedRange range, out string reason)
        {
            range = new OpponentWeightedRange(_combos, "static-test");
            reason = "static-test";
            return true;
        }
    }
}
