
using Microsoft.Extensions.DependencyInjection;
using PokerAnalyzer.Application.Analysis;

namespace PokerAnalyzer.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPokerAnalyzerApplication(this IServiceCollection services)
    {
        // Core analysis orchestrator
        services.AddScoped<HandAnalyzer>();

        // Analysis of stored/uploaded hands (DB → Domain → Analyzer)
        services.AddScoped<IStoredHandAnalysisService, StoredHandAnalysisService>();

        return services;
    }
}
