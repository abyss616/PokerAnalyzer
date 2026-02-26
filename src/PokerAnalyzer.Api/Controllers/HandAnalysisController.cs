using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokerAnalyzer.Api.Logging;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Application.Engines;
using PokerAnalyzer.Infrastructure.Persistence;

[ApiController]
[Route("api/analysis")]
public sealed class HandAnalysisController : ControllerBase
{
    private readonly PokerDbContext _db;
    private readonly HandAnalyzer _handAnalyzer;
    private readonly ILogger<HandAnalysisController> _logger;
    private readonly UiLogStore _uiLogStore;

    public HandAnalysisController(
        PokerDbContext db,
        HandAnalyzer handAnalyzer,
        ILogger<HandAnalysisController> logger,
        UiLogStore uiLogStore)
    {
        _db = db;
        _handAnalyzer = handAnalyzer;
        _logger = logger;
        _uiLogStore = uiLogStore;
    }

    [HttpGet("hand/{handId:guid}")]
    public Task<ActionResult<HandAnalysisResult>> AnalyzeHand(
        Guid handId,
        CancellationToken ct)
    {
        return null!;
    }

    [HttpGet("session/{sessionId:guid}/hand-number/{handNumber:int}")]
    public async Task<ActionResult<HandSolverResponse>> AnalyzeSessionHandNumber(
        Guid sessionId,
        int handNumber,
        [FromQuery] string? correlationId,
        CancellationToken ct)
    {
        correlationId ??= HttpContext.TraceIdentifier;
        _uiLogStore.Clear(correlationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId
        });

        var utcStart = DateTimeOffset.UtcNow;
        var total = System.Diagnostics.Stopwatch.StartNew();
        var clientVersion = Request.Headers["X-Client-Version"].FirstOrDefault() ?? Request.Headers.UserAgent.ToString();

        _logger.LogInformation(
            "AnalyzeSessionHandNumber start. CorrelationId={CorrelationId}, SessionId={SessionId}, HandNumber={HandNumber}, User={User}, UtcStart={UtcStart}, ClientVersion={ClientVersion}",
            correlationId,
            sessionId,
            handNumber,
            User?.Identity?.Name ?? "anonymous",
            utcStart,
            clientVersion);

        try
        {
            _logger.LogInformation("Check handNumber > 0. CorrelationId={CorrelationId}, HandNumber={HandNumber}", correlationId, handNumber);
            if (handNumber <= 0)
                return BadRequest("Hand number must be greater than zero.");

            var dbTimer = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("Load session from DB. CorrelationId={CorrelationId}, SessionId={SessionId}, HandNumber={HandNumber}", correlationId, sessionId, handNumber);
            var session = await _db.Sessions
                .AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Include(s => s.Hands.Where(h => h.HandNumber == handNumber))
                    .ThenInclude(h => h.Players)
                .Include(s => s.Hands.Where(h => h.HandNumber == handNumber))
                    .ThenInclude(h => h.Actions)
                .Include(s => s.Hands.Where(h => h.HandNumber == handNumber))
                    .ThenInclude(h => h.Board)
                .FirstOrDefaultAsync(ct);
            dbTimer.Stop();

            _logger.LogInformation(
                "Load session from DB done. CorrelationId={CorrelationId}, HandsLoaded={HandsLoaded}, PlayersLoaded={PlayersLoaded}, ActionsLoaded={ActionsLoaded}, BoardLoaded={BoardLoaded}, DbDurationMs={DbDurationMs}",
                correlationId,
                session?.Hands.Count ?? 0,
                session?.Hands.Sum(h => h.Players.Count) ?? 0,
                session?.Hands.Sum(h => h.Actions.Count) ?? 0,
                session?.Hands.Count(h => h.Board is not null) ?? 0,
                dbTimer.ElapsedMilliseconds);

            if (session is null)
                return NotFound("Session not found.");

            _logger.LogInformation("Find hand by number. CorrelationId={CorrelationId}, HandNumber={HandNumber}", correlationId, handNumber);
            var hand = session.Hands.FirstOrDefault();

            _logger.LogInformation(
                "Find hand by number done. CorrelationId={CorrelationId}, HandId={HandId}, HandNumber={HandNumber}, PlayerCount={PlayerCount}, ActionCount={ActionCount}",
                correlationId,
                hand?.Id,
                hand?.HandNumber,
                hand?.Players.Count ?? 0,
                hand?.Actions.Count ?? 0);

            if (hand is null)
                return NotFound("Hand not found in session.");

            var domainHand = MapToDomainHand(hand, session, correlationId);
            var solverResult = _handAnalyzer.Analyze(domainHand);
            var preflopSummary = BuildPreflopSummary(hand);

            var engineSummary = BuildEngineResult("CFR+ Solver", solverResult, domainHand);
            _logger.LogInformation(
                "Build response. CorrelationId={CorrelationId}, HandId={HandId}, DecisionCount={DecisionCount}, EnginesReturned={EnginesReturned}, PreflopSummaryLength={PreflopSummaryLength}",
                correlationId,
                domainHand.HandId,
                solverResult.Decisions.Count,
                1,
                preflopSummary.Players.Count);

            total.Stop();
            _logger.LogInformation("Return OK. CorrelationId={CorrelationId}, HttpStatus={HttpStatus}, TotalDurationMs={TotalDurationMs}", correlationId, 200, total.ElapsedMilliseconds);
            var logs = _uiLogStore.GetSnapshot(correlationId)
                .Select(entry => new UiLogLine(entry.Timestamp, entry.Level, entry.Category, entry.Message))
                .ToArray();

            return Ok(new HandSolverResponse(domainHand.HandId, handNumber, engineSummary.DecisionCount, preflopSummary, new[] { engineSummary }, logs));
        }
        catch (Exception ex)
        {
            total.Stop();
            _logger.LogError(ex, "Fail. Stage={Stage}, ExceptionType={ExceptionType}, Message={Message}, CorrelationId={CorrelationId}", "AnalyzeSessionHandNumber", ex.GetType().Name, ex.Message, correlationId);
            return Problem($"Analysis failed. CorrelationId={correlationId}");
        }
    }

    [HttpGet("session/{sessionId:guid}")]
    public Task<ActionResult<IReadOnlyList<HandAnalysisResult>>> AnalyzeSession(
    Guid sessionId,
    CancellationToken ct)
    {
        return null!;
    }


    private static PokerAnalyzer.Domain.HandHistory.Hand MapToDomainHand(
        Hand hand,
        HandHistorySession session)
    {
        return MapToDomainHandCore(hand, session, logger: null, correlationId: null);
    }

    private PokerAnalyzer.Domain.HandHistory.Hand MapToDomainHand(
        Hand hand,
        HandHistorySession session,
        string correlationId)
    {
        return MapToDomainHandCore(hand, session, _logger, correlationId);
    }

    private static PokerAnalyzer.Domain.HandHistory.Hand MapToDomainHandCore(
        Hand hand,
        HandHistorySession session,
        ILogger? logger,
        string? correlationId)
    {
        if (hand.Players.Count == 0)
            throw new InvalidOperationException("Hand has no players.");

        logger?.LogInformation("Mapping stage start. CorrelationId={CorrelationId}, HandId={HandId}", correlationId, hand.Id);

        var playerIds = hand.Players
            .OrderBy(p => p.Seat)
            .ToDictionary(p => p.Name, _ => PlayerId.New(), StringComparer.OrdinalIgnoreCase);

        var seats = hand.Players
            .OrderBy(p => p.Seat)
            .Select(p => new PlayerSeat(
                playerIds[p.Name],
                p.Name,
                p.Seat,
                Position.Unknown,
                new ChipAmount(ToChipAmount(p.StackStart))
            ))
            .ToList();

        var dealerSeatNumber = hand.Players.FirstOrDefault(p => p.Dealer)?.Seat;
        logger?.LogInformation("Players mapped. CorrelationId={CorrelationId}, PlayerNames={PlayerNames}, Seats={Seats}, DealerSeat={DealerSeat}, HeroName={HeroName}, HeroSeat={HeroSeat}",
            correlationId,
            hand.Players.Select(p => p.Name).ToArray(),
            hand.Players.Select(p => p.Seat).ToArray(),
            dealerSeatNumber,
            hand.Players.FirstOrDefault(p => p.IsHero)?.Name ?? hand.Players[0].Name,
            hand.Players.FirstOrDefault(p => p.IsHero)?.Seat ?? hand.Players[0].Seat);
        var positionResolution = PositionAssigner.Assign(hand.Players, hand.Actions, session.MaxSeats);
        var positionsBySeat = positionResolution.PositionsBySeat;
        seats = seats
            .Select(seat => seat with
            {
                Position = positionsBySeat.TryGetValue(seat.SeatNumber, out var position)
                    ? position
                    : Position.Unknown
            })
            .ToList();

        logger?.LogInformation("Positions assigned. CorrelationId={CorrelationId}, PositionsBySeat={PositionsBySeat}", correlationId, string.Join(", ", positionsBySeat.Select(kvp => $"{kvp.Key}:{kvp.Value}")));
        if (seats.Any(seat => seat.Position == Position.Unknown))
            logger?.LogWarning("Positions assigned warning. CorrelationId={CorrelationId}, UnknownPositions=true", correlationId);

        var hero = hand.Players.FirstOrDefault(p => p.IsHero) ?? hand.Players[0];
        var heroId = playerIds[hero.Name];
        var heroSeat = seats.First(seat => seat.Id == heroId);
        var hasDuplicateVillainPosition = seats.Any(seat =>
            seat.Id != heroId &&
            seat.Position == heroSeat.Position &&
            seat.Position != Position.Unknown);

        if (hasDuplicateVillainPosition)
        {
            logger?.LogError("Hero and Villain same position. CorrelationId={CorrelationId}, HeroPosition={HeroPosition}, VillainPositions={VillainPositions}",
                correlationId,
                heroSeat.Position,
                seats.Where(seat => seat.Id != heroId).Select(seat => seat.Position).ToArray());
            throw new InvalidOperationException($"Invalid hand state: Hero and Villain cannot have the same position ({heroSeat.Position}).");
        }

        var holeCards = ParseHoleCards(hand.HeroHoleCards);
        logger?.LogInformation("Hole cards parsed. CorrelationId={CorrelationId}, HeroHoleCardsRaw={HeroHoleCardsRaw}, HeroHoleCardsParsed={HeroHoleCardsParsed}", correlationId, hand.HeroHoleCards, holeCards?.ToString());

        var seatsByPlayerId = seats.ToDictionary(seat => seat.Id);
        var sbSeat = seats.FirstOrDefault(seat => seat.Position == Position.SB);
        var bbSeat = seats.FirstOrDefault(seat => seat.Position == Position.BB);

        var actions = hand.Actions
            .OrderBy(a => a.ActionIndex)
            .Select(a =>
            {
                if (!playerIds.TryGetValue(a.Player, out var actorId))
                    throw new InvalidOperationException($"Invalid action mapping: unknown player '{a.Player}' at actionIndex={a.ActionIndex}.");

                var actorSeat = seatsByPlayerId[actorId].SeatNumber;

                if (a.Type == ActionType.PostSmallBlind)
                {
                    logger?.LogInformation("Validate blind action mapping. CorrelationId={CorrelationId}, ComputedSbSeat={ComputedSbSeat}, ComputedBbSeat={ComputedBbSeat}, ActorSeat={ActorSeat}, ActionIndex={ActionIndex}", correlationId, sbSeat?.SeatNumber, bbSeat?.SeatNumber, actorSeat, a.ActionIndex);
                    if (sbSeat is null)
                        throw new InvalidOperationException("Invalid action mapping: no SB seat found for PostSmallBlind.");

                    if (actorSeat != sbSeat.SeatNumber)
                        throw new InvalidOperationException($"Invalid action mapping: dealerSeat={dealerSeatNumber?.ToString() ?? "none"}, computedSbSeat={sbSeat.SeatNumber}, computedBbSeat={bbSeat?.SeatNumber.ToString() ?? "none"}, actorSeat={actorSeat}, actionIndex={a.ActionIndex}.");
                }

                if (a.Type == ActionType.PostBigBlind)
                {
                    logger?.LogInformation("Validate blind action mapping. CorrelationId={CorrelationId}, ComputedSbSeat={ComputedSbSeat}, ComputedBbSeat={ComputedBbSeat}, ActorSeat={ActorSeat}, ActionIndex={ActionIndex}", correlationId, sbSeat?.SeatNumber, bbSeat?.SeatNumber, actorSeat, a.ActionIndex);
                    if (bbSeat is null)
                        throw new InvalidOperationException("Invalid action mapping: no BB seat found for PostBigBlind.");

                    if (actorSeat != bbSeat.SeatNumber)
                        throw new InvalidOperationException($"Invalid action mapping: dealerSeat={dealerSeatNumber?.ToString() ?? "none"}, computedSbSeat={sbSeat?.SeatNumber.ToString() ?? "none"}, computedBbSeat={bbSeat.SeatNumber}, actorSeat={actorSeat}, actionIndex={a.ActionIndex}.");
                }

                return new BettingAction(
                    a.Street,
                    actorId,
                    a.Type,
                    new ChipAmount(ToChipAmount(a.Amount))
                );
            })
            .ToList();

        var board = hand.Board ?? new Board();
        logger?.LogInformation("Actions mapped. CorrelationId={CorrelationId}, TotalActions={TotalActions}, PreflopActions={PreflopActions}, PostBlindsSkippedCount={PostBlindsSkippedCount}",
            correlationId,
            actions.Count,
            actions.Count(action => action.Street == Street.Preflop),
            hand.Actions.Count(action => action.Type is ActionType.PostSmallBlind or ActionType.PostBigBlind));

        var domainHand = new PokerAnalyzer.Domain.HandHistory.Hand(
            hand.Id,
            new ChipAmount(ToChipAmount(session.SmallBlind)),
            new ChipAmount(ToChipAmount(session.BigBlind)),
            seats,
            heroId,
            holeCards,
            board,
            actions
        );

        logger?.LogInformation("Domain hand built. CorrelationId={CorrelationId}, DomainHandId={DomainHandId}, Board={Board}, SB={SB}, BB={BB}", correlationId, domainHand.HandId, domainHand.Board, domainHand.SmallBlind.Value, domainHand.BigBlind.Value);
        return domainHand;
    }

    private static long ToChipAmount(decimal? value)
    {
        if (!value.HasValue)
            return 0;

        return (long)Math.Round(value.Value * 100m, MidpointRounding.AwayFromZero);
    }

    private static HoleCards? ParseHoleCards(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var compact = value.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (compact.Length != 4)
            return null;

        try
        {
            return HoleCards.Parse(compact);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static EngineSolverResult BuildEngineResult(string engineName, HandAnalysisResult result, PokerAnalyzer.Domain.HandHistory.Hand hand)
    {
        var decisionSummaries = result.Decisions
            .Select(decision =>
            {
                var primaryRecommendation = decision.Recommendation.PrimaryAction ?? decision.Recommendation.RankedActions.FirstOrDefault();
                var villainContext = TryGetLatestVillainContext(hand, decision.ActionIndex);
                var heroSeat = hand.Seats.FirstOrDefault(seat => seat.Id == hand.HeroId);
                return new EngineDecisionSummary(
                    decision.ActionIndex,
                    decision.Street.ToString(),
                    hand.HeroHoleCards?.ToString(),
                    heroSeat?.Position.ToString(),
                    villainContext?.Position,
                    villainContext?.Action,
                    FormatAction(decision.ActualAction.Type, decision.ActualAction.Amount),
                    primaryRecommendation is null
                        ? "N/A"
                        : FormatAction(primaryRecommendation.Type, primaryRecommendation.ToAmount),
                    decision.Recommendation.PrimaryEV ?? primaryRecommendation?.EstimatedEv,
                    decision.Recommendation.ReferenceEV,
                    decision.Recommendation.PrimaryExplanation,
                    decision.Recommendation.ReferenceExplanation
                );
            })
            .ToList();

        return new EngineSolverResult(engineName, result.Decisions.Count, decisionSummaries);
    }

    private static VillainActionContext? TryGetLatestVillainContext(PokerAnalyzer.Domain.HandHistory.Hand hand, int actionIndex)
    {
        if (actionIndex < 0 || actionIndex >= hand.Actions.Count)
            return null;

        var heroStreet = hand.Actions[actionIndex].Street;

        for (var i = actionIndex - 1; i >= 0; i--)
        {
            var action = hand.Actions[i];
            if (action.Street != heroStreet)
                continue;

            if (action.ActorId == hand.HeroId)
                continue;

            var seat = hand.Seats.FirstOrDefault(playerSeat => playerSeat.Id == action.ActorId);
            return new VillainActionContext(
                seat?.Position.ToString(),
                FormatAction(action.Type, action.Amount));
        }

        return null;
    }

    private sealed record VillainActionContext(string? Position, string Action);

    private static PreflopSummary BuildPreflopSummary(Hand hand)
    {
        var tableSize = Math.Max(hand.Players.Where(p => p.Seat > 0).Select(p => p.Seat).DefaultIfEmpty(0).Max(), 2);
        var positionResolution = PositionAssigner.Assign(hand.Players, hand.Actions, tableSize);
        var positionsBySeat = positionResolution.PositionsBySeat;
        var playersBySeat = hand.Players
            .Where(p => p.Seat > 0)
            .ToDictionary(p => p.Seat);

        var preflopActions = hand.Actions
            .Where(a => a.Street == Street.Preflop)
            .OrderBy(a => a.ActionIndex)
            .ToList();

        var lines = new List<PlayerPreflopLine>(capacity: 6);

        for (var seatNumber = 1; seatNumber <= 6; seatNumber++)
        {
            playersBySeat.TryGetValue(seatNumber, out var player);
            var playerActions = player is null
                ? new List<PreflopActionItem>()
                : preflopActions
                    .Where(a => string.Equals(a.Player, player.Name, StringComparison.Ordinal))
                    .Select((action, index) => new PreflopActionItem(
                        index + 1,
                        action.Type.ToString(),
                        action.Amount,
                        action.ToAmount,
                        FormatPreflopAction(action.Type, action.Amount, action.ToAmount)))
                    .ToList();

            var totalPutIn = playerActions.Sum(a => a.Amount ?? 0m);
            var foldedPreflop = playerActions.Any(a => string.Equals(a.ActionType, ActionType.Fold.ToString(), StringComparison.Ordinal));

            lines.Add(new PlayerPreflopLine(
                seatNumber,
                player?.Name,
                ToUiPosition(player is not null && positionsBySeat.TryGetValue(player.Seat, out var pos) ? pos : Position.Unknown),
                player?.StackStart,
                playerActions,
                totalPutIn,
                foldedPreflop,
                player is not null));
        }

        return new PreflopSummary(hand.Id, hand.GameCode, lines, positionResolution.DealerSeat, positionResolution.SbSeat, positionResolution.BbSeat);
    }

    private static string? ToUiPosition(Position position)
    {
        if (position == Position.Unknown)
            return null;

        return position switch
        {
            Position.HJ or Position.LJ => "MP",
            Position.UTG1 or Position.UTG2 => "UTG",
            _ => position.ToString()
        };
    }

    private static string FormatPreflopAction(ActionType type, decimal? amount, decimal? toAmount)
    {
        return type switch
        {
            ActionType.PostSmallBlind => $"Post SB {FormatCurrency(amount)}",
            ActionType.PostBigBlind => $"Post BB {FormatCurrency(amount)}",
            ActionType.Raise when toAmount.HasValue => $"Raise to {FormatCurrency(toAmount)}",
            ActionType.Raise => $"Raise {FormatCurrency(amount)}",
            ActionType.AllIn => $"All-in {FormatCurrency(amount)}",
            ActionType.Call => $"Call {FormatCurrency(amount)}",
            ActionType.Bet => $"Bet {FormatCurrency(amount)}",
            ActionType.Check => "Check",
            ActionType.Fold => "Fold",
            _ => type.ToString()
        };
    }

    private static string FormatCurrency(decimal? value) => $"€{(value ?? 0m):0.00}";

    private static string FormatAction(ActionType actionType, ChipAmount? toAmount)
    {
        if (toAmount is null || actionType is ActionType.Call or ActionType.Check or ActionType.Fold)
            return actionType.ToString();

        return $"{actionType} to {toAmount.Value.Value / 100m:0.##}";
    }

    public sealed record HandSolverResponse(
        Guid HandId,
        int HandNumber,
        int DecisionCount,
        PreflopSummary PreflopSummary,
        IReadOnlyList<EngineSolverResult> Engines,
        IReadOnlyList<UiLogLine> Logs);

    public sealed record PreflopSummary(
        Guid HandId,
        long GameCode,
        IReadOnlyList<PlayerPreflopLine> Players,
        int? DealerSeat,
        int? SbSeat,
        int? BbSeat);

    public sealed record PlayerPreflopLine(
        int Seat,
        string? Name,
        string? Position,
        decimal? StartingStack,
        IReadOnlyList<PreflopActionItem> Actions,
        decimal TotalPutIn,
        bool FoldedPreflop,
        bool Occupied);

    public sealed record PreflopActionItem(
        int Order,
        string ActionType,
        decimal? Amount,
        decimal? ToAmount,
        string Display);


    public sealed record UiLogLine(
        DateTimeOffset Timestamp,
        string Level,
        string Category,
        string Message);

    public sealed record EngineSolverResult(
        string Engine,
        int DecisionCount,
        IReadOnlyList<EngineDecisionSummary> Decisions);

    public sealed record EngineDecisionSummary(
        int ActionIndex,
        string Street,
        string? HeroCards,
        string? HeroPosition,
        string? VillainPosition,
        string? VillainAction,
        string HeroAction,
        string RecommendedAction,
        decimal? PrimaryEv,
        decimal? ReferenceEv,
        string? PrimaryExplanation,
        string? ReferenceExplanation);
}
