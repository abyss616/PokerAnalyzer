namespace PokerAnalyzer.Infrastructure.Persistence;

public static class PokerDbPaths
{
    public static string GetDefaultSqlitePath()
    {
        // AppContext.BaseDirectory works reliably for API + Blazor Server.
        var baseDir = AppContext.BaseDirectory;

        var dataDir = Path.Combine(baseDir, "data");
        Directory.CreateDirectory(dataDir);

        return Path.Combine(dataDir, "pokeranalyzer.db");
    }
}
