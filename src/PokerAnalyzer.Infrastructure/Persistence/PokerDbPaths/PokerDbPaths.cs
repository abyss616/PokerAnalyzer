namespace PokerAnalyzer.Infrastructure.Persistence;

public static class PokerDbPaths
{
    public static string GetDefaultSqlitePath()
    {
        // AppContext.BaseDirectory works reliably for API + Blazor Server.
        var root = Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "PokerAnalyzer");

        Directory.CreateDirectory(root);

        return Path.Combine(root, "poker.db");
    }
}
