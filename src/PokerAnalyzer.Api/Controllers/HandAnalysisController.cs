using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerAnalyzer.Application.Analysis;
using PokerAnalyzer.Domain.Cards;
using PokerAnalyzer.Domain.Game;
using PokerAnalyzer.Infrastructure.Persistence;

[ApiController]
[Route("api/analysis")]
public sealed class HandAnalysisController : ControllerBase
{
    private readonly PokerDbContext _db;
    private readonly HandAnalyzer _analyzer;

    public HandAnalysisController(PokerDbContext db, HandAnalyzer analyzer)
    {
        _db = db;
        _analyzer = analyzer;
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
            .Include(s => s.Hands)
                .ThenInclude(h => h.Players)
            .Include(s => s.Hands)
                .ThenInclude(h => h.Actions)
            .Include(s => s.Hands)
                .ThenInclude(h => h.Board)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null)
            return NotFound("Session not found.");

        var hand = session.Hands
            .OrderBy(h => h.HandNumber ?? int.MaxValue)
            .FirstOrDefault(h => h.HandNumber == handNumber);

        if (hand is null)
            return NotFound("Hand not found in session.");

        var domainHand = MapToDomainHand(hand, session);
        var result = _analyzer.Analyze(domainHand);

        return Ok(new HandSolverResponse(result.HandId, handNumber, result.Decisions.Count));
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
                MapPosition(p.Position),
                new ChipAmount(ToChipAmount(p.StackStart))
            ))
            .ToList();

        var hero = hand.Players.FirstOrDefault(p => p.IsHero) ?? hand.Players[0];
        var heroId = playerIds[hero.Name];
        var holeCards = ParseHoleCards(hand.HeroHoleCards);

        var actions = hand.Actions
            .OrderBy(a => a.ActionIndex)
            .Select(a =>
            {
                var actorId = playerIds.TryGetValue(a.Player, out var id) ? id : heroId;
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

    private static Position MapPosition(HandPlayer.Position position) => position switch
    {
        HandPlayer.Position.UTG => Position.UTG,
        HandPlayer.Position.UTG1 => Position.UTG1,
        HandPlayer.Position.UTG2 => Position.UTG2,
        HandPlayer.Position.LJ => Position.LJ,
        HandPlayer.Position.HJ => Position.HJ,
        HandPlayer.Position.CO => Position.CO,
        HandPlayer.Position.BTN => Position.BTN,
        HandPlayer.Position.SB => Position.SB,
        HandPlayer.Position.BB => Position.BB,
        _ => Position.Unknown
    };

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

    public sealed record HandSolverResponse(Guid HandId, int HandNumber, int DecisionCount);
}
