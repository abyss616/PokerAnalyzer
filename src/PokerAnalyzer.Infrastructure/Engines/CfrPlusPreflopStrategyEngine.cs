using Microsoft.Extensions.Logging;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class CfrPlusPreflopStrategyEngine : IStrategyEngine
{
    private readonly IMonteCarloReferenceEngine _monteCarloReference;
    private readonly CfrPlusPreflopSolver _solver;
    private readonly PreflopSolverConfig _config;
    private readonly ILogger<CfrPlusPreflopStrategyEngine> _logger;

    private readonly object _solveLock = new();
    private Task<PreflopSolveResult>? _solveTask;


    public CfrPlusPreflopStrategyEngine(IMonteCarloReferenceEngine monteCarloReference)
        : this(
            monteCarloReference,
            new CfrPlusPreflopSolver(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider())),
            new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 2, RaiseSizingAbstraction.Default),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CfrPlusPreflopStrategyEngine>.Instance)
    {
    }

    public CfrPlusPreflopStrategyEngine(
        IMonteCarloReferenceEngine monteCarloReference,
        CfrPlusPreflopSolver solver,
        PreflopSolverConfig config,
        ILogger<CfrPlusPreflopStrategyEngine> logger)
    {
        _monteCarloReference = monteCarloReference;
        _solver = solver;
        _config = config;
        _logger = logger;
    }

    public Recommendation Recommend(HandState state, HeroContext hero)
    {
        var reference = _monteCarloReference.EvaluateReference(state, hero);
        if (state.Street != Street.Preflop || hero.HeroHoleCards is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var key = PreflopLiveStateMapper.TryMap(state, hero);
        if (key is null)
            return BuildUnsupportedRecommendation(reference, "Unsupported preflop state for solver abstraction");

        var solved = GetOrStartSolveAsync(CancellationToken.None).GetAwaiter().GetResult();
        var query = _solver.QueryStrategy(solved, key, hero.HeroHoleCards.Value.ToString());

        var ranked = query.ActionFrequencies.OrderByDescending(k => k.Value)
            .Select(k => new RecommendedAction(k.Key, null, (decimal)k.Value)).ToList();

        return new Recommendation(
            ranked,
            ranked.FirstOrDefault(),
            query.EstimatedEvBb,
            reference.ReferenceEV,
            $"CFR+ preflop solver key={query.InfoSet.HistorySig}/{query.InfoSet.ActingPosition}, best={query.BestAction}, ev={query.EstimatedEvBb:0.###}bb",
            reference.ReferenceExplanation);
    }

    public PreflopSolveResult SolvePreflop(PreflopSolverConfig config) => _solver.SolvePreflop(config);

    public StrategyQueryResult QueryStrategy(PreflopInfoSetKey key, string heroHand)
    {
        var solved = GetOrStartSolveAsync(CancellationToken.None).GetAwaiter().GetResult();
        return _solver.QueryStrategy(solved, key, heroHand);
    }

    private Task<PreflopSolveResult> GetOrStartSolveAsync(CancellationToken ct)
    {
        lock (_solveLock)
        {
            _solveTask ??= Task.Run(() =>
            {
                _logger.LogInformation("Starting preflop solve...");
                var start = Environment.TickCount64;
                var solved = _solver.SolvePreflop(_config);
                _logger.LogInformation("Preflop solve completed in {DurationMs} ms", Environment.TickCount64 - start);
                return solved;
            }, CancellationToken.None);

            return _solveTask.WaitAsync(ct);
        }
    }

    private static Recommendation BuildUnsupportedRecommendation(Recommendation reference, string reason)
        => new([], null, null, reference.ReferenceEV, reason, reference.ReferenceExplanation);
}

public static class PreflopLiveStateMapper
{
    public static PreflopInfoSetKey? TryMap(HandState state, HeroContext hero)
    {
        if (hero.PlayerPositions is null || !hero.PlayerPositions.TryGetValue(hero.HeroId, out var heroPos))
            return null;

        var history = hero.ActionHistory?.Where(a => a.Street == Street.Preflop).Select(a => a.Type).ToList() ?? [];
        var playerCount = hero.PlayerPositions.Count;
        var toCallBb = hero.BigBlind.Value <= 0 ? 0 : (int)Math.Round(state.GetToCall(hero.HeroId).Value / (decimal)hero.BigBlind.Value);
        var raises = history.Count(a => a is ActionType.Raise or ActionType.Bet or ActionType.AllIn);
        var lastRaiseBb = raises == 0 ? 0 : Math.Max(2, toCallBb);
        var eff = (int)Math.Round(state.Stacks[hero.HeroId].Value / (decimal)hero.BigBlind.Value);

        return new PreflopInfoSetKey(playerCount, heroPos, PreflopHistorySignature.Build(history), toCallBb, lastRaiseBb, eff);
    }
}
