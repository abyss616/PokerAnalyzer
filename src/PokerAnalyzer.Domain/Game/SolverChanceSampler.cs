using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Domain.Game;

public sealed class SolverChanceSampler : IChanceSampler
{
    public bool IsChanceNode(SolverHandState state)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        if (IsTerminalState(state))
            return false;

        if (HasPendingPlayerDecision(state))
            return false;

        return GetBoardCardsToDeal(state) > 0;
    }

    public SolverHandState Sample(SolverHandState state, Random rng)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        if (!IsChanceNode(state))
            return state;

        var availableDeck = BuildDeckExcludingKnown(state);

        var privateCardsByPlayer = new Dictionary<PlayerId, HoleCards>(state.PrivateCardsByPlayer);
        DealMissingPrivateCards(state, privateCardsByPlayer, availableDeck, rng);

        var boardCards = state.BoardCards.ToList();
        var cardsToDeal = GetBoardCardsToDeal(state);
        if (cardsToDeal > 0)
            DealBoardCards(boardCards, availableDeck, cardsToDeal, rng);

        return state.With(
            boardCards: boardCards,
            privateCardsByPlayer: privateCardsByPlayer,
            street: AdvanceStreetForBoardCount(state.Street, cardsToDeal));
    }

    private static void DealMissingPrivateCards(
        SolverHandState state,
        IDictionary<PlayerId, HoleCards> privateCardsByPlayer,
        IList<Card> availableDeck,
        Random rng)
    {
        var missingPlayers = GetPlayersMissingPrivateCards(state);
        foreach (var player in missingPlayers)
        {
            if (availableDeck.Count < 2)
                throw new InvalidOperationException("Not enough cards left in deck to deal private cards.");

            var first = DrawRandomCard(availableDeck, rng);
            var second = DrawRandomCard(availableDeck, rng);
            privateCardsByPlayer[player.PlayerId] = new HoleCards(first, second);
        }
    }

    private static void DealBoardCards(ICollection<Card> boardCards, IList<Card> availableDeck, int cardsToDeal, Random rng)
    {
        if (availableDeck.Count < cardsToDeal)
            throw new InvalidOperationException("Not enough cards left in deck to deal board cards.");

        for (var i = 0; i < cardsToDeal; i++)
            boardCards.Add(DrawRandomCard(availableDeck, rng));
    }

    private static List<SolverPlayerState> GetPlayersMissingPrivateCards(SolverHandState state)
        => state.Players
            .Where(player => player.IsActive && !state.PrivateCardsByPlayer.ContainsKey(player.PlayerId))
            .OrderBy(player => player.SeatIndex)
            .ToList();

    private static bool HasPendingPlayerDecision(SolverHandState state)
    {
        if (IsAwaitingBoardChance(state))
            return false;

        return state.GenerateLegalActions().Count > 0;
    }

    private static int GetBoardCardsToDeal(SolverHandState state)
    {
        if (!IsAwaitingBoardChance(state))
            return 0;

        return state.Street switch
        {
            Street.Preflop when state.BoardCards.Count == 0 => 3,
            Street.Flop when state.BoardCards.Count == 3 => 1,
            Street.Turn when state.BoardCards.Count == 4 => 1,
            _ => 0
        };
    }

    private static bool IsAwaitingBoardChance(SolverHandState state)
    {
        if (state.CurrentBetSize.Value != 0 || state.RaisesThisStreet != 0)
            return false;

        return state.Players.All(player => player.CurrentStreetContribution.Value == 0);
    }

    private static bool IsTerminalState(SolverHandState state)
        => state.Players.Count(player => player.IsActive) <= 1;

    private static Street AdvanceStreetForBoardCount(Street currentStreet, int boardCardsDealt)
    {
        if (boardCardsDealt == 0)
            return currentStreet;

        return currentStreet switch
        {
            Street.Preflop => Street.Flop,
            Street.Flop => Street.Turn,
            Street.Turn => Street.River,
            _ => currentStreet
        };
    }

    private static Card DrawRandomCard(IList<Card> availableDeck, Random rng)
    {
        var idx = rng.Next(availableDeck.Count);
        var card = availableDeck[idx];
        availableDeck.RemoveAt(idx);
        return card;
    }

    private static List<Card> BuildDeckExcludingKnown(SolverHandState state)
    {
        var excluded = new HashSet<Card>(state.DeadCards);
        foreach (var card in state.BoardCards)
            excluded.Add(card);

        foreach (var privateCards in state.PrivateCardsByPlayer.Values)
        {
            excluded.Add(privateCards.First);
            excluded.Add(privateCards.Second);
        }

        var deck = new List<Card>(52 - excluded.Count);
        foreach (Rank rank in Enum.GetValues<Rank>())
        {
            foreach (Suit suit in Enum.GetValues<Suit>())
            {
                var card = new Card(rank, suit);
                if (!excluded.Contains(card))
                    deck.Add(card);
            }
        }

        return deck;
    }
}
