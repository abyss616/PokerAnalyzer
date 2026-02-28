namespace PokerAnalyzer.Infrastructure.Engines;

public static class PreflopSizingNormalizer
{
    public static string Bucket(decimal sizeBb)
    {
        if (sizeBb <= 2.5m) return "SMALL";
        if (sizeBb <= 4.0m) return "MEDIUM";
        if (sizeBb <= 10.0m) return "LARGE";
        return "JAM";
    }
}
