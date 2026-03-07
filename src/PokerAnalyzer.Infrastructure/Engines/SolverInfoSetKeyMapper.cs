using System.Globalization;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class SolverInfoSetKeyMapper
{
    private readonly PreflopStateExtractor _preflopExtractor;

    public SolverInfoSetKeyMapper() : this(new PreflopStateExtractor())
    {
    }

    internal SolverInfoSetKeyMapper(PreflopStateExtractor preflopExtractor)
    {
        _preflopExtractor = preflopExtractor;
    }

    public SolverInfoSetMappingResult Map(SolverHandState state)
    {
        if (!state.PrivateCardsByPlayer.TryGetValue(state.ActingPlayerId, out var actingHoleCards))
            return SolverInfoSetMappingResult.Unsupported("Acting player's hole cards are required to build an information set key.");

        if (state.Street == Street.Preflop)
            return MapPreflop(state, actingHoleCards);

        // Postflop fallback keeps the mapper extensible while avoiding overbuilding.
        // It intentionally includes only actor-visible information.
        var actingPosition = state.Players.First(p => p.PlayerId == state.ActingPlayerId).Position;
        var key = SolverInfoSetKey.CreatePostflop(
            actingPosition,
            actingHoleCards,
            state.BoardCards,
            state.ActionHistory,
            state.Pot,
            state.CurrentBetSize,
            state.ToCall,
            state.RaisesThisStreet,
            state.Street,
            state.Players,
            state.ActingPlayerId);

        return SolverInfoSetMappingResult.Supported(key, preflopKey: null);
    }

    private SolverInfoSetMappingResult MapPreflop(SolverHandState state, HoleCards actingHoleCards)
    {
        var seats = state.Players.Select(p => new PlayerSeat(
            p.PlayerId,
            p.PlayerId.Value.ToString("N"),
            p.SeatIndex,
            p.Position,
            p.Stack + p.TotalContribution)).ToList();

        var actions = state.ActionHistory.Select(x => new PreflopInputAction(
            x.PlayerId,
            ToPreflopActionType(x.ActionType),
            ToBigBlinds(x.Amount, state.Config.BigBlind))).ToList();

        var extraction = _preflopExtractor.TryExtract(
            seats,
            actions,
            state.ActingPlayerId,
            state.Config.SmallBlind.Value,
            state.Config.BigBlind.Value);

        if (!extraction.IsSupported || extraction.Key is null)
            return SolverInfoSetMappingResult.Unsupported(extraction.UnsupportedReason ?? "Preflop key extraction is unsupported.");

        var key = SolverInfoSetKey.CreatePreflop(extraction.Key, actingHoleCards);
        return SolverInfoSetMappingResult.Supported(key, extraction.Key);
    }

    private static string ToPreflopActionType(ActionType type)
        => type switch
        {
            ActionType.Fold => "FOLD",
            ActionType.Check => "CHECK",
            ActionType.Call => "CALL",
            ActionType.Bet => "RAISE_TO",
            ActionType.Raise => "RAISE_TO",
            ActionType.AllIn => "ALL_IN",
            ActionType.PostBigBlind => "POST_BB",
            ActionType.PostSmallBlind => "POST_SB",
            _ => "TYPE_4"
        };

    private static decimal ToBigBlinds(ChipAmount amount, ChipAmount bigBlind)
    {
        if (bigBlind.Value <= 0)
            throw new InvalidOperationException("Big blind must be positive when mapping action amounts to big blinds.");

        return decimal.Round(amount.Value / bigBlind.Value, 2);
    }
}

public sealed record SolverInfoSetMappingResult(bool IsSupported, SolverInfoSetKey? Key, PreflopInfoSetKey? PreflopKey, string? UnsupportedReason)
{
    public static SolverInfoSetMappingResult Supported(SolverInfoSetKey key, PreflopInfoSetKey? preflopKey)
        => new(true, key, preflopKey, null);

    public static SolverInfoSetMappingResult Unsupported(string reason)
        => new(false, null, null, reason);
}

public sealed record SolverInfoSetKey(
    Street Street,
    Position ActingPosition,
    string PrivateCardsKey,
    string PublicBoardKey,
    string PublicHistoryKey,
    string BettingStateKey,
    string PublicPlayerStateKey,
    PreflopInfoSetKey? PreflopKey,
    string CanonicalKey) : IComparable<SolverInfoSetKey>
{
    public int CompareTo(SolverInfoSetKey? other)
        => string.CompareOrdinal(CanonicalKey, other?.CanonicalKey);

    public static SolverInfoSetKey CreatePreflop(PreflopInfoSetKey preflopKey, HoleCards actingHoleCards)
    {
        var privateCards = BuildHoleCardsKey(actingHoleCards);
        var canonical = string.Create(CultureInfo.InvariantCulture, $"preflop/{preflopKey.SolverKey}/hero={privateCards}");

        return new SolverInfoSetKey(
            Street.Preflop,
            preflopKey.ActingPosition,
            privateCards,
            string.Empty,
            preflopKey.HistorySignature,
            preflopKey.SolverKey,
            string.Empty,
            preflopKey,
            canonical);
    }

    public static SolverInfoSetKey CreatePostflop(
        Position actingPosition,
        HoleCards actingHoleCards,
        IReadOnlyList<Card> boardCards,
        IReadOnlyList<SolverActionEntry> actionHistory,
        ChipAmount pot,
        ChipAmount currentBetSize,
        ChipAmount toCall,
        int raisesThisStreet,
        Street street,
        IReadOnlyList<SolverPlayerState> players,
        PlayerId actingPlayerId)
    {
        var privateCards = BuildHoleCardsKey(actingHoleCards);
        var board = BuildCardsKey(boardCards);
        var history = BuildPublicActionHistoryKey(actionHistory);
        var bettingState = string.Create(
            CultureInfo.InvariantCulture,
            $"pot={pot.Value}|bet={currentBetSize.Value}|tocall={toCall.Value}|raises={raisesThisStreet}");
        var publicPlayerState = BuildPublicPlayerStateKey(players, actingPlayerId);
        var canonical = $"{street.ToString().ToLowerInvariant()}/pos={actingPosition}/hero={privateCards}/board={board}/hist={history}/bet={bettingState}/pub={publicPlayerState}";

        return new SolverInfoSetKey(
            street,
            actingPosition,
            privateCards,
            board,
            history,
            bettingState,
            publicPlayerState,
            null,
            canonical);
    }

    private static string BuildHoleCardsKey(HoleCards holeCards)
        => BuildCardsKey([holeCards.First, holeCards.Second]);

    private static string BuildCardsKey(IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0)
            return string.Empty;

        return string.Join('-', cards
            .OrderByDescending(c => c.Rank)
            .ThenByDescending(c => c.Suit)
            .Select(c => c.ToString()));
    }

    private static string BuildPublicActionHistoryKey(IReadOnlyList<SolverActionEntry> actionHistory)
    {
        if (actionHistory.Count == 0)
            return string.Empty;

        return string.Join('|', actionHistory.Select(a => string.Create(CultureInfo.InvariantCulture, $"{(int)a.ActionType}:{a.Amount.Value}")));
    }

    private static string BuildPublicPlayerStateKey(IReadOnlyList<SolverPlayerState> players, PlayerId actingPlayerId)
    {
        if (players.Count == 0)
            return string.Empty;

        var ordered = players.OrderBy(p => p.SeatIndex).ToArray();
        var acting = ordered.FirstOrDefault(p => p.PlayerId == actingPlayerId)
            ?? throw new InvalidOperationException($"Acting player {actingPlayerId} not found in postflop state.");

        var effectiveStack = ComputeEffectiveStack(ordered, actingPlayerId);
        var statusMask = new string(ordered.Select(GetPlayerStatusCode).ToArray());

        var stackVector = string.Join('.', ordered.Select(p => FormatChip(p.Stack)));
        var streetContributionVector = string.Join('.', ordered.Select(p => FormatChip(p.CurrentStreetContribution)));
        var totalContributionVector = string.Join('.', ordered.Select(p => FormatChip(p.TotalContribution)));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"act={acting.SeatIndex}|eff={FormatChip(effectiveStack)}|m={statusMask}|s={stackVector}|sc={streetContributionVector}|tc={totalContributionVector}");
    }

    private static ChipAmount ComputeEffectiveStack(IReadOnlyList<SolverPlayerState> players, PlayerId actingPlayerId)
    {
        var acting = players.First(p => p.PlayerId == actingPlayerId);
        var opponents = players
            .Where(p => p.PlayerId != actingPlayerId && !p.IsFolded)
            .Select(p => p.Stack)
            .ToArray();

        if (opponents.Length == 0)
            return acting.Stack;

        var shortestOpponent = opponents.MinBy(s => s.Value)!;
        return acting.Stack.Value <= shortestOpponent.Value ? acting.Stack : shortestOpponent;
    }

    private static char GetPlayerStatusCode(SolverPlayerState player)
        => player.IsFolded
            ? 'f'
            : player.IsAllIn
                ? 'i'
                : 'a';

    private static string FormatChip(ChipAmount amount)
        => amount.Value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
