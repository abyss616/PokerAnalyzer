using Microsoft.EntityFrameworkCore;

public sealed class PlayerProfile
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public HandHistorySession Session { get; set; } = null!;

    public string Player { get; set; } = "";

    public int Hands { get; set; }

    public PreflopStats PreflopModel { get; set; } = new();


    public FlopStatsByPosition FlopModel { get; set; } = new();
    public TurnStatsByPosition TurnModel { get; set; } = new();
    public RiverStatsByPosition RiverModel { get; set; } = new();

    // Optional: by-position dictionaries
    public List<PositionStats> ByPosition { get; set; } = new();
}

public sealed class PositionStats
{
    public enum PositionEnum { UTG, HJ, CO, BTN, SB, BB }

    public Guid Id { get; set; }
    public PositionEnum Position { get; set; }
    public Guid PlayerProfileId { get; set; }
    public PlayerProfile PlayerProfile { get; set; } = null!;
}


#region Preflop

[Owned]
public sealed class PreflopStats
{
    public PositionPreflopStatsByPosition Positions { get; set; } = new();
}

[Owned]
public sealed class PositionPreflopStatsByPosition
{
    public PositionPreflopStats Utg { get; set; } = new();
    public PositionPreflopStats Hj { get; set; } = new();
    public PositionPreflopStats Co { get; set; } = new();
    public PositionPreflopStats Btn { get; set; } = new();
    public PositionPreflopStats Sb { get; set; } = new();
    public PositionPreflopStats Bb { get; set; } = new();
}

[Owned]
public sealed class PositionPreflopStats
{
    public int VpipHands { get; set; }
    public int PfrHands { get; set; }
    public int ThreeBetHands { get; set; }
    public int FacedThreeBetHands { get; set; }
    public int FoldToThreeBetHands { get; set; }
    public int FacedLateOpenHands { get; set; }
    public int FoldedVsLateOpenHands { get; set; }
    public int FirstInHands { get; set; }
    public int RaisedFirstInHands { get; set; }
}

#endregion

#region Flop

[Owned]
public sealed class FlopStatsByPosition
{
    public PositionFlopStatsByPosition Positions { get; set; } = new();
}

[Owned]
public sealed class PositionFlopStatsByPosition
{
    public FlopStats Utg { get; set; } = new();
    public FlopStats Hj { get; set; } = new();
    public FlopStats Co { get; set; } = new();
    public FlopStats Btn { get; set; } = new();
    public FlopStats Sb { get; set; } = new();
    public FlopStats Bb { get; set; } = new();
}

[Owned]
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

#endregion

#region Turn

[Owned]
public sealed class TurnStatsByPosition
{
    public PositionTurnStatsByPosition Positions { get; set; } = new();
}

[Owned]
public sealed class PositionTurnStatsByPosition
{
    public TurnStats Utg { get; set; } = new();
    public TurnStats Hj { get; set; } = new();
    public TurnStats Co { get; set; } = new();
    public TurnStats Btn { get; set; } = new();
    public TurnStats Sb { get; set; } = new();
    public TurnStats Bb { get; set; } = new();
}

[Owned]
public sealed class TurnStats
{
    public int SawTurn { get; set; }
    public int WentToShowdown { get; set; }
    public int WonAtShowdown { get; set; }
    public int TurnCBet { get; set; }
    public int TurnCheck { get; set; }
    public int TurnFoldToBet { get; set; }
    public decimal TurnAggressionFactor { get; set; }
    public decimal TurnBetSizePercentPot { get; set; }
    public int TurnRaiseVsBet { get; set; }
    public int TurnWTSDCarryover { get; set; }
}

#endregion

#region River

[Owned]
public sealed class RiverStatsByPosition
{
    public PositionRiverStatsByPosition Positions { get; set; } = new();
}

[Owned]
public sealed class PositionRiverStatsByPosition
{
    public RiverStats Utg { get; set; } = new();
    public RiverStats Hj { get; set; } = new();
    public RiverStats Co { get; set; } = new();
    public RiverStats Btn { get; set; } = new();
    public RiverStats Sb { get; set; } = new();
    public RiverStats Bb { get; set; } = new();
}

[Owned]
public sealed class RiverStats
{
    public int SawRiver { get; set; }
    public int WentToShowdown { get; set; }
    public int WonAtShowdown { get; set; }
    public int RiverBetOpportunities { get; set; }
    public int RiverBetsWhenCheckedTo { get; set; }
    public int RiverFacedBet { get; set; }
    public int RiverCallsVsBet { get; set; }
    public int RiverFoldToBet { get; set; }
    public int RiverRaiseVsBet { get; set; }
    public decimal RiverAggressionFactor { get; set; }
    public decimal RiverBetSizePercentPot { get; set; }
}

#endregion
