namespace PokerAnalyzer.Domain.Game;

public static class SolverStateStepper
{
    private const string DebugEnvironmentVariable = "PA_SOLVER_DEBUG_STEPPER";

    public static SolverHandState Step(SolverHandState state, LegalAction action)
        => Step(state, action, legalActions: null);

    public static SolverHandState Step(
        SolverHandState state,
        LegalAction action,
        IReadOnlyList<LegalAction>? legalActions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        var acting = state.Players.FirstOrDefault(p => p.PlayerId == state.ActingPlayerId)
            ?? throw new InvalidOperationException($"Acting player {state.ActingPlayerId} is not seated in the hand state.");

        if (!acting.IsActive || acting.IsAllIn || acting.Stack.Value <= 0)
            throw new InvalidOperationException($"Acting player {acting.PlayerId} is not able to act.");

        EnsureActionIsLegal(state, action, legalActions);
        LogStepTransition(
            "before",
            state,
            action,
            legalActions,
            bettingRoundComplete: null,
            "Applying action.");

        var players = state.Players.ToArray();
        var actingIndex = acting.SeatIndex;
        var nextCurrentBet = state.CurrentBetSize;
        var nextLastRaiseSize = state.LastRaiseSize;
        var nextRaisesThisStreet = state.RaisesThisStreet;
        var nextPot = state.Pot;
        var actionAmount = ChipAmount.Zero;
        if (state.Street == Street.Preflop &&
    action.ActionType == ActionType.Bet &&
    state.CurrentBetSize.Value > 0)
        {
            throw new InvalidOperationException(
                $"Invalid preflop Bet action encountered. currentBet={state.CurrentBetSize.Value}, toCall={state.ToCall.Value}, amount={action.Amount?.Value}");
        }
        switch (action.ActionType)
        {
            case ActionType.Fold:
                players[actingIndex] = acting with { IsFolded = true };
                break;
            case ActionType.Check:
                if (state.ToCall.Value != 0)
                    throw new InvalidOperationException($"Player {acting.PlayerId} cannot check while facing {state.ToCall.Value} chips to call.");
                break;
            case ActionType.Call:
                {
                    var toCall = state.ToCall;
                    if (toCall.Value <= 0)
                        throw new InvalidOperationException($"Player {acting.PlayerId} cannot call when there is nothing to call.");

                    var pay = toCall.Value > acting.Stack.Value ? acting.Stack : toCall;
                    var target = acting.CurrentStreetContribution + pay;

                    players[actingIndex] = ApplyContribution(acting, pay);
                    nextPot += pay;
                    actionAmount = target;
                    break;
                }
            case ActionType.Bet:
            {
                if (state.ToCall.Value != 0)
                    throw new InvalidOperationException($"Player {acting.PlayerId} cannot bet while facing {state.ToCall.Value} chips to call.");

                var toAmount = RequireTargetAmount(action, acting, "Bet");
                var delta = toAmount - acting.CurrentStreetContribution;
                players[actingIndex] = ApplyContribution(acting, delta);
                nextPot += delta;
                nextLastRaiseSize = toAmount;
                nextCurrentBet = toAmount;
                nextRaisesThisStreet++;
                actionAmount = toAmount;
                break;
            }
            case ActionType.Raise:
            {
                var toCall = state.ToCall;
                var isBigBlindOptionVsLimp = IsBigBlindOptionVsLimpPreflopSpot(state, acting);
                if (toCall.Value <= 0 && !isBigBlindOptionVsLimp)
                    throw new InvalidOperationException($"Player {acting.PlayerId} cannot raise when there is no outstanding bet.");

                var toAmount = RequireTargetAmount(action, acting, "Raise");
                if (toAmount <= state.CurrentBetSize)
                    throw new InvalidOperationException($"Raise to {toAmount.Value} must be greater than current bet size {state.CurrentBetSize.Value}.");

                var raiseIncrement = toAmount - state.CurrentBetSize;
                if (raiseIncrement < state.LastRaiseSize)
                {
                    throw new InvalidOperationException(
                        $"Raise to {toAmount.Value} is below minimum full raise increment {state.LastRaiseSize.Value}.");
                }

                var delta = toAmount - acting.CurrentStreetContribution;
                players[actingIndex] = ApplyContribution(acting, delta);
                nextPot += delta;
                nextCurrentBet = toAmount;
                nextLastRaiseSize = raiseIncrement;
                nextRaisesThisStreet++;
                actionAmount = toAmount;
                break;
            }
            case ActionType.AllIn:
            {
                var target = acting.CurrentStreetContribution + acting.Stack;
                var delta = target - acting.CurrentStreetContribution;
                if (delta.Value <= 0)
                    throw new InvalidOperationException($"Player {acting.PlayerId} cannot go all-in with zero chips.");

                players[actingIndex] = ApplyContribution(acting, delta, isAllInOverride: true);
                nextPot += delta;
                if (target > state.CurrentBetSize)
                {
                    nextLastRaiseSize = target - state.CurrentBetSize;
                    nextCurrentBet = target;
                    nextRaisesThisStreet++;
                }

                actionAmount = target;
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported action type {action.ActionType} for solver traversal step.");
        }
        var updatedHistory = AppendAction(state.ActionHistory, new SolverActionEntry(acting.PlayerId, action.ActionType, actionAmount));

        var activePlayers = players.Where(p => p.IsActive).ToArray();
        if (activePlayers.Length == 0)
            throw new InvalidOperationException("State transition produced no active players.");

        var bettingRoundComplete = IsBettingRoundComplete(state, players, updatedHistory);
        if (bettingRoundComplete)
        {
            players = players
                .Select(p => p with { CurrentStreetContribution = ChipAmount.Zero })
                .ToArray();

            nextCurrentBet = ChipAmount.Zero;
            nextLastRaiseSize = state.Config.BigBlind;
            nextRaisesThisStreet = 0;
        }

        var nextActingPlayer = ResolveNextActingPlayer(state, players, acting.SeatIndex, bettingRoundComplete);
        var nextActingPlayerId = nextActingPlayer?.PlayerId;
        if (nextActingPlayerId is null)
        {
            if (!bettingRoundComplete)
                throw new InvalidOperationException("Unable to resolve next acting player for a live betting round.");

            nextActingPlayerId = ResolveCompletedRoundActingPlayerId(players, acting.PlayerId);
        }

        var nextState = state.WithNormalized(
            actingPlayerId: nextActingPlayerId,
            pot: nextPot,
            currentBetSize: nextCurrentBet,
            lastRaiseSize: nextLastRaiseSize,
            raisesThisStreet: nextRaisesThisStreet,
            players: players,
            actionHistory: updatedHistory);

        LogStepTransition(
            "after",
            nextState,
            action,
            legalActions,
            bettingRoundComplete,
            "Completed action transition.");

        return nextState;
    }

    private static PlayerId ResolveCompletedRoundActingPlayerId(
        IReadOnlyList<SolverPlayerState> players,
        PlayerId fallbackPlayerId)
    {
        var activeActionable = players.FirstOrDefault(p => p.IsActive && !p.IsAllIn && p.Stack.Value > 0);
        if (activeActionable is not null)
            return activeActionable.PlayerId;

        var active = players.FirstOrDefault(p => p.IsActive);
        return active?.PlayerId ?? fallbackPlayerId;
    }

    private static bool IsBigBlindOptionVsLimpPreflopSpot(SolverHandState state, SolverPlayerState acting)
    {
        if (state.Street != Street.Preflop)
            return false;

        if (acting.Position != Position.BB)
            return false;

        if (state.ToCall.Value != 0)
            return false;

        if (acting.CurrentStreetContribution != state.CurrentBetSize)
            return false;

        var hasAggressivePreflopAction = state.ActionHistory.Any(a =>
            a.ActionType == ActionType.Bet ||
            a.ActionType == ActionType.Raise ||
            a.ActionType == ActionType.AllIn);

        if (hasAggressivePreflopAction)
            return false;

        return state.ActionHistory.Any(a => a.ActionType == ActionType.Call);
    }

    private static void EnsureActionIsLegal(SolverHandState state, LegalAction action, IReadOnlyList<LegalAction>? legalActions)
    {
        var candidateActions = legalActions ?? state.GenerateLegalActions();
        if (candidateActions.Contains(action))
            return;

        if (action.Amount is not null && candidateActions.Any(a => a.ActionType == action.ActionType && a.Amount is null))
            return;

        if (action.ActionType == ActionType.AllIn && IsEquivalentAllInActionLegal(state, candidateActions))
            return;

        throw new InvalidOperationException($"Action {action} is not legal for acting player {state.ActingPlayerId}.");
    }

    private static SolverActionEntry[] AppendAction(IReadOnlyList<SolverActionEntry> history, SolverActionEntry nextAction)
    {
        var updatedHistory = new SolverActionEntry[history.Count + 1];
        for (var i = 0; i < history.Count; i++)
            updatedHistory[i] = history[i];

        updatedHistory[^1] = nextAction;
        return updatedHistory;
    }

    private static bool IsEquivalentAllInActionLegal(SolverHandState state, IReadOnlyList<LegalAction> legalActions)
    {
        var acting = state.Players.First(p => p.PlayerId == state.ActingPlayerId);
        var toCall = state.ToCall;
        var jamTarget = acting.CurrentStreetContribution + acting.Stack;

        if (toCall.Value > 0 && acting.Stack <= toCall)
        {
            return legalActions.Any(a => a.ActionType == ActionType.Call && a.Amount == acting.Stack);
        }

        if (toCall.Value == 0)
        {
            return legalActions.Any(a => a.ActionType == ActionType.Bet && (a.Amount is null || a.Amount == jamTarget));
        }

        return legalActions.Any(a => a.ActionType == ActionType.Raise && (a.Amount is null || a.Amount == jamTarget));
    }

    private static ChipAmount RequireTargetAmount(LegalAction action, SolverPlayerState acting, string actionName)
    {
        if (action.Amount is null)
            throw new InvalidOperationException($"{actionName} action requires an explicit target contribution amount.");

        var toAmount = action.Amount.Value;
        if (toAmount <= acting.CurrentStreetContribution)
        {
            throw new InvalidOperationException(
                $"{actionName} to {toAmount.Value} must exceed current contribution {acting.CurrentStreetContribution.Value}.");
        }

        var delta = toAmount - acting.CurrentStreetContribution;
        if (delta > acting.Stack)
            throw new InvalidOperationException($"{actionName} to {toAmount.Value} exceeds available stack for player {acting.PlayerId}.");

        return toAmount;
    }

    private static SolverPlayerState ApplyContribution(SolverPlayerState player, ChipAmount delta, bool? isAllInOverride = null)
    {
        if (delta.Value < 0)
            throw new InvalidOperationException("Contribution delta cannot be negative.");

        if (delta > player.Stack)
            throw new InvalidOperationException($"Contribution delta {delta.Value} exceeds stack {player.Stack.Value} for player {player.PlayerId}.");

        var nextStack = player.Stack - delta;
        var nextStreetContribution = player.CurrentStreetContribution + delta;
        var nextTotalContribution = player.TotalContribution + delta;

        var isAllIn = isAllInOverride ?? nextStack.Value == 0;

        return player with
        {
            Stack = nextStack,
            CurrentStreetContribution = nextStreetContribution,
            TotalContribution = nextTotalContribution,
            IsAllIn = isAllIn
        };
    }

    private static bool IsBettingRoundComplete(
        SolverHandState previousState,
        IReadOnlyList<SolverPlayerState> players,
        IReadOnlyList<SolverActionEntry> updatedHistory)
    {
        var activeCount = players.Count(p => p.IsActive);
        if (activeCount <= 1)
            return true;

        var actionablePlayers = players.Where(p => p.IsActive && !p.IsAllIn && p.Stack.Value > 0).ToArray();
        if (actionablePlayers.Length == 0)
            return true;

        var currentBet = players.Max(p => p.CurrentStreetContribution.Value);
        var anyOutstandingCall = actionablePlayers.Any(p => p.CurrentStreetContribution.Value < currentBet);
        if (anyOutstandingCall)
        {
            LogRoundCompletionDecision(previousState, players, updatedHistory, false, "At least one actionable player is still behind the current bet.");
            return false;
        }

        if (currentBet > 0)
        {
            var roundClosed = HasActionReturnedAfterCurrentBetWasEstablished(actionablePlayers, updatedHistory, currentBet);
            LogRoundCompletionDecision(
                previousState,
                players,
                updatedHistory,
                roundClosed,
                roundClosed
                    ? "No outstanding call and all actionable players have responded since the current bet was established."
                    : "No outstanding call but at least one actionable player has not yet responded since the current bet was established.");
            return roundClosed;
        }

        var trailingChecks = 0;
        for (var i = updatedHistory.Count - 1; i >= 0; i--)
        {
            var entry = updatedHistory[i];
            if (entry.ActionType != ActionType.Check)
                break;

            trailingChecks++;
        }

        var checksCloseRound = trailingChecks >= actionablePlayers.Length;
        LogRoundCompletionDecision(
            previousState,
            players,
            updatedHistory,
            checksCloseRound,
            $"Zero-bet street closure check with trailingChecks={trailingChecks} and actionablePlayers={actionablePlayers.Length}.");
        return checksCloseRound;
    }

    private static bool HasActionReturnedAfterCurrentBetWasEstablished(
        IReadOnlyList<SolverPlayerState> actionablePlayers,
        IReadOnlyList<SolverActionEntry> updatedHistory,
        long currentBet)
    {
        var currentBetSetterIndex = -1;
        for (var i = updatedHistory.Count - 1; i >= 0; i--)
        {
            var entry = updatedHistory[i];
            if (entry.Amount.Value != currentBet)
                continue;

            if (entry.ActionType is ActionType.Bet
                or ActionType.Raise
                or ActionType.AllIn
                or ActionType.PostSmallBlind
                or ActionType.PostBigBlind)
            {
                currentBetSetterIndex = i;
                break;
            }
        }

        if (currentBetSetterIndex < 0)
            return true;

        var mostRecentActionIndexByPlayer = new Dictionary<PlayerId, int>();
        for (var i = 0; i < updatedHistory.Count; i++)
            mostRecentActionIndexByPlayer[updatedHistory[i].PlayerId] = i;

        var betSetterEntry = updatedHistory[currentBetSetterIndex];
        var betSetterPlayerId = betSetterEntry.PlayerId;
        var requiresBetSetterOptionClosure = betSetterEntry.ActionType is ActionType.PostSmallBlind or ActionType.PostBigBlind;
        foreach (var player in actionablePlayers)
        {
            if (player.PlayerId == betSetterPlayerId && !requiresBetSetterOptionClosure)
                continue;

            if (!mostRecentActionIndexByPlayer.TryGetValue(player.PlayerId, out var lastActionIndex))
                return false;

            if (lastActionIndex <= currentBetSetterIndex)
                return false;
        }

        return true;
    }

    private static void LogStepTransition(
        string phase,
        SolverHandState state,
        LegalAction action,
        IReadOnlyList<LegalAction>? legalActions,
        bool? bettingRoundComplete,
        string note)
    {
        if (!IsDebugLoggingEnabled())
            return;

        var playerSnapshot = string.Join(
            " | ",
            state.Players.Select(p =>
                $"id={p.PlayerId.Value},seat={p.SeatIndex},pos={p.Position},active={p.IsActive},allIn={p.IsAllIn},stack={p.Stack.Value},street={p.CurrentStreetContribution.Value},total={p.TotalContribution.Value}"));

        var legalActionsText = legalActions is null
            ? "<not provided>"
            : string.Join(", ", legalActions.Select(a => $"{a.ActionType}:{a.Amount?.Value}"));

        Console.WriteLine(
            $"[SolverStateStepper:{phase}] note={note} street={state.Street} acting={state.ActingPlayerId.Value} " +
            $"action={action.ActionType}:{action.Amount?.Value} currentBet={state.CurrentBetSize.Value} toCall={state.ToCall.Value} " +
            $"pot={state.Pot.Value} bettingRoundComplete={bettingRoundComplete?.ToString() ?? "n/a"} " +
            $"legalActions=[{legalActionsText}] players=[{playerSnapshot}]");
    }

    private static void LogRoundCompletionDecision(
        SolverHandState previousState,
        IReadOnlyList<SolverPlayerState> players,
        IReadOnlyList<SolverActionEntry> updatedHistory,
        bool decision,
        string reason)
    {
        if (!IsDebugLoggingEnabled())
            return;

        var contributions = string.Join(
            " | ",
            players.Select(p =>
                $"id={p.PlayerId.Value},active={p.IsActive},allIn={p.IsAllIn},stack={p.Stack.Value},street={p.CurrentStreetContribution.Value}"));

        var trailingActions = string.Join(
            ", ",
            updatedHistory.TakeLast(Math.Min(5, updatedHistory.Count)).Select(a => $"{a.PlayerId.Value}:{a.ActionType}:{a.Amount.Value}"));

        Console.WriteLine(
            $"[SolverStateStepper:round-complete] street={previousState.Street} acting={previousState.ActingPlayerId.Value} " +
            $"previousCurrentBet={previousState.CurrentBetSize.Value} decision={decision} reason={reason} " +
            $"players=[{contributions}] recentHistory=[{trailingActions}]");
    }

    private static bool IsDebugLoggingEnabled()
    {
        var flag = Environment.GetEnvironmentVariable(DebugEnvironmentVariable);
        return string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static SolverPlayerState? ResolveNextActingPlayer(
    SolverHandState state,
    IReadOnlyList<SolverPlayerState> players,
    int actingSeatIndex,
    bool bettingRoundComplete)
    {
        if (bettingRoundComplete && state.Street == Street.Preflop)
            return null;

        var actionablePlayers = players.Where(p => p.IsActive && !p.IsAllIn && p.Stack.Value > 0).ToArray();
        if (actionablePlayers.Length == 1)
            return actionablePlayers[0];

        if (actionablePlayers.Length == 0)
            return null;

        var referenceSeat = bettingRoundComplete
            ? GetNextStreetReferenceSeat(state, players)
            : actingSeatIndex;

        var currentBet = players.Max(p => p.CurrentStreetContribution.Value);
        for (var offset = 1; offset <= players.Count; offset++)
        {
            var seat = (referenceSeat + offset) % players.Count;
            var next = players[seat];
            if (!next.IsActive || next.IsAllIn || next.Stack.Value <= 0)
                continue;

            if (!bettingRoundComplete && currentBet > 0 && next.CurrentStreetContribution.Value >= currentBet)
                continue;

            return next;
        }

        for (var offset = 1; offset <= players.Count; offset++)
        {
            var seat = (referenceSeat + offset) % players.Count;
            var next = players[seat];
            if (next.IsActive && !next.IsAllIn && next.Stack.Value > 0)
                return next;
        }

        return null;
    }

    private static int GetNextStreetReferenceSeat(SolverHandState state, IReadOnlyList<SolverPlayerState> players)
    {
        if (state.Street == Street.Preflop)
        {
            var bb = players.FirstOrDefault(p => p.Position == Position.BB);
            if (bb is not null)
                return bb.SeatIndex;
        }

        return state.ButtonSeatIndex;
    }
}
