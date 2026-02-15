using Microsoft.EntityFrameworkCore;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Persistence;
using System.Text.Json;
using System.Xml.Linq;

public interface IHandHistoryIngestService
{
    Task<Guid> IngestAsync(string originalFileName, string xml, CancellationToken ct);
}

public sealed partial class HandHistoryIngestService : IHandHistoryIngestService
{
    private readonly PokerDbContext _db;

    public HandHistoryIngestService(PokerDbContext db) => _db = db;

    public async Task<Guid> IngestAsync(string originalFileName, string xml, CancellationToken ct)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("Missing <session> root.");
        var sessionCode = ParseLong(root.Attribute("sessioncode")?.Value);
        // dedupe
        var existing = await _db.Sessions.SingleOrDefaultAsync(x => x.SessionCode == sessionCode, ct);
        if (existing != null)
            return existing.Id;

       

        var session = new HandHistorySession
        {
            SessionCode = sessionCode,
            RawXml = xml
        };

        // ---- session general ----
        var g = root.Element("general");
        if (g != null)
        {
            session.Nickname = g.Element("nickname")?.Value?.Trim();
            session.Currency = g.Element("currency")?.Value?.Trim();
            session.MaxSeats = ParseInt(g.Element("tablesize")?.Value);

            session.StartedAtUtc = ParseDateUtc(g.Element("startdate")?.Value);

            session.TotalBets = ParseMoney(g.Element("bets")?.Value);
            session.TotalWin = ParseMoney(g.Element("wins")?.Value);
            if (session.TotalBets.HasValue && session.TotalWin.HasValue)
                session.Result = session.TotalWin.Value - session.TotalBets.Value;

            // gametype example: "Holdem NL €0.01/€0.02"
            var gametype = g.Element("gametype")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(gametype))
            {
                // crude but effective
                session.Game = gametype.Contains("Holdem", StringComparison.OrdinalIgnoreCase) ? "Holdem" : null;
                session.Gametype = gametype.Contains("NL", StringComparison.OrdinalIgnoreCase) ? "NL" : null;

                var (sb, bb) = ParseBlindsFromGametype(gametype);
                session.SmallBlind = sb;
                session.BigBlind = bb;
            }

            session.HandCount = ParseInt(g.Element("gamecount")?.Value);
            session.RealMoney = string.Equals(g.Element("mode")?.Value?.Trim(), "real", StringComparison.OrdinalIgnoreCase);
        }

        // ---- games => hands ----
        var i = 0;
        foreach (var game in root.Elements("game"))
        {

            var hand = ParseHand(game, session.Nickname);
            hand.HandNumber = i + 1;
            if (i == 18)
            {
                var jsonString = JsonSerializer.Serialize(hand);
            }
            session.Hands.Add(hand);
            i++;
        }

        foreach (var profile in BuildPlayerProfiles(session.Hands))
        {
            session.Players.Add(profile);
        }

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session.Id;
    }
}
