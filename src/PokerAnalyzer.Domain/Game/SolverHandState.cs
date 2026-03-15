using System.Collections.ObjectModel;
using System.Text;
using PokerAnalyzer.Domain.Cards;

namespace PokerAnalyzer.Domain.Game;

public sealed record SolverHandState
{
    public sealed record ValidationIssue(
        string ErrorCode,
        string Message,
        int? OffendingActionIndex,
        PlayerId ActingPlayer,
        Street Street,
        ChipAmount CurrentBet,
        ChipAmount Pot,
        ChipAmount LastRaiseSize,
        int RaisesThisStreet,
        string ActionHistorySummary,
        string PlayerContributionSummary,
        ChipAmount ToCall,
        PlayerId? LastAggressor);

    public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
    {
        public bool IsValid => Issues.Count == 0;
        public ValidationIssue? FirstIssue => Issues.FirstOrDefault();
    }

    private static readonly IReadOnlyDictionary<PlayerId, HoleCards> EmptyPrivateCardsByPlayer
        = new ReadOnlyDictionary<PlayerId, HoleCards>(new Dictionary<PlayerId, HoleCards>());

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
        : this(
            config,
            street,
            buttonSeatIndex,
            actingPlayerId,
            pot,
            currentBetSize,
            lastRaiseSize,
            raisesThisStreet,
            CreateNormalizedPlayers(players),
            CreateNormalizedActions(actionHistory),
            CreateNormalizedCards(boardCards),
            CreateNormalizedCards(deadCards),
            NormalizePrivateCardsByPlayer(privateCardsByPlayer),
            assumeNormalizedCollections: true)
    {
    }

    private SolverHandState(
        GameConfig config,
        Street street,
        int buttonSeatIndex,
        PlayerId actingPlayerId,
        ChipAmount pot,
        ChipAmount currentBetSize,
        ChipAmount lastRaiseSize,
        int raisesThisStreet,
        IReadOnlyList<SolverPlayerState> players,
        IReadOnlyList<SolverActionEntry> actionHistory,
        IReadOnlyList<Card> boardCards,
        IReadOnlyList<Card> deadCards,
        IReadOnlyDictionary<PlayerId, HoleCards> privateCardsByPlayer,
        bool assumeNormalizedCollections)
    {
        Config = config;
        Street = street;
        ButtonSeatIndex = buttonSeatIndex;
        ActingPlayerId = actingPlayerId;
        Pot = pot;
        CurrentBetSize = currentBetSize;
        LastRaiseSize = lastRaiseSize;
        RaisesThisStreet = raisesThisStreet;

        if (!assumeNormalizedCollections)
            throw new InvalidOperationException("Non-normalized collection construction must use the public constructor.");

        Players = players;
        EnsurePlayersSeatOrdered(players);

        ActionHistory = actionHistory;
        ActionHistorySignature = BuildActionHistorySignature(actionHistory);

        BoardCards = boardCards;
        DeadCards = deadCards;
        PrivateCardsByPlayer = privateCardsByPlayer;

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

    internal SolverHandState WithNormalized(
        Street? street = null,
        PlayerId? actingPlayerId = null,
        ChipAmount? pot = null,
        ChipAmount? currentBetSize = null,
        ChipAmount? lastRaiseSize = null,
        int? raisesThisStreet = null,
        IReadOnlyList<SolverPlayerState>? players = null,
        IReadOnlyList<SolverActionEntry>? actionHistory = null,
        IReadOnlyList<Card>? boardCards = null,
        IReadOnlyList<Card>? deadCards = null,
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
            privateCardsByPlayer ?? PrivateCardsByPlayer,
            assumeNormalizedCollections: true);

    public IReadOnlyList<LegalAction> GenerateLegalActions(IBetSizeSetProvider? sizeProvider = null)
        => SolverLegalActionGenerator.GenerateLegalActions(this, sizeProvider);

    public SolverHandState Apply(LegalAction action)
        => SolverStateStepper.Step(this, action);


    public ValidationResult Validate()
    {
        if (TryValidateNonNegativeStacks(out var issue))
            return new ValidationResult([issue]);

        if (TryValidatePotConsistency(out issue))
            return new ValidationResult([issue]);

        if (TryValidateNoDuplicateCards(out issue))
            return new ValidationResult([issue]);

        if (!SolverTraversalGuards.IsTerminalLikeState(this) && TryValidateActingPlayerIsActive(out issue))
            return new ValidationResult([issue]);

        if (TryValidateBettingState(out issue))
            return new ValidationResult([issue]);

        if (TryValidateActionHistoryConsistency(out issue))
            return new ValidationResult([issue]);

        return new ValidationResult(Array.Empty<ValidationIssue>());
    }

    internal void EnsureValid()
    {
        var result = Validate();
        if (result.IsValid)
            return;

        var issue = result.FirstIssue!;
        throw new InvalidOperationException($"[{issue.ErrorCode}] {issue.Message} | " +
            $"Context: actionIndex={(issue.OffendingActionIndex?.ToString() ?? "n/a")}, " +
            $"actingPlayer={issue.ActingPlayer}, street={issue.Street}, currentBet={issue.CurrentBet.Value}, " +
            $"pot={issue.Pot.Value}, lastRaise={issue.LastRaiseSize.Value}, raisesThisStreet={issue.RaisesThisStreet}, " +
            $"toCall={issue.ToCall.Value}, lastAggressor={(issue.LastAggressor?.ToString() ?? "n/a")}, " +
            $"actions={issue.ActionHistorySummary}, contributions={issue.PlayerContributionSummary}.");
    }

    private bool TryValidateNonNegativeStacks(out ValidationIssue? issue)
    {
        if (CurrentBetSize.Value < 0)
        {
            issue = BuildIssue("NEGATIVE_CURRENT_BET", $"Current bet size cannot be negative ({CurrentBetSize.Value}).");
            return true;
        }

        if (LastRaiseSize.Value < 0)
        {
            issue = BuildIssue("NEGATIVE_LAST_RAISE", $"Last raise size cannot be negative ({LastRaiseSize.Value}).");
            return true;
        }

        if (RaisesThisStreet < 0)
        {
            issue = BuildIssue("NEGATIVE_RAISE_COUNT", $"Raises this street cannot be negative ({RaisesThisStreet}).");
            return true;
        }

        foreach (var player in Players)
        {
            if (player.Stack.Value < 0)
            {
                issue = BuildIssue("NEGATIVE_STACK", $"Player {player.PlayerId} has negative stack ({player.Stack.Value}).");
                return true;
            }

            if (player.CurrentStreetContribution.Value < 0)
            {
                issue = BuildIssue("NEGATIVE_STREET_CONTRIBUTION", $"Player {player.PlayerId} has negative current-street contribution ({player.CurrentStreetContribution.Value}).");
                return true;
            }

            if (player.TotalContribution.Value < 0)
            {
                issue = BuildIssue("NEGATIVE_TOTAL_CONTRIBUTION", $"Player {player.PlayerId} has negative total contribution ({player.TotalContribution.Value}).");
                return true;
            }

            if (player.TotalContribution < player.CurrentStreetContribution)
            {
                issue = BuildIssue("STREET_GT_TOTAL_CONTRIBUTION", $"Player {player.PlayerId} current-street contribution exceeds total contribution.");
                return true;
            }

            if (player.IsAllIn && player.Stack.Value != 0)
            {
                issue = BuildIssue("ALLIN_WITH_STACK", $"Player {player.PlayerId} is marked all-in but has stack {player.Stack.Value}.");
                return true;
            }

            if (player.Stack.Value == 0 && !player.IsAllIn && !player.IsFolded)
            {
                issue = BuildIssue("ZERO_STACK_NOT_ALLIN", $"Player {player.PlayerId} has zero stack and must be marked all-in or folded.");
                return true;
            }

            if (player.IsAllIn && player.IsFolded)
            {
                issue = BuildIssue("ALLIN_AND_FOLDED", $"Player {player.PlayerId} cannot be both all-in and folded.");
                return true;
            }

            var chipsInPlay = player.Stack + player.TotalContribution;
            if (chipsInPlay > Config.StartingStack)
            {
                issue = BuildIssue("CHIPS_EXCEED_STARTING_STACK", $"Player {player.PlayerId} has stack + contribution ({chipsInPlay.Value}) above configured starting stack ({Config.StartingStack.Value}).");
                return true;
            }
        }

        issue = null;
        return false;
    }

    private bool TryValidatePotConsistency(out ValidationIssue? issue)
    {
        var contributionSum = Players.Aggregate(ChipAmount.Zero, (sum, p) => sum + p.TotalContribution);
        if (contributionSum != Pot)
        {
            issue = BuildIssue("POT_CONTRIBUTION_MISMATCH", $"Pot inconsistency: pot is {Pot.Value}, but summed player contributions are {contributionSum.Value}.");
            return true;
        }

        issue = null;
        return false;
    }

    private bool TryValidateNoDuplicateCards(out ValidationIssue? issue)
    {
        if (BoardCards.Count > 5)
        {
            issue = BuildIssue("BOARD_CARD_COUNT_EXCEEDED", $"Board cannot contain more than 5 cards (found {BoardCards.Count}).");
            return true;
        }

        foreach (var playerId in PrivateCardsByPlayer.Keys)
        {
            if (Players.All(p => p.PlayerId != playerId))
            {
                issue = BuildIssue("PRIVATE_CARDS_FOR_NON_SEATED_PLAYER", $"Private cards were provided for non-seated player {playerId}.");
                return true;
            }
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
        {
            issue = BuildIssue("DUPLICATE_CARD", $"Duplicate card detected in solver state: {duplicate.Key}.");
            return true;
        }

        issue = null;
        return false;
    }

    private bool TryValidateActingPlayerIsActive(out ValidationIssue? issue)
    {
        if (Players.Count == 0)
        {
            issue = BuildIssue("NO_PLAYERS", "Solver hand state must contain at least one player.");
            return true;
        }

        var duplicatePlayerId = Players.GroupBy(p => p.PlayerId).FirstOrDefault(g => g.Count() > 1);
        if (duplicatePlayerId is not null)
        {
            issue = BuildIssue("DUPLICATE_PLAYER_ID", $"Duplicate player id detected: {duplicatePlayerId.Key}.");
            return true;
        }

        var duplicateSeat = Players.GroupBy(p => p.SeatIndex).FirstOrDefault(g => g.Count() > 1);
        if (duplicateSeat is not null)
        {
            issue = BuildIssue("DUPLICATE_SEAT_INDEX", $"Duplicate seat index detected: {duplicateSeat.Key}.");
            return true;
        }

        for (var seat = 0; seat < Players.Count; seat++)
        {
            if (Players[seat].SeatIndex != seat)
            {
                issue = BuildIssue("NON_CONTIGUOUS_SEAT_ORDER", $"Players must be normalized to contiguous seat-order indexing. Expected seat {seat}, found {Players[seat].SeatIndex}.");
                return true;
            }
        }

        var player = Players.FirstOrDefault(p => p.PlayerId == ActingPlayerId);
        if (player is null)
        {
            issue = BuildIssue("ACTING_PLAYER_NOT_SEATED", $"Acting player {ActingPlayerId} is not seated in the state.");
            return true;
        }

        if (!player.IsActive)
        {
            issue = BuildIssue("ACTING_PLAYER_FOLDED", $"Acting player {ActingPlayerId} is folded and cannot act.");
            return true;
        }

        if (player.IsAllIn)
        {
            issue = BuildIssue("ACTING_PLAYER_ALLIN", $"Acting player {ActingPlayerId} is all-in and cannot act.");
            return true;
        }

        if (player.Stack.Value <= 0)
        {
            issue = BuildIssue("ACTING_PLAYER_NO_CHIPS", $"Acting player {ActingPlayerId} has no chips remaining and cannot act.");
            return true;
        }

        issue = null;
        return false;
    }

    private bool TryValidateBettingState(out ValidationIssue? issue)
    {
        var maxStreetContribution = Players.Max(p => p.CurrentStreetContribution.Value);
        if (CurrentBetSize.Value != maxStreetContribution)
        {
            issue = BuildIssue("CURRENT_BET_MISMATCH", $"Current bet size mismatch: CurrentBetSize is {CurrentBetSize.Value}, but max player street contribution is {maxStreetContribution}.");
            return true;
        }

        if (CurrentBetSize.Value == 0 && RaisesThisStreet > 0)
        {
            issue = BuildIssue("RAISE_COUNT_WITHOUT_BET", "RaisesThisStreet cannot be greater than zero when CurrentBetSize is zero.");
            return true;
        }

        issue = null;
        return false;
    }

    private bool TryValidateActionHistoryConsistency(out ValidationIssue? issue)
    {
        if (ActionHistory.Count == 0)
        {
            issue = null;
            return false;
        }

        var stateByPlayer = Players.ToDictionary(
            p => p.PlayerId,
            _ => new ActionValidationState(false, false, ChipAmount.Zero));

        var currentBet = ChipAmount.Zero;
        var lastRaiseSize = ChipAmount.Zero;

        for (var i = 0; i < ActionHistory.Count; i++)
        {
            var action = ActionHistory[i];

            if (!stateByPlayer.TryGetValue(action.PlayerId, out var playerState))
            {
                issue = BuildIssue("HISTORY_NON_SEATED_PLAYER", $"ActionHistory[{i}] contains action from non-seated player {action.PlayerId}.", i, currentBet, lastRaiseSize);
                return true;
            }

            if (playerState.HasFoldedInHistory)
            {
                issue = BuildIssue("HISTORY_ACTION_AFTER_FOLD", $"ActionHistory[{i}] contains action by folded player {action.PlayerId}. Action={action.ActionType}, Amount={action.Amount.Value}.", i, currentBet, lastRaiseSize);
                return true;
            }

            if (playerState.AllIn)
            {
                issue = BuildIssue("HISTORY_ACTION_AFTER_ALLIN", $"Action history contains action by all-in player {action.PlayerId}.", i, currentBet, lastRaiseSize);
                return true;
            }

            var toCall = currentBet - playerState.Contribution;
            if (toCall.Value < 0)
                toCall = ChipAmount.Zero;

            switch (action.ActionType)
            {
                case ActionType.Fold:
                    if (action.Amount != ChipAmount.Zero)
                    {
                        issue = BuildIssue("HISTORY_FOLD_NONZERO", $"Fold action for player {action.PlayerId} must have zero amount.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    stateByPlayer[action.PlayerId] = playerState with { HasFoldedInHistory = true };
                    break;
                case ActionType.Check:
                    if (action.Amount != ChipAmount.Zero)
                    {
                        issue = BuildIssue("HISTORY_CHECK_NONZERO", $"Check action for player {action.PlayerId} must have zero amount.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    if (toCall.Value > 0)
                    {
                        issue = BuildIssue("HISTORY_CHECK_FACING_BET", $"Action history inconsistency: player {action.PlayerId} checked while facing {toCall.Value} to call.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    break;
                case ActionType.Call:
                    if (toCall.Value == 0)
                    {
                        issue = BuildIssue("HISTORY_CALL_NOTHING_OWED", $"Action history inconsistency: player {action.PlayerId} called when nothing was owed.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    stateByPlayer[action.PlayerId] = playerState with { Contribution = playerState.Contribution + toCall };
                    break;
                case ActionType.Bet:
                    if (toCall.Value != 0)
                    {
                        issue = BuildIssue("HISTORY_BET_FACING_BET", $"Action history inconsistency: player {action.PlayerId} bet while facing an outstanding bet.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    if (TryValidateAggressiveAmount(action, playerState.Contribution, "Bet", i, currentBet, lastRaiseSize, out issue))
                        return true;

                    lastRaiseSize = action.Amount - currentBet;
                    currentBet = action.Amount;
                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount };
                    break;
                case ActionType.Raise:
                    var isBigBlindOptionVsLimpRaise = IsBigBlindOptionVsLimpRaiseAction(i, action, playerState, currentBet);
                    if (toCall.Value == 0 && !isBigBlindOptionVsLimpRaise)
                    {
                        issue = BuildIssue("HISTORY_RAISE_WITHOUT_BET", $"Action history inconsistency: player {action.PlayerId} raised when no bet was outstanding.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    if (TryValidateAggressiveAmount(action, playerState.Contribution, "Raise", i, currentBet, lastRaiseSize, out issue))
                        return true;

                    if (action.Amount <= currentBet)
                    {
                        issue = BuildIssue("HISTORY_RAISE_NOT_ABOVE_CURRENT_BET", $"Action history inconsistency: raise by player {action.PlayerId} to {action.Amount.Value} does not exceed current bet {currentBet.Value}.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    var raiseIncrement = action.Amount - currentBet;
                    if (lastRaiseSize.Value > 0 && raiseIncrement < lastRaiseSize)
                    {
                        issue = BuildIssue("HISTORY_RAISE_BELOW_MIN_INCREMENT", $"Action history inconsistency: raise by player {action.PlayerId} to {action.Amount.Value} is below minimum full-raise increment {lastRaiseSize.Value}.", i, currentBet, lastRaiseSize);
                        return true;
                    }

                    lastRaiseSize = raiseIncrement;
                    currentBet = action.Amount;
                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount };
                    break;
                case ActionType.AllIn:
                    if (TryValidateAggressiveAmount(action, playerState.Contribution, "All-in", i, currentBet, lastRaiseSize, out issue))
                        return true;

                    if (action.Amount > currentBet)
                    {
                        lastRaiseSize = action.Amount - currentBet;
                        currentBet = action.Amount;
                    }

                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount, AllIn = true };
                    break;
                case ActionType.PostSmallBlind:
                case ActionType.PostBigBlind:
                    if (TryValidateAggressiveAmount(action, playerState.Contribution, "Blind post", i, currentBet, lastRaiseSize, out issue))
                        return true;

                    if (action.Amount > currentBet)
                    {
                        lastRaiseSize = action.Amount - currentBet;
                        currentBet = action.Amount;
                    }

                    stateByPlayer[action.PlayerId] = playerState with { Contribution = action.Amount };
                    break;
                case ActionType.SitOut:
                    stateByPlayer[action.PlayerId] = playerState with { HasFoldedInHistory = true };
                    break;
                default:
                    issue = BuildIssue("HISTORY_UNSUPPORTED_ACTION", $"Unsupported action type in history: {action.ActionType}.", i, currentBet, lastRaiseSize);
                    return true;
            }
        }

        issue = null;
        return false;
    }

    private bool TryValidateAggressiveAmount(
        SolverActionEntry action,
        ChipAmount priorContribution,
        string actionName,
        int actionIndex,
        ChipAmount currentBet,
        ChipAmount lastRaiseSize,
        out ValidationIssue? issue)
    {
        if (action.Amount.Value <= 0)
        {
            issue = BuildIssue("HISTORY_AGGRESSIVE_NONPOSITIVE", $"{actionName} action for player {action.PlayerId} must use a positive to-amount.", actionIndex, currentBet, lastRaiseSize);
            return true;
        }

        if (action.Amount <= priorContribution)
        {
            issue = BuildIssue("HISTORY_AGGRESSIVE_NOT_INCREASING", $"{actionName} action for player {action.PlayerId} must increase street contribution.", actionIndex, currentBet, lastRaiseSize);
            return true;
        }

        issue = null;
        return false;
    }

    private ValidationIssue BuildIssue(
        string errorCode,
        string message,
        int? offendingActionIndex = null,
        ChipAmount? currentBetOverride = null,
        ChipAmount? lastRaiseSizeOverride = null)
    {
        return new ValidationIssue(
            errorCode,
            message,
            offendingActionIndex,
            ActingPlayerId,
            Street,
            currentBetOverride ?? CurrentBetSize,
            Pot,
            lastRaiseSizeOverride ?? LastRaiseSize,
            RaisesThisStreet,
            BuildActionHistorySummary(),
            BuildPlayerContributionSummary(),
            ResolveToCallSafe(),
            ResolveLastAggressor());
    }

    private string BuildActionHistorySummary()
    {
        if (ActionHistory.Count == 0)
            return "<empty>";

        return string.Join(" | ", ActionHistory.Select((a, i) => $"#{i}:{a.PlayerId}:{a.ActionType}:{a.Amount.Value}"));
    }

    private string BuildPlayerContributionSummary()
    {
        if (Players.Count == 0)
            return "<no-players>";

        return string.Join(", ",
            Players.Select(p =>
                $"{p.PlayerId}[seat={p.SeatIndex},street={p.CurrentStreetContribution.Value},total={p.TotalContribution.Value},stack={p.Stack.Value},folded={p.IsFolded},allIn={p.IsAllIn}]"));
    }

    private ChipAmount ResolveToCallSafe()
    {
        var acting = Players.FirstOrDefault(p => p.PlayerId == ActingPlayerId);
        if (acting is null)
            return ChipAmount.Zero;

        var toCall = CurrentBetSize - acting.CurrentStreetContribution;
        return toCall.Value < 0 ? ChipAmount.Zero : toCall;
    }

    private PlayerId? ResolveLastAggressor()
    {
        for (var i = ActionHistory.Count - 1; i >= 0; i--)
        {
            var actionType = ActionHistory[i].ActionType;
            if (actionType == ActionType.Bet || actionType == ActionType.Raise || actionType == ActionType.AllIn)
                return ActionHistory[i].PlayerId;
        }

        return null;
    }

    private readonly record struct ActionValidationState(bool HasFoldedInHistory, bool AllIn, ChipAmount Contribution);

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

    private static IReadOnlyList<SolverPlayerState> CreateNormalizedPlayers(IEnumerable<SolverPlayerState> players)
    {
        var orderedPlayers = players.OrderBy(p => p.SeatIndex).ToArray();
        return Array.AsReadOnly(orderedPlayers);
    }

    private static IReadOnlyList<SolverActionEntry> CreateNormalizedActions(IEnumerable<SolverActionEntry>? actionHistory)
    {
        var actions = (actionHistory ?? Array.Empty<SolverActionEntry>()).ToArray();
        return Array.AsReadOnly(actions);
    }

    private static IReadOnlyList<Card> CreateNormalizedCards(IEnumerable<Card>? cards)
        => Array.AsReadOnly((cards ?? Array.Empty<Card>()).ToArray());

    private static void EnsurePlayersSeatOrdered(IReadOnlyList<SolverPlayerState> players)
    {
        for (var i = 1; i < players.Count; i++)
        {
            if (players[i - 1].SeatIndex > players[i].SeatIndex)
            {
                throw new InvalidOperationException(
                    "SolverHandState fast-path construction requires players to be pre-sorted by SeatIndex.");
            }
        }
    }

    private static IReadOnlyDictionary<PlayerId, HoleCards> NormalizePrivateCardsByPlayer(
        IReadOnlyDictionary<PlayerId, HoleCards>? privateCardsByPlayer)
    {
        if (privateCardsByPlayer is null || privateCardsByPlayer.Count == 0)
            return EmptyPrivateCardsByPlayer;

        if (privateCardsByPlayer is ReadOnlyDictionary<PlayerId, HoleCards>)
            return privateCardsByPlayer;

        return new ReadOnlyDictionary<PlayerId, HoleCards>(new Dictionary<PlayerId, HoleCards>(privateCardsByPlayer));
    }
}
