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

    public IReadOnlyList<LegalAction> GenerateLegalActions(IBetSizeSetProvider? sizeProvider = null)
        => SolverLegalActionGenerator.GenerateLegalActions(this, sizeProvider);

    public SolverHandState Apply(LegalAction action)
        => SolverStateStepper.Step(this, action);

    internal void ValidateNonNegativeStacks()
    {
        if (CurrentBetSize.Value < 0)
            throw new InvalidOperationException($"Current bet size cannot be negative ({CurrentBetSize.Value}).");

        if (LastRaiseSize.Value < 0)
            throw new InvalidOperationException($"Last raise size cannot be negative ({LastRaiseSize.Value}).");

        if (RaisesThisStreet < 0)
            throw new InvalidOperationException($"Raises this street cannot be negative ({RaisesThisStreet}).");

        foreach (var player in Players)
        {
            if (player.Stack.Value < 0)
                throw new InvalidOperationException($"Player {player.PlayerId} has negative stack ({player.Stack.Value}).");

            if (player.CurrentStreetContribution.Value < 0)
                throw new InvalidOperationException($"Player {player.PlayerId} has negative current-street contribution ({player.CurrentStreetContribution.Value}).");

            if (player.TotalContribution.Value < 0)
                throw new InvalidOperationException($"Player {player.PlayerId} has negative total contribution ({player.TotalContribution.Value}).");

            if (player.TotalContribution < player.CurrentStreetContribution)
                throw new InvalidOperationException($"Player {player.PlayerId} current-street contribution exceeds total contribution.");

            if (player.IsAllIn && player.Stack.Value != 0)
                throw new InvalidOperationException($"Player {player.PlayerId} is marked all-in but has stack {player.Stack.Value}.");

            if (player.Stack.Value == 0 && !player.IsAllIn && !player.IsFolded)
                throw new InvalidOperationException($"Player {player.PlayerId} has zero stack and must be marked all-in or folded.");

            if (player.IsAllIn && player.IsFolded)
                throw new InvalidOperationException($"Player {player.PlayerId} cannot be both all-in and folded.");

            var chipsInPlay = player.Stack + player.TotalContribution;
            if (chipsInPlay > Config.StartingStack)
                throw new InvalidOperationException($"Player {player.PlayerId} has stack + contribution ({chipsInPlay.Value}) above configured starting stack ({Config.StartingStack.Value}).");
        }
    }

    internal void ValidatePotConsistency()
    {
        var contributionSum = Players.Aggregate(ChipAmount.Zero, (sum, p) => sum + p.TotalContribution);
        if (contributionSum != Pot)
            throw new InvalidOperationException($"Pot inconsistency: pot is {Pot.Value}, but summed player contributions are {contributionSum.Value}.");

    }

    internal void ValidateNoDuplicateCards()
    {
        if (BoardCards.Count > 5)
            throw new InvalidOperationException($"Board cannot contain more than 5 cards (found {BoardCards.Count}).");

        foreach (var playerId in PrivateCardsByPlayer.Keys)
        {
            if (Players.All(p => p.PlayerId != playerId))
                throw new InvalidOperationException($"Private cards were provided for non-seated player {playerId}.");
        }

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
        if (Players.Count == 0)
            throw new InvalidOperationException("Solver hand state must contain at least one player.");

        var duplicatePlayerId = Players.GroupBy(p => p.PlayerId).FirstOrDefault(g => g.Count() > 1);
        if (duplicatePlayerId is not null)
            throw new InvalidOperationException($"Duplicate player id detected: {duplicatePlayerId.Key}.");

        var duplicateSeat = Players.GroupBy(p => p.SeatIndex).FirstOrDefault(g => g.Count() > 1);
        if (duplicateSeat is not null)
            throw new InvalidOperationException($"Duplicate seat index detected: {duplicateSeat.Key}.");

        var player = Players.FirstOrDefault(p => p.PlayerId == ActingPlayerId);
        if (player is null)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} is not seated in the state.");

        if (!player.IsActive)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} is folded and cannot act.");

        var hasActionablePlayers = Players.Any(p => p.IsActive && !p.IsAllIn && p.Stack.Value > 0);
        if (!hasActionablePlayers)
            return;

        if (player.IsAllIn)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} is all-in and cannot act.");

        if (player.Stack.Value <= 0)
            throw new InvalidOperationException($"Acting player {ActingPlayerId} has no chips remaining and cannot act.");
    }

    internal void ValidateBettingState()
    {
        var maxStreetContribution = Players.Max(p => p.CurrentStreetContribution.Value);
        if (CurrentBetSize.Value != maxStreetContribution)
        {
            throw new InvalidOperationException(
                $"Current bet size mismatch: CurrentBetSize is {CurrentBetSize.Value}, but max player street contribution is {maxStreetContribution}.");
        }

        if (CurrentBetSize.Value == 0 && RaisesThisStreet > 0)
            throw new InvalidOperationException("RaisesThisStreet cannot be greater than zero when CurrentBetSize is zero.");
    }

    internal void ValidateActionHistoryConsistency()
    {
        if (ActionHistory.Count == 0)
            return;

        var stateByPlayer = Players.ToDictionary(
            p => p.PlayerId,
            _ => new ActionValidationState(Active: true, AllIn: false, Contribution: ChipAmount.Zero));

        var currentBet = ChipAmount.Zero;
        var lastRaiseSize = ChipAmount.Zero;

        foreach (var action in ActionHistory)
        {
            switch (action.ActionType)
            {
                case ActionType.DealFlop:
                case ActionType.DealTurn:
                case ActionType.DealRiver:
                    if (action.Amount != ChipAmount.Zero)
                    {
                        throw new InvalidOperationException(
                            $"{action.ActionType} action must have zero amount.");
                    }

                    foreach (var playerId in stateByPlayer.Keys.ToArray())
                    {
                        var priorState = stateByPlayer[playerId];
                        stateByPlayer[playerId] = priorState with { Contribution = ChipAmount.Zero };
                    }

                    currentBet = ChipAmount.Zero;
                    lastRaiseSize = ChipAmount.Zero;
                    continue;
            }

            if (!stateByPlayer.TryGetValue(action.PlayerId, out var playerState))
                throw new InvalidOperationException($"Action history contains action from non-seated player {action.PlayerId}.");

            if (!playerState.Active)
                throw new InvalidOperationException($"Action history contains action by folded player {action.PlayerId}.");

            if (playerState.AllIn)
                throw new InvalidOperationException($"Action history contains action by all-in player {action.PlayerId}.");

            var toCall = currentBet - playerState.Contribution;
            if (toCall.Value < 0)
                toCall = ChipAmount.Zero;

            switch (action.ActionType)
            {
                case ActionType.Fold:
                    if (action.Amount != ChipAmount.Zero)
                        throw new InvalidOperationException($"Fold action for player {action.PlayerId} must have zero amount.");
                    stateByPlayer[action.PlayerId] = playerState with { Active = false };
                    break;
                case ActionType.Check:
                    if (action.Amount != ChipAmount.Zero)
                        throw new InvalidOperationException($"Check action for player {action.PlayerId} must have zero amount.");
                    if (toCall.Value > 0)
                        throw new InvalidOperationException($"Action history inconsistency: player {action.PlayerId} checked while facing {toCall.Value} to call.");
                    break;
                case ActionType.Call:
                    if (toCall.Value == 0)
                        throw new InvalidOperationException($"Action history inconsistency: player {action.PlayerId} called when nothing was owed.");
                    stateByPlayer[action.PlayerId] = playerState with { Contribution = playerState.Contribution + toCall };
                    break;
                case ActionType.Bet:
                    if (toCall.Value != 0)
                        throw new InvalidOperationException($"Action history inconsistency: player {action.PlayerId} bet while facing an outstanding bet.");
                    ValidateAggressiveAmount(action, playerState.Contribution, "Bet");
                    lastRaiseSize = action.Amount - currentBet;
                    currentBet = action.Amount;
                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount };
                    break;
                case ActionType.Raise:
                    if (toCall.Value == 0)
                        throw new InvalidOperationException($"Action history inconsistency: player {action.PlayerId} raised when no bet was outstanding.");
                    ValidateAggressiveAmount(action, playerState.Contribution, "Raise");
                    if (action.Amount <= currentBet)
                        throw new InvalidOperationException($"Action history inconsistency: raise by player {action.PlayerId} to {action.Amount.Value} does not exceed current bet {currentBet.Value}.");

                    var raiseIncrement = action.Amount - currentBet;
                    if (lastRaiseSize.Value > 0 && raiseIncrement < lastRaiseSize)
                    {
                        throw new InvalidOperationException(
                            $"Action history inconsistency: raise by player {action.PlayerId} to {action.Amount.Value} is below minimum full-raise increment {lastRaiseSize.Value}.");
                    }

                    lastRaiseSize = raiseIncrement;
                    currentBet = action.Amount;
                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount };
                    break;
                case ActionType.AllIn:
                    ValidateAggressiveAmount(action, playerState.Contribution, "All-in");
                    if (action.Amount > currentBet)
                    {
                        lastRaiseSize = action.Amount - currentBet;
                        currentBet = action.Amount;
                    }

                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount, AllIn = true };
                    break;
                case ActionType.PostSmallBlind:
                case ActionType.PostBigBlind:
                    ValidateAggressiveAmount(action, playerState.Contribution, "Blind post");
                    if (action.Amount > currentBet)
                    {
                        lastRaiseSize = action.Amount - currentBet;
                        currentBet = action.Amount;
                    }

                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount };
                    break;
                case ActionType.SitOut:
                    stateByPlayer[action.PlayerId] = playerState with { Active = false };
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported action type in history: {action.ActionType}.");
            }
        }
    }

    private static void ValidateAggressiveAmount(SolverActionEntry action, ChipAmount priorContribution, string actionName)
    {
        if (action.Amount.Value <= 0)
            throw new InvalidOperationException($"{actionName} action for player {action.PlayerId} must use a positive to-amount.");

        if (action.Amount <= priorContribution)
            throw new InvalidOperationException($"{actionName} action for player {action.PlayerId} must increase street contribution.");
    }

    internal void EnsureValid()
    {
        ValidateNonNegativeStacks();
        ValidatePotConsistency();
        ValidateNoDuplicateCards();
        ValidateActingPlayerIsActive();
        ValidateBettingState();
        ValidateActionHistoryConsistency();
    }

    private readonly record struct ActionValidationState(bool Active, bool AllIn, ChipAmount Contribution);

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
