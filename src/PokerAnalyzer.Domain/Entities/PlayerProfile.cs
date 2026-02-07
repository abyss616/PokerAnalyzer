public sealed class PlayerProfile
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public HandHistorySession Session { get; set; } = null!;

    public string Player { get; set; } = "";

    public int Hands { get; set; }

    public PreflopStats PreflopModel { get; set; } = new();

    public int SawFlop { get; set; }
    public int WentToShowdown { get; set; }
    public int WonAtShowdown { get; set; }

    public FlopStats FlopModel { get; set; } = new();
    public TurnStats TurnModel { get; set; } = new();
    public RiverStats RiverModel { get; set; } = new();

    // Optional: by-position dictionaries
    public List<PositionStats> ByPosition { get; set; } = new();
}

public sealed class PositionStats
{
    public PositionEnum Position { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
    public enum PositionEnum { UTG, UTG1, UTG2, LJ, HJ, CO, BTN, SB, BB }
    public int Hands { get; set; }
    public int Vpip { get; set; }
    public int Pfr { get; set; }
    public int ThreeBet { get; set; }
}

public sealed class PreflopStats
{
    public int VpipHands { get; set; }
    public int PfrHands { get; set; }
    public int ThreeBetHands { get; set; }
    public int FacedThreeBetHands { get; set; }
    public int FoldToThreeBetHands { get; set; }
}


public sealed class FlopStats
{
    public int SawFlop { get; set; }
    public int WentToShowdown { get; set; }
    public int WonAtShowdown { get; set; }
    public int CBetOpportunities { get; set; }
    public int CBets { get; set; }
    public int FoldToCBetOpportunities { get; set; }
    public int FoldToCBet { get; set; }
    public int DonkBets { get; set; }
    public int FirstFoldToCBet { get; set; }
    public int CallVsCBet { get; set; }
    public int RaiseVsCBet { get; set; }
    public int MultiwayCBets { get; set; }
    public int ProbeBets { get; set; }
}

public sealed class TurnStats
{
    public int SawTurn { get; set; }
    public int WentToShowdown { get; set; }
    public int CBetOpportunities { get; set; }
    public int CBets { get; set; }
    public int CheckOpportunities { get; set; }
    public int Checks { get; set; }
    public int FoldToBetOpportunities { get; set; }
    public int FoldToBet { get; set; }
    public int TurnBetCount { get; set; }
    public int TurnRaiseCount { get; set; }
    public int TurnCallCount { get; set; }
    public decimal TurnBetSizePotSum { get; set; }
    public int TurnBetSizeSamples { get; set; }
    public int RaiseVsBetOpportunities { get; set; }
    public int RaiseVsBet { get; set; }
}

public sealed class RiverStats
{
    public int SawRiver { get; set; }
    public int WentToShowdown { get; set; }
    public int WonAtShowdown { get; set; }
    public int CBetOpportunities { get; set; }
    public int CBets { get; set; }
    public int FoldToCBetOpportunities { get; set; }
    public int FoldToCBet { get; set; }
    public int DonkBets { get; set; }
    public int FirstFoldToCBet { get; set; }
    public int CallVsCBet { get; set; }
    public int RaiseVsCBet { get; set; }
    public int MultiwayCBets { get; set; }
    public int ProbeBets { get; set; }
}
