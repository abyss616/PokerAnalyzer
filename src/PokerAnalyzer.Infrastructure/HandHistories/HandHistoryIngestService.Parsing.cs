using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Domain.Helpers;
using PokerAnalyzer.Infrastructure.Helpers;
using System.Xml.Linq;

public sealed partial class HandHistoryIngestService
{
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
        var actionSequenceNumber = 0;
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
                    SequenceNumber = actionSequenceNumber++,
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
        var orderedActions = hand.Actions.OrderBy(a => a.SequenceNumber).ToList();
        hand.PreflopAggressor = orderedActions.Where(a => a.Street == Street.Preflop).Where(a => PreFlopOperations.IsPreflopAggressive(a.Type)).Select(a => a.Player).LastOrDefault();
        var flopAggressor = FlopOperations.CalculateFlopAggressor(orderedActions);
        hand.FlopAggressor = flopAggressor;

        return hand;
    }

    private static Street DetectStreet(XElement round)
    {
        // Prefer the explicit <cards type="..."> markers
        var cardType = round.Elements("cards").Select(c => c.Attribute("type")?.Value).FirstOrDefault();
        if (string.Equals(cardType, "Flop", StringComparison.OrdinalIgnoreCase)) return Street.Flop;
        if (string.Equals(cardType, "Turn", StringComparison.OrdinalIgnoreCase)) return Street.Turn;
        if (string.Equals(cardType, "River", StringComparison.OrdinalIgnoreCase)) return Street.River;
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
}
