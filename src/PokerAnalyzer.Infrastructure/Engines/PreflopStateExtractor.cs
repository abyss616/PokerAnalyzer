using PokerAnalyzer.Domain.Game;
using System.Diagnostics;
using System.Globalization;

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
        if (seats.Count == 0)
            return Unsupported("Cannot extract preflop key: no seats were provided.", []);

        if (bigBlind <= 0)
            return Unsupported($"Cannot extract preflop key: invalid big blind ({bigBlind}).", []);


        if (!byId.ContainsKey(actingPlayerId))
            return Unsupported("Cannot extract preflop key: acting player was not found in seat list.", []);

        var contrib = seats.ToDictionary(s => s.Id, _ => 0m);
        var stacks = seats.ToDictionary(s => s.Id, s => (decimal)s.StartingStack.Value);
        var pot = 0m;
        var betToCall = 0m;
        var raiseDepth = 0;
        PlayerId? lastAggressor = null;
        var hadPriorCallOrCompletion = false;
        var hasOpen = false;
        var hadColdCallerAfterOpen = false;
        var playersWithPriorVoluntaryAction = new HashSet<PlayerId>();
        var callerPlayerIds = new HashSet<PlayerId>();
        var foldedPlayers = new HashSet<PlayerId>();
        decimal? openSizeBucketBb = null;
        decimal? threeBetSizeBucketBb = null;
        decimal? squeezeSizeBucketBb = null;
        decimal? fourBetSizeBucketBb = null;

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
        try
        {
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
                        // Table blinds are posted up front, but action history can also include
                        // additional blind posts (e.g. dead/missed blinds by non-SB/BB players).
                        // Applying to the target amount avoids double-counting regular blinds
                        // while still accounting for extra posted blinds.
                        ApplyToAmount(act.PlayerId, amountChips);
                        betToCall = Math.Max(betToCall, contrib[act.PlayerId]);
                        break;
                    case "RAISE_TO":
                    case "ALL_IN":
                        // Only ignore parser-noise "min-opens" from blinds in unopened pots.
                        // CO/BTN/etc opens to 2bb are real opens and must advance the tree (fixture_18).
                        var pos = byId[act.PlayerId].Position;

                        if (raiseDepth == 0 && betToCall <= bigBlind && act.AmountBb <= 2m
                            && (pos == Position.SB || pos == Position.BB))
                        {
                            break;
                        }

                        ApplyToAmount(act.PlayerId, amountChips);
                        if (contrib[act.PlayerId] > betToCall)
                        {
                            betToCall = contrib[act.PlayerId];
                            lastAggressor = act.PlayerId;
                            raiseDepth++;

                            var raiseSizeBb = decimal.Round(contrib[act.PlayerId] / bigBlind, 2);
                            if (!hasOpen)
                            {
                                hasOpen = true;
                                openSizeBucketBb = raiseSizeBb;
                            }
                            else if (raiseDepth == 2)
                            {
                                if (hadColdCallerAfterOpen)
                                {
                                    squeezeSizeBucketBb = raiseSizeBb;
                                }
                                else
                                {
                                    threeBetSizeBucketBb = raiseSizeBb;
                                }
                            }
                            else if (raiseDepth == 3)
                            {
                                fourBetSizeBucketBb = raiseSizeBb;
                            }

                            playersWithPriorVoluntaryAction.Add(act.PlayerId);
                        }
                        break;

                    case "CALL":
                        ApplyDelta(act.PlayerId, amountChips);
                        hadPriorCallOrCompletion = true;
                        callerPlayerIds.Add(act.PlayerId);
                        if (hasOpen && raiseDepth == 1 && !playersWithPriorVoluntaryAction.Contains(act.PlayerId))
                            hadColdCallerAfterOpen = true;
                        playersWithPriorVoluntaryAction.Add(act.PlayerId);
                        break;
                    case "CHECK":
                    case "TYPE_4":
                        break;
                    case "FOLD":
                        foldedPlayers.Add(act.PlayerId);
                        callerPlayerIds.Remove(act.PlayerId);
                        break;
                }
            }

            var actingSeat = byId[actingPlayerId];
            var currentBetBb = bigBlind == 0 ? 0 : decimal.Round(betToCall / bigBlind, 2);
            var actingContribBb = bigBlind == 0 ? 0 : decimal.Round(contrib[actingPlayerId] / bigBlind, 2);
            var toCallBb = Math.Max(0m, decimal.Round(currentBetBb - actingContribBb, 2));
            var potBb = bigBlind == 0 ? 0 : decimal.Round(pot / bigBlind, 2);
            var activeOpponentCount = seats.Count(s => s.Id != actingPlayerId && !foldedPlayers.Contains(s.Id));
            var callerCount = callerPlayerIds.Count(id => id != actingPlayerId && !foldedPlayers.Contains(id));
            var playersBehindCount = CountPlayersBehind(seats, foldedPlayers, actingPlayerId);

            var priorActionsBeforeActing = raw.ToArray();
            var historySignature = BuildSignature(
                actingSeat.Position,
                raiseDepth,
                hadPriorCallOrCompletion,
                hadColdCallerAfterOpen,
                toCallBb);

            var bigBlindSeat = seats.FirstOrDefault(s => s.Position == Position.BB);
            Position? facingPos = lastAggressor.HasValue
                ? byId[lastAggressor.Value].Position
                : bigBlindSeat?.Position;
            var facingStack = lastAggressor.HasValue
                ? stacks[lastAggressor.Value]
                : (bigBlindSeat is not null ? stacks[bigBlindSeat.Id] : stacks.Values.Max());
            var effectiveStackBb = decimal.Round(Math.Min(stacks[actingPlayerId], facingStack) / bigBlind, 2);

            decimal? isoSizeBucketBb = null;
            decimal? jamThresholdBucketBb = 18m;

            var solverKey = BuildSolverKey(
                historySignature,
                actingSeat.Position,
                effectiveStackBb,
                openSizeBucketBb,
                isoSizeBucketBb,
                threeBetSizeBucketBb,
                squeezeSizeBucketBb,
                fourBetSizeBucketBb,
                jamThresholdBucketBb);
            var key = new PreflopInfoSetKey(
                actingSeat.Position,
                facingPos,
                historySignature,
                raiseDepth,
                toCallBb,
                effectiveStackBb,
                openSizeBucketBb,
                isoSizeBucketBb,
                threeBetSizeBucketBb,
                squeezeSizeBucketBb,
                fourBetSizeBucketBb,
                jamThresholdBucketBb,
                solverKey,
                activeOpponentCount,
                callerCount,
                playersBehindCount);

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
                effectiveStackBb,
                activeOpponentCount,
                callerCount,
                playersBehindCount);

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
                OpenSizeBucket = FormatBucket(openSizeBucketBb),
                IsoSizeBucket = FormatBucket(isoSizeBucketBb),
                ThreeBetBucket = FormatBucket(threeBetSizeBucketBb),
                SqueezeBucket = FormatBucket(squeezeSizeBucketBb),
                FourBetBucket = FormatBucket(fourBetSizeBucketBb),
                JamThreshold = jamThresholdBucketBb ?? 0m,
                SolverKey = solverKey,
                RawActionHistory = raw,
                PriorActionsBeforeActing = priorActionsBeforeActing,
                HadPriorCallOrCompletion = hadPriorCallOrCompletion,
                ActiveOpponentCount = activeOpponentCount,
                CallerCount = callerCount,
                PlayersBehindCount = playersBehindCount,
                ActingPlayersFirstActionType = null
            };

            WriteDebugTrace(
                priorActionsBeforeActing,
                hadPriorCallOrCompletion,
                actingSeat.Position,
                toCallBb,
                currentBetBb,
                historySignature,
                solverKey);

            var validation = PreflopKeyValidator.Validate(key, ctx);
            if (!validation.IsValid)
                return PreflopExtractionResult.Unsupported(validation.Reason ?? "Unsupported preflop key.", trace);

            return PreflopExtractionResult.Supported(key, trace);
        }
        catch (Exception ex)
        {
            return Unsupported($"Preflop extraction failed: {ex.Message}", raw);
        }

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

        PreflopExtractionResult Unsupported(string reason, IReadOnlyList<PreflopRawActionTrace> rawActions)
        {
            var trace = new PreflopQueryTrace
            {
                ActingPlayerId = actingPlayerId,
                ActingPosition = byId.TryGetValue(actingPlayerId, out var seat) ? seat.Position : Position.Unknown,
                FacingPlayerId = null,
                FacingPosition = null,
                HistorySignature = "UNSUPPORTED",
                RaiseDepth = 0,
                ToCallBb = 0,
                CurrentBetBb = 0,
                ActingContribBb = 0,
                PotBb = 0,
                EffectiveStackBb = 0,
                OpenSizeBucket = "NA",
                IsoSizeBucket = "NA",
                ThreeBetBucket = "NA",
                SqueezeBucket = "NA",
                FourBetBucket = "NA",
                JamThreshold = 18m,
                SolverKey = "UNSUPPORTED",
                RawActionHistory = rawActions,
                PriorActionsBeforeActing = rawActions,
                HadPriorCallOrCompletion = false,
                ActiveOpponentCount = 0,
                CallerCount = 0,
                PlayersBehindCount = 0,
                ActingPlayersFirstActionType = null
            };

            return PreflopExtractionResult.Unsupported(reason, trace);
        }
    }

    private static string BuildSignature(
        Position acting,
        int raiseDepth,
        bool hadPriorCallOrCompletion,
        bool hadColdCallerAfterOpen,
        decimal toCallBb)
    {
        if (raiseDepth == 0)
        {
            if (acting == Position.SB)
                return "UNOPENED_SB";

            // In BB option spots, prior SB completion must not collapse to unopened.
            if (hadPriorCallOrCompletion)
                return acting == Position.BB && toCallBb == 0m
                    ? "LIMP_OPTION"
                    : "LIMP";

            return "UNOPENED";
        }

        return raiseDepth switch
        {
            1 => "VS_OPEN",
            2 when hadColdCallerAfterOpen => "VS_SQUEEZE",
            2 => "VS_3BET",
            3 => "VS_4BET",
            _ => "VS_5BET"
        };
    }

    private static int CountPlayersBehind(
        IReadOnlyList<PlayerSeat> seats,
        IReadOnlySet<PlayerId> foldedPlayers,
        PlayerId actingPlayerId)
    {
        var actingSeat = seats.FirstOrDefault(s => s.Id == actingPlayerId);
        if (actingSeat is null)
            return 0;

        return seats.Count(s =>
            s.Id != actingPlayerId
            && !foldedPlayers.Contains(s.Id)
            && s.Position != Position.Unknown
            && s.Position > actingSeat.Position);
    }

    private static void WriteDebugTrace(
        IReadOnlyList<PreflopRawActionTrace> priorActionsBeforeActing,
        bool hadPriorCallOrCompletion,
        Position actingPosition,
        decimal toCallBb,
        decimal currentBetBb,
        string historySignature,
        string solverKey)
    {
        var priorActionSummary = priorActionsBeforeActing.Count == 0
            ? "none"
            : string.Join(", ", priorActionsBeforeActing.Select(a => $"{a.Position}:{a.ActionType}:{a.AmountBb}"));

        Debug.WriteLine(
            $"[PreflopStateExtractor] priorActionsBeforeActing={priorActionSummary}; hadPriorCallOrCompletion={hadPriorCallOrCompletion}; actingPosition={actingPosition}; toCallBb={toCallBb}; currentBetBb={currentBetBb}; historySignature={historySignature}; solverKey={solverKey}");
    }

    private static string BuildSolverKey(
        string historySignature,
        Position actingPosition,
        decimal effectiveStackBb,
        decimal? openSizeBucketBb,
        decimal? isoSizeBucketBb,
        decimal? threeBetSizeBucketBb,
        decimal? squeezeSizeBucketBb,
        decimal? fourBetSizeBucketBb,
        decimal? jamThresholdBucketBb)
    {
        var parts = new List<string>
        {
            $"v2/{historySignature}/{actingPosition}",
            $"eff={FormatBucketValue(effectiveStackBb)}"
        };

        AppendIfValue(parts, "open", openSizeBucketBb);
        AppendIfValue(parts, "iso", isoSizeBucketBb);
        AppendIfValue(parts, "3bet", threeBetSizeBucketBb);
        AppendIfValue(parts, "squeeze", squeezeSizeBucketBb);
        AppendIfValue(parts, "4bet", fourBetSizeBucketBb);
        AppendIfValue(parts, "jam", jamThresholdBucketBb);

        return string.Join('/', parts);
    }

    private static void AppendIfValue(List<string> parts, string name, decimal? value)
    {
        if (!value.HasValue)
            return;

        parts.Add($"{name}={FormatBucketValue(value.Value)}");
    }

    private static string FormatBucket(decimal? value)
        => value.HasValue ? FormatBucketValue(value.Value) : "NA";

    private static string FormatBucketValue(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed record PreflopInputAction(PlayerId PlayerId, string Type, decimal AmountBb);
