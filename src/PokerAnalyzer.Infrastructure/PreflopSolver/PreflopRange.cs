using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Infrastructure.PreflopSolver;

public readonly record struct Combo(Card First, Card Second)
{
    public bool Intersects(IReadOnlySet<Card> deadCards) => deadCards.Contains(First) || deadCards.Contains(Second);
    public override string ToString() => $"{First}{Second}";
}

public readonly record struct HandClass(string Label, Rank High, Rank Low, bool? Suited)
{
    public static HandClass Parse(string label)
    {
        var normalized = label.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 3)
            throw new FormatException($"Invalid hand class: {label}");

        var high = ParseRank(normalized[0]);
        var low = ParseRank(normalized[1]);
        bool? suited = normalized.Length == 2 ? null : normalized[2] switch
        {
            'S' => true,
            'O' => false,
            _ => throw new FormatException($"Invalid hand class: {label}")
        };

        return new HandClass(normalized, high, low, suited);
    }

    public int ComboWeight => High == Low ? 6 : Suited == true ? 4 : 12;

    private static Rank ParseRank(char c) => c switch
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
        _ => throw new FormatException($"Invalid rank in hand class: {c}")
    };
}

public static class PreflopRange
{
    private static readonly Suit[] Suits = Enum.GetValues<Suit>();
    private static readonly IReadOnlyList<HandClass> Classes = BuildClasses();

    public static IReadOnlyList<HandClass> AllClasses => Classes;

    public static IReadOnlyList<Combo> ExpandToCombos(HandClass handClass)
    {
        var combos = new List<Combo>(handClass.ComboWeight);

        if (handClass.High == handClass.Low)
        {
            for (var i = 0; i < Suits.Length; i++)
            for (var j = i + 1; j < Suits.Length; j++)
                combos.Add(new Combo(new Card(handClass.High, Suits[i]), new Card(handClass.Low, Suits[j])));

            return combos;
        }

        if (handClass.Suited == true)
        {
            foreach (var suit in Suits)
                combos.Add(new Combo(new Card(handClass.High, suit), new Card(handClass.Low, suit)));

            return combos;
        }

        foreach (var s1 in Suits)
        foreach (var s2 in Suits)
        {
            if (s1 == s2)
                continue;
            combos.Add(new Combo(new Card(handClass.High, s1), new Card(handClass.Low, s2)));
        }

        return combos;
    }

    public static Dictionary<Combo, double> BuildUniformComboDistribution(IReadOnlyCollection<HandClass> classes, IReadOnlySet<Card>? deadCards = null)
    {
        var weights = new Dictionary<Combo, double>();
        foreach (var handClass in classes)
        foreach (var combo in ExpandToCombos(handClass))
        {
            if (deadCards is not null && combo.Intersects(deadCards))
                continue;
            weights[combo] = 1d;
        }

        var total = weights.Values.Sum();
        if (total <= 0)
            return new Dictionary<Combo, double>();

        return weights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / total);
    }

    public static Dictionary<HandClass, double> BuildClassDistribution(IReadOnlySet<Card>? deadCards = null)
    {
        var classWeights = new Dictionary<HandClass, double>();
        foreach (var handClass in Classes)
        {
            var alive = ExpandToCombos(handClass).Count(c => deadCards is null || !c.Intersects(deadCards));
            if (alive > 0)
                classWeights[handClass] = alive;
        }

        var total = classWeights.Values.Sum();
        return classWeights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / total);
    }

    private static IReadOnlyList<HandClass> BuildClasses()
    {
        var ranks = Enum.GetValues<Rank>().OrderByDescending(r => (int)r).ToArray();
        var list = new List<HandClass>(169);
        for (var i = 0; i < ranks.Length; i++)
        for (var j = 0; j < ranks.Length; j++)
        {
            if (i == j)
            {
                list.Add(new HandClass($"{RankChar(ranks[i])}{RankChar(ranks[j])}", ranks[i], ranks[j], null));
            }
            else if (i < j)
            {
                list.Add(new HandClass($"{RankChar(ranks[i])}{RankChar(ranks[j])}S", ranks[i], ranks[j], true));
            }
            else
            {
                list.Add(new HandClass($"{RankChar(ranks[j])}{RankChar(ranks[i])}O", ranks[j], ranks[i], false));
            }
        }

        return list.DistinctBy(h => h.Label).ToArray();
    }

    private static char RankChar(Rank rank) => rank switch
    {
        Rank.Two => '2', Rank.Three => '3', Rank.Four => '4', Rank.Five => '5', Rank.Six => '6', Rank.Seven => '7',
        Rank.Eight => '8', Rank.Nine => '9', Rank.Ten => 'T', Rank.Jack => 'J', Rank.Queen => 'Q', Rank.King => 'K', Rank.Ace => 'A',
        _ => '?'
    };
}
