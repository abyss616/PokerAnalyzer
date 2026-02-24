using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public HandAnalysisController(
        PokerDbContext db,
        HandAnalyzer handAnalyzer)
    {
        _db = db;
        _handAnalyzer = handAnalyzer;
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
        CancellationToken ct)
    {
        if (handNumber <= 0)
            return BadRequest("Hand number must be greater than zero.");

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

        if (session is null)
            return NotFound("Session not found.");

        var hand = session.Hands.FirstOrDefault();

        if (hand is null)
            return NotFound("Hand not found in session.");

        var domainHand = MapToDomainHand(hand, session);
        var solverResult = _handAnalyzer.Analyze(domainHand);

        var engineSummary = BuildEngineResult("CFR+ Solver", solverResult, domainHand);
        return Ok(new HandSolverResponse(domainHand.HandId, handNumber, engineSummary.DecisionCount, new[] { engineSummary }));
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
        if (hand.Players.Count == 0)
            throw new InvalidOperationException("Hand has no players.");

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
        var positionsBySeat = PositionAssigner.Assign(hand.Players, hand.Actions, dealerSeatNumber);
        seats = seats
            .Select(seat => seat with
            {
                Position = positionsBySeat.TryGetValue(seat.SeatNumber, out var position)
                    ? position
                    : Position.Unknown
            })
            .ToList();

        var hero = hand.Players.FirstOrDefault(p => p.IsHero) ?? hand.Players[0];
        var heroId = playerIds[hero.Name];
        var heroSeat = seats.First(seat => seat.Id == heroId);
        var hasDuplicateVillainPosition = seats.Any(seat =>
            seat.Id != heroId &&
            seat.Position == heroSeat.Position &&
            seat.Position != Position.Unknown);

        if (hasDuplicateVillainPosition)
            throw new InvalidOperationException($"Invalid hand state: Hero and Villain cannot have the same position ({heroSeat.Position}).");

        var holeCards = ParseHoleCards(hand.HeroHoleCards);

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
                    if (sbSeat is null)
                        throw new InvalidOperationException("Invalid action mapping: no SB seat found for PostSmallBlind.");

                    if (actorSeat != sbSeat.SeatNumber)
                        throw new InvalidOperationException($"Invalid action mapping: dealerSeat={dealerSeatNumber?.ToString() ?? "none"}, computedSbSeat={sbSeat.SeatNumber}, computedBbSeat={bbSeat?.SeatNumber.ToString() ?? "none"}, actorSeat={actorSeat}, actionIndex={a.ActionIndex}.");
                }

                if (a.Type == ActionType.PostBigBlind)
                {
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

        return new PokerAnalyzer.Domain.HandHistory.Hand(
            hand.Id,
            new ChipAmount(ToChipAmount(session.SmallBlind)),
            new ChipAmount(ToChipAmount(session.BigBlind)),
            seats,
            heroId,
            holeCards,
            board,
            actions
        );
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
        IReadOnlyList<EngineSolverResult> Engines);

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
