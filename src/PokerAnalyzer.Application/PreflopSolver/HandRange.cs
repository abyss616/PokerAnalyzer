using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Application.PreflopSolver;

public static class HandRange
{
    private static readonly Suit[] Suits = [Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades];
    private static readonly Rank[] Ranks = Enum.GetValues<Rank>();

    public static IReadOnlyList<Combo> AllCombos { get; } = BuildAllCombos();
    public static IReadOnlyList<string> AllClasses { get; } = BuildClasses();

    public static IReadOnlyList<Combo> ClassToCombos(string handClass)
    {
        if (handClass.Length < 2)
            throw new ArgumentException("Invalid hand class.", nameof(handClass));

        var r1 = ParseRank(handClass[0]);
        var r2 = ParseRank(handClass[1]);
        var suitedness = handClass.Length == 3 ? handClass[2] : 'p';

        var result = new List<Combo>();
        if (r1 == r2)
        {
            for (var i = 0; i < Suits.Length; i++)
                for (var j = i + 1; j < Suits.Length; j++)
                    result.Add(new Combo(new Card(r1, Suits[i]), new Card(r2, Suits[j])));
            return result;
        }

        foreach (var s1 in Suits)
        {
            foreach (var s2 in Suits)
            {
                var suited = s1 == s2;
                if (suitedness == 's' && !suited) continue;
                if (suitedness == 'o' && suited) continue;
                result.Add(new Combo(new Card(r1, s1), new Card(r2, s2)));
            }
        }

        return result;
    }

    public static string ComboToClass(Combo combo)
    {
        var pair = combo.C1.Rank == combo.C2.Rank;
        var (hi, lo) = combo.C1.Rank >= combo.C2.Rank
            ? (combo.C1.Rank, combo.C2.Rank)
            : (combo.C2.Rank, combo.C1.Rank);

        if (pair)
            return $"{RankChar(hi)}{RankChar(lo)}";

        var suited = combo.C1.Suit == combo.C2.Suit;
        return $"{RankChar(hi)}{RankChar(lo)}{(suited ? 's' : 'o')}";
    }

    public static IReadOnlyDictionary<Combo, decimal> UniformDistribution(IEnumerable<Combo> combos)
    {
        var list = combos.Distinct().ToList();
        var p = 1m / list.Count;
        return list.ToDictionary(c => c, _ => p);
    }

    public static IReadOnlyDictionary<Combo, decimal> RemoveDeadCards(
        IReadOnlyDictionary<Combo, decimal> distribution,
        IEnumerable<Card> deadCards)
    {
        var dead = deadCards.ToHashSet();
        var filtered = distribution.Where(kv => !dead.Contains(kv.Key.C1) && !dead.Contains(kv.Key.C2)).ToList();
        var sum = filtered.Sum(x => x.Value);
        if (sum <= 0) return new Dictionary<Combo, decimal>();
        return filtered.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }

    private static List<Combo> BuildAllCombos()
    {
        var deck = new List<Card>(52);
        foreach (var r in Ranks)
            foreach (var s in Suits)
                deck.Add(new Card(r, s));

        var result = new List<Combo>(1326);
        for (var i = 0; i < deck.Count; i++)
            for (var j = i + 1; j < deck.Count; j++)
                result.Add(new Combo(deck[i], deck[j]));

        return result;
    }

    private static List<string> BuildClasses()
    {
        var desc = Ranks.OrderByDescending(x => x).ToArray();
        var classes = new List<string>(169);
        for (var i = 0; i < desc.Length; i++)
        {
            for (var j = 0; j < desc.Length; j++)
            {
                if (i == j) classes.Add($"{RankChar(desc[i])}{RankChar(desc[j])}");
                else if (i < j) classes.Add($"{RankChar(desc[i])}{RankChar(desc[j])}s");
                else classes.Add($"{RankChar(desc[j])}{RankChar(desc[i])}o");
            }
        }

        return classes.Distinct().OrderBy(x => x.Length).ThenBy(x => x).ToList();
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
        _ => throw new ArgumentOutOfRangeException(nameof(c))
    };

    private static char RankChar(Rank r) => r switch
    {
        Rank.Two => '2', Rank.Three => '3', Rank.Four => '4', Rank.Five => '5', Rank.Six => '6',
        Rank.Seven => '7', Rank.Eight => '8', Rank.Nine => '9', Rank.Ten => 'T', Rank.Jack => 'J',
        Rank.Queen => 'Q', Rank.King => 'K', Rank.Ace => 'A', _ => '?'
    };
}
