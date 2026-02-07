using PokerAnalyzer.Domain.Game;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed partial class HandHistoryIngestService
{
    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static bool IsVoluntaryPreflopInvestment(ActionType type) =>
        type is ActionType.Call or ActionType.Raise or ActionType.AllIn or ActionType.Bet;

    private static bool IsAggressivePostflopAction(ActionType type) =>
        type is ActionType.Bet or ActionType.Raise or ActionType.AllIn;

    private static bool IsRaisePostflopAction(ActionType type) =>
        type is ActionType.Raise or ActionType.AllIn;

    private static decimal? ParseMoney(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // examples: "€0.05", "€0.00"
        s = s.Trim();
        s = s.Replace("€", "");
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        // sometimes comma decimals in some exports:
        if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("lv-LV"), out v))
            return v;
        return null;
    }

    private static DateTime? ParseDateUtc(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // your XML: "2026-01-24 13:17:08"
        if (DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        if (DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
            return dt;
        return null;
    }

    private static long ParseLong(string? s) => long.TryParse(s, out var v) ? v : 0;
    private static int? ParseInt(string? s) => int.TryParse(s, out var v) ? v : (int?)null;

    private static (decimal? sb, decimal? bb) ParseBlindsFromGametype(string gametype)
    {
        // "Holdem NL €0.01/€0.02"
        var m = Regex.Match(gametype, @"€(?<sb>\d+(\.\d+)?)\/€(?<bb>\d+(\.\d+)?)");
        if (!m.Success) return (null, null);
        return (ParseMoney("€" + m.Groups["sb"].Value), ParseMoney("€" + m.Groups["bb"].Value));
    }

    private static string NormalizeCards(string raw)
    {
        // Input examples:
        // "S7 S9 CQ" -> "7s 9s Qc"
        // "HQ"      -> "Qh"
        // "HK DK"   -> "Kh Kd"
        // "X X"     -> "X X"
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        raw = raw.Trim();
        if (raw == "X X") return raw;

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var norm = parts.Select(NormalizeOneCard);
        return string.Join(' ', norm);
    }

    private static string NormalizeOneCard(string token)
    {
        // token like "S7" or "CQ" or "H10"
        token = token.Trim();
        if (token == "X") return "X";

        char suit = token[0];
        var rank = token.Substring(1); // "7", "Q", "10", "K", "A"

        char suitOut = suit switch
        {
            'S' => 's',
            'H' => 'h',
            'D' => 'd',
            'C' => 'c',
            _ => '?'
        };

        // rank already OK; ensure "10" not "T" (either is fine, pick one)
        return $"{rank}{suitOut}";
    }
}
