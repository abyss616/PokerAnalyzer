using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;
using PokerAnalyzer.Infrastructure.Persistence;
using System;

namespace PokerAnalyzer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPokerAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<DummyStrategyEngine>();
        services.AddSingleton<MonteCarloStrategyEngine>();
        services.AddSingleton<CfrPlusPreflopStrategyEngine>();
        services.AddSingleton<IStrategyEngine>(sp =>
        {
            var useLegacy = Environment.GetEnvironmentVariable("POKER_ANALYZER_USE_LEGACY_MONTECARLO");
            return string.Equals(useLegacy, "1", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<MonteCarloStrategyEngine>()
                : sp.GetRequiredService<CfrPlusPreflopStrategyEngine>();
        });
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
