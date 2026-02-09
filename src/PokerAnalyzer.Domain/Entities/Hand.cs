using PokerAnalyzer.Domain.Game;

public sealed class Hand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? HandNumber { get; set; }

    public long GameCode { get; set; }                 // gamecode
    public DateTime? StartedAtUtc { get; set; }        // date

    public decimal? Pot { get; set; }                  // <general pot="€0.27">
    public decimal? Rake { get; set; }                 // <general rake="€0.01">

    // Optional future expansions:
    public string? Flop { get; set; }
    public string? Turn { get; set; }
    public string? River { get; set; }
    public string? HeroHoleCards { get; set; }

    public string? ButtonPlayer { get; set; }
    public string? PreflopAggressor { get; set; }   // last raiser preflop
    public string? FlopAggressor { get; set; }

    public Board? Board { get; set; }
    public List<HandPlayer> Players { get; set; } = new();
    public List<HandAction> Actions { get; set; } = new();
    public List<ShowdownHand> Showdown { get; set; } = new();// "As Qs"
    public List<PlayerHandSummary> PlayerSummaries { get; set; } = new();

}
public sealed record FlopTexture
{
    public bool IsPaired { get; init; }
    public bool IsMonotone { get; init; }
    public bool IsTwoTone { get; init; }
    public bool IsRainbow { get; init; }
    public bool HasAceOrKing { get; init; }
    public bool HasTwoBroadways { get; init; }
}

public sealed class HandPlayer
{
    public Guid Id { get; set; } = Guid.NewGuid();  // ✅ PK

    public Guid HandId { get; set; }                // ✅ FK
    public Hand Hand { get; set; } = null!;
    public string Name { get; set; } = "";
    public int Seat { get; set; }
    public decimal? StackStart { get; set; }
    public Position PlayerPosition { get; set; }
    public enum Position
    {
        UTG, UTG1, UTG2, LJ, HJ, CO, BTN, SB, BB
    }
    public bool IsHero { get; set; }
}

public sealed class HandAction
{
    public Guid Id { get; set; } = Guid.NewGuid();   // ✅ PK
    public int ActionIndex { get; set; }

    public Guid HandId { get; set; }                 // ✅ FK
    public Hand Hand { get; set; } = null!;
    public Street Street { get; set; }
    public string Player { get; set; } = "";
    public ActionType Type { get; set; }
    public decimal? Amount { get; set; }      // bet/raise/call amount
    public decimal? ToAmount { get; set; }    // optional: raise-to sizing
    public string? Notes { get; set; }        // e.g. "3bet", "squeeze" (you can compute)
}

public sealed class ShowdownHand
{
    public Guid Id { get; set; } = Guid.NewGuid();   // ✅ PK

    public Guid HandId { get; set; }                 // ✅ FK
    public Hand Hand { get; set; } = null!;
    public string Player { get; set; } = "";
    public string? HoleCards { get; set; }    // only if revealed (e.g. "Ah Kd")
    public bool Won { get; set; }
    public decimal? WonAmount { get; set; }
}

public sealed class PlayerHandSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();   // ✅ PK

    public Guid HandId { get; set; }                 // ✅ FK
    public Hand Hand { get; set; } = null!;
    public string Player { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public decimal? StackStart { get; set; }
    public decimal? StackEnd { get; set; }
    public string? RevealedHoleCards { get; set; }
}

public sealed class RevealedHandClassification
{
    public string Player { get; set; } = "";
    public string HoleCards { get; set; } = "";
    public bool Suited { get; set; }
    public bool PocketPair { get; set; }
    public int Gap { get; set; } // 0 connector, 1 one-gapper, etc.
    public string RankBucket { get; set; } = ""; // e.g. "high", "middle", "low"
    public string? ShowdownMadeHand { get; set; } // e.g. "two_pair"
}
