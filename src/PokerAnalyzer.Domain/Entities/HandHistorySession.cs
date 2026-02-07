public sealed class HandHistorySession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // From <session sessioncode="...">
    public long SessionCode { get; set; }

    // From <general>
    public string? Game { get; set; }          // "Holdem"
    public string? Gametype { get; set; }      // "NL"
    public string? Currency { get; set; }      // "EUR"
    public decimal? SmallBlind { get; set; }
    public decimal? BigBlind { get; set; }
    public int? MaxSeats { get; set; }
    public bool? RealMoney { get; set; }
    public string? Nickname { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public int? HandCount { get; set; }
    public decimal? TotalBets { get; set; }
    public decimal? TotalWin { get; set; }
    public decimal? Result { get; set; }

    // Raw upload tracking
    public string OriginalFileName { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public string RawXml { get; set; } = ""; // store raw; later you can move to blob if desired
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Hand> Hands { get; set; } = new();
    public List<OpponentProfile> Opponents { get; set; } = new();
}
