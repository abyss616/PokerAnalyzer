using PokerAnalyzer.Domain.Game;

namespace PokerAnalyzer.Infrastructure.Engines;

public sealed class PreflopStateExtractor
{
    public PreflopExtractionResult TryExtract(
        IReadOnlyList<PlayerSeat> seats,
        IReadOnlyList<PreflopInputAction> actionsBeforeDecision,
        PlayerId actingPlayerId,
        decimal smallBlind,
        decimal bigBlind)
    {
        var byId = seats.ToDictionary(s => s.Id);
        var contrib = seats.ToDictionary(s => s.Id, _ => 0m);
        var stacks = seats.ToDictionary(s => s.Id, s => (decimal)s.StartingStack.Value);
        var pot = 0m;
        var betToCall = 0m;
        var raiseDepth = 0;
        PlayerId? lastAggressor = null;

        void PostBlind(Position position, decimal amount)
        {
            var seat = seats.FirstOrDefault(s => s.Position == position);
            if (seat is null || amount <= 0) return;
            ApplyDelta(seat.Id, amount);
            betToCall = Math.Max(betToCall, contrib[seat.Id]);
        }

        PostBlind(Position.SB, smallBlind);
        PostBlind(Position.BB, bigBlind);

        var raw = new List<PreflopRawActionTrace>();
        foreach (var act in actionsBeforeDecision)
        {
            if (!byId.ContainsKey(act.PlayerId))
                continue;

            var amountChips = act.AmountBb * bigBlind;
            raw.Add(new PreflopRawActionTrace(Street.Preflop, act.PlayerId, byId[act.PlayerId].Position, act.Type, amountChips, act.AmountBb));

            switch (act.Type)
            {
                case "POST_SB":
                case "POST_BB":
                    ApplyDelta(act.PlayerId, amountChips);
                    betToCall = Math.Max(betToCall, contrib[act.PlayerId]);
                    break;
                case "RAISE_TO":
                case "ALL_IN":
                    ApplyToAmount(act.PlayerId, amountChips);
                    if (contrib[act.PlayerId] > betToCall)
                    {
                        betToCall = contrib[act.PlayerId];
                        lastAggressor = act.PlayerId;
                        raiseDepth++;
                    }
                    break;
                case "CALL":
                    ApplyDelta(act.PlayerId, amountChips);
                    break;
                case "CHECK":
                case "FOLD":
                case "TYPE_4":
                    break;
            }
        }

        var actingSeat = byId[actingPlayerId];
        var toCallChips = Math.Max(0, betToCall - contrib[actingPlayerId]);
        var toCallBb = bigBlind == 0 ? 0 : decimal.Round(toCallChips / bigBlind, 2);
        var currentBetBb = bigBlind == 0 ? 0 : decimal.Round(betToCall / bigBlind, 2);
        var actingContribBb = bigBlind == 0 ? 0 : decimal.Round(contrib[actingPlayerId] / bigBlind, 2);
        var potBb = bigBlind == 0 ? 0 : decimal.Round(pot / bigBlind, 2);

        var historySignature = BuildSignature(actingSeat.Position, raiseDepth);
        var bigBlindSeat = seats.FirstOrDefault(s => s.Position == Position.BB);
        Position? facingPos = lastAggressor.HasValue
            ? byId[lastAggressor.Value].Position
            : bigBlindSeat?.Position;
        var facingStack = lastAggressor.HasValue
            ? stacks[lastAggressor.Value]
            : (bigBlindSeat is not null ? stacks[bigBlindSeat.Id] : stacks.Values.Max());
        var effectiveStackBb = decimal.Round(Math.Min(stacks[actingPlayerId], facingStack) / bigBlind, 2);

        var openBucket = PreflopSizingNormalizer.Bucket(currentBetBb);
        var threeBetBucket = raiseDepth >= 2 ? PreflopSizingNormalizer.Bucket(currentBetBb) : "NA";
        var fourBetBucket = raiseDepth >= 3 ? PreflopSizingNormalizer.Bucket(currentBetBb) : "NA";
        var isoBucket = "NA";
        var squeezeBucket = "NA";
        var jamThreshold = 18m;

        var solverKey = $"{historySignature}|A:{actingSeat.Position}|F:{facingPos?.ToString() ?? "NONE"}|D:{raiseDepth}|TC:{toCallBb:0.##}|O:{openBucket}|3B:{threeBetBucket}|4B:{fourBetBucket}";
        var key = new PreflopInfoSetKey(
            actingSeat.Position,
            facingPos,
            historySignature,
            raiseDepth,
            toCallBb,
            effectiveStackBb,
            openBucket,
            isoBucket,
            threeBetBucket,
            squeezeBucket,
            fourBetBucket,
            jamThreshold,
            solverKey);

        var ctx = new PreflopSpotContext(
            actingPlayerId,
            actingSeat.Position,
            lastAggressor,
            facingPos,
            raiseDepth,
            toCallBb,
            currentBetBb,
            actingContribBb,
            potBb,
            effectiveStackBb);

        var trace = new PreflopQueryTrace
        {
            ActingPlayerId = actingPlayerId,
            ActingPosition = actingSeat.Position,
            FacingPlayerId = lastAggressor,
            FacingPosition = facingPos,
            HistorySignature = historySignature,
            RaiseDepth = raiseDepth,
            ToCallBb = toCallBb,
            CurrentBetBb = currentBetBb,
            ActingContribBb = actingContribBb,
            PotBb = potBb,
            EffectiveStackBb = effectiveStackBb,
            OpenSizeBucket = openBucket,
            IsoSizeBucket = isoBucket,
            ThreeBetBucket = threeBetBucket,
            SqueezeBucket = squeezeBucket,
            FourBetBucket = fourBetBucket,
            JamThreshold = jamThreshold,
            SolverKey = solverKey,
            RawActionHistory = raw
        };

        var validation = PreflopKeyValidator.Validate(key, ctx);
        if (!validation.IsValid)
            return PreflopExtractionResult.Unsupported(validation.Reason ?? "Unsupported preflop key.", trace);

        return PreflopExtractionResult.Supported(key, trace);

        void ApplyDelta(PlayerId playerId, decimal delta)
        {
            if (delta <= 0) return;
            contrib[playerId] += delta;
            stacks[playerId] -= delta;
            pot += delta;
        }

        void ApplyToAmount(PlayerId playerId, decimal toAmount)
        {
            var delta = toAmount - contrib[playerId];
            if (delta > 0)
                ApplyDelta(playerId, delta);
        }
    }

    private static string BuildSignature(Position acting, int raiseDepth)
    {
        if (raiseDepth == 0)
            return acting == Position.SB ? "UNOPENED_SB" : "OPEN";

        return raiseDepth switch
        {
            1 => "VS_OPEN",
            2 => "VS_3BET",
            3 => "VS_4BET",
            _ => "VS_5BET"
        };
    }
}

public sealed record PreflopInputAction(PlayerId PlayerId, string Type, decimal AmountBb);
