using System.Collections.ObjectModel;
using System.Text;
using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Domain.Game;

public sealed record SolverHandState
{
    public GameConfig Config { get; }
    public Street Street { get; }
    public int ButtonSeatIndex { get; }
    public PlayerId ActingPlayerId { get; }
    public ChipAmount Pot { get; }
    public ChipAmount CurrentBetSize { get; }
    public ChipAmount LastRaiseSize { get; }
    public int RaisesThisStreet { get; }

    public IReadOnlyList<SolverPlayerState> Players { get; }
    public IReadOnlyList<SolverActionEntry> ActionHistory { get; }
    public string ActionHistorySignature { get; }

    public IReadOnlyList<Card> BoardCards { get; }
    public IReadOnlyList<Card> DeadCards { get; }
    public IReadOnlyDictionary<PlayerId, HoleCards> PrivateCardsByPlayer { get; }

    public ChipAmount ToCall
    {
        get
        {
            var acting = GetActingPlayer();
            var toCall = CurrentBetSize - acting.CurrentStreetContribution;
            return toCall.Value < 0 ? ChipAmount.Zero : toCall;
        }
    }

    public SolverHandState(
        GameConfig config,
        Street street,
        int buttonSeatIndex,
        PlayerId actingPlayerId,
        ChipAmount pot,
        ChipAmount currentBetSize,
        ChipAmount lastRaiseSize,
        int raisesThisStreet,
        IEnumerable<SolverPlayerState> players,
        IEnumerable<SolverActionEntry>? actionHistory = null,
        IEnumerable<Card>? boardCards = null,
        IEnumerable<Card>? deadCards = null,
        IReadOnlyDictionary<PlayerId, HoleCards>? privateCardsByPlayer = null)
    {
        Config = config;
        Street = street;
        ButtonSeatIndex = buttonSeatIndex;
        ActingPlayerId = actingPlayerId;
        Pot = pot;
        CurrentBetSize = currentBetSize;
        LastRaiseSize = lastRaiseSize;
        RaisesThisStreet = raisesThisStreet;

        var orderedPlayers = players.OrderBy(p => p.SeatIndex).ToArray();
        Players = Array.AsReadOnly(orderedPlayers);

        var actions = (actionHistory ?? Array.Empty<SolverActionEntry>()).ToArray();
        ActionHistory = Array.AsReadOnly(actions);
        ActionHistorySignature = BuildActionHistorySignature(actions);

        BoardCards = Array.AsReadOnly((boardCards ?? Array.Empty<Card>()).ToArray());
        DeadCards = Array.AsReadOnly((deadCards ?? Array.Empty<Card>()).ToArray());
        PrivateCardsByPlayer = privateCardsByPlayer is null
            ? new ReadOnlyDictionary<PlayerId, HoleCards>(new Dictionary<PlayerId, HoleCards>())
            : new ReadOnlyDictionary<PlayerId, HoleCards>(new Dictionary<PlayerId, HoleCards>(privateCardsByPlayer));

        EnsureValid();
    }

    public SolverHandState With(
        Street? street = null,
        PlayerId? actingPlayerId = null,
        ChipAmount? pot = null,
        ChipAmount? currentBetSize = null,
        ChipAmount? lastRaiseSize = null,
        int? raisesThisStreet = null,
        IEnumerable<SolverPlayerState>? players = null,
        IEnumerable<SolverActionEntry>? actionHistory = null,
        IEnumerable<Card>? boardCards = null,
        IEnumerable<Card>? deadCards = null,
        IReadOnlyDictionary<PlayerId, HoleCards>? privateCardsByPlayer = null)
        => new(
            Config,
            street ?? Street,
            ButtonSeatIndex,
            actingPlayerId ?? ActingPlayerId,
            pot ?? Pot,
            currentBetSize ?? CurrentBetSize,
            lastRaiseSize ?? LastRaiseSize,
            raisesThisStreet ?? RaisesThisStreet,
            players ?? Players,
            actionHistory ?? ActionHistory,
            boardCards ?? BoardCards,
            deadCards ?? DeadCards,
            privateCardsByPlayer ?? PrivateCardsByPlayer);

    internal void ValidateNonNegativeStacks()
    {
        foreach (var player in Players)
        {
            if (player.Stack.Value < 0)
                throw new InvalidOperationException($"Player {player.PlayerId} has negative stack ({player.Stack.Value}).");

            if (player.CurrentStreetContribution.Value < 0)
                throw new InvalidOperationException($"Player {player.PlayerId} has negative current-street contribution ({player.CurrentStreetContribution.Value}).");

            if (player.TotalContribution.Value < 0)
                throw new InvalidOperationException($"Player {player.PlayerId} has negative total contribution ({player.TotalContribution.Value}).");
        }
    }

    internal void ValidatePotConsistency()
    {
        var contributionSum = Players.Aggregate(ChipAmount.Zero, (sum, p) => sum + p.TotalContribution);
        if (contributionSum != Pot)
            throw new InvalidOperationException($"Pot inconsistency: pot is {Pot.Value}, but summed player contributions are {contributionSum.Value}.");

        foreach (var player in Players)
        {
            if (player.TotalContribution < player.CurrentStreetContribution)
                throw new InvalidOperationException($"Player {player.PlayerId} current-street contribution exceeds total contribution.");
        }
    }

    internal void ValidateNoDuplicateCards()
    {
        var cards = new List<Card>(BoardCards.Count + DeadCards.Count + (PrivateCardsByPlayer.Count * 2));
        cards.AddRange(BoardCards);
        cards.AddRange(DeadCards);

        foreach (var privateCards in PrivateCardsByPlayer.Values)
        {
            cards.Add(privateCards.First);
            cards.Add(privateCards.Second);
        }

        var duplicate = cards.GroupBy(card => card).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate card detected in solver state: {duplicate.Key}.");
    }

    internal void ValidateActingPlayerIsActive()
    {
        var player = Players.FirstOrDefault(p => p.PlayerId == ActingPlayerId);
        if (player is null)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} is not seated in the state.");

        if (!player.IsActive)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} is folded and cannot act.");

        if (player.IsAllIn)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} is all-in and cannot act.");
    }

    internal void EnsureValid()
    {
        ValidateNonNegativeStacks();
        ValidatePotConsistency();
        ValidateNoDuplicateCards();
        ValidateActingPlayerIsActive();
    }

    private SolverPlayerState GetActingPlayer()
        => Players.First(p => p.PlayerId == ActingPlayerId);

    private static string BuildActionHistorySignature(IEnumerable<SolverActionEntry> actions)
    {
        var sb = new StringBuilder();
        foreach (var action in actions)
        {
            if (sb.Length > 0)
                sb.Append('|');

            sb.Append(action);
        }

        return sb.ToString();
    }
}
