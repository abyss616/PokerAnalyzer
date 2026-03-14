namespace PokerAnalyzer.Domain.Cards;

public readonly record struct HoleCards(Card First, Card Second)
{
    public override string ToString() => $"{First}{Second}";

    public static HoleCards Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Hole cards text is empty.");

        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        var firstLength = DetectCardLength(text, 0);
        var secondStart = firstLength;
        var secondLength = DetectCardLength(text, secondStart);

        if (secondStart + secondLength != text.Length)
            throw new FormatException($"Hole cards must be two cards like 'AsKh' or 'Jc10c'. Current hand: {text}");

        var c1 = Card.Parse(NormalizeCard(text.Substring(0, firstLength)));
        var c2 = Card.Parse(NormalizeCard(text.Substring(secondStart, secondLength)));

        if (c1 == c2)
            throw new FormatException("Hole cards cannot contain duplicates.");

        return new HoleCards(c1, c2);
    }

    private static int DetectCardLength(string text, int start)
    {
        var remaining = text.Length - start;
        if (remaining < 2)
            throw new FormatException($"Hole cards must be two cards like 'AsKh' or 'Jc10c'. Current hand: {text}");

        if (remaining >= 3 && text[start] == '1' && text[start + 1] == '0')
            return 3;

        return 2;
    }

    private static string NormalizeCard(string card)
        => card.StartsWith("10", StringComparison.OrdinalIgnoreCase)
            ? $"T{card[2]}"
            : card;
}
