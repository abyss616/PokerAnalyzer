namespace PokerAnalyzer.Domain.PreflopTree;

public sealed class PreflopPublicState
{
    public int PlayerCount { get; init; }

    public int ActingIndex { get; set; }

    public bool[] InHand { get; init; } = default!;

    public int[] ContribBb { get; init; } = default!;

    public int[] StackBb { get; init; } = default!;

    public int CurrentToCallBb { get; set; }

    public int LastRaiseToBb { get; set; }

    public int RaisesCount { get; set; }

    public int PotBb { get; set; }

    public int LastAggressorIndex { get; set; }

    public int? LastActionWasRaiseByIndex { get; set; }

    public bool BettingClosed { get; set; }

    public PreflopPublicState Clone()
    {
        return new PreflopPublicState
        {
            PlayerCount = PlayerCount,
            ActingIndex = ActingIndex,
            InHand = (bool[])InHand.Clone(),
            ContribBb = (int[])ContribBb.Clone(),
            StackBb = (int[])StackBb.Clone(),
            CurrentToCallBb = CurrentToCallBb,
            LastRaiseToBb = LastRaiseToBb,
            RaisesCount = RaisesCount,
            PotBb = PotBb,
            LastAggressorIndex = LastAggressorIndex,
            LastActionWasRaiseByIndex = LastActionWasRaiseByIndex,
            BettingClosed = BettingClosed
        };
    }
}
