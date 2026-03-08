namespace PokerAnalyzer.Domain.Cards;

public readonly record struct HoleCards(Card First, Card Second)
{
    public override string ToString() => $"{First}{Second}";

    public static HoleCards Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Hole cards text is empty.");

        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (text.Length != 4)
            throw new FormatException("Hole cards must be 4 characters like 'AsKh'.");

        var c1 = Card.Parse(text[..2]);
        var c2 = Card.Parse(text[2..4]);

        if (c1 == c2)
            throw new FormatException("Hole cards cannot contain duplicates.");

        return new HoleCards(c1, c2);
    }
}
