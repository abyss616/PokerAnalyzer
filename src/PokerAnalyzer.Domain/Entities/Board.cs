using PokerAnalyzer.Domain.Cards;
public class Board
{
    public Guid Id { get; private set; }   // PK
    public Guid HandId { get; private set; }

    private readonly List<Card> _flop = new();
    public IReadOnlyList<Card> Flop => _flop;

    public Card? Turn { get; private set; }
    public Card? River { get; private set; }

    public bool HasFlop => _flop.Count == 3;
    public bool HasTurn => Turn is not null;
    public bool HasRiver => River is not null;

    public Board() { } // EF

    public Board(IEnumerable<Card> flop, Card? turn, Card? river)
    {
        _flop.AddRange(flop);
        Turn = turn;
        River = river;
    }
}
