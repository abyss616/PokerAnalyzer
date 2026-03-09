namespace PokerAnalyzer.Domain.Game;

public static class SolverLegalActionGenerator
{
    private const long UnopenedPreflopOpenNumerator = 5;
    private const long UnopenedPreflopOpenDenominator = 2;

    public static IReadOnlyList<LegalAction> GenerateLegalActions(SolverHandState state, IBetSizeSetProvider? sizeProvider = null)
    {
        var acting = state.Players.FirstOrDefault(p => p.PlayerId == state.ActingPlayerId);
        if (acting is null || !acting.IsActive || acting.IsAllIn || acting.Stack.Value <= 0)
            return Array.Empty<LegalAction>();

        var toCall = state.ToCall;
        var maxTotalBet = acting.CurrentStreetContribution + acting.Stack;

        var actions = new List<LegalAction>(8);

        if (toCall.Value == 0)
        {
            actions.Add(new LegalAction(ActionType.Check));

            var minTotalBetToCall = ResolveMinBetTarget(acting.CurrentStreetContribution, state.LastRaiseSize);

            if (sizeProvider is null)
            {
                var defaultBetTarget = minTotalBetToCall <= maxTotalBet ? minTotalBetToCall : maxTotalBet;
                actions.Add(new LegalAction(ActionType.Bet, defaultBetTarget));
                return actions.AsReadOnly();
            }

            var betSizes = GetDistinctSortedSizes(sizeProvider.GetBetSizes(state));
            AddSizedAggressionActions(actions, ActionType.Bet, betSizes, minTotalBetToCall, maxTotalBet, includeMinBound: true);

            // AllIn exists in the enum but this solver model represents jams as Bet/Raise amounts for deterministic sizing.
            return actions.AsReadOnly();
        }

        actions.Add(new LegalAction(ActionType.Fold));

        var callAmount = toCall.Value > acting.Stack.Value ? acting.Stack : toCall;
        if (callAmount.Value > 0)
            actions.Add(new LegalAction(ActionType.Call, callAmount));

        if (IsUnopenedPreflopSpot(state))
        {
            var unopenedOpenSize = ResolveUnopenedPreflopOpenSize(state.Config.BigBlind);
            var minTotalBetInUnopened = state.CurrentBetSize + state.LastRaiseSize;

            if (unopenedOpenSize >= minTotalBetInUnopened && unopenedOpenSize <= maxTotalBet)
                actions.Add(new LegalAction(ActionType.Raise, unopenedOpenSize));

            return actions.AsReadOnly();
        }

        var minTotalBet = state.CurrentBetSize + state.LastRaiseSize;
        var canFullRaise = maxTotalBet >= minTotalBet;

        if (sizeProvider is null)
        {
            if (canFullRaise)
                actions.Add(new LegalAction(ActionType.Raise, minTotalBet));

            return actions.AsReadOnly();
        }

        var raiseSizes = GetDistinctSortedSizes(sizeProvider.GetRaiseSizes(state));
        if (canFullRaise)
        {
            AddSizedAggressionActions(actions, ActionType.Raise, raiseSizes, minTotalBet, maxTotalBet, includeMinBound: true);
        }
        else
        {
            // No full raise is available; keep the model deterministic and avoid generating short all-in raise
            // categories until explicitly modeled by the domain.
        }

        return actions.AsReadOnly();
    }

    private static bool IsUnopenedPreflopSpot(SolverHandState state)
    {
        if (state.Street != Street.Preflop)
            return false;

        if (state.RaisesThisStreet != 0)
            return false;

        if (state.CurrentBetSize != state.Config.BigBlind)
            return false;

        if (state.LastRaiseSize != state.Config.BigBlind)
            return false;

        return state.ActionHistory.All(action => action.ActionType is ActionType.PostSmallBlind or ActionType.PostBigBlind);
    }

    private static ChipAmount ResolveUnopenedPreflopOpenSize(ChipAmount bigBlind)
    {
        var scaled = checked(bigBlind.Value * UnopenedPreflopOpenNumerator);
        if (scaled % UnopenedPreflopOpenDenominator != 0)
        {
            throw new InvalidOperationException(
                $"Configured big blind {bigBlind.Value} does not support exact 2.5bb sizing in integer-chip representation.");
        }

        return new ChipAmount(scaled / UnopenedPreflopOpenDenominator);
    }

    private static ChipAmount ResolveMinBetTarget(ChipAmount currentContribution, ChipAmount lastRaiseSize)
    {
        var minBetIncrement = Math.Max(1, lastRaiseSize.Value);
        return currentContribution + new ChipAmount(minBetIncrement);
    }

    private static void AddSizedAggressionActions(
        ICollection<LegalAction> actions,
        ActionType actionType,
        IReadOnlyList<ChipAmount> candidateSizes,
        ChipAmount minTotalBet,
        ChipAmount maxTotalBet,
        bool includeMinBound)
    {
        foreach (var size in candidateSizes)
        {
            var aboveMin = includeMinBound ? size >= minTotalBet : size > minTotalBet;
            if (!aboveMin)
                continue;

            if (size > maxTotalBet)
                continue;

            actions.Add(new LegalAction(actionType, size));
        }

        // Always include a jam amount as a deterministic fallback if it is legal and not duplicated.
        if ((includeMinBound ? maxTotalBet >= minTotalBet : maxTotalBet > minTotalBet)
            && !actions.Any(a => a.ActionType == actionType && a.Amount == maxTotalBet))
        {
            actions.Add(new LegalAction(actionType, maxTotalBet));
        }
    }

    private static IReadOnlyList<ChipAmount> GetDistinctSortedSizes(IReadOnlyList<ChipAmount>? sizes)
    {
        if (sizes is null || sizes.Count == 0)
            return Array.Empty<ChipAmount>();

        return sizes
            .Where(size => size.Value > 0)
            .Distinct()
            .OrderBy(size => size.Value)
            .ToArray();
    }
}
