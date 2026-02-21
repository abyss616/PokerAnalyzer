using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;
using PokerAnalyzer.Infrastructure.Persistence;

namespace PokerAnalyzer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPokerAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<IPreflopSolverService, PreflopSolverService>();
        var useLegacy = string.Equals(Environment.GetEnvironmentVariable("POKER_ANALYZER_USE_LEGACY_HEURISTIC"), "1", StringComparison.Ordinal);
        if (useLegacy)
            services.AddSingleton<IStrategyEngine, DummyStrategyEngine>();
        else
            services.AddSingleton<IStrategyEngine, SolverBackedStrategyEngine>();

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
