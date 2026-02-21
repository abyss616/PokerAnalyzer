using PokerAnalyzer.Domain.Cards;

public class Card : IEquatable<Card>
{
    public Rank Rank { get; private set; }
    public Suit Suit { get; private set; }

    public Card() { } // EF

    public Card(Rank rank, Suit suit)
    {
        Rank = rank;
        Suit = suit;
    }
    public override string ToString() => $"{RankToChar(Rank)}{SuitToChar(Suit)}";

    public bool Equals(Card? other) => other is not null && Rank == other.Rank && Suit == other.Suit;
    public override bool Equals(object? obj) => obj is Card other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Rank, (int)Suit);

    public static bool operator ==(Card? left, Card? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(Card? left, Card? right) => !(left == right);

    public static Card Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length != 2)
            throw new FormatException("Card must be 2 characters like 'As'.");

        var rank = ParseRank(text[0]);
        var suit = ParseSuit(text[1]);

        return new Card(rank, suit);
    }

    private static Rank ParseRank(char c) => char.ToUpperInvariant(c) switch
    {
        '2' => Rank.Two,
        '3' => Rank.Three,
        '4' => Rank.Four,
        '5' => Rank.Five,
        '6' => Rank.Six,
        '7' => Rank.Seven,
        '8' => Rank.Eight,
        '9' => Rank.Nine,
        'T' => Rank.Ten,
        'J' => Rank.Jack,
        'Q' => Rank.Queen,
        'K' => Rank.King,
        'A' => Rank.Ace,
        _ => throw new FormatException($"Invalid rank '{c}'.")
    };
    public static Suit ParseSuit(char c) => char.ToLowerInvariant(c) switch
    {
        'c' => Suit.Clubs,
        'd' => Suit.Diamonds,
        'h' => Suit.Hearts,
        's' => Suit.Spades,
        _ => throw new FormatException($"Invalid suit '{c}'.")
    };

    private static char RankToChar(Rank rank) => rank switch
    {
        Rank.Two => '2',
        Rank.Three => '3',
        Rank.Four => '4',
        Rank.Five => '5',
        Rank.Six => '6',
        Rank.Seven => '7',
        Rank.Eight => '8',
        Rank.Nine => '9',
        Rank.Ten => 'T',
        Rank.Jack => 'J',
        Rank.Queen => 'Q',
        Rank.King => 'K',
        Rank.Ace => 'A',
        _ => throw new ArgumentOutOfRangeException()
    };
    public static char SuitToChar(Suit suit) => suit switch
    {
        Suit.Clubs => 'c',
        Suit.Diamonds => 'd',
        Suit.Hearts => 'h',
        Suit.Spades => 's',
        _ => throw new FormatException($"Invalid suit value '{suit}'.")
    };
}
