namespace PokerAnalyzer.Domain.PreflopTree;

public static class PreflopRules
{
    public static int GetToCall(PreflopPublicState state, int playerIndex)
    {
        return int.Max(0, state.CurrentToCallBb - state.ContribBb[playerIndex]);
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

    public static int NextActingIndex(PreflopPublicState state)
    {
        for (var offset = 1; offset <= state.PlayerCount; offset++)
        {
            var index = (state.ActingIndex + offset) % state.PlayerCount;
            if (state.InHand[index] && !IsAllIn(state, index))
            {
                return index;
            }
        }

        return -1;
    }

    public static bool IsAllIn(PreflopPublicState state, int playerIndex)
    {
        return state.ContribBb[playerIndex] >= state.StackBb[playerIndex];
    }
}
