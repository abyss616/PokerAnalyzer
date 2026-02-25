namespace PokerAnalyzer.Domain.PreflopTree;

public static class PreflopRules
{
    public static PreflopPublicState CreateInitialState(
        int playerCount,
        int stackBb,
        int smallBlindBb,
        int bigBlindBb)
    {
        if (playerCount < 2 || playerCount > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "Player count must be between 2 and 6.");
        }

        if (smallBlindBb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(smallBlindBb), "Small blind must be greater than 0.");
        }

        if (bigBlindBb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bigBlindBb), "Big blind must be greater than 0.");
        }

        if (stackBb <= bigBlindBb)
        {
            throw new ArgumentOutOfRangeException(nameof(stackBb), "Stack must be greater than the big blind.");
        }

        var state = new PreflopPublicState
        {
            PlayerCount = playerCount,
            InHand = Enumerable.Repeat(true, playerCount).ToArray(),
            ContribBb = new int[playerCount],
            StackBb = Enumerable.Repeat(stackBb, playerCount).ToArray(),
            PotBb = 0
        };

        var smallBlindIndex = playerCount == 2 ? 0 : 1;
        var bigBlindIndex = playerCount == 2 ? 1 : 2;

        state.ContribBb[smallBlindIndex] = smallBlindBb;
        state.StackBb[smallBlindIndex] -= smallBlindBb;
        state.PotBb += smallBlindBb;

        state.ContribBb[bigBlindIndex] = bigBlindBb;
        state.StackBb[bigBlindIndex] -= bigBlindBb;
        state.PotBb += bigBlindBb;

        state.CurrentToCallBb = bigBlindBb;
        state.LastRaiseToBb = bigBlindBb;
        state.RaisesCount = 1;
        state.LastAggressorIndex = bigBlindIndex;
        state.LastActionWasRaiseByIndex = null;
        state.BettingClosed = false;
        state.ActingIndex = (bigBlindIndex + 1) % playerCount;

        return state;
    }

    public static int GetToCall(PreflopPublicState state, int playerIndex)
    {
        return Math.Max(0, state.CurrentToCallBb - state.ContribBb[playerIndex]);
    }

    public static List<PreflopAction> GetLegalActions(PreflopPublicState state, PreflopSizingConfig sizing)
    {
        var i = state.ActingIndex;
        if (!state.InHand[i] || state.StackBb[i] == 0)
        {
            return [];
        }

        var toCall = GetToCall(state, i);
        var maxRaiseTo = state.ContribBb[i] + state.StackBb[i];
        var legal = new List<PreflopAction>
        {
            new(PreflopActionType.Fold)
        };

        if (toCall == 0)
        {
            legal.Add(new PreflopAction(PreflopActionType.Check));

            foreach (var raiseTo in sizing.OpenRaiseToBb)
            {
                if (raiseTo > state.CurrentToCallBb && raiseTo <= maxRaiseTo)
                {
                    legal.Add(new PreflopAction(PreflopActionType.RaiseTo, raiseTo));
                }
            }

            if (sizing.AllowAllInAlways && maxRaiseTo > state.CurrentToCallBb)
            {
                legal.Add(new PreflopAction(PreflopActionType.AllIn));
            }

            return legal;
        }

        if (toCall <= state.StackBb[i])
        {
            legal.Add(new PreflopAction(PreflopActionType.Call));
        }

        var raiseSizes = state.RaisesCount switch
        {
            1 => sizing.ThreeBetToBb,
            2 => sizing.FourBetToBb,
            _ => Array.Empty<int>()
        };

        foreach (var raiseTo in raiseSizes)
        {
            if (raiseTo > state.CurrentToCallBb && raiseTo <= maxRaiseTo)
            {
                legal.Add(new PreflopAction(PreflopActionType.RaiseTo, raiseTo));
            }
        }

        if (sizing.AllowAllInAlways && maxRaiseTo > state.CurrentToCallBb)
        {
            legal.Add(new PreflopAction(PreflopActionType.AllIn));
        }

        return legal;
    }

    public static PreflopPublicState ApplyAction(PreflopPublicState state, PreflopAction action)
    {
        var newState = state.Clone();
        var i = state.ActingIndex;
        var toCall = GetToCall(state, i);
        var nextActing = i;

        switch (action.Type)
        {
            case PreflopActionType.Fold:
                newState.InHand[i] = false;
                break;
            case PreflopActionType.Check:
                if (toCall != 0)
                {
                    throw new InvalidOperationException("Check is only legal when there is nothing to call.");
                }

                break;
            case PreflopActionType.Call:
                if (toCall <= 0)
                {
                    throw new InvalidOperationException("Call is only legal when facing a bet.");
                }

                if (state.StackBb[i] < toCall)
                {
                    throw new InvalidOperationException("Call is only legal when stack covers the amount to call.");
                }

                newState.ContribBb[i] += toCall;
                newState.StackBb[i] -= toCall;
                newState.PotBb += toCall;
                break;
            case PreflopActionType.RaiseTo:
                ApplyRaiseTo(state, newState, i, action.RaiseToBb);
                break;
            case PreflopActionType.AllIn:
                ApplyAllIn(state, newState, i);
                break;
            default:
                throw new InvalidOperationException($"Unsupported action type: {action.Type}.");
        }

        if (!IsTerminal(newState, out _))
        {
            nextActing = GetNextActingIndex(newState);
            if (IsBettingClosed(newState))
            {
                // In unopened pots the big blind is entitled to an option after everyone calls.
                // Do not close the round until that option is exercised.
                var unopenedBigBlindOptionPending =
                    state.LastActionWasRaiseByIndex is null &&
                    state.RaisesCount == 1 &&
                    nextActing == state.LastAggressorIndex;

                if (!unopenedBigBlindOptionPending)
                {
                    newState.BettingClosed = true;
                }
            }
        }

        if (IsTerminal(newState, out _))
        {
            return newState;
        }

        if (nextActing == i)
        {
            newState.BettingClosed = true;
            return newState;
        }

        newState.ActingIndex = nextActing;
        return newState;
    }

    public static bool IsBettingClosed(PreflopPublicState state)
    {
        var activeCount = 0;
        var allInCount = 0;
        for (var i = 0; i < state.PlayerCount; i++)
        {
            if (state.InHand[i])
            {
                activeCount++;
                if (IsAllIn(state, i))
                {
                    allInCount++;
                }
            }
        }

        if (activeCount <= 1)
        {
            return true;
        }

        if (allInCount == activeCount)
        {
            return false;
        }

        for (var i = 0; i < state.PlayerCount; i++)
        {
            if (!state.InHand[i] || IsAllIn(state, i))
            {
                continue;
            }

            if (state.ContribBb[i] != state.CurrentToCallBb)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsTerminal(PreflopPublicState state, out string reason)
    {
        var inHandCount = state.InHand.Count(x => x);
        if (inHandCount <= 1)
        {
            reason = "AllFolded";
            return true;
        }

        var allInCount = 0;
        for (var i = 0; i < state.PlayerCount; i++)
        {
            if (!state.InHand[i])
            {
                continue;
            }

            if (state.StackBb[i] == 0)
            {
                allInCount++;
            }
            else
            {
                allInCount = 0;
                break;
            }
        }

        if (allInCount > 0)
        {
            reason = "AllIn";
            return true;
        }

        if (state.BettingClosed)
        {
            reason = "BettingClosed";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public static int GetNextActingIndex(PreflopPublicState state)
    {
        for (var offset = 1; offset <= state.PlayerCount; offset++)
        {
            var index = (state.ActingIndex + offset) % state.PlayerCount;
            if (state.InHand[index] && !IsAllIn(state, index))
            {
                return index;
            }
        }

        return state.ActingIndex;
    }

    public static int NextActingIndex(PreflopPublicState state)
    {
        return GetNextActingIndex(state);
    }

    public static bool IsAllIn(PreflopPublicState state, int playerIndex)
    {
        return state.StackBb[playerIndex] == 0;
    }

    private static void ApplyRaiseTo(PreflopPublicState previousState, PreflopPublicState state, int playerIndex, int raiseTo)
    {
        if (raiseTo <= previousState.CurrentToCallBb)
        {
            throw new InvalidOperationException("RaiseTo must be greater than current amount to call.");
        }

        var maxRaiseTo = previousState.ContribBb[playerIndex] + previousState.StackBb[playerIndex];
        if (raiseTo > maxRaiseTo)
        {
            throw new InvalidOperationException("RaiseTo cannot exceed player all-in amount.");
        }

        var delta = raiseTo - previousState.ContribBb[playerIndex];
        state.ContribBb[playerIndex] = raiseTo;
        state.StackBb[playerIndex] -= delta;
        state.PotBb += delta;
        state.CurrentToCallBb = raiseTo;
        state.LastRaiseToBb = raiseTo;
        state.RaisesCount += 1;
        state.LastAggressorIndex = playerIndex;
        state.LastActionWasRaiseByIndex = playerIndex;
        state.BettingClosed = false;
    }

    private static void ApplyAllIn(PreflopPublicState previousState, PreflopPublicState state, int playerIndex)
    {
        var raiseTo = previousState.ContribBb[playerIndex] + previousState.StackBb[playerIndex];
        if (raiseTo <= previousState.ContribBb[playerIndex])
        {
            throw new InvalidOperationException("AllIn is only legal when player has chips remaining.");
        }

        if (raiseTo == previousState.CurrentToCallBb)
        {
            var toCall = GetToCall(previousState, playerIndex);
            state.ContribBb[playerIndex] += toCall;
            state.StackBb[playerIndex] -= toCall;
            state.PotBb += toCall;
            return;
        }

        if (raiseTo < previousState.CurrentToCallBb)
        {
            throw new InvalidOperationException("AllIn below the amount to call is not supported in this abstraction.");
        }

        ApplyRaiseTo(previousState, state, playerIndex, raiseTo);
    }
}
