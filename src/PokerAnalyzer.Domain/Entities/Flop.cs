namespace PokerAnalyzer.Domain.Entities
{
    public class Flop
    {
        public Guid Id { get; set; }      // PK
        public Guid HandId { get; set; }  // FK
        public Hand Hand { get; set; } = null!;
        public string? CBetOpportunityPlayer { get; set; }
        public string? CBetPlayer { get; set; }
        public string? DonkBet { get; set; }
        public string? FoldToFlopCBet { get; set; }
        public string? GetFirstRaiseVsFlopCBet { get; set; }
        public string? GetFirstCallVsFlopCBet { get; set; }
        public string? GetMultiwayFlopCBetPlayer { get; set; }
        public string? GetFlopProbeBet { get; set; }
        public FlopTexture? FlopTexture { get; set; }
    }
}
