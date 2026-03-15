using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using System.Collections.Concurrent;
using System.Threading;

namespace PokerAnalyzer.Application.PreflopSolver;

public interface IOpponentRangeProvider
{
    bool TryGetRange(OpponentRangeRequest request, out OpponentWeightedRange range, out string reason);
}

public sealed record OpponentRangeRequest(
    Position HeroPosition,
    Position? VillainPosition,
    PreflopNodeFamily NodeFamily,
    int RaiseDepth,
    bool IsHeadsUp,
    string? SolverKey,
    double? PercentileOverride = null);

public sealed record OpponentWeightedRange(
    IReadOnlyList<WeightedHoleCards> WeightedCombos,
    string Description);

public readonly record struct WeightedHoleCards(HoleCards Cards, double Weight);

public enum PreflopNodeFamily : byte
{
    Unknown = 0,
    Unopened = 1,
    FacingLimp = 2,
    FacingRaise = 3,
    Facing3Bet = 4,
    Facing4Bet = 5,
    Squeeze = 6
}

public sealed class EquityBasedPreflopLeafEvaluator : IPreflopLeafEvaluator
{
    private readonly IOpponentRangeProvider _rangeProvider;
    private readonly IPreflopLeafEvaluator _fallbackEvaluator;
    private readonly int _samplesPerMatchup;
    private readonly IPreflopPopulationProfileProvider _populationProfileProvider;


    private enum RootEvaluatorMode
    {
        Unsupported = 0,
        TrueHeadsUp = 1,
        AbstractedHeadsUp = 2
    }

    public EquityBasedPreflopLeafEvaluator(
        IOpponentRangeProvider rangeProvider,
        IPreflopLeafEvaluator fallbackEvaluator,
        int samplesPerMatchup = 160,
        IPreflopPopulationProfileProvider? populationProfileProvider = null)
    {
        _rangeProvider = rangeProvider ?? throw new ArgumentNullException(nameof(rangeProvider));
        _fallbackEvaluator = fallbackEvaluator ?? throw new ArgumentNullException(nameof(fallbackEvaluator));
        _samplesPerMatchup = Math.Max(32, samplesPerMatchup);
        _populationProfileProvider = populationProfileProvider ?? new NamedPreflopPopulationProfileProvider(PreflopPopulationProfiles.GtoLikeName);
    }

    public PreflopLeafEvaluation Evaluate(PreflopLeafEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rootActiveOpponentCount = context.RootState.Players.Count(p => p.PlayerId != context.HeroPlayerId && p.IsActive);
        var leafActiveOpponentCount = context.LeafState.Players.Count(p => p.PlayerId != context.HeroPlayerId && p.IsActive);
        var nodeFamily = PreflopNodeFamilyClassifier.Classify(context);
        var rootEvaluatorMode = DetermineRootEvaluatorMode(context, nodeFamily, rootActiveOpponentCount);

        if (context.RootAction.ActionType == ActionType.Fold)
            return EvaluateFold(context, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount);

        if ((rootEvaluatorMode == RootEvaluatorMode.AbstractedHeadsUp || rootEvaluatorMode == RootEvaluatorMode.TrueHeadsUp)
            && TryEvaluateFacingRaiseActionAware(context, nodeFamily, rootActiveOpponentCount, leafActiveOpponentCount, out var facingRaiseEvaluation))
        {
            return facingRaiseEvaluation;
        }

        if (rootEvaluatorMode == RootEvaluatorMode.AbstractedHeadsUp
            && (TryEvaluateBtnUnopenedActionAware(context, nodeFamily, rootActiveOpponentCount, leafActiveOpponentCount, out var abstracted)
                || TryEvaluateUnopenedActionAware(context, nodeFamily, rootActiveOpponentCount, leafActiveOpponentCount, out abstracted)
                || TryEvaluateFacingLimpActionAware(context, nodeFamily, rootActiveOpponentCount, leafActiveOpponentCount, out abstracted)))
        {
            return abstracted;
        }

        if (rootEvaluatorMode == RootEvaluatorMode.TrueHeadsUp)
        {
            var villain = context.RootState.Players.FirstOrDefault(p => p.PlayerId != context.HeroPlayerId && p.IsActive);
            if (villain is not null)
                return EvaluateHeadsUp(context, nodeFamily, villain, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount);

            return Fallback(context, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount, "expected heads-up root but no active opponent found");
        }

        return Fallback(context, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount, $"unsupported root evaluator mode for family={nodeFamily}, rootActiveOpponents={rootActiveOpponentCount}");
    }

    private bool TryEvaluateFacingRaiseActionAware(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, int rootActiveOpponentCount, int leafActiveOpponentCount, out PreflopLeafEvaluation evaluation)
    {
        evaluation = default!;
        if (nodeFamily != PreflopNodeFamily.FacingRaise)
            return false;

        var rootOpponents = context.RootState.Players.Where(p => p.PlayerId != context.HeroPlayerId && p.IsActive).ToArray();
        if (rootOpponents.Length == 0)
            return false;

        var opener = ResolveFacingRaiseOpener(context);
        if (opener is null)
            return false;

        var facingContext = BuildFacingRaiseContext(context, opener.Position, rootOpponents.Length);
        var activeProfile = _populationProfileProvider.ActiveProfile;
        var rangeContributors = new List<(OpponentWeightedRange Range, double Weight, string Label, string Reason)>();

        foreach (var opponent in rootOpponents)
        {
            var percentileOverride = GetFacingRaisePercentileOverride(opponent.Position, opener.Position, facingContext, activeProfile);
            var request = new OpponentRangeRequest(context.HeroPosition, opponent.Position, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey, percentileOverride);
            if (!_rangeProvider.TryGetRange(request, out var opponentRange, out var opponentReason))
                return false;

            var weight = GetFacingRaiseContributorWeight(opponent.Position, opener.Position, facingContext);
            rangeContributors.Add((opponentRange, weight, opponent.Position.ToString(), opponentReason));
        }

        var combined = BlendRanges(rangeContributors.Select(x => (x.Range, x.Weight)).ToArray());
        var detail = $"Facing-raise generalized abstraction ({_populationProfileProvider.ActiveProfileName}): hero={facingContext.HeroPosition}, opener={facingContext.OpenerPosition}, behind={facingContext.PlayersLeftBehindHero}, hu={facingContext.IsHeadsUpVsOpener}, ip={facingContext.IsInPositionVsOpener}, class={facingContext.StructuralClass}; contributors={string.Join(", ", rangeContributors.Select(x => $"{x.Label}w={x.Weight:0.00}"))}; {string.Join("; ", rangeContributors.Select(x => $"{x.Label}={x.Reason}"))}";

        var baseEval = EvaluateAgainstRange(
            context,
            nodeFamily,
            villainPosition: null,
            combined,
            detail,
            rootEvaluatorMode: rootActiveOpponentCount == 1 ? RootEvaluatorMode.TrueHeadsUp : RootEvaluatorMode.AbstractedHeadsUp,
            rootActiveOpponentCount: rootActiveOpponentCount,
            leafActiveOpponentCount: leafActiveOpponentCount,
            evaluatorType: "GeneralizedFacingRaise",
            abstractionSource: "GeneralizedFacingRaise",
            abstractedOpponentCount: 1,
            syntheticDefenderLabel: "SyntheticRaiseDefender",
            foldProbability: null,
            continueProbability: null,
            summaryPrefix: "Level-2 generalized facing-raise abstraction");

        if (!baseEval.UtilityByPlayer.TryGetValue(context.HeroPlayerId, out var continueBranchUtility))
            continueBranchUtility = 0d;

        var handClass = baseEval.Details?.HandClass ?? ClassifyHand(context.HeroCards);

        var bigBlind = Math.Max(1d, context.RootState.Config.BigBlind.Value);
        var potBb = context.RootState.Pot.Value / bigBlind;
        var actionType = context.RootAction.ActionType;
        var actionSizeBb = (context.RootAction.Amount?.Value ?? 0L) / bigBlind;
        var jamSizeBb = (context.RootState.Players.First(p => p.PlayerId == context.HeroPlayerId).CurrentStreetContribution.Value + context.RootState.Players.First(p => p.PlayerId == context.HeroPlayerId).Stack.Value) / bigBlind;
        var isJamAction = actionType is ActionType.AllIn || (actionType == ActionType.Raise && actionSizeBb >= jamSizeBb - 0.01d);

        var allFold = 1d;
        foreach (var opponent in rootOpponents)
        {
            var foldProbability = GetFacingRaiseFoldProbability(opponent.Position, opener.Position, facingContext, actionType, actionSizeBb, isJamAction, activeProfile);
            allFold *= foldProbability;
        }

        allFold = Math.Clamp(allFold, 0d, 1d);
        var continueProbability = Math.Clamp(1d - allFold, 0d, 1d);

        var immediateComponent = 0d;
        var continueComponent = 0d;
        var heroUtility = continueBranchUtility;

        if (actionType == ActionType.Call)
        {
            allFold = 0d;
            continueProbability = 1d;
            var squeezeRiskPenalty = GetFacingRaiseSqueezeRiskPenalty(facingContext);
            var positionalPenalty = facingContext.IsInPositionVsOpener ? 0d : 0.01d;
            heroUtility = continueBranchUtility - squeezeRiskPenalty - positionalPenalty;
            continueComponent = heroUtility;
        }
        else if (actionType == ActionType.Raise || actionType == ActionType.AllIn)
        {
            immediateComponent = allFold * potBb;
            continueComponent = continueProbability * continueBranchUtility;
            var riskPenalty = continueProbability * activeProfile.RaiseRiskPenaltyFactor * Math.Max(0d, actionSizeBb - 2d);
            var leveragePenalty = isJamAction ? 0.12d + (0.02d * facingContext.PlayersLeftBehindHero) : 0.05d + (0.01d * facingContext.PlayersLeftBehindHero);
            var realizationPenalty = continueProbability * GetFacingRaiseRealizationPenalty(handClass, facingContext, activeProfile);
            heroUtility = immediateComponent + continueComponent - riskPenalty - leveragePenalty - realizationPenalty;
        }

        var utility = baseEval.UtilityByPlayer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        utility[context.HeroPlayerId] = heroUtility;
        var actionLabel = isJamAction ? "Jam" : actionType.ToString();

        evaluation = baseEval with
        {
            UtilityByPlayer = utility,
            Reason = $"{baseEval.Reason}, actionAware={actionLabel}, utility={heroUtility:0.000}, class={facingContext.StructuralClass}",
            Details = baseEval.Details! with
            {
                HeroUtility = heroUtility,
                RootActionType = actionLabel,
                FoldProbability = allFold,
                ContinueProbability = continueProbability,
                ImmediateWinComponent = immediateComponent,
                ContinueComponent = continueComponent,
                ContinueBranchUtility = continueBranchUtility,
                DisplaySummary = $"{baseEval.Details!.DisplaySummary} Action={actionLabel}, EV={heroUtility:0.000}, fold={allFold:0.000}, continue={continueProbability:0.000}, class={facingContext.StructuralClass}, profile={_populationProfileProvider.ActiveProfileName}.",
                ActivePopulationProfile = _populationProfileProvider.ActiveProfileName,
                VillainPosition = opener.Position.ToString(),
                RationaleSummary = $"Facing-raise generalized evaluator ({facingContext.StructuralClass}) models hero={facingContext.HeroPosition}, opener={facingContext.OpenerPosition}, IP={facingContext.IsInPositionVsOpener}, behind={facingContext.PlayersLeftBehindHero}, eff={facingContext.EffectiveStackBb:0.##}bb under {_populationProfileProvider.ActiveProfileName}."
            }
        };

        return true;
    }

    private bool TryEvaluateFacingLimpActionAware(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, int rootActiveOpponentCount, int leafActiveOpponentCount, out PreflopLeafEvaluation evaluation)
    {
        evaluation = default!;
        if (nodeFamily != PreflopNodeFamily.FacingLimp)
            return false;

        var rootOpponents = context.RootState.Players.Where(p => p.PlayerId != context.HeroPlayerId && p.IsActive).ToArray();
        var limpers = rootOpponents.Where(IsLimper).ToArray();
        if (limpers.Length == 0)
            return false;

        var activeProfile = _populationProfileProvider.ActiveProfile;
        var playersLeftBehind = CountPlayersLeftBehind(context.RootState.Players, context.HeroPlayerId);
        var rangeContributors = new List<(OpponentWeightedRange Range, double Weight, string Label, string Reason)>();

        foreach (var opponent in rootOpponents)
        {
            var percentileOverride = GetFacingLimpPercentileOverride(opponent.Position, activeProfile);
            var request = new OpponentRangeRequest(context.HeroPosition, opponent.Position, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey, percentileOverride);
            if (!_rangeProvider.TryGetRange(request, out var opponentRange, out var opponentReason))
                return false;

            var weight = GetFacingLimpContributorWeight(opponent.Position, IsLimper(opponent), playersLeftBehind);
            rangeContributors.Add((opponentRange, weight, opponent.Position.ToString(), opponentReason));
        }

        var combined = BlendRanges(rangeContributors.Select(x => (x.Range, x.Weight)).ToArray());
        var detail = $"Facing-limp synthetic field ({_populationProfileProvider.ActiveProfileName}): hero={context.HeroPosition}, limpers={limpers.Length}, behind={playersLeftBehind}, contributors={string.Join(", ", rangeContributors.Select(x => $"{x.Label}w={x.Weight:0.00}"))}; {string.Join("; ", rangeContributors.Select(x => $"{x.Label}={x.Reason}"))}";
        var baseEval = EvaluateAgainstRange(
            context,
            nodeFamily,
            villainPosition: null,
            combined,
            detail,
            rootEvaluatorMode: RootEvaluatorMode.AbstractedHeadsUp,
            rootActiveOpponentCount: rootActiveOpponentCount,
            leafActiveOpponentCount: leafActiveOpponentCount,
            evaluatorType: "AbstractedHeadsUp",
            abstractionSource: "SyntheticFieldFacingLimp",
            abstractedOpponentCount: 1,
            syntheticDefenderLabel: "SyntheticLimpFieldDefender",
            foldProbability: null,
            continueProbability: null,
            summaryPrefix: "Level-2 facing-limp abstraction");

        if (!baseEval.UtilityByPlayer.TryGetValue(context.HeroPlayerId, out var continueBranchUtility))
            continueBranchUtility = 0d;

        var bigBlind = Math.Max(1d, context.RootState.Config.BigBlind.Value);
        var potBb = context.RootState.Pot.Value / bigBlind;
        var actionType = context.RootAction.ActionType;
        var actionSizeBb = (context.RootAction.Amount?.Value ?? 0L) / bigBlind;
        var sizeDelta = Math.Max(0d, actionSizeBb - 5.5d);

        var allFold = 1d;
        foreach (var opponent in rootOpponents)
        {
            var foldProbability = GetFacingLimpFoldProbability(opponent.Position, IsLimper(opponent), sizeDelta, activeProfile, playersLeftBehind);
            allFold *= foldProbability;
        }

        allFold = Math.Clamp(allFold, 0d, 1d);
        var continueProbability = Math.Clamp(1d - allFold, 0d, 1d);

        var immediateComponent = 0d;
        var continueComponent = 0d;
        var heroUtility = continueBranchUtility;
        if (actionType == ActionType.Raise)
        {
            immediateComponent = allFold * potBb;
            continueComponent = continueProbability * continueBranchUtility;
            var riskPenalty = continueProbability * activeProfile.RaiseRiskPenaltyFactor * Math.Max(0d, actionSizeBb - 1d);
            var crowdingPenalty = 0.02d * rootOpponents.Length;
            heroUtility = immediateComponent + continueComponent - riskPenalty - crowdingPenalty;
        }
        else if (actionType is ActionType.Call or ActionType.Check)
        {
            allFold = 0d;
            continueProbability = 1d;
            var overlimpPenalty = 0.03d + (0.01d * rootOpponents.Length);
            heroUtility = continueBranchUtility - overlimpPenalty;
            continueComponent = heroUtility;
        }

        var utility = baseEval.UtilityByPlayer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        utility[context.HeroPlayerId] = heroUtility;
        var actionLabel = actionType.ToString();

        evaluation = baseEval with
        {
            UtilityByPlayer = utility,
            Reason = $"{baseEval.Reason}, actionAware={actionLabel}, utility={heroUtility:0.000}",
            Details = baseEval.Details! with
            {
                HeroUtility = heroUtility,
                RootActionType = actionLabel,
                FoldProbability = allFold,
                ContinueProbability = continueProbability,
                ImmediateWinComponent = immediateComponent,
                ContinueComponent = continueComponent,
                ContinueBranchUtility = continueBranchUtility,
                DisplaySummary = $"{baseEval.Details!.DisplaySummary} Action={actionLabel}, EV={heroUtility:0.000}, fold={allFold:0.000}, continue={continueProbability:0.000}, profile={_populationProfileProvider.ActiveProfileName}.",
                ActivePopulationProfile = _populationProfileProvider.ActiveProfileName,
                RationaleSummary = $"Facing-limp {context.HeroPosition} {actionLabel} uses a synthetic continuing field with size-sensitive fold/continue decomposition under {_populationProfileProvider.ActiveProfileName}."
            }
        };

        return true;
    }

    private bool TryEvaluateBtnUnopenedActionAware(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, int rootActiveOpponentCount, int leafActiveOpponentCount, out PreflopLeafEvaluation evaluation)
    {
        evaluation = default!;
        if (context.HeroPosition != Position.BTN || nodeFamily != PreflopNodeFamily.Unopened)
            return false;

        var rootOpponents = context.RootState.Players.Where(p => p.PlayerId != context.HeroPlayerId && p.IsActive).ToArray();
        var sb = rootOpponents.FirstOrDefault(p => p.Position == Position.SB);
        var bb = rootOpponents.FirstOrDefault(p => p.Position == Position.BB);
        if (sb is null || bb is null)
            return false;

        var activeProfile = _populationProfileProvider.ActiveProfile;
        var sbContinue = activeProfile.SbContinueUnopenedVsBtn;
        var bbContinue = activeProfile.BbContinueUnopenedVsBtn;
        var allFold = Math.Clamp((1d - sbContinue) * (1d - bbContinue), 0d, 1d);
        var sbOnly = sbContinue * (1d - bbContinue);
        var bbOnly = (1d - sbContinue) * bbContinue;
        var bothContinue = sbContinue * bbContinue;
        var continueProbability = Math.Clamp(1d - allFold, 0d, 1d);

        var sbWeight = continueProbability > 0d ? (sbOnly + 0.5d * bothContinue) / continueProbability : 0.5d;
        var bbWeight = continueProbability > 0d ? (bbOnly + 0.5d * bothContinue) / continueProbability : 0.5d;

        var sbReq = new OpponentRangeRequest(context.HeroPosition, Position.SB, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey, activeProfile.SbContinueRangePercentileUnopenedVsBtn);
        var bbReq = new OpponentRangeRequest(context.HeroPosition, Position.BB, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey, activeProfile.BbContinueRangePercentileUnopenedVsBtn);

        if (!_rangeProvider.TryGetRange(sbReq, out var sbRange, out var sbReason))
            return false;
        if (!_rangeProvider.TryGetRange(bbReq, out var bbRange, out var bbReason))
            return false;

        var combined = BlendRanges(sbRange, sbWeight, bbRange, bbWeight);
        var detail = $"BTN unopened weighted-blind abstraction ({_populationProfileProvider.ActiveProfileName}): P(all fold)={allFold:0.000}, P(continue)={continueProbability:0.000}, SBw={sbWeight:0.000}, BBw={bbWeight:0.000}, sbPct={activeProfile.SbContinueRangePercentileUnopenedVsBtn:0.00}, bbPct={activeProfile.BbContinueRangePercentileUnopenedVsBtn:0.00}; sb={sbReason}; bb={bbReason}";
        var baseEval = EvaluateAgainstRange(
            context,
            nodeFamily,
            villainPosition: null,
            combined,
            detail,
            rootEvaluatorMode: RootEvaluatorMode.AbstractedHeadsUp,
            rootActiveOpponentCount: rootActiveOpponentCount,
            leafActiveOpponentCount: leafActiveOpponentCount,
            evaluatorType: "AbstractedHeadsUp",
            abstractionSource: "WeightedBlindsBTNUnopened",
            abstractedOpponentCount: 1,
            syntheticDefenderLabel: "SyntheticBlindDefender",
            foldProbability: allFold,
            continueProbability: continueProbability,
            summaryPrefix: "Level-2 BTN unopened abstraction");

        if (!baseEval.UtilityByPlayer.TryGetValue(context.HeroPlayerId, out var continueBranchUtility))
            continueBranchUtility = 0d;

        var handClass = baseEval.Details?.HandClass ?? ClassifyHand(context.HeroCards);
        var realizationPenalty = GetBtnUnopenedRealizationPenalty(handClass, activeProfile);
        var adjustedContinueBranchUtility = continueBranchUtility - realizationPenalty;

        var immediateWinBb = 1.5d;
        var openSizeBb = 2.5d;
        var immediateComponent = 0d;
        var continueComponent = 0d;
        var heroUtility = continueBranchUtility;
        var actionType = context.RootAction.ActionType;

        if (actionType == ActionType.Raise)
        {
            immediateComponent = allFold * immediateWinBb;
            continueComponent = continueProbability * adjustedContinueBranchUtility;
            var riskPenalty = continueProbability * activeProfile.RaiseRiskPenaltyFactor * openSizeBb;
            heroUtility = immediateComponent + continueComponent - riskPenalty;
        }
        else if (actionType is ActionType.Call or ActionType.Check)
        {
            var limpPenalty = 0.04d;
            heroUtility = adjustedContinueBranchUtility - limpPenalty;
            continueComponent = heroUtility;
            immediateComponent = 0d;
        }

        var utility = baseEval.UtilityByPlayer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        utility[context.HeroPlayerId] = heroUtility;

        var actionLabel = actionType.ToString();
        var summary = actionType == ActionType.Raise
            ? $"{baseEval.Details!.DisplaySummary} Action={actionLabel}, EV={heroUtility:0.000} = fold({allFold:0.000})*pot({immediateWinBb:0.00}) + continue({continueProbability:0.000})*Vcont({adjustedContinueBranchUtility:0.000}), profile={_populationProfileProvider.ActiveProfileName}."
            : $"{baseEval.Details!.DisplaySummary} Action={actionLabel}, utility {heroUtility:0.000} from limp/check continuation approximation.";

        evaluation = baseEval with
        {
            UtilityByPlayer = utility,
            Reason = $"{baseEval.Reason}, actionAware={actionLabel}, utility={heroUtility:0.000}",
            Details = baseEval.Details! with
            {
                HeroUtility = heroUtility,
                RootActionType = actionLabel,
                ImmediateWinComponent = immediateComponent,
                ContinueComponent = continueComponent,
                ContinueBranchUtility = adjustedContinueBranchUtility,
                DisplaySummary = summary,
                ActivePopulationProfile = _populationProfileProvider.ActiveProfileName,
                RationaleSummary = $"Unopened BTN {actionLabel} uses action-sensitive utility with {_populationProfileProvider.ActiveProfileName} population assumptions: fold-equity immediate component + continuation branch utility."
            }
        };

        return true;
    }

    private bool TryEvaluateUnopenedActionAware(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, int rootActiveOpponentCount, int leafActiveOpponentCount, out PreflopLeafEvaluation evaluation)
    {
        evaluation = default!;
        if (context.HeroPosition == Position.BTN || nodeFamily != PreflopNodeFamily.Unopened)
            return false;

        var rootOpponents = context.RootState.Players.Where(p => p.PlayerId != context.HeroPlayerId && p.IsActive).ToArray();
        if (!rootOpponents.Any(p => p.Position == Position.BB))
            return false;

        var activeProfile = _populationProfileProvider.ActiveProfile;
        var playersLeftBehind = CountPlayersLeftBehind(context.RootState.Players, context.HeroPlayerId);
        var rangeContributors = new List<(OpponentWeightedRange Range, double Weight, string Label, string Reason, double? PercentileOverride)>();

        foreach (var opponent in rootOpponents)
        {
            var percentileOverride = GetUnopenedPercentileOverride(context.HeroPosition, opponent.Position, activeProfile);
            var request = new OpponentRangeRequest(context.HeroPosition, opponent.Position, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey, percentileOverride);
            if (!_rangeProvider.TryGetRange(request, out var opponentRange, out var opponentReason))
                return false;

            var weight = GetUnopenedContributorWeight(context.HeroPosition, opponent.Position, playersLeftBehind);
            rangeContributors.Add((opponentRange, weight, opponent.Position.ToString(), opponentReason, percentileOverride));
        }

        var combined = BlendRanges(rangeContributors.Select(x => (x.Range, x.Weight)).ToArray());
        var detail = $"Unopened synthetic field ({_populationProfileProvider.ActiveProfileName}): hero={context.HeroPosition}, behind={playersLeftBehind}, contributors={string.Join(", ", rangeContributors.Select(x => $"{x.Label}w={x.Weight:0.00}"))}; {string.Join("; ", rangeContributors.Select(x => $"{x.Label}pct={(x.PercentileOverride?.ToString("0.00") ?? "table")}, {x.Reason}"))}";
        var abstractionSource = context.HeroPosition == Position.SB ? "SyntheticFieldSbUnopened" : "SyntheticFieldUnopened";
        var syntheticDefenderLabel = context.HeroPosition == Position.SB ? "SyntheticSbUnopenedDefender" : "SyntheticUnopenedFieldDefender";
        var summaryPrefix = context.HeroPosition == Position.SB ? "Level-2 SB unopened abstraction" : "Level-2 unopened abstraction";
        var baseEval = EvaluateAgainstRange(
            context,
            nodeFamily,
            villainPosition: null,
            combined,
            detail,
            rootEvaluatorMode: RootEvaluatorMode.AbstractedHeadsUp,
            rootActiveOpponentCount: rootActiveOpponentCount,
            leafActiveOpponentCount: leafActiveOpponentCount,
            evaluatorType: "AbstractedHeadsUp",
            abstractionSource: abstractionSource,
            abstractedOpponentCount: 1,
            syntheticDefenderLabel: syntheticDefenderLabel,
            foldProbability: null,
            continueProbability: null,
            summaryPrefix: summaryPrefix);

        if (!baseEval.UtilityByPlayer.TryGetValue(context.HeroPlayerId, out var continueBranchUtility))
            continueBranchUtility = 0d;

        var bigBlind = Math.Max(1d, context.RootState.Config.BigBlind.Value);
        var potBb = context.RootState.Pot.Value / bigBlind;
        var actionType = context.RootAction.ActionType;
        var actionSizeBb = actionType == ActionType.Raise ? context.RootAction.Amount?.Value??0.0 / bigBlind : 1d;
        var sizeDelta = Math.Max(0d, actionSizeBb - 5.5d);

        var allFold = 1d;
        foreach (var opponent in rootOpponents)
        {
            var foldProbability = GetUnopenedFoldProbability(context.HeroPosition, opponent.Position, sizeDelta, activeProfile, playersLeftBehind);
            allFold *= foldProbability;
        }

        allFold = Math.Clamp(allFold, 0d, 1d);
        var continueProbability = Math.Clamp(1d - allFold, 0d, 1d);

        var immediateComponent = 0d;
        var continueComponent = 0d;
        var heroUtility = continueBranchUtility;
        if (actionType == ActionType.Raise)
        {
            immediateComponent = allFold * potBb;
            continueComponent = continueProbability * continueBranchUtility;
            var riskPenalty = continueProbability * activeProfile.RaiseRiskPenaltyFactor * Math.Max(0d, actionSizeBb - 1d);
            var oopPenalty = GetUnopenedPositionalPenalty(context.HeroPosition, rootOpponents.Length, playersLeftBehind);
            heroUtility = immediateComponent + continueComponent - riskPenalty - oopPenalty;
        }
        else if (actionType is ActionType.Call or ActionType.Check)
        {
            allFold = 0d;
            continueProbability = 1d;
            var completePenalty = GetUnopenedPassivePenalty(context.HeroPosition, playersLeftBehind, rootOpponents.Length);
            heroUtility = continueBranchUtility - completePenalty;
            continueComponent = heroUtility;
        }

        var utility = baseEval.UtilityByPlayer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        utility[context.HeroPlayerId] = heroUtility;
        var actionLabel = actionType.ToString();

        evaluation = baseEval with
        {
            UtilityByPlayer = utility,
            Reason = $"{baseEval.Reason}, actionAware={actionLabel}, utility={heroUtility:0.000}",
            Details = baseEval.Details! with
            {
                HeroUtility = heroUtility,
                RootActionType = actionLabel,
                FoldProbability = allFold,
                ContinueProbability = continueProbability,
                ImmediateWinComponent = immediateComponent,
                ContinueComponent = continueComponent,
                ContinueBranchUtility = continueBranchUtility,
                DisplaySummary = $"{baseEval.Details!.DisplaySummary} Action={actionLabel}, EV={heroUtility:0.000}, fold={allFold:0.000}, continue={continueProbability:0.000}, profile={_populationProfileProvider.ActiveProfileName}.",
                ActivePopulationProfile = _populationProfileProvider.ActiveProfileName,
                RationaleSummary = $"Unopened {context.HeroPosition} {actionLabel} uses a synthetic defender field with action-sensitive fold/continue decomposition under {_populationProfileProvider.ActiveProfileName}."
            }
        };

        return true;
    }

    private PreflopLeafEvaluation EvaluateHeadsUp(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, SolverPlayerState villain, RootEvaluatorMode rootEvaluatorMode, int rootActiveOpponentCount, int leafActiveOpponentCount)
    {
        var request = new OpponentRangeRequest(
            context.HeroPosition,
            villain.Position,
            nodeFamily,
            context.RootState.RaisesThisStreet,
            IsHeadsUp: true,
            context.SolverKey,
            PercentileOverride: null);

        if (!_rangeProvider.TryGetRange(request, out var range, out var rangeReason))
            return Fallback(context, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount, $"range provider miss ({rangeReason})");

        return EvaluateAgainstRange(
            context,
            nodeFamily,
            villain.Position.ToString(),
            range,
            rangeReason,
            rootEvaluatorMode: rootEvaluatorMode,
            rootActiveOpponentCount: rootActiveOpponentCount,
            leafActiveOpponentCount: leafActiveOpponentCount,
            evaluatorType: "TrueHeadsUp",
            abstractionSource: null,
            abstractedOpponentCount: 1,
            syntheticDefenderLabel: villain.Position.ToString(),
            foldProbability: null,
            continueProbability: null,
            summaryPrefix: $"Level-2 equity leaf");
    }

    private PreflopLeafEvaluation EvaluateAgainstRange(
        PreflopLeafEvaluationContext context,
        PreflopNodeFamily nodeFamily,
        string? villainPosition,
        OpponentWeightedRange range,
        string rangeReason,
        RootEvaluatorMode rootEvaluatorMode,
        int rootActiveOpponentCount,
        int leafActiveOpponentCount,
        string evaluatorType,
        string? abstractionSource,
        int? abstractedOpponentCount,
        string? syntheticDefenderLabel,
        double? foldProbability,
        double? continueProbability,
        string summaryPrefix)
    {
        var filteredRange = range.WeightedCombos
            .Where(w => w.Weight > 0d && !SharesCard(w.Cards, context.HeroCards))
            .ToArray();

        if (filteredRange.Length == 0)
            return Fallback(context, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount, "range empty after blocker filtering");

        var weightedTotal = filteredRange.Sum(w => w.Weight);
        if (weightedTotal <= 0d)
            return Fallback(context, rootEvaluatorMode, rootActiveOpponentCount, leafActiveOpponentCount, "range has non-positive total weight");

        var heroEquity = 0d;
        foreach (var combo in filteredRange)
        {
            var matchupEquity = DeterministicPreflopEquity.CalculateHeadsUpEquity(context.HeroCards, combo.Cards, _samplesPerMatchup);
            heroEquity += (combo.Weight / weightedTotal) * matchupEquity;
        }

        var heroUtility = (heroEquity - 0.5d) * 2d;
        var continueBranchUtility = heroUtility;
        double? immediateComponent = null;
        double? continueComponent = null;
        var actionType = context.RootAction.ActionType;
        double? facingLimpFoldProbability = null;
        double? facingLimpContinueProbability = null;

        if (nodeFamily == PreflopNodeFamily.FacingLimp && string.Equals(villainPosition, nameof(Position.SB), StringComparison.OrdinalIgnoreCase))
        {
            var bigBlind = Math.Max(1d, context.RootState.Config.BigBlind.Value);
            var potBb = context.RootState.Pot.Value / bigBlind;
            var actionAmountBb = (context.RootAction.Amount?.Value ?? 0L) / bigBlind;

            if (actionType == ActionType.Raise)
            {
                // Facing limp options in blind-vs-blind spots need action-size-sensitive utility; otherwise
                // check and multiple raise sizes collapse to identical EV and regrets stay exactly zero.
                var raiseFoldProbability = Math.Clamp(0.12d + (0.045d * Math.Max(0d, actionAmountBb - 5.5d)), 0.08d, 0.42d);
                facingLimpFoldProbability = raiseFoldProbability;
                facingLimpContinueProbability = 1d - raiseFoldProbability;
                var riskBb = Math.Max(0d, actionAmountBb - 1d);
                var riskPenalty = _populationProfileProvider.ActiveProfile.RaiseRiskPenaltyFactor * riskBb;

                immediateComponent = raiseFoldProbability * potBb;
                continueComponent = (1d - raiseFoldProbability) * continueBranchUtility;
                heroUtility = immediateComponent.Value + continueComponent.Value - riskPenalty;
            }
            else if (actionType is ActionType.Check or ActionType.Call)
            {
                var passivePenalty = 0.03d;
                heroUtility = continueBranchUtility - passivePenalty;
                facingLimpFoldProbability = 0d;
                facingLimpContinueProbability = 1d;
                immediateComponent = 0d;
                continueComponent = heroUtility;
            }
        }
        var heroHand = ToHandLabel(context.HeroCards);
        var handClass = ClassifyHand(context.HeroCards);
        var percentile = Math.Clamp((heroEquity - 0.30d) / 0.40d, 0d, 1d);
        var blockerSummary = BuildBlockerSummary(context.HeroCards, villainPosition is null ? null : Enum.Parse<Position>(villainPosition));
        var utility = context.LeafState.Players.ToDictionary(player => player.PlayerId, _ => 0d);
        utility[context.HeroPlayerId] = heroUtility;

        var summary = $"{summaryPrefix}: {heroHand} vs {range.Description} ({villainPosition ?? syntheticDefenderLabel ?? "synthetic"}) -> equity {heroEquity:0.000}, utility {heroUtility:0.000}";
        var rationaleSummary = $"{heroHand} is classified as {handClass}. Strategy is primarily driven by equity {heroEquity:0.000} against the weighted {range.Description} range.";
        return new PreflopLeafEvaluation(
            utility,
            $"equity leaf evaluator: type={evaluatorType}, family={nodeFamily}, villainPos={villainPosition ?? "synthetic"}, range={range.Description}, filteredCombos={filteredRange.Length}, equity={heroEquity:0.000}, utility={heroUtility:0.000}, detail={rangeReason}",
            new PreflopLeafEvaluationDetails(
                HeroHand: heroHand,
                UsedEquityEvaluator: true,
                UsedFallbackEvaluator: false,
                EvaluatorType: evaluatorType,
                AbstractionSource: abstractionSource,
                ActualActiveOpponentCount: leafActiveOpponentCount,
                AbstractedOpponentCount: abstractedOpponentCount,
                SyntheticDefenderLabel: syntheticDefenderLabel,
                NodeFamily: nodeFamily.ToString(),
                HeroPosition: context.HeroPosition.ToString(),
                VillainPosition: villainPosition,
                IsHeadsUp: rootEvaluatorMode == RootEvaluatorMode.TrueHeadsUp || abstractedOpponentCount == 1,
                RangeDescription: range.Description,
                RangeDetail: rangeReason,
                FoldProbability: foldProbability ?? facingLimpFoldProbability,
                ContinueProbability: continueProbability ?? facingLimpContinueProbability,
                RootActionType: context.RootAction.ActionType.ToString(),
                ImmediateWinComponent: immediateComponent,
                ContinueComponent: continueComponent,
                ContinueBranchUtility: nodeFamily == PreflopNodeFamily.FacingLimp ? continueBranchUtility : null,
                FilteredCombos: filteredRange.Length,
                HeroEquity: heroEquity,
                HeroUtility: heroUtility,
                EquityVsRangePercentile: percentile,
                HandClass: handClass,
                BlockerSummary: blockerSummary,
                RationaleSummary: rationaleSummary,
                FallbackReason: null,
                DisplaySummary: summary,
                RootEvaluatorMode: rootEvaluatorMode.ToString(),
                RootActiveOpponentCount: rootActiveOpponentCount,
                LeafActiveOpponentCount: leafActiveOpponentCount,
                UsedDirectAbstractionShortcut: rootEvaluatorMode == RootEvaluatorMode.AbstractedHeadsUp,
                ActivePopulationProfile: _populationProfileProvider.ActiveProfileName));
    }

    private static OpponentWeightedRange BlendRanges(OpponentWeightedRange first, double firstWeight, OpponentWeightedRange second, double secondWeight)
        => BlendRanges(new[] { (first, firstWeight), (second, secondWeight) });

    private static OpponentWeightedRange BlendRanges(IReadOnlyList<(OpponentWeightedRange Range, double Weight)> ranges)
    {
        var map = new Dictionary<HoleCards, double>();
        foreach (var (range, weight) in ranges)
        {
            foreach (var combo in range.WeightedCombos)
                map[combo.Cards] = map.TryGetValue(combo.Cards, out var prior) ? prior + (combo.Weight * weight) : combo.Weight * weight;
        }

        var blended = map.Where(x => x.Value > 0d).Select(x => new WeightedHoleCards(x.Key, x.Value)).ToArray();
        var description = string.Join("+", ranges.Select(x => x.Range.Description));
        return new OpponentWeightedRange(blended, $"{description} (weighted)");
    }

    private PreflopLeafEvaluation EvaluateFold(PreflopLeafEvaluationContext context, RootEvaluatorMode rootEvaluatorMode, int rootActiveOpponentCount, int leafActiveOpponentCount)
    {
        var utility = context.LeafState.Players.ToDictionary(player => player.PlayerId, _ => 0d);
        return new PreflopLeafEvaluation(
            utility,
            "equity leaf evaluator: root action fold -> utility 0",
            new PreflopLeafEvaluationDetails(
                HeroHand: ToHandLabel(context.HeroCards),
                UsedEquityEvaluator: false,
                UsedFallbackEvaluator: true,
                EvaluatorType: "FoldZeroUtility",
                AbstractionSource: null,
                ActualActiveOpponentCount: leafActiveOpponentCount,
                AbstractedOpponentCount: null,
                SyntheticDefenderLabel: null,
                NodeFamily: PreflopNodeFamilyClassifier.Classify(context).ToString(),
                HeroPosition: context.HeroPosition.ToString(),
                VillainPosition: null,
                IsHeadsUp: rootEvaluatorMode == RootEvaluatorMode.TrueHeadsUp,
                RangeDescription: null,
                RangeDetail: null,
                FoldProbability: null,
                ContinueProbability: null,
                RootActionType: context.RootAction.ActionType.ToString(),
                ImmediateWinComponent: null,
                ContinueComponent: null,
                ContinueBranchUtility: null,
                FilteredCombos: null,
                HeroEquity: null,
                HeroUtility: 0d,
                EquityVsRangePercentile: null,
                HandClass: ClassifyHand(context.HeroCards),
                BlockerSummary: BuildBlockerSummary(context.HeroCards, null),
                RationaleSummary: "Fold action terminates the branch so utility is fixed at zero.",
                FallbackReason: "root action fold",
                DisplaySummary: "Leaf utility is zero because root action is fold.",
                RootEvaluatorMode: rootEvaluatorMode.ToString(),
                RootActiveOpponentCount: rootActiveOpponentCount,
                LeafActiveOpponentCount: leafActiveOpponentCount,
                UsedDirectAbstractionShortcut: rootEvaluatorMode == RootEvaluatorMode.AbstractedHeadsUp,
                ActivePopulationProfile: _populationProfileProvider.ActiveProfileName));
    }

    private PreflopLeafEvaluation Fallback(PreflopLeafEvaluationContext context, RootEvaluatorMode rootEvaluatorMode, int rootActiveOpponentCount, int leafActiveOpponentCount, string reason)
    {
        var evaluation = _fallbackEvaluator.Evaluate(context);
        var existingDetails = evaluation.Details;
        var summary = $"Fallback leaf evaluator used: {reason}";

        return evaluation with
        {
            Reason = $"equity evaluator fallback: {reason}; {evaluation.Reason}",
            Details = new PreflopLeafEvaluationDetails(
                HeroHand: ToHandLabel(context.HeroCards),
                UsedEquityEvaluator: false,
                UsedFallbackEvaluator: true,
                EvaluatorType: "HeuristicFallback",
                AbstractionSource: existingDetails?.AbstractionSource,
                ActualActiveOpponentCount: leafActiveOpponentCount,
                AbstractedOpponentCount: existingDetails?.AbstractedOpponentCount,
                SyntheticDefenderLabel: existingDetails?.SyntheticDefenderLabel,
                NodeFamily: PreflopNodeFamilyClassifier.Classify(context).ToString(),
                HeroPosition: context.HeroPosition.ToString(),
                VillainPosition: context.LeafState.Players.FirstOrDefault(p => p.PlayerId != context.HeroPlayerId && p.IsActive)?.Position.ToString(),
                IsHeadsUp: rootEvaluatorMode == RootEvaluatorMode.TrueHeadsUp || existingDetails?.AbstractedOpponentCount == 1,
                RangeDescription: existingDetails?.RangeDescription,
                RangeDetail: existingDetails?.RangeDetail,
                FoldProbability: existingDetails?.FoldProbability,
                ContinueProbability: existingDetails?.ContinueProbability,
                RootActionType: existingDetails?.RootActionType ?? context.RootAction.ActionType.ToString(),
                ImmediateWinComponent: existingDetails?.ImmediateWinComponent,
                ContinueComponent: existingDetails?.ContinueComponent,
                ContinueBranchUtility: existingDetails?.ContinueBranchUtility,
                FilteredCombos: existingDetails?.FilteredCombos,
                HeroEquity: existingDetails?.HeroEquity,
                HeroUtility: evaluation.UtilityByPlayer.TryGetValue(context.HeroPlayerId, out var heroUtility) ? heroUtility : existingDetails?.HeroUtility,
                EquityVsRangePercentile: existingDetails?.EquityVsRangePercentile,
                HandClass: existingDetails?.HandClass ?? ClassifyHand(context.HeroCards),
                BlockerSummary: existingDetails?.BlockerSummary ?? BuildBlockerSummary(context.HeroCards, null),
                RationaleSummary: existingDetails?.RationaleSummary ?? "Heuristic fallback used because equity range evaluation was unavailable.",
                FallbackReason: reason,
                DisplaySummary: existingDetails?.DisplaySummary ?? summary,
                RootEvaluatorMode: rootEvaluatorMode.ToString(),
                RootActiveOpponentCount: rootActiveOpponentCount,
                LeafActiveOpponentCount: leafActiveOpponentCount,
                UsedDirectAbstractionShortcut: rootEvaluatorMode == RootEvaluatorMode.AbstractedHeadsUp,
                ActivePopulationProfile: existingDetails?.ActivePopulationProfile ?? _populationProfileProvider.ActiveProfileName)
        };
    }


    private static RootEvaluatorMode DetermineRootEvaluatorMode(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, int rootActiveOpponentCount)
    {
        if (rootActiveOpponentCount == 1)
            return RootEvaluatorMode.TrueHeadsUp;

        if ((nodeFamily == PreflopNodeFamily.Unopened || nodeFamily == PreflopNodeFamily.FacingLimp || nodeFamily == PreflopNodeFamily.FacingRaise)
            && rootActiveOpponentCount >= 2)
        {
            return RootEvaluatorMode.AbstractedHeadsUp;
        }

        return RootEvaluatorMode.Unsupported;
    }

    private static string ToHandLabel(HoleCards cards)
    {
        var ranks = new[] { cards.First.Rank, cards.Second.Rank }.OrderByDescending(ToRankValue).ToArray();
        if (ranks[0] == ranks[1])
            return $"{ToRankChar(ranks[0])}{ToRankChar(ranks[1])}";

        var suited = cards.First.Suit == cards.Second.Suit;
        return $"{ToRankChar(ranks[0])}{ToRankChar(ranks[1])}{(suited ? 's' : 'o')}";
    }

    private static string ClassifyHand(HoleCards cards)
    {
        var ranks = new[] { cards.First.Rank, cards.Second.Rank }.OrderByDescending(ToRankValue).ToArray();
        var high = ToRankValue(ranks[0]);
        var low = ToRankValue(ranks[1]);
        var suited = cards.First.Suit == cards.Second.Suit;

        if (high == low)
            return "Pair";

        if (suited && low <= 5)
            return "Suited wheel";

        if (high >= 11 && low >= 9)
            return suited ? "Suited broadway" : "Offsuit broadway";

        if (!suited && high == 14 && low <= 10)
            return "Weak offsuit ace";

        if (!suited && high <= 10 && low <= 6)
            return "Offsuit trash";

        return suited ? "Suited connector/gapper" : "Offsuit connector/gapper";
    }


    private static double GetBtnUnopenedRealizationPenalty(string handClass, PreflopPopulationProfile profile)
    {
        if (string.Equals(handClass, "Offsuit broadway", StringComparison.Ordinal))
            return profile.OffsuitBroadwayRealizationPenalty;

        if (string.Equals(handClass, "Weak offsuit ace", StringComparison.Ordinal)
            || string.Equals(handClass, "Offsuit trash", StringComparison.Ordinal))
        {
            return profile.WeakOffsuitRealizationPenalty;
        }

        return 0d;
    }

    private static string BuildBlockerSummary(HoleCards cards, Position? villainPosition)
        => $"Contains {cards.First.Rank}/{cards.Second.Rank} blockers versus {(villainPosition?.ToString() ?? "opponent")} continuing range.";

    private static int ToRankValue(Rank rank) => (int)rank;
    private static char ToRankChar(Rank rank) => rank switch
    {
        Rank.Ten => 'T',
        Rank.Jack => 'J',
        Rank.Queen => 'Q',
        Rank.King => 'K',
        Rank.Ace => 'A',
        _ => ((int)rank).ToString()[0]
    };

    private static bool SharesCard(HoleCards left, HoleCards right)
        => left.First == right.First
        || left.First == right.Second
        || left.Second == right.First
        || left.Second == right.Second;

    private static FacingRaiseStructuralContext BuildFacingRaiseContext(PreflopLeafEvaluationContext context, Position openerPosition, int opponentCount)
    {
        var playersLeftBehindHero = CountPlayersLeftBehind(context.RootState.Players, context.HeroPlayerId);
        var isHeadsUpVsOpener = opponentCount == 1;
        var isInPosition = IsInPositionPostflop(context.HeroPosition, openerPosition);
        var structuralClass = ClassifyFacingRaiseStructure(context.HeroPosition, openerPosition, isHeadsUpVsOpener, isInPosition, playersLeftBehindHero);
        return new FacingRaiseStructuralContext(
            context.HeroPosition,
            openerPosition,
            playersLeftBehindHero,
            isHeadsUpVsOpener,
            isInPosition,
            (decimal)context.RootEffectiveStackBb,
            structuralClass);
    }

    private SolverPlayerState? ResolveFacingRaiseOpener(PreflopLeafEvaluationContext context)
    {
        var playersById = context.RootState.Players.ToDictionary(p => p.PlayerId);
        var aggressor = context.RootState.ActionHistory
            .Where(a => a.PlayerId != context.HeroPlayerId && (a.ActionType == ActionType.Bet || a.ActionType == ActionType.Raise || a.ActionType == ActionType.AllIn))
            .LastOrDefault();

        if (aggressor.PlayerId != default && playersById.TryGetValue(aggressor.PlayerId, out var opener))
            return opener;

        return context.RootState.Players.FirstOrDefault(p => p.PlayerId != context.HeroPlayerId && p.IsActive);
    }

    private static string ClassifyFacingRaiseStructure(Position heroPosition, Position openerPosition, bool isHeadsUpVsOpener, bool isInPositionVsOpener, int playersLeftBehindHero)
    {
        if (heroPosition is Position.SB or Position.BB && openerPosition is Position.CO or Position.BTN)
            return "BlindDefenseVsLateOpen";

        if (isInPositionVsOpener && openerPosition is Position.UTG or Position.HJ or Position.CO)
            return "InPositionVsEarlierOpen";

        if (!isInPositionVsOpener && heroPosition is Position.HJ or Position.CO or Position.BTN)
            return "OutOfPositionColdDefense";

        if (!isHeadsUpVsOpener && playersLeftBehindHero > 0)
            return "FieldBehindFacingRaise";

        return "GeneralFacingRaise";
    }

    private static bool IsInPositionPostflop(Position heroPosition, Position openerPosition)
        => GetPostflopOrder(heroPosition) > GetPostflopOrder(openerPosition);

    private static int GetPostflopOrder(Position position)
        => position switch
        {
            Position.SB => 0,
            Position.BB => 1,
            Position.UTG => 2,
            Position.HJ => 3,
            Position.CO => 4,
            Position.BTN => 5,
            _ => 0
        };

    private static double? GetFacingRaisePercentileOverride(Position opponentPosition, Position openerPosition, FacingRaiseStructuralContext context, PreflopPopulationProfile profile)
    {
        if (opponentPosition == openerPosition)
        {
            var openerPercentile = context.StructuralClass switch
            {
                "BlindDefenseVsLateOpen" => 0.16d,
                "InPositionVsEarlierOpen" => 0.12d,
                _ => 0.14d
            };

            if (profile.RaiseRiskPenaltyFactor >= 0.11d)
                openerPercentile += 0.01d;

            return Math.Clamp(openerPercentile, 0.10d, 0.18d);
        }

        if (opponentPosition == Position.SB)
            return Math.Clamp((profile.SbContinueRangePercentileUnopenedVsBtn * 0.24d) + 0.02d, 0.08d, 0.18d);

        if (opponentPosition == Position.BB)
            return Math.Clamp((profile.BbContinueRangePercentileUnopenedVsBtn * 0.22d) + 0.03d, 0.08d, 0.18d);

        var coldContinuePercentile = context.PlayersLeftBehindHero > 0 ? 0.14d : 0.12d;
        if (profile.RaiseRiskPenaltyFactor >= 0.11d)
            coldContinuePercentile += 0.01d;
        return Math.Clamp(coldContinuePercentile, 0.09d, 0.18d);
    }

    private static double GetFacingRaiseContributorWeight(Position opponentPosition, Position openerPosition, FacingRaiseStructuralContext context)
    {
        var baseWeight = opponentPosition == openerPosition ? 1.25d : 0.85d;
        if (opponentPosition is Position.SB or Position.BB)
            baseWeight += 0.05d;

        if (context.PlayersLeftBehindHero > 0 && opponentPosition != openerPosition)
            baseWeight += 0.10d;

        if (!context.IsInPositionVsOpener)
            baseWeight += 0.05d;

        return baseWeight;
    }

    private static double GetFacingRaiseFoldProbability(Position opponentPosition, Position openerPosition, FacingRaiseStructuralContext context, ActionType actionType, double actionSizeBb, bool isJamAction, PreflopPopulationProfile profile)
    {
        if (actionType == ActionType.Call)
            return 0d;

        var baseFold = opponentPosition == openerPosition ? 0.34d : 0.44d;
        if (opponentPosition is Position.SB or Position.BB)
            baseFold += 0.02d;

        if (context.StructuralClass == "BlindDefenseVsLateOpen")
            baseFold -= 0.06d;

        if (context.StructuralClass == "FieldBehindFacingRaise")
            baseFold += 0.02d;

        if (!context.IsInPositionVsOpener)
            baseFold -= 0.03d;

        var sizeLift = isJamAction
            ? 0.10d
            : 0.045d * Math.Max(0d, actionSizeBb - 9d);

        var depthAdjustment = context.EffectiveStackBb >= 50m ? -0.01d : 0.01d;
        var populationAdjustment = profile.RaiseRiskPenaltyFactor >= 0.11d
            ? -0.05d
            : profile.RaiseRiskPenaltyFactor <= 0.075d ? 0.02d : 0d;

        return Math.Clamp(baseFold + sizeLift + depthAdjustment + populationAdjustment, 0.08d, 0.90d);
    }

    private static double GetFacingRaiseSqueezeRiskPenalty(FacingRaiseStructuralContext context)
    {
        var behindPenalty = 0.015d * context.PlayersLeftBehindHero;
        var oopPenalty = context.IsInPositionVsOpener ? 0d : 0.02d;
        return behindPenalty + oopPenalty;
    }

    private static double GetFacingRaiseRealizationPenalty(string handClass, FacingRaiseStructuralContext context, PreflopPopulationProfile profile)
    {
        var positionalPenalty = context.IsInPositionVsOpener ? 0d : 0.02d;
        var behindPenalty = 0.01d * context.PlayersLeftBehindHero;

        if (string.Equals(handClass, "Weak offsuit ace", StringComparison.Ordinal)
            || string.Equals(handClass, "Offsuit trash", StringComparison.Ordinal))
        {
            return profile.WeakOffsuitRealizationPenalty + positionalPenalty + behindPenalty + 0.03d;
        }

        if (string.Equals(handClass, "Offsuit broadway", StringComparison.Ordinal))
            return profile.OffsuitBroadwayRealizationPenalty + positionalPenalty + (0.5d * behindPenalty);

        return positionalPenalty * 0.5d;
    }

    private readonly record struct FacingRaiseStructuralContext(
        Position HeroPosition,
        Position OpenerPosition,
        int PlayersLeftBehindHero,
        bool IsHeadsUpVsOpener,
        bool IsInPositionVsOpener,
        decimal EffectiveStackBb,
        string StructuralClass);

    private static bool IsLimper(SolverPlayerState player)
        => player.Position is not Position.SB and not Position.BB;

    private static int CountPlayersLeftBehind(IReadOnlyList<SolverPlayerState> players, PlayerId heroId)
    {
        var hero = players.FirstOrDefault(p => p.PlayerId == heroId);
        if (hero is null)
            return 0;

        return players.Count(p => p.PlayerId != heroId && p.IsActive && IsBehind(hero.Position, p.Position));
    }

    private static bool IsBehind(Position heroPosition, Position opponentPosition)
        => GetPreflopOrder(opponentPosition) > GetPreflopOrder(heroPosition);

    private static int GetPreflopOrder(Position position)
        => position switch
        {
            Position.UTG => 0,
            Position.HJ => 1,
            Position.CO => 2,
            Position.BTN => 3,
            Position.SB => 4,
            Position.BB => 5,
            _ => 0
        };

    private static double? GetFacingLimpPercentileOverride(Position position, PreflopPopulationProfile profile)
        => position switch
        {
            Position.SB => profile.SbContinueRangePercentileUnopenedVsBtn,
            Position.BB => profile.BbContinueRangePercentileUnopenedVsBtn,
            _ => null
        };

    private static double? GetUnopenedPercentileOverride(Position heroPosition, Position position, PreflopPopulationProfile profile)
    {
        if (heroPosition == Position.SB)
        {
            return position switch
            {
                Position.BB => profile.BbContinueRangePercentileUnopenedVsBtn,
                Position.BTN => profile.SbContinueRangePercentileUnopenedVsBtn,
                _ => null
            };
        }

        return position switch
        {
            Position.BB => profile.BbContinueRangePercentileUnopenedVsBtn,
            Position.BTN => profile.SbContinueRangePercentileUnopenedVsBtn,
            Position.SB => profile.SbContinueRangePercentileUnopenedVsBtn,
            _ => null
        };
    }

    private static double GetFacingLimpContributorWeight(Position position, bool isLimper, int playersLeftBehind)
    {
        var baseWeight = position switch
        {
            Position.SB => 0.70d,
            Position.BB => 0.85d,
            Position.BTN => 0.95d,
            _ => isLimper ? 1.10d : 1.00d
        };

        return baseWeight + (0.02d * Math.Min(playersLeftBehind, 3));
    }

    private static double GetFacingLimpFoldProbability(Position position, bool isLimper, double sizeDelta, PreflopPopulationProfile profile, int playersLeftBehind)
    {
        var baseFold = position switch
        {
            Position.SB => 1d - profile.SbContinueUnopenedVsBtn,
            Position.BB => 1d - profile.BbContinueUnopenedVsBtn,
            _ => isLimper ? 0.44d : 0.47d
        };

        var sizeAdjustment = isLimper ? 0.05d * sizeDelta : 0.03d * sizeDelta;
        var behindAdjustment = 0.01d * Math.Min(playersLeftBehind, 3);
        return Math.Clamp(baseFold + sizeAdjustment + behindAdjustment, 0.20d, 0.92d);
    }

    private static double GetUnopenedContributorWeight(Position heroPosition, Position position, int playersLeftBehind)
    {
        if (heroPosition == Position.SB)
            return GetSbUnopenedContributorWeight(position, playersLeftBehind);

        var baseWeight = position switch
        {
            Position.BTN => 1.20d,
            Position.SB => 0.90d,
            Position.BB => 1.10d,
            _ => 1.00d
        };

        return baseWeight + (0.02d * Math.Min(playersLeftBehind, 3));
    }

    private static double GetUnopenedFoldProbability(Position heroPosition, Position position, double sizeDelta, PreflopPopulationProfile profile, int playersLeftBehind)
    {
        if (heroPosition == Position.SB)
            return GetSbUnopenedFoldProbability(position, sizeDelta, profile, playersLeftBehind);

        var baseFold = position switch
        {
            Position.BB => 1d - profile.BbContinueUnopenedVsBtn,
            Position.SB => 1d - profile.SbContinueUnopenedVsBtn,
            Position.BTN => 0.42d,
            _ => 0.48d
        };

        var sizeAdjustment = 0.03d * sizeDelta;
        var behindAdjustment = 0.01d * Math.Min(playersLeftBehind, 3);
        return Math.Clamp(baseFold + sizeAdjustment + behindAdjustment, 0.18d, 0.94d);
    }

    private static double GetUnopenedPositionalPenalty(Position heroPosition, int activeOpponents, int playersLeftBehind)
        => heroPosition switch
        {
            Position.SB => 0.05d + (0.01d * activeOpponents),
            Position.HJ => 0.02d + (0.01d * playersLeftBehind),
            Position.CO => 0.015d + (0.008d * playersLeftBehind),
            _ => 0.02d + (0.008d * Math.Max(0, activeOpponents - 1))
        };

    private static double GetUnopenedPassivePenalty(Position heroPosition, int playersLeftBehind, int activeOpponents)
        => heroPosition switch
        {
            Position.SB => 0.02d + (0.01d * playersLeftBehind),
            Position.HJ => 0.025d + (0.008d * playersLeftBehind),
            Position.CO => 0.022d + (0.007d * playersLeftBehind),
            _ => 0.02d + (0.006d * Math.Max(0, activeOpponents - 1))
        };

    private static double GetSbUnopenedContributorWeight(Position position, int playersLeftBehind)
    {
        var baseWeight = position switch
        {
            Position.BB => 1.15d,
            Position.BTN => 0.95d,
            _ => 1.00d
        };

        return baseWeight + (0.03d * Math.Min(playersLeftBehind, 2));
    }

    private static double GetSbUnopenedFoldProbability(Position position, double sizeDelta, PreflopPopulationProfile profile, int playersLeftBehind)
    {
        var baseFold = position switch
        {
            Position.BB => 1d - profile.BbContinueUnopenedVsBtn,
            Position.BTN => 1d - profile.SbContinueUnopenedVsBtn,
            _ => 0.52d
        };

        var sizeAdjustment = 0.04d * sizeDelta;
        var behindAdjustment = 0.01d * Math.Min(playersLeftBehind, 2);
        return Math.Clamp(baseFold + sizeAdjustment + behindAdjustment, 0.20d, 0.94d);
    }
}

public sealed class TableDrivenOpponentRangeProvider : IOpponentRangeProvider
{
    private readonly Dictionary<PreflopNodeFamily, double> _percentByFamily = new()
    {
        [PreflopNodeFamily.Unopened] = 0.45,
        [PreflopNodeFamily.FacingLimp] = 0.35,
        [PreflopNodeFamily.FacingRaise] = 0.18,
        [PreflopNodeFamily.Facing3Bet] = 0.08,
        [PreflopNodeFamily.Facing4Bet] = 0.03,
        [PreflopNodeFamily.Squeeze] = 0.06
    };

    private readonly ConcurrentDictionary<RangeDefinitionKey, OpponentWeightedRange> _rangeCache = new();
    private long _rangeBuildCount;

    public long RangeBuildCount => Interlocked.Read(ref _rangeBuildCount);
    public int CachedRangeCount => _rangeCache.Count;

    public bool TryGetRange(OpponentRangeRequest request, out OpponentWeightedRange range, out string reason)
    {
        if (!request.IsHeadsUp)
        {
            reason = "multiway context not yet modeled";
            range = new OpponentWeightedRange(Array.Empty<WeightedHoleCards>(), "unsupported");
            return false;
        }

        var percentile = request.PercentileOverride ?? (_percentByFamily.TryGetValue(request.NodeFamily, out var familyPercentile) ? familyPercentile : double.NaN);

        if (double.IsNaN(percentile))
        {
            reason = $"unsupported node family {request.NodeFamily}";
            range = new OpponentWeightedRange(Array.Empty<WeightedHoleCards>(), "unsupported");
            return false;
        }

        var cacheKey = new RangeDefinitionKey(
            request.HeroPosition,
            request.VillainPosition,
            request.NodeFamily,
            request.RaiseDepth,
            request.IsHeadsUp,
            request.SolverKey,
            request.PercentileOverride);

        var rangeHit = _rangeCache.GetOrAdd(cacheKey, _ =>
        {
            Interlocked.Increment(ref _rangeBuildCount);
            var rangeSize = Math.Max(1, (int)Math.Round(AllDistinctHoleCardsCache.RankedCombosByStrength.Count * percentile));
            var weightedCombos = new WeightedHoleCards[rangeSize];

            for (var i = 0; i < rangeSize; i++)
                weightedCombos[i] = new WeightedHoleCards(AllDistinctHoleCardsCache.RankedCombosByStrength[i], 1d);

            return new OpponentWeightedRange(weightedCombos, request.NodeFamily.ToString());
        });

        var source = request.PercentileOverride.HasValue ? "profile-override" : "table-default";
        reason = $"table-range percentile={percentile:0.00} source={source}";
        range = rangeHit;
        return true;
    }

    private readonly record struct RangeDefinitionKey(
        Position HeroPosition,
        Position? VillainPosition,
        PreflopNodeFamily NodeFamily,
        int RaiseDepth,
        bool IsHeadsUp,
        string? SolverKey,
        double? PercentileOverride);
}

internal static class PreflopNodeFamilyClassifier
{
    public static PreflopNodeFamily Classify(PreflopLeafEvaluationContext context)
    {
        if (TryFromSolverKey(context.SolverKey, out var fromKey))
            return fromKey;

        return context.RootState.RaisesThisStreet switch
        {
            0 => PreflopNodeFamily.Unopened,
            1 => PreflopNodeFamily.FacingRaise,
            2 => PreflopNodeFamily.Facing3Bet,
            3 => PreflopNodeFamily.Facing4Bet,
            _ => PreflopNodeFamily.Unknown
        };
    }

    private static bool TryFromSolverKey(string? solverKey, out PreflopNodeFamily family)
    {
        family = PreflopNodeFamily.Unknown;
        if (string.IsNullOrWhiteSpace(solverKey))
            return false;

        var parts = solverKey.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        family = parts[1] switch
        {
            "UNOPENED" or "UNOPENED_SB" or "UNOPENED_CHECK" or "UNOPENED_FOLD" => PreflopNodeFamily.Unopened,
            "LIMP" or "LIMP_OPTION" => PreflopNodeFamily.FacingLimp,
            "VS_OPEN" => PreflopNodeFamily.FacingRaise,
            "VS_3BET" => PreflopNodeFamily.Facing3Bet,
            "VS_4BET" => PreflopNodeFamily.Facing4Bet,
            "SQUEEZE" => PreflopNodeFamily.Squeeze,
            _ => PreflopNodeFamily.Unknown
        };

        return true;
    }
}

internal static class AllDistinctHoleCardsCache
{
    public static readonly IReadOnlyList<HoleCards> AllCombos = BuildAllCombos();
    public static readonly IReadOnlyList<HoleCards> RankedCombosByStrength = AllCombos.OrderByDescending(PreflopHandStrengthScorer.Score).ToArray();

    private static IReadOnlyList<HoleCards> BuildAllCombos()
    {
        var deck = new List<Card>(52);
        foreach (Rank rank in Enum.GetValues<Rank>())
        foreach (Suit suit in Enum.GetValues<Suit>())
            deck.Add(new Card(rank, suit));

        var combos = new List<HoleCards>(1326);
        for (var i = 0; i < deck.Count - 1; i++)
        for (var j = i + 1; j < deck.Count; j++)
            combos.Add(new HoleCards(deck[i], deck[j]));

        return combos;
    }
}

internal static class PreflopHandStrengthScorer
{
    public static double Score(HoleCards holeCards)
    {
        var first = holeCards.First;
        var second = holeCards.Second;
        var highRank = Math.Max((int)first.Rank, (int)second.Rank);
        var lowRank = Math.Min((int)first.Rank, (int)second.Rank);
        var highNorm = (highRank - 2d) / 12d;
        var lowNorm = (lowRank - 2d) / 12d;

        if (first.Rank == second.Rank)
            return 0.45 + (0.35 * highNorm);

        var score = (0.35 * highNorm) + (0.25 * lowNorm);
        if (first.Suit == second.Suit)
            score += 0.08;

        var gap = Math.Max(0, highRank - lowRank - 1);
        score += gap switch
        {
            0 => 0.07,
            1 => 0.04,
            2 => 0.01,
            _ => Math.Max(-0.1, -0.02 * (gap - 2))
        };

        return score;
    }
}

internal static class DeterministicPreflopEquity
{
    private static readonly ConcurrentDictionary<MatchupEquityCanonicalKey, double> HeadsUpEquityCache = new();
    private static long _matchupComputationCount;

    public static long MatchupComputationCount => Interlocked.Read(ref _matchupComputationCount);
    public static int CachedMatchupCount => HeadsUpEquityCache.Count;

    public static double CalculateHeadsUpEquity(HoleCards hero, HoleCards villain, int samples)
    {
        var key = MatchupEquityKey.Create(hero, villain, samples);
        if (HeadsUpEquityCache.TryGetValue(key.CanonicalKey, out var cachedEquity))
            return key.HeroMatchesCanonicalOrder ? cachedEquity : 1d - cachedEquity;

        var canonicalEquity = CalculateHeadsUpEquityCore(key.CanonicalHero, key.CanonicalVillain, samples);
        var cached = HeadsUpEquityCache.GetOrAdd(key.CanonicalKey, canonicalEquity);
        return key.HeroMatchesCanonicalOrder ? cached : 1d - cached;
    }

    private static double CalculateHeadsUpEquityCore(HoleCards hero, HoleCards villain, int samples)
    {
        Interlocked.Increment(ref _matchupComputationCount);
        var deck = BuildDeckExcluding(hero, villain);
        var board = new Card[5];
        var cards = deck.ToArray();
        var wins = 0d;
        var seed = BuildSeed(hero, villain);
        var rng = new Random(seed);

        for (var i = 0; i < samples; i++)
        {
            for (var k = 0; k < 5; k++)
            {
                var swap = rng.Next(k, cards.Length);
                (cards[k], cards[swap]) = (cards[swap], cards[k]);
                board[k] = cards[k];
            }

            var heroRank = Evaluate7(hero, board);
            var villainRank = Evaluate7(villain, board);

            if (heroRank > villainRank)
                wins += 1d;
            else if (heroRank == villainRank)
                wins += 0.5d;
        }

        return wins / samples;
    }

    private static List<Card> BuildDeckExcluding(HoleCards hero, HoleCards villain)
    {
        var excluded = new HashSet<Card> { hero.First, hero.Second, villain.First, villain.Second };
        var deck = new List<Card>(48);
        foreach (Rank rank in Enum.GetValues<Rank>())
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            var card = new Card(rank, suit);
            if (!excluded.Contains(card))
                deck.Add(card);
        }

        return deck;
    }

    private static int BuildSeed(HoleCards hero, HoleCards villain)
        => HashCode.Combine(hero.First, hero.Second, villain.First, villain.Second);

    private static long Evaluate7(HoleCards hole, IReadOnlyList<Card> board)
    {
        var cards = new Card[7];
        cards[0] = hole.First;
        cards[1] = hole.Second;
        for (var i = 0; i < 5; i++) cards[i + 2] = board[i];

        var best = long.MinValue;
        for (var a = 0; a < 3; a++)
        for (var b = a + 1; b < 4; b++)
        for (var c = b + 1; c < 5; c++)
        for (var d = c + 1; d < 6; d++)
        for (var e = d + 1; e < 7; e++)
        {
            var rank = Evaluate5(cards[a], cards[b], cards[c], cards[d], cards[e]);
            if (rank > best) best = rank;
        }

        return best;
    }

    private static long Evaluate5(Card c1, Card c2, Card c3, Card c4, Card c5)
    {
        Span<int> counts = stackalloc int[15];
        Span<int> suitCounts = stackalloc int[5];
        Span<int> ranks = stackalloc int[5] { (int)c1.Rank, (int)c2.Rank, (int)c3.Rank, (int)c4.Rank, (int)c5.Rank };
        for (var i = 0; i < 5; i++) counts[ranks[i]]++;
        suitCounts[(int)c1.Suit]++; suitCounts[(int)c2.Suit]++; suitCounts[(int)c3.Suit]++; suitCounts[(int)c4.Suit]++; suitCounts[(int)c5.Suit]++;
        var flush = suitCounts[1] == 5 || suitCounts[2] == 5 || suitCounts[3] == 5 || suitCounts[4] == 5;

        var straightHigh = 0;
        for (var h = 14; h >= 5; h--)
            if (counts[h] > 0 && counts[h - 1] > 0 && counts[h - 2] > 0 && counts[h - 3] > 0 && counts[h - 4] > 0) { straightHigh = h; break; }
        if (straightHigh == 0 && counts[14] > 0 && counts[2] > 0 && counts[3] > 0 && counts[4] > 0 && counts[5] > 0) straightHigh = 5;

        if (flush && straightHigh > 0) return Key(8, straightHigh, 0, 0, 0, 0);

        var fours = 0; var three = 0;
        Span<int> pairs = stackalloc int[2]; var pairCount = 0;
        for (var r = 14; r >= 2; r--)
        {
            if (counts[r] == 4) fours = r;
            else if (counts[r] == 3) three = r;
            else if (counts[r] == 2 && pairCount < 2) pairs[pairCount++] = r;
        }

        if (fours > 0)
            return Key(7, fours, HighestExcluding(counts, fours), 0, 0, 0);

        if (three > 0 && pairCount > 0)
            return Key(6, three, pairs[0], 0, 0, 0);

        if (flush)
        {
            var sorted = SortRanksDesc(ranks);
            return Key(5, sorted[0], sorted[1], sorted[2], sorted[3], sorted[4]);
        }

        if (straightHigh > 0) return Key(4, straightHigh, 0, 0, 0, 0);

        if (three > 0)
            return Key(3, three, HighestExcluding(counts, three), HighestExcluding(counts, three, HighestExcluding(counts, three)), 0, 0);

        if (pairCount >= 2)
        {
            var hp = Math.Max(pairs[0], pairs[1]);
            var lp = Math.Min(pairs[0], pairs[1]);
            return Key(2, hp, lp, HighestExcluding(counts, hp, lp), 0, 0);
        }

        if (pairCount == 1)
        {
            var p = pairs[0];
            var k1 = HighestExcluding(counts, p);
            var k2 = HighestExcluding(counts, p, k1);
            var k3 = HighestExcluding(counts, p, k1, k2);
            return Key(1, p, k1, k2, k3, 0);
        }

        var highCards = SortRanksDesc(ranks);
        return Key(0, highCards[0], highCards[1], highCards[2], highCards[3], highCards[4]);
    }

    private static Span<int> SortRanksDesc(Span<int> ranks)
    {
        for (var i = 1; i < ranks.Length; i++)
        {
            var v = ranks[i];
            var j = i - 1;
            while (j >= 0 && ranks[j] < v)
            {
                ranks[j + 1] = ranks[j];
                j--;
            }

            ranks[j + 1] = v;
        }

        return ranks;
    }

    private static int HighestExcluding(Span<int> counts, params int[] excludes)
    {
        for (var r = 14; r >= 2; r--)
        {
            if (counts[r] == 0) continue;
            var isExcluded = false;
            for (var i = 0; i < excludes.Length; i++)
            {
                if (r != excludes[i])
                    continue;

                isExcluded = true;
                break;
            }

            if (!isExcluded) return r;
        }

        return 0;
    }

    private static long Key(int cat, int a, int b, int c, int d, int e)
        => ((long)cat << 24) | ((long)a << 20) | ((long)b << 16) | ((long)c << 12) | ((long)d << 8) | ((long)e << 4);

    private readonly record struct MatchupEquityKey(MatchupEquityCanonicalKey CanonicalKey, HoleCards CanonicalHero, HoleCards CanonicalVillain, bool HeroMatchesCanonicalOrder)
    {
        public static MatchupEquityKey Create(HoleCards hero, HoleCards villain, int samples)
        {
            var heroKey = HoleCardsKey.Create(hero);
            var villainKey = HoleCardsKey.Create(villain);

            if (heroKey.CompareTo(villainKey) <= 0)
                return new MatchupEquityKey(new MatchupEquityCanonicalKey(heroKey, villainKey, samples), hero, villain, HeroMatchesCanonicalOrder: true);

            return new MatchupEquityKey(new MatchupEquityCanonicalKey(villainKey, heroKey, samples), villain, hero, HeroMatchesCanonicalOrder: false);
        }
    }

    private readonly record struct MatchupEquityCanonicalKey(HoleCardsKey First, HoleCardsKey Second, int Samples);

    private readonly record struct HoleCardsKey(int LowCard, int HighCard) : IComparable<HoleCardsKey>
    {
        public static HoleCardsKey Create(HoleCards cards)
        {
            var first = CardKey(cards.First);
            var second = CardKey(cards.Second);
            return first <= second
                ? new HoleCardsKey(first, second)
                : new HoleCardsKey(second, first);
        }

        public int CompareTo(HoleCardsKey other)
        {
            var lowCompare = LowCard.CompareTo(other.LowCard);
            return lowCompare != 0 ? lowCompare : HighCard.CompareTo(other.HighCard);
        }

        private static int CardKey(Card card)
            => ((int)card.Rank * 10) + (int)card.Suit;
    }
}
