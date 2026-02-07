using PokerAnalyzer.Domain.Cards;

public class Card
{
    public Rank Rank { get; private set; }
    public Suit Suit { get; private set; }

    public Card() { } // EF

    public Card(Rank rank, Suit suit)
    {
        Rank = rank;
        Suit = suit;
    }
}
