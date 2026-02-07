using System.Globalization;

namespace PokerAnalyzer.Domain.Cards;

public readonly record struct Card(Rank Rank, Suit Suit)
{
    public override string ToString() => $"{RankToChar(Rank)}{SuitToChar(Suit)}";

    public static Card Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Card text is empty.");

        text = text.Trim();
        if (text.Length != 2)
            throw new FormatException("Card must be 2 characters like 'As' or 'Td'.");

        return new Card(ParseRank(text[0]), ParseSuit(text[1]));
    }

    public static bool TryParse(string text, out Card card)
    {
        try { card = Parse(text); return true; }
        catch { card = default; return false; }
    }

    private static Rank ParseRank(char c)
        => char.ToUpperInvariant(c) switch
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
            _ => throw new FormatException($"Invalid rank character '{c}'.")
        };

    private static Suit ParseSuit(char c)
        => char.ToLowerInvariant(c) switch
        {
            'c' => Suit.Clubs,
            'd' => Suit.Diamonds,
            'h' => Suit.Hearts,
            's' => Suit.Spades,
            _ => throw new FormatException($"Invalid suit character '{c}'.")
        };

    private static char RankToChar(Rank r) => r switch
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
        _ => throw new ArgumentOutOfRangeException(nameof(r))
    };

    private static char SuitToChar(Suit s) => s switch
    {
        Suit.Clubs => 'c',
        Suit.Diamonds => 'd',
        Suit.Hearts => 'h',
        Suit.Spades => 's',
        _ => throw new ArgumentOutOfRangeException(nameof(s))
    };
}
