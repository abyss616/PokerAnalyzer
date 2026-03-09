namespace PokerAnalyzer.Domain.Game;

public static class SolverLegalActionGenerator
{
    private const long UnopenedPreflopOpenNumerator = 5;
    private const long UnopenedPreflopOpenDenominator = 2;

    public static IReadOnlyList<LegalAction> GenerateLegalActions(
        SolverHandState state,
        IBetSizeSetProvider? sizeProvider = null
        )
    {
    

        if (SolverTraversalGuards.IsCompletedPreflopState(state))
            return Array.Empty<LegalAction>();

        var acting = state.Players.FirstOrDefault(p => p.PlayerId == state.ActingPlayerId);
        if (acting is null)
        {
            //Console.WriteLine($"GenerateLegalActions: no acting player found for ActingPlayerId={state.ActingPlayerId.Value}.");
            return Array.Empty<LegalAction>();
        }

        //Console.WriteLine(
        //    $"GenerateLegalActions: actingPlayer={acting.PlayerId.Value}, " +
        //    $"position={acting.Position}, stack={acting.Stack.Value}, " +
        //    $"streetContribution={acting.CurrentStreetContribution.Value}, " +
        //    $"isActive={acting.IsActive}, isAllIn={acting.IsAllIn}, " +
        //    $"stateCurrentBet={state.CurrentBetSize.Value}, stateLastRaise={state.LastRaiseSize.Value}, " +
        //    $"pot={state.Pot.Value}, toCall={state.ToCall.Value}, raisesThisStreet={state.RaisesThisStreet}.");

        if (!acting.IsActive || acting.IsAllIn || acting.Stack.Value <= 0)
        {
            //Console.WriteLine(
            //    $"GenerateLegalActions: player cannot act. " +
            //    $"isActive={acting.IsActive}, isAllIn={acting.IsAllIn}, stack={acting.Stack.Value}.");
            //return Array.Empty<LegalAction>();
        }

        var toCall = state.ToCall;
        var maxTotalBet = acting.CurrentStreetContribution + acting.Stack;

        //Console.WriteLine(
        //    $"GenerateLegalActions: computed maxTotalBet={maxTotalBet.Value} " +
        //    $"(streetContribution {acting.CurrentStreetContribution.Value} + stack {acting.Stack.Value}).");

        var actions = new List<LegalAction>(8);

        if (toCall.Value == 0)
        {
            actions.Add(new LegalAction(ActionType.Check));
            //Console.WriteLine("GenerateLegalActions: added Check because toCall == 0.");

            var isCheckedToSpot = state.CurrentBetSize.Value == 0;
            var aggressiveActionType = isCheckedToSpot ? ActionType.Bet : ActionType.Raise;
            var minTotalBetToCall = isCheckedToSpot
                ? ResolveMinBetTarget(acting.CurrentStreetContribution, state.LastRaiseSize)
                : state.CurrentBetSize + state.LastRaiseSize;

            //Console.WriteLine(
            //    $"GenerateLegalActions: no-call branch. isCheckedToSpot={isCheckedToSpot}, " +
            //    $"aggressiveActionType={aggressiveActionType}, minTotalBetToCall={minTotalBetToCall.Value}.");

            if (sizeProvider is null)
            {
                var defaultBetTarget = minTotalBetToCall <= maxTotalBet ? minTotalBetToCall : maxTotalBet;
                actions.Add(new LegalAction(aggressiveActionType, defaultBetTarget));
                Console.WriteLine(
                    $"GenerateLegalActions: sizeProvider is null, added default {aggressiveActionType} " +
                    $"to {defaultBetTarget.Value}.");
                return actions.AsReadOnly();
            }

            var aggressiveSizes = isCheckedToSpot
                ? GetDistinctSortedSizes(sizeProvider.GetBetSizes(state))
                : GetDistinctSortedSizes(sizeProvider.GetRaiseSizes(state));

            Console.WriteLine(
                "GenerateLegalActions: sizeProvider returned aggressive sizes = [" +
                string.Join(", ", aggressiveSizes.Select(x => x.Value)) + "].");

            AddSizedAggressionActions(
                actions,
                aggressiveActionType,
                aggressiveSizes,
                minTotalBetToCall,
                maxTotalBet,
                includeMinBound: true,
                acting.CurrentStreetContribution);

            //Console.WriteLine(
            //    "GenerateLegalActions: final actions (toCall == 0) = [" +
            //    string.Join(", ", actions.Select(a => $"{a.ActionType}:{a.Amount?.Value}")) + "].");

            return actions.AsReadOnly();
        }

        actions.Add(new LegalAction(ActionType.Fold));
        Console.WriteLine("GenerateLegalActions: added Fold.");

        // IMPORTANT: Call uses TO-AMOUNT semantics here, consistent with solver action history.
        var callDelta = toCall.Value > acting.Stack.Value ? acting.Stack : toCall;
        var callTarget = acting.CurrentStreetContribution + callDelta;

        //Console.WriteLine(
        //    $"GenerateLegalActions: callDelta={callDelta.Value}, " +
        //    $"callTarget={callTarget.Value}, currentContribution={acting.CurrentStreetContribution.Value}.");

        if (callTarget > acting.CurrentStreetContribution)
        {
            actions.Add(new LegalAction(ActionType.Call, callTarget));
            //Console.WriteLine($"GenerateLegalActions: added Call to {callTarget.Value}.");
        }
        else
        {
            //Console.WriteLine(
            //    $"GenerateLegalActions: skipped Call because callTarget ({callTarget.Value}) " +
            //    $"<= currentContribution ({acting.CurrentStreetContribution.Value}).");
        }

        if (IsUnopenedPreflopSpot(state))
        {
            var unopenedOpenSize = ResolveUnopenedPreflopOpenSize(state.Config.BigBlind);
            var minTotalBetInUnopened = state.CurrentBetSize + state.LastRaiseSize;

            //Console.WriteLine(
            //    $"GenerateLegalActions: unopened preflop spot. unopenedOpenSize={unopenedOpenSize.Value}, " +
            //    $"minTotalBetInUnopened={minTotalBetInUnopened.Value}, maxTotalBet={maxTotalBet.Value}.");

            if (unopenedOpenSize >= minTotalBetInUnopened && unopenedOpenSize <= maxTotalBet)
            {
                actions.Add(new LegalAction(ActionType.Raise, unopenedOpenSize));
               // Console.WriteLine($"GenerateLegalActions: added unopened Raise to {unopenedOpenSize.Value}.");
            }
            else
            {
                //Console.WriteLine("GenerateLegalActions: unopened Raise not added because target is out of bounds.");
            }

            //Console.WriteLine(
            //    "GenerateLegalActions: final actions (unopened preflop) = [" +
            //    string.Join(", ", actions.Select(a => $"{a.ActionType}:{a.Amount?.Value}")) + "].");

            return actions.AsReadOnly();
        }

        var minTotalBet = state.CurrentBetSize + state.LastRaiseSize;
        var canFullRaise = maxTotalBet >= minTotalBet;

        //Console.WriteLine(
        //    $"GenerateLegalActions: post-open spot. minTotalBet={minTotalBet.Value}, " +
        //    $"maxTotalBet={maxTotalBet.Value}, canFullRaise={canFullRaise}.");

        if (sizeProvider is null)
        {
            if (canFullRaise)
            {
                actions.Add(new LegalAction(ActionType.Raise, minTotalBet));
                //Console.WriteLine($"GenerateLegalActions: sizeProvider is null, added default Raise to {minTotalBet.Value}.");
            }
            else
            {
                //Console.WriteLine("GenerateLegalActions: sizeProvider is null, no full raise available.");
            }

            //Console.WriteLine(
            //    "GenerateLegalActions: final actions = [" +
            //    string.Join(", ", actions.Select(a => $"{a.ActionType}:{a.Amount?.Value}")) + "].");

            return actions.AsReadOnly();
        }
        var raiseSizes = GetDistinctSortedSizes(sizeProvider.GetRaiseSizes(state));
        //Console.WriteLine(
        //    "GenerateLegalActions: sizeProvider returned raise sizes = [" +
        //    string.Join(", ", raiseSizes.Select(x => x.Value)) + "].");

        if (canFullRaise)
        {
            AddSizedAggressionActions(
                actions,
                ActionType.Raise,
                raiseSizes,
                minTotalBet,
                maxTotalBet,
                includeMinBound: true,
                acting.CurrentStreetContribution);

            //Console.WriteLine(
            //    "GenerateLegalActions: after AddSizedAggressionActions, actions = [" +
            //    string.Join(", ", actions.Select(a => $"{a.ActionType}:{a.Amount?.Value}")) + "].");
        }
        else
        {
        //    Console.WriteLine(
        //        "GenerateLegalActions: no full raise available; short all-in raise not modeled, so no Raise action added.");
        }

        return actions.AsReadOnly();
    }

    private static bool IsUnopenedPreflopSpot(SolverHandState state)
    {
    //    Console.WriteLine(
    //$"IsUnopenedPreflopSpot: street={state.Street}, raisesThisStreet={state.RaisesThisStreet}, " +
    //$"currentBet={state.CurrentBetSize.Value}, lastRaise={state.LastRaiseSize.Value}, " +
    //$"actionHistory=[{string.Join(", ", state.ActionHistory.Select(a => $"{a.ActionType}:{a.Amount.Value}"))}]");
        if (state.Street != Street.Preflop)
            return false;

        // Unopened preflop means no voluntary aggressive action has occurred yet.
        // Blind posts do not count as opening the pot.
        var isAggressive = state.ActionHistory.Any(a =>
            a.ActionType == ActionType.Bet ||
            a.ActionType == ActionType.Raise ||
            a.ActionType == ActionType.AllIn);
        return !isAggressive;
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
        bool includeMinBound,
        ChipAmount currentContribution)
    {
        foreach (var targetAmount in candidateSizes)
        {
            if (targetAmount <= currentContribution)
                continue;

            var aboveMin = includeMinBound ? targetAmount >= minTotalBet : targetAmount > minTotalBet;
            if (!aboveMin)
                continue;

            if (targetAmount > maxTotalBet)
                continue;

            actions.Add(new LegalAction(actionType, targetAmount));
        }

        Console.WriteLine(
    $"AddSizedAggressionActions: actionType={actionType}, minTotalBet={minTotalBet.Value}, " +
    $"maxTotalBet={maxTotalBet.Value}, currentContribution={currentContribution.Value}, " +
    $"candidateSizes=[{string.Join(", ", candidateSizes.Select(x => x.Value))}]");

        foreach (var targetAmount in candidateSizes)
        {
            Console.WriteLine(
                $"  candidate target={targetAmount.Value}, " +
                $"gtContribution={targetAmount > currentContribution}, " +
                $"meetsMin={(includeMinBound ? targetAmount >= minTotalBet : targetAmount > minTotalBet)}, " +
                $"leMax={targetAmount <= maxTotalBet}");
        }

        Console.WriteLine(
            $"  jamFallbackWillBeAdded=" +
            $"{((includeMinBound ? maxTotalBet >= minTotalBet : maxTotalBet > minTotalBet) && !actions.Any(a => a.ActionType == actionType && a.Amount == maxTotalBet))}");

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
