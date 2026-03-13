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
    string? SolverKey);

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

    public EquityBasedPreflopLeafEvaluator(
        IOpponentRangeProvider rangeProvider,
        IPreflopLeafEvaluator fallbackEvaluator,
        int samplesPerMatchup = 160)
    {
        _rangeProvider = rangeProvider ?? throw new ArgumentNullException(nameof(rangeProvider));
        _fallbackEvaluator = fallbackEvaluator ?? throw new ArgumentNullException(nameof(fallbackEvaluator));
        _samplesPerMatchup = Math.Max(32, samplesPerMatchup);
    }

    public PreflopLeafEvaluation Evaluate(PreflopLeafEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.RootAction.ActionType == ActionType.Fold)
            return EvaluateFold(context);

        if (TryEvaluateBtnUnopenedActionAware(context, out var btnActionAware))
            return btnActionAware;

        var activeOpponents = context.LeafState.Players.Where(p => p.PlayerId != context.HeroPlayerId && p.IsActive).ToArray();
        var nodeFamily = PreflopNodeFamilyClassifier.Classify(context);

        if (activeOpponents.Length == 1)
            return EvaluateHeadsUp(context, nodeFamily, activeOpponents[0]);

        return Fallback(context, $"expected heads-up leaf but found {activeOpponents.Length} active opponents");
    }

    private bool TryEvaluateBtnUnopenedActionAware(PreflopLeafEvaluationContext context, out PreflopLeafEvaluation evaluation)
    {
        evaluation = default!;
        var nodeFamily = PreflopNodeFamilyClassifier.Classify(context);
        if (context.HeroPosition != Position.BTN || nodeFamily != PreflopNodeFamily.Unopened)
            return false;

        var activeOpponents = context.LeafState.Players.Where(p => p.PlayerId != context.HeroPlayerId && p.IsActive).ToArray();
        var sb = activeOpponents.FirstOrDefault(p => p.Position == Position.SB);
        var bb = activeOpponents.FirstOrDefault(p => p.Position == Position.BB);
        if (sb is null || bb is null)
            return false;

        const double sbContinue = 0.23;
        const double bbContinue = 0.34;
        var allFold = Math.Clamp((1d - sbContinue) * (1d - bbContinue), 0d, 1d);
        var sbOnly = sbContinue * (1d - bbContinue);
        var bbOnly = (1d - sbContinue) * bbContinue;
        var bothContinue = sbContinue * bbContinue;
        var continueProbability = Math.Clamp(1d - allFold, 0d, 1d);

        var sbWeight = continueProbability > 0d ? (sbOnly + 0.5d * bothContinue) / continueProbability : 0.5d;
        var bbWeight = continueProbability > 0d ? (bbOnly + 0.5d * bothContinue) / continueProbability : 0.5d;

        var sbReq = new OpponentRangeRequest(context.HeroPosition, Position.SB, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey);
        var bbReq = new OpponentRangeRequest(context.HeroPosition, Position.BB, nodeFamily, context.RootState.RaisesThisStreet, true, context.SolverKey);

        if (!_rangeProvider.TryGetRange(sbReq, out var sbRange, out var sbReason))
            return false;
        if (!_rangeProvider.TryGetRange(bbReq, out var bbRange, out var bbReason))
            return false;

        var combined = BlendRanges(sbRange, sbWeight, bbRange, bbWeight);
        var detail = $"BTN unopened weighted-blind abstraction: P(all fold)={allFold:0.000}, P(continue)={continueProbability:0.000}, SBw={sbWeight:0.000}, BBw={bbWeight:0.000}; sb={sbReason}; bb={bbReason}";
        var baseEval = EvaluateAgainstRange(
            context,
            nodeFamily,
            villainPosition: null,
            combined,
            detail,
            activeOpponentCount: activeOpponents.Length,
            evaluatorType: "AbstractedHeadsUp",
            abstractionSource: "WeightedBlindsBTNUnopened",
            abstractedOpponentCount: 1,
            syntheticDefenderLabel: "SyntheticBlindDefender",
            foldProbability: allFold,
            continueProbability: continueProbability,
            summaryPrefix: "Level-2 BTN unopened abstraction");

        if (!baseEval.UtilityByPlayer.TryGetValue(context.HeroPlayerId, out var continueBranchUtility))
            continueBranchUtility = 0d;

        var immediateWinBb = 1.5d;
        var openSizeBb = 2.5d;
        var immediateComponent = 0d;
        var continueComponent = 0d;
        var heroUtility = continueBranchUtility;
        var actionType = context.RootAction.ActionType;

        if (actionType == ActionType.Raise)
        {
            immediateComponent = allFold * immediateWinBb;
            continueComponent = continueProbability * continueBranchUtility;
            var riskPenalty = continueProbability * 0.08d * openSizeBb;
            heroUtility = immediateComponent + continueComponent - riskPenalty;
        }
        else if (actionType is ActionType.Call or ActionType.Check)
        {
            var limpPenalty = 0.04d;
            heroUtility = continueBranchUtility - limpPenalty;
            continueComponent = heroUtility;
            immediateComponent = 0d;
        }

        var utility = baseEval.UtilityByPlayer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        utility[context.HeroPlayerId] = heroUtility;

        var actionLabel = actionType.ToString();
        var summary = actionType == ActionType.Raise
            ? $"{baseEval.Details!.DisplaySummary} Action={actionLabel}, EV={heroUtility:0.000} = fold({allFold:0.000})*pot({immediateWinBb:0.00}) + continue({continueProbability:0.000})*Vcont({continueBranchUtility:0.000})."
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
                ContinueBranchUtility = continueBranchUtility,
                DisplaySummary = summary,
                RationaleSummary = $"Unopened BTN {actionLabel} uses action-sensitive utility: fold-equity immediate component + continuation branch utility."
            }
        };

        return true;
    }

    private PreflopLeafEvaluation EvaluateHeadsUp(PreflopLeafEvaluationContext context, PreflopNodeFamily nodeFamily, SolverPlayerState villain)
    {
        var request = new OpponentRangeRequest(
            context.HeroPosition,
            villain.Position,
            nodeFamily,
            context.RootState.RaisesThisStreet,
            IsHeadsUp: true,
            context.SolverKey);

        if (!_rangeProvider.TryGetRange(request, out var range, out var rangeReason))
            return Fallback(context, $"range provider miss ({rangeReason})");

        return EvaluateAgainstRange(
            context,
            nodeFamily,
            villain.Position.ToString(),
            range,
            rangeReason,
            activeOpponentCount: 1,
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
        int activeOpponentCount,
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
            return Fallback(context, "range empty after blocker filtering");

        var weightedTotal = filteredRange.Sum(w => w.Weight);
        if (weightedTotal <= 0d)
            return Fallback(context, "range has non-positive total weight");

        var heroEquity = 0d;
        foreach (var combo in filteredRange)
        {
            var matchupEquity = DeterministicPreflopEquity.CalculateHeadsUpEquity(context.HeroCards, combo.Cards, _samplesPerMatchup);
            heroEquity += (combo.Weight / weightedTotal) * matchupEquity;
        }

        var heroUtility = (heroEquity - 0.5d) * 2d;
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
                ActualActiveOpponentCount: activeOpponentCount,
                AbstractedOpponentCount: abstractedOpponentCount,
                SyntheticDefenderLabel: syntheticDefenderLabel,
                NodeFamily: nodeFamily.ToString(),
                HeroPosition: context.HeroPosition.ToString(),
                VillainPosition: villainPosition,
                IsHeadsUp: activeOpponentCount == 1,
                RangeDescription: range.Description,
                RangeDetail: rangeReason,
                FoldProbability: foldProbability,
                ContinueProbability: continueProbability,
                RootActionType: context.RootAction.ActionType.ToString(),
                ImmediateWinComponent: null,
                ContinueComponent: null,
                ContinueBranchUtility: null,
                FilteredCombos: filteredRange.Length,
                HeroEquity: heroEquity,
                HeroUtility: heroUtility,
                EquityVsRangePercentile: percentile,
                HandClass: handClass,
                BlockerSummary: blockerSummary,
                RationaleSummary: rationaleSummary,
                FallbackReason: null,
                DisplaySummary: summary));
    }

    private static OpponentWeightedRange BlendRanges(OpponentWeightedRange first, double firstWeight, OpponentWeightedRange second, double secondWeight)
    {
        var map = new Dictionary<HoleCards, double>();
        foreach (var combo in first.WeightedCombos)
            map[combo.Cards] = map.TryGetValue(combo.Cards, out var prior) ? prior + (combo.Weight * firstWeight) : combo.Weight * firstWeight;

        foreach (var combo in second.WeightedCombos)
            map[combo.Cards] = map.TryGetValue(combo.Cards, out var prior) ? prior + (combo.Weight * secondWeight) : combo.Weight * secondWeight;

        var blended = map.Where(x => x.Value > 0d).Select(x => new WeightedHoleCards(x.Key, x.Value)).ToArray();
        return new OpponentWeightedRange(blended, $"{first.Description}+{second.Description} (weighted)");
    }

    private PreflopLeafEvaluation EvaluateFold(PreflopLeafEvaluationContext context)
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
                ActualActiveOpponentCount: context.LeafState.Players.Count(p => p.PlayerId != context.HeroPlayerId && p.IsActive),
                AbstractedOpponentCount: null,
                SyntheticDefenderLabel: null,
                NodeFamily: PreflopNodeFamilyClassifier.Classify(context).ToString(),
                HeroPosition: context.HeroPosition.ToString(),
                VillainPosition: null,
                IsHeadsUp: context.LeafState.Players.Count(p => p.IsActive) == 2,
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
                DisplaySummary: "Leaf utility is zero because root action is fold."));
    }

    private PreflopLeafEvaluation Fallback(PreflopLeafEvaluationContext context, string reason)
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
                ActualActiveOpponentCount: context.LeafState.Players.Count(p => p.PlayerId != context.HeroPlayerId && p.IsActive),
                AbstractedOpponentCount: existingDetails?.AbstractedOpponentCount,
                SyntheticDefenderLabel: existingDetails?.SyntheticDefenderLabel,
                NodeFamily: PreflopNodeFamilyClassifier.Classify(context).ToString(),
                HeroPosition: context.HeroPosition.ToString(),
                VillainPosition: context.LeafState.Players.FirstOrDefault(p => p.PlayerId != context.HeroPlayerId && p.IsActive)?.Position.ToString(),
                IsHeadsUp: context.LeafState.Players.Count(p => p.IsActive) == 2,
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
                DisplaySummary: existingDetails?.DisplaySummary ?? summary)
        };
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

        if (!suited && high <= 10 && low <= 6)
            return "Offsuit trash";

        return suited ? "Suited connector/gapper" : "Offsuit connector/gapper";
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

        if (!_percentByFamily.TryGetValue(request.NodeFamily, out var percentile))
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
            request.SolverKey);

        var rangeHit = _rangeCache.GetOrAdd(cacheKey, _ =>
        {
            Interlocked.Increment(ref _rangeBuildCount);
            var rangeSize = Math.Max(1, (int)Math.Round(AllDistinctHoleCardsCache.RankedCombosByStrength.Count * percentile));
            var weightedCombos = new WeightedHoleCards[rangeSize];

            for (var i = 0; i < rangeSize; i++)
                weightedCombos[i] = new WeightedHoleCards(AllDistinctHoleCardsCache.RankedCombosByStrength[i], 1d);

            return new OpponentWeightedRange(weightedCombos, request.NodeFamily.ToString());
        });

        reason = $"table-range percentile={percentile:0.00}";
        range = rangeHit;
        return true;
    }

    private readonly record struct RangeDefinitionKey(
        Position HeroPosition,
        Position? VillainPosition,
        PreflopNodeFamily NodeFamily,
        int RaiseDepth,
        bool IsHeadsUp,
        string? SolverKey);
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
            "LIMP" => PreflopNodeFamily.FacingLimp,
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
