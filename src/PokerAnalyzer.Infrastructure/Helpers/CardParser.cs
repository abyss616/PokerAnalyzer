using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Domain.Helpers
{
    public static class CardParser
    {
        private static Card Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Card value is null/empty.", nameof(value));

            value = value.Trim();

            // Supported inputs:
            // Rank+Suit: "Td", "As", "10d"
            // Suit+Rank: "dT", "SA", "D10"
            if (value.Length is < 2 or > 3)
                throw new ArgumentException($"Invalid card value '{value}'");

            // Helper locals
            static bool IsSuitChar(char c) => c is 'c' or 'd' or 'h' or 's' or 'C' or 'D' or 'H' or 'S';

            // Determine if suit is first or last
            if (IsSuitChar(value[0]))
            {
                // Suit first: "D10", "HQ", "sa"
                var suitChar = value[0];
                var rankStr = value.Substring(1); // "10" or "Q" etc
                return new Card(ParseRank(rankStr), ParseSuit(suitChar));
            }
            else
            {
                // Rank first: "10d", "Td", "As"
                var suitChar = value[^1];
                var rankStr = value.Substring(0, value.Length - 1); // "10" or "T" etc
                return new Card(ParseRank(rankStr), ParseSuit(suitChar));
            }
        }

        private static Rank ParseRank(string s) => s switch
        {
            "2" => Rank.Two,
            "3" => Rank.Three,
            "4" => Rank.Four,
            "5" => Rank.Five,
            "6" => Rank.Six,
            "7" => Rank.Seven,
            "8" => Rank.Eight,
            "9" => Rank.Nine,
            "T" or "t" => Rank.Ten,
            "10" => Rank.Ten,
            "J" or "j" => Rank.Jack,
            "Q" or "q" => Rank.Queen,
            "K" or "k" => Rank.King,
            "A" or "a" => Rank.Ace,
            _ => throw new ArgumentException($"Invalid rank '{s}'")
        };

        private static Suit ParseSuit(char c) => c switch
        {
            'c' or 'C' => Suit.Clubs,
            'd' or 'D' => Suit.Diamonds,
            'h' or 'H' => Suit.Hearts,
            's' or 'S' => Suit.Spades,
            _ => throw new ArgumentException($"Invalid suit '{c}'")
        };

        public static void FillBoardFromStrings(
           Hand hand,
           IReadOnlyList<string> flop,
           string? turn,
           string? river)
        {
            if (hand is null)
                throw new ArgumentNullException(nameof(hand));

            if (flop is null || flop.Count != 3)
            {
                hand.Board = new Board();
                return;
            }

            var flopCards = flop.Select(CardParser.Parse).ToArray();

            hand.Board = new Board(
                flopCards,
                turn is not null ? CardParser.Parse(turn) : null,
                river is not null ? CardParser.Parse(river) : null
            );
        }
    }
}
