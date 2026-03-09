using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Application.PreflopAnalysis;
using PokerAnalyzer.Application.PreflopSolver;
using PokerAnalyzer.Infrastructure.Engines;
using PokerAnalyzer.Infrastructure.HandHistories;
using PokerAnalyzer.Infrastructure.Persistence;
using PokerAnalyzer.Infrastructure.PreflopAnalysis;

namespace PokerAnalyzer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPokerAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<IStrategyEngine, DummyStrategyEngine>();
        services.AddSingleton<IAllInEquityCalculator, AllInEquityCalculator>();
        services.AddSingleton<IFlopContinuationValueCalculator, FlopContinuationValueCalculator>();
        services.AddTransient<HandAnalyzer>();
        services.AddPokerAnalyzerDb();
        services.AddScoped<IHandHistoryRepository, HandHistoryRepository>();
        services.AddSingleton<IHandHistoryParser, XmlHandHistoryParser>();
        services.AddSingleton<PreflopStateExtractor>();
        services.AddScoped<IPreflopHandAnalysisService, PreflopHandAnalysisService>();
        services.AddSingleton<IRegretStore, InMemoryRegretStore>();
        services.AddSingleton<IAverageStrategyStore, InMemoryAverageStrategyStore>();
        services.AddSingleton<IPreflopTrainingProgressStore, InMemoryPreflopTrainingProgressStore>();
        services.AddSingleton<IPreflopInfoSetMapper, PreflopInfoSetMapper>();
        services.AddSingleton<IPreflopStrategyQueryService, PreflopStrategyQueryService>();
        services.AddSingleton<IPreflopStrategyProvider, LivePreflopSolveService>();
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
