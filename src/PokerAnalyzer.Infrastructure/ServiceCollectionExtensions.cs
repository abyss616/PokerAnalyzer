using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;
using PokerAnalyzer.Infrastructure.Persistence;
using PokerAnalyzer.Infrastructure.PreflopSolver;

namespace PokerAnalyzer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPokerAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<DummyStrategyEngine>();
        services.AddSingleton<MonteCarloStrategyEngine>();
        services.AddSingleton<IMonteCarloReferenceEngine>(sp => sp.GetRequiredService<MonteCarloStrategyEngine>());
        services.AddSingleton(new PreflopSolverConfig(140, 100m, new RakeConfig(0.05m, 1.0m, NoFlopNoDrop: true), 2, RaiseSizingAbstraction.Default));
        services.AddSingleton(new PreflopTerminalEvaluator(new ApproxMonteCarloContinuationValueProvider()));
        services.AddSingleton<CfrPlusPreflopSolver>();
        services.AddSingleton<CfrPlusPreflopStrategyEngine>();
        services.AddSingleton<IStrategyEngine>(sp => sp.GetRequiredService<CfrPlusPreflopStrategyEngine>());
        services.AddTransient<HandAnalyzer>();
        services.AddPokerAnalyzerDb();
        services.AddScoped<IHandHistoryRepository, HandHistoryRepository>();
        services.AddSingleton<IHandHistoryParser, XmlHandHistoryParser>();
        return services;
    }

    private static IServiceCollection AddPokerAnalyzerDb(this IServiceCollection services)
    {
        services.AddDbContextFactory<PokerDbContext>(opt =>
        {
            var dbPath = PokerDbPaths.GetDefaultSqlitePath();
            opt.UseSqlite($"Data Source={PokerDbPaths.GetDefaultSqlitePath()}");
        });

        return services;
    }
}
