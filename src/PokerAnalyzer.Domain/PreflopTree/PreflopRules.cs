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
        state.ActingIndex = (bigBlindIndex + 1) % playerCount;

        return state;
    }

    public static int GetToCall(PreflopPublicState state, int playerIndex)
    {
        return Math.Max(0, state.CurrentToCallBb - state.ContribBb[playerIndex]);
    }

    public static List<PreflopAction> GetLegalActions(PreflopPublicState state)
    {
        var i = state.ActingIndex;
        var toCall = GetToCall(state, i);
        var stack = state.StackBb[i];

        if (!state.InHand[i] || stack == 0)
        {
            return [];
        }

        if (toCall == 0)
        {
            return
            [
                new PreflopAction(PreflopActionType.Check),
                new PreflopAction(PreflopActionType.Fold)
            ];
        }

        if (stack >= toCall)
        {
            return
            [
                new PreflopAction(PreflopActionType.Fold),
                new PreflopAction(PreflopActionType.Call)
            ];
        }

        return [new PreflopAction(PreflopActionType.Fold)];
    }

    public static PreflopPublicState ApplyAction(PreflopPublicState state, PreflopAction action)
    {
        var newState = state.Clone();
        var i = state.ActingIndex;
        var toCall = GetToCall(state, i);

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
            default:
                throw new InvalidOperationException($"Unsupported action type: {action.Type}.");
        }

        newState.ActingIndex = GetNextActingIndex(newState);

        return newState;
    }

    public static bool IsBettingClosed(PreflopPublicState state)
    {
        var activeCount = 0;
        for (var i = 0; i < state.PlayerCount; i++)
        {
            if (state.InHand[i])
            {
                activeCount++;
            }
        }

        if (activeCount <= 1)
        {
            return true;
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
}
