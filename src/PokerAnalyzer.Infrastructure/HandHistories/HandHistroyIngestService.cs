using Microsoft.EntityFrameworkCore;
using PokerAnalyzer.Domain.Entities;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.Helpers;
using PokerAnalyzer.Infrastructure.Helpers;
using PokerAnalyzer.Infrastructure.Persistence;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public interface IHandHistoryIngestService
{
    Task<Guid> IngestAsync(string originalFileName, string xml, CancellationToken ct);
}

public sealed class HandHistoryIngestService : IHandHistoryIngestService
{
    private readonly PokerDbContext _db;

    public HandHistoryIngestService(PokerDbContext db) => _db = db;

    public async Task<Guid> IngestAsync(string originalFileName, string xml, CancellationToken ct)
    {
        var sha = Sha256Hex(xml);

        // dedupe
        var existing = await _db.Sessions.SingleOrDefaultAsync(x => x.ContentSha256 == sha, ct);
        if (existing != null)
            return existing.Id;

        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("Missing <session> root.");

        var session = new HandHistorySession
        {
            SessionCode = ParseLong(root.Attribute("sessioncode")?.Value),
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
            if (i == 18)
            {
                var jsonString = JsonSerializer.Serialize(hand);
            }
            session.Hands.Add(hand);
            i++;
        }

        foreach (var profile in BuildOpponentProfiles(session.Hands, session.Nickname))
        {
            session.Opponents.Add(profile);
        }

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session.Id;
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static IEnumerable<OpponentProfile> BuildOpponentProfiles(IEnumerable<Hand> hands, string? heroName)
    {
        var profiles = new Dictionary<string, OpponentProfile>(StringComparer.Ordinal);

        foreach (var hand in hands)
        {
            var preflopActions = hand.Actions
                .Where(a => a.Street == Street.Preflop)
                .ToList();

            var activePlayers = preflopActions
                .Where(a => a.Type != ActionType.SitOut)
                .Select(a => a.Player)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var player in activePlayers)
            {
                if (!string.IsNullOrWhiteSpace(heroName) && string.Equals(player, heroName, StringComparison.Ordinal))
                    continue;

                GetOrCreate(profiles, player).Hands++;
            }

            var vpipPlayers = new HashSet<string>(StringComparer.Ordinal);
            var pfrPlayers = new HashSet<string>(StringComparer.Ordinal);
            var threeBetPlayers = new HashSet<string>(StringComparer.Ordinal);
            var facedThreeBetPlayers = new HashSet<string>(StringComparer.Ordinal);
            var foldToThreeBetPlayers = new HashSet<string>(StringComparer.Ordinal);

            string? preflopRaiser = null;
            bool sawRaise = false;
            bool sawThreeBet = false;

            foreach (var action in preflopActions)
            {
                if (action.Type == ActionType.SitOut) continue;

                if (IsVoluntaryPreflopInvestment(action.Type))
                {
                    vpipPlayers.Add(action.Player);
                }

                if (PreFlopOperations.IsPreflopAggressive(action.Type))
                {
                    if (!sawRaise)
                    {
                        sawRaise = true;
                        preflopRaiser = action.Player;
                        pfrPlayers.Add(action.Player);
                    }
                    else if (!sawThreeBet && !string.Equals(action.Player, preflopRaiser, StringComparison.Ordinal))
                    {
                        sawThreeBet = true;
                        threeBetPlayers.Add(action.Player);
                        if (!string.IsNullOrWhiteSpace(preflopRaiser))
                            facedThreeBetPlayers.Add(preflopRaiser);
                    }
                }

                if (sawThreeBet &&
                    action.Type == ActionType.Fold &&
                    !string.IsNullOrWhiteSpace(preflopRaiser) &&
                    string.Equals(action.Player, preflopRaiser, StringComparison.Ordinal))
                {
                    foldToThreeBetPlayers.Add(action.Player);
                }
            }

            IncrementProfiles(profiles, vpipPlayers, heroName, p => p.VpipHands++);
            IncrementProfiles(profiles, pfrPlayers, heroName, p => p.PfrHands++);
            IncrementProfiles(profiles, threeBetPlayers, heroName, p => p.ThreeBetHands++);
            IncrementProfiles(profiles, facedThreeBetPlayers, heroName, p => p.FacedThreeBetHands++);
            IncrementProfiles(profiles, foldToThreeBetPlayers, heroName, p => p.FoldToThreeBetHands++);
        }

        return profiles.Values;
    }

    private static OpponentProfile GetOrCreate(Dictionary<string, OpponentProfile> profiles, string player)
    {
        if (profiles.TryGetValue(player, out var existing)) return existing;
        var created = new OpponentProfile { Player = player };
        profiles[player] = created;
        return created;
    }

    private static void IncrementProfiles(
        Dictionary<string, OpponentProfile> profiles,
        IEnumerable<string> players,
        string? heroName,
        Action<OpponentProfile> increment)
    {
        foreach (var player in players)
        {
            if (!string.IsNullOrWhiteSpace(heroName) && string.Equals(player, heroName, StringComparison.Ordinal))
                continue;

            increment(GetOrCreate(profiles, player));
        }
    }

    private static bool IsVoluntaryPreflopInvestment(ActionType type) =>
        type is ActionType.Call or ActionType.Raise or ActionType.AllIn or ActionType.Bet;



    private static Hand ParseHand(XElement game, string? heroName)
    {
        var hand = new Hand
        {
            GameCode = ParseLong(game.Attribute("gamecode")?.Value)
        };

        // game/general: startdate, players
        var gen = game.Element("general");
        if (gen != null)
        {
            hand.StartedAtUtc = ParseDateUtc(gen.Element("startdate")?.Value);

            var players = gen.Element("players")?.Elements("player") ?? Enumerable.Empty<XElement>();
            foreach (var p in players)
            {
                var name = (p.Attribute("name")?.Value ?? "").Trim();
                hand.Players.Add(new HandPlayer
                {
                    Name = name,
                    Seat = ParseInt(p.Attribute("seat")?.Value) ?? 0,
                    StackStart = ParseMoney(p.Attribute("chips")?.Value),
                    IsHero = heroName != null && string.Equals(name, heroName, StringComparison.Ordinal)
                });
            }

            // Pot + rake are not explicit tags in your XML, but are present per-player:
            // bet="€0.12" rakeamount="€0.01"
            hand.Pot = players.Select(x => ParseMoney(x.Attribute("bet")?.Value) ?? 0m).Sum();
            hand.Rake = players.Select(x => ParseMoney(x.Attribute("rakeamount")?.Value) ?? 0m).Sum();
        }

        // rounds => streets + actions + cards
        foreach (var round in game.Elements("round"))
        {
            var street = DetectStreet(round);

            // board cards appear as <cards type="Flop">S7 S9 CQ</cards> etc.
            foreach (var c in round.Elements("cards"))
            {
                var type = c.Attribute("type")?.Value;
                var txt = (c.Value ?? "").Trim();

                if (string.Equals(type, "Flop", StringComparison.OrdinalIgnoreCase))
                    hand.Flop = NormalizeCards(txt);      // "S7 S9 CQ" -> "7s 9s Qc"
                else if (string.Equals(type, "Turn", StringComparison.OrdinalIgnoreCase))
                    hand.Turn = NormalizeCards(txt);      // "HQ" -> "Qh"
                else if (string.Equals(type, "River", StringComparison.OrdinalIgnoreCase))
                    hand.River = NormalizeCards(txt);
                else if (string.Equals(type, "Pocket", StringComparison.OrdinalIgnoreCase))
                {
                    var player = c.Attribute("player")?.Value?.Trim();
                    if (heroName != null && string.Equals(player, heroName, StringComparison.Ordinal))
                    {
                        // hero pocket is actually shown in your file in most hands
                        hand.HeroHoleCards = NormalizeCards(txt);
                    }

                    // also store revealed opponent cards later into Showdown
                    // (we’ll pick them up when building showdown list)
                }
            }

            foreach (var a in round.Elements("action"))
            {
                var player = (a.Attribute("player")?.Value ?? "").Trim();
                var typeCode = a.Attribute("type")?.Value?.Trim();
                var amount = ParseMoney(a.Attribute("sum")?.Value);

                var type = MapActionType(typeCode);

                // ✔ Correct: folds/checks/sitout never put chips in
                if (type is ActionType.Fold or ActionType.Check or ActionType.SitOut)
                    amount = null;

                hand.Actions.Add(new HandAction
                {
                    Street = street,
                    Player = player,
                    Type = type,
                    Amount = amount,
                    Notes = null
                });
            }

        }

        // showdown: winners + revealed pockets
        // Winners appear in players list: win="€0.15" etc.
        var playersNode = game.Element("general")?.Element("players")?.Elements("player") ?? Enumerable.Empty<XElement>();
        var winners = playersNode
            .Select(p => new
            {
                Name = (p.Attribute("name")?.Value ?? "").Trim(),
                Win = ParseMoney(p.Attribute("win")?.Value)
            })
            .Where(x => (x.Win ?? 0m) > 0m)
            .ToList();

        // revealed pockets are <cards type="Pocket" player="X">H10 DJ</cards> when shown
        var revealed = game
            .Descendants("cards")
            .Where(c => string.Equals(c.Attribute("type")?.Value, "Pocket", StringComparison.OrdinalIgnoreCase))
            .Select(c => new
            {
                Player = (c.Attribute("player")?.Value ?? "").Trim(),
                Cards = NormalizeCards((c.Value ?? "").Trim())
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Player) && !string.IsNullOrWhiteSpace(x.Cards) && x.Cards != "X X")
            .ToDictionary(x => x.Player, x => x.Cards);

        foreach (var w in winners)
        {
            hand.Showdown.Add(new ShowdownHand
            {
                Player = w.Name,
                Won = true,
                WonAmount = w.Win,
                HoleCards = revealed.TryGetValue(w.Name, out var cards) ? cards : null
            });
        }

        // Optional: add non-winner revealed hands too
        foreach (var kv in revealed)
        {
            if (hand.Showdown.Any(s => s.Player == kv.Key)) continue;
            hand.Showdown.Add(new ShowdownHand
            {
                Player = kv.Key,
                Won = false,
                WonAmount = 0m,
                HoleCards = kv.Value
            });
        }

        CardParser.FillBoardFromStrings(
      hand,
      hand.Flop?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [string.Empty],
      hand.Turn,
      hand.River
  );

        //1st tier stats
        hand.PreflopAggressor = hand.Actions.Where(a => a.Street == Street.Preflop).Where(a => PreFlopOperations.IsPreflopAggressive(a.Type)).Select(a => a.Player).LastOrDefault();
        var flopAggressor = FlopOperations.CalculateFlopAggressor(hand.Actions);
        hand.FlopAggressor = flopAggressor;
        var cbet = FlopOperations.GetFlopCBetResult(hand.Actions);
        hand.FlopModel.CBetOpportunityPlayer = cbet.OpportunityPlayer;
        hand.FlopModel.CBetPlayer = cbet.CBetPlayer;
        var donkBet = FlopOperations.GetFlopDonkBet(hand.Actions);
        hand.FlopModel.DonkBet = donkBet.DonkBettor;
        var foldToCBet = FlopOperations.GetFirstFoldToFlopCBet(hand.Actions);
        hand.FlopModel.FoldToFlopCBet = foldToCBet.Folder;
        //2nd tier stats
        hand.FlopModel.GetFirstCallVsFlopCBet = FlopTier2Operations.GetFirstCallVsFlopCBet(hand.Actions).Caller;
        hand.FlopModel.GetFirstRaiseVsFlopCBet = FlopTier2Operations.GetFirstRaiseVsFlopCBet(hand.Actions).Raiser;
        hand.FlopModel.GetMultiwayFlopCBetPlayer = FlopTier2Operations.GetMultiwayFlopCBetPlayer(hand.Actions);
        //3rd tier
        hand.FlopModel.GetFlopProbeBet = FlopTier3Operations.GetFlopProbeBet(hand.Actions).ProbeBettor;
        if (hand.Board is not null && hand.Board.Flop.Count()==3)
            hand.FlopModel.FlopTexture = FlopTier3Operations.ClassifyFlopTexture(hand.Board.Flop);


        return hand;
    }

    private static Street DetectStreet(XElement round)
    {
        // Prefer the explicit <cards type="..."> markers
        var cardType = round.Elements("cards").Select(c => c.Attribute("type")?.Value).FirstOrDefault();
        if (string.Equals(cardType, "Flop", StringComparison.OrdinalIgnoreCase)) return Street.Flop;
        if (string.Equals(cardType, "Turn", StringComparison.OrdinalIgnoreCase)) return Street.Turn;
        if (string.Equals(cardType, "River", StringComparison.OrdinalIgnoreCase)) return Street.River;

        // Otherwise round no:
        var no = ParseInt(round.Attribute("no")?.Value) ?? -1;
        return no switch
        {
            0 => Street.Preflop, // blinds & antes are still preflop context for stats
            1 => Street.Preflop,
            _ => Street.Showdown
        };
    }

    private static ActionType MapActionType(string? code) => code switch
    {
        "0" => ActionType.Fold,
        "3" => ActionType.Call,
        "4" => ActionType.Check,
        "5" => ActionType.Bet,
        "23" => ActionType.Raise,

        "1" => ActionType.PostSmallBlind,
        "2" => ActionType.PostBigBlind,

        "7" => ActionType.AllIn,

        _ => ActionType.SitOut
    };

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
