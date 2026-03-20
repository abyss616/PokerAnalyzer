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
        => SampleWithProbability(state, rng).NextState;

    public ChanceSampleResult SampleWithProbability(SolverHandState state, Random rng)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        if (!IsChanceNode(state))
            return new ChanceSampleResult(state, 1d);

        var availableDeck = BuildDeckExcludingKnown(state);
        var samplingProbability = 1d;

        var privateCardsByPlayer = new Dictionary<PlayerId, HoleCards>(state.PrivateCardsByPlayer);
        samplingProbability *= DealMissingPrivateCards(state, privateCardsByPlayer, availableDeck, rng);

        var boardCards = state.BoardCards.ToList();
        var cardsToDeal = GetBoardCardsToDeal(state);
        if (cardsToDeal > 0)
            samplingProbability *= DealBoardCards(boardCards, availableDeck, cardsToDeal, rng);

        var nextState = state.With(
            boardCards: boardCards,
            privateCardsByPlayer: privateCardsByPlayer,
            street: AdvanceStreetForBoardCount(state.Street, cardsToDeal));

        return new ChanceSampleResult(nextState, samplingProbability);
    }

    private static double DealMissingPrivateCards(
        SolverHandState state,
        IDictionary<PlayerId, HoleCards> privateCardsByPlayer,
        IList<Card> availableDeck,
        Random rng)
    {
        var missingPlayers = GetPlayersMissingPrivateCards(state);
        var probability = 1d;

        foreach (var player in missingPlayers)
        {
            if (availableDeck.Count < 2)
                throw new InvalidOperationException("Not enough cards left in deck to deal private cards.");

            var first = DrawRandomCard(availableDeck, rng, out var firstProbability);
            var second = DrawRandomCard(availableDeck, rng, out var secondProbability);
            probability *= firstProbability * secondProbability;
            privateCardsByPlayer[player.PlayerId] = new HoleCards(first, second);
        }

        return probability;
    }

    private static double DealBoardCards(ICollection<Card> boardCards, IList<Card> availableDeck, int cardsToDeal, Random rng)
    {
        if (availableDeck.Count < cardsToDeal)
            throw new InvalidOperationException("Not enough cards left in deck to deal board cards.");

        var probability = 1d;
        for (var i = 0; i < cardsToDeal; i++)
        {
            boardCards.Add(DrawRandomCard(availableDeck, rng, out var cardProbability));
            probability *= cardProbability;
        }

        return probability;
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
        if (!HasValidBoardCountForStreet(state))
            return false;

        if (state.CurrentBetSize.Value != 0 || state.RaisesThisStreet != 0)
            return false;

        if (!state.Players.All(player => player.CurrentStreetContribution.Value == 0))
            return false;

        if (!AllActivePlayersHavePrivateCards(state))
            return false;

        return state.Street switch
        {
            Street.Preflop => IsCompletedPreflopSnapshot(state),
            Street.Flop => HasPostBlindAction(state),
            Street.Turn => HasPostBlindAction(state),
            _ => false
        };
    }

    private static bool HasValidBoardCountForStreet(SolverHandState state)
        => state.Street switch
        {
            Street.Preflop => state.BoardCards.Count == 0,
            Street.Flop => state.BoardCards.Count == 3,
            Street.Turn => state.BoardCards.Count == 4,
            _ => false
        };

    private static bool AllActivePlayersHavePrivateCards(SolverHandState state)
        => state.Players
            .Where(player => player.IsActive)
            .All(player => state.PrivateCardsByPlayer.ContainsKey(player.PlayerId));

    private static bool IsCompletedPreflopSnapshot(SolverHandState state)
    {
        var hasPostedSmallBlind = state.ActionHistory.Any(a => a.ActionType == ActionType.PostSmallBlind);
        var hasPostedBigBlind = state.ActionHistory.Any(a => a.ActionType == ActionType.PostBigBlind);
        if (!hasPostedSmallBlind || !hasPostedBigBlind)
            return false;

        if (!HasPostBlindAction(state))
            return false;

        var forcedBlinds = state.Config.SmallBlind + state.Config.BigBlind;
        return state.Pot >= forcedBlinds;
    }

    private static bool HasPostBlindAction(SolverHandState state)
        => state.ActionHistory.Any(a => a.ActionType is not ActionType.PostSmallBlind and not ActionType.PostBigBlind);

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

    private static Card DrawRandomCard(IList<Card> availableDeck, Random rng, out double samplingProbability)
    {
        var countBeforeDraw = availableDeck.Count;
        var idx = rng.Next(countBeforeDraw);
        var card = availableDeck[idx];
        availableDeck.RemoveAt(idx);
        samplingProbability = countBeforeDraw > 0 ? 1d / countBeforeDraw : 0d;
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
