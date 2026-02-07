public sealed class OpponentProfile
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public HandHistorySession Session { get; set; } = null!;

    public string Player { get; set; } = "";

    public int Hands { get; set; }

    public int VpipHands { get; set; }
    public int PfrHands { get; set; }
    public int ThreeBetHands { get; set; }
    public int FacedThreeBetHands { get; set; }
    public int FoldToThreeBetHands { get; set; }

    public int SawFlop { get; set; }
    public int WentToShowdown { get; set; }
    public int WonAtShowdown { get; set; }

    public int FlopCBetOpportunities { get; set; }
    public int FlopCBets { get; set; }
    public int FoldToFlopCBetOpportunities { get; set; }
    public int FoldToFlopCBet { get; set; }

    // Optional: by-position dictionaries
    public List<PositionStats> ByPosition { get; set; } = new();
}

public sealed class PositionStats
{
    public PositionEnum Position { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OpponentProfileId { get; set; }
    public OpponentProfile OpponentProfile { get; set; } = null!;
    public enum PositionEnum { UTG, UTG1, UTG2, LJ, HJ, CO, BTN, SB, BB }
    public int Hands { get; set; }
    public int Vpip { get; set; }
    public int Pfr { get; set; }
    public int ThreeBet { get; set; }
}