using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerAnalyzer.Infrastructure;
using PokerAnalyzer.Infrastructure.Persistence;

[ApiController]
[Route("api/hand-histories")]
public sealed class HandHistoriesController : ControllerBase
{
    private readonly IHandHistoryIngestService _ingest;
    private readonly PokerDbContext _db;

    public HandHistoriesController(IHandHistoryIngestService ingest, PokerDbContext db)
    {
        _ingest = ingest;
        _db = db;
    }

    [HttpPost("upload-xml")]
    [RequestSizeLimit(20_000_000)] // 20 MB; adjust
    public async Task<IActionResult> UploadXml([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (!file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only .xml files are supported.");

        using var sr = new StreamReader(file.OpenReadStream());
        var xml = await sr.ReadToEndAsync(ct);

        var sessionId = await _ingest.IngestAsync(file.FileName, xml, ct);

        return Ok(new { sessionId });
    }

    [HttpGet("{sessionId:guid}/player-stats")]
    public async Task<ActionResult<IReadOnlyList<PlayerStatsResponse>>> GetPlayerStats(
        Guid sessionId,
        CancellationToken ct)
    {
        var players = await _db.PlayerProfiles
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.Player)
            .ToListAsync(ct);

        if (players.Count == 0)
            return NotFound();

        var response = players.Select(BuildPlayerStats).ToList();
        return Ok(response);
    }

    private static PlayerStatsResponse BuildPlayerStats(PlayerProfile profile)
    {
        var hands = profile.Hands;
        var preflop = SumPreflop(profile.PreflopModel.Positions);
        var flop = SumFlop(profile.FlopModel.Positions);
        var turn = SumTurn(profile.TurnModel.Positions);
        var river = SumRiver(profile.RiverModel.Positions);

        var stats = new List<PlayerStatPercent>
        {
            new("VPIP", Percent(preflop.VpipHands, hands)),
            new("PFR", Percent(preflop.PfrHands, hands)),
            new("3Bet", Percent(preflop.ThreeBetHands, hands)),
            new("Faced 3Bet", Percent(preflop.FacedThreeBetHands, hands)),
            new("Fold to 3Bet", Percent(preflop.FoldToThreeBetHands, hands)),
            new("Saw Flop", Percent(flop.SawFlop, hands)),
            new("Flop WTSD", Percent(flop.WentToShowdown, hands)),
            new("Flop W$SD", Percent(flop.WonAtShowdown, hands)),
            new("Flop CBet Opp", Percent(flop.CBetOpportunities, hands)),
            new("Flop CBet", Percent(flop.CBets, hands)),
            new("Fold to Flop CBet Opp", Percent(flop.FoldToCBetOpportunities, hands)),
            new("Fold to Flop CBet", Percent(flop.FoldToCBet, hands)),
            new("Donk Bets", Percent(flop.DonkBets, hands)),
            new("First Fold to CBet", Percent(flop.FirstFoldToCBet, hands)),
            new("Call vs CBet", Percent(flop.CallVsCBet, hands)),
            new("Raise vs CBet", Percent(flop.RaiseVsCBet, hands)),
            new("Multiway CBet", Percent(flop.MultiwayCBets, hands)),
            new("Probe Bets", Percent(flop.ProbeBets, hands)),
            new("Saw Turn", Percent(turn.SawTurn, hands)),
            new("Turn WTSD", Percent(turn.WentToShowdown, hands)),
            new("Turn W$SD", Percent(turn.WonAtShowdown, hands)),
            new("Turn CBet", Percent(turn.TurnCBet, hands)),
            new("Turn Check", Percent(turn.TurnCheck, hands)),
            new("Turn Fold to Bet", Percent(turn.TurnFoldToBet, hands)),
            new("Turn Aggression", Percent(turn.TurnAggressionFactor, hands)),
            new("Turn Bet Size % Pot", Percent(turn.TurnBetSizePercentPot, hands)),
            new("Turn Raise vs Bet", Percent(turn.TurnRaiseVsBet, hands)),
            new("Turn WTSD Carryover", Percent(turn.TurnWTSDCarryover, hands)),
            new("Saw River", Percent(river.SawRiver, hands)),
            new("River WTSD", Percent(river.WentToShowdown, hands)),
            new("River W$SD", Percent(river.WonAtShowdown, hands)),
            new("River Bet Opp", Percent(river.RiverBetOpportunities, hands)),
            new("River Bets When Checked To", Percent(river.RiverBetsWhenCheckedTo, hands)),
            new("River Faced Bet", Percent(river.RiverFacedBet, hands)),
            new("River Calls vs Bet", Percent(river.RiverCallsVsBet, hands)),
            new("River Fold to Bet", Percent(river.RiverFoldToBet, hands)),
            new("River Raise vs Bet", Percent(river.RiverRaiseVsBet, hands)),
            new("River Aggression", Percent(river.RiverAggressionFactor, hands)),
            new("River Bet Size % Pot", Percent(river.RiverBetSizePercentPot, hands)),
        };

        return new PlayerStatsResponse(profile.Player, hands, stats);
    }

    private static PositionPreflopStats SumPreflop(PositionPreflopStatsByPosition positions) => new()
    {
        VpipHands = positions.Utg.VpipHands + positions.Hj.VpipHands + positions.Co.VpipHands + positions.Btn.VpipHands + positions.Sb.VpipHands + positions.Bb.VpipHands,
        PfrHands = positions.Utg.PfrHands + positions.Hj.PfrHands + positions.Co.PfrHands + positions.Btn.PfrHands + positions.Sb.PfrHands + positions.Bb.PfrHands,
        ThreeBetHands = positions.Utg.ThreeBetHands + positions.Hj.ThreeBetHands + positions.Co.ThreeBetHands + positions.Btn.ThreeBetHands + positions.Sb.ThreeBetHands + positions.Bb.ThreeBetHands,
        FacedThreeBetHands = positions.Utg.FacedThreeBetHands + positions.Hj.FacedThreeBetHands + positions.Co.FacedThreeBetHands + positions.Btn.FacedThreeBetHands + positions.Sb.FacedThreeBetHands + positions.Bb.FacedThreeBetHands,
        FoldToThreeBetHands = positions.Utg.FoldToThreeBetHands + positions.Hj.FoldToThreeBetHands + positions.Co.FoldToThreeBetHands + positions.Btn.FoldToThreeBetHands + positions.Sb.FoldToThreeBetHands + positions.Bb.FoldToThreeBetHands
    };

    private static FlopStats SumFlop(PositionFlopStatsByPosition positions) => new()
    {
        SawFlop = positions.Utg.SawFlop + positions.Hj.SawFlop + positions.Co.SawFlop + positions.Btn.SawFlop + positions.Sb.SawFlop + positions.Bb.SawFlop,
        WentToShowdown = positions.Utg.WentToShowdown + positions.Hj.WentToShowdown + positions.Co.WentToShowdown + positions.Btn.WentToShowdown + positions.Sb.WentToShowdown + positions.Bb.WentToShowdown,
        WonAtShowdown = positions.Utg.WonAtShowdown + positions.Hj.WonAtShowdown + positions.Co.WonAtShowdown + positions.Btn.WonAtShowdown + positions.Sb.WonAtShowdown + positions.Bb.WonAtShowdown,
        CBetOpportunities = positions.Utg.CBetOpportunities + positions.Hj.CBetOpportunities + positions.Co.CBetOpportunities + positions.Btn.CBetOpportunities + positions.Sb.CBetOpportunities + positions.Bb.CBetOpportunities,
        CBets = positions.Utg.CBets + positions.Hj.CBets + positions.Co.CBets + positions.Btn.CBets + positions.Sb.CBets + positions.Bb.CBets,
        FoldToCBetOpportunities = positions.Utg.FoldToCBetOpportunities + positions.Hj.FoldToCBetOpportunities + positions.Co.FoldToCBetOpportunities + positions.Btn.FoldToCBetOpportunities + positions.Sb.FoldToCBetOpportunities + positions.Bb.FoldToCBetOpportunities,
        FoldToCBet = positions.Utg.FoldToCBet + positions.Hj.FoldToCBet + positions.Co.FoldToCBet + positions.Btn.FoldToCBet + positions.Sb.FoldToCBet + positions.Bb.FoldToCBet,
        DonkBets = positions.Utg.DonkBets + positions.Hj.DonkBets + positions.Co.DonkBets + positions.Btn.DonkBets + positions.Sb.DonkBets + positions.Bb.DonkBets,
        FirstFoldToCBet = positions.Utg.FirstFoldToCBet + positions.Hj.FirstFoldToCBet + positions.Co.FirstFoldToCBet + positions.Btn.FirstFoldToCBet + positions.Sb.FirstFoldToCBet + positions.Bb.FirstFoldToCBet,
        CallVsCBet = positions.Utg.CallVsCBet + positions.Hj.CallVsCBet + positions.Co.CallVsCBet + positions.Btn.CallVsCBet + positions.Sb.CallVsCBet + positions.Bb.CallVsCBet,
        RaiseVsCBet = positions.Utg.RaiseVsCBet + positions.Hj.RaiseVsCBet + positions.Co.RaiseVsCBet + positions.Btn.RaiseVsCBet + positions.Sb.RaiseVsCBet + positions.Bb.RaiseVsCBet,
        MultiwayCBets = positions.Utg.MultiwayCBets + positions.Hj.MultiwayCBets + positions.Co.MultiwayCBets + positions.Btn.MultiwayCBets + positions.Sb.MultiwayCBets + positions.Bb.MultiwayCBets,
        ProbeBets = positions.Utg.ProbeBets + positions.Hj.ProbeBets + positions.Co.ProbeBets + positions.Btn.ProbeBets + positions.Sb.ProbeBets + positions.Bb.ProbeBets
    };

    private static TurnStats SumTurn(PositionTurnStatsByPosition positions) => new()
    {
        SawTurn = positions.Utg.SawTurn + positions.Hj.SawTurn + positions.Co.SawTurn + positions.Btn.SawTurn + positions.Sb.SawTurn + positions.Bb.SawTurn,
        WentToShowdown = positions.Utg.WentToShowdown + positions.Hj.WentToShowdown + positions.Co.WentToShowdown + positions.Btn.WentToShowdown + positions.Sb.WentToShowdown + positions.Bb.WentToShowdown,
        WonAtShowdown = positions.Utg.WonAtShowdown + positions.Hj.WonAtShowdown + positions.Co.WonAtShowdown + positions.Btn.WonAtShowdown + positions.Sb.WonAtShowdown + positions.Bb.WonAtShowdown,
        TurnCBet = positions.Utg.TurnCBet + positions.Hj.TurnCBet + positions.Co.TurnCBet + positions.Btn.TurnCBet + positions.Sb.TurnCBet + positions.Bb.TurnCBet,
        TurnCheck = positions.Utg.TurnCheck + positions.Hj.TurnCheck + positions.Co.TurnCheck + positions.Btn.TurnCheck + positions.Sb.TurnCheck + positions.Bb.TurnCheck,
        TurnFoldToBet = positions.Utg.TurnFoldToBet + positions.Hj.TurnFoldToBet + positions.Co.TurnFoldToBet + positions.Btn.TurnFoldToBet + positions.Sb.TurnFoldToBet + positions.Bb.TurnFoldToBet,
        TurnAggressionFactor = positions.Utg.TurnAggressionFactor + positions.Hj.TurnAggressionFactor + positions.Co.TurnAggressionFactor + positions.Btn.TurnAggressionFactor + positions.Sb.TurnAggressionFactor + positions.Bb.TurnAggressionFactor,
        TurnBetSizePercentPot = positions.Utg.TurnBetSizePercentPot + positions.Hj.TurnBetSizePercentPot + positions.Co.TurnBetSizePercentPot + positions.Btn.TurnBetSizePercentPot + positions.Sb.TurnBetSizePercentPot + positions.Bb.TurnBetSizePercentPot,
        TurnRaiseVsBet = positions.Utg.TurnRaiseVsBet + positions.Hj.TurnRaiseVsBet + positions.Co.TurnRaiseVsBet + positions.Btn.TurnRaiseVsBet + positions.Sb.TurnRaiseVsBet + positions.Bb.TurnRaiseVsBet,
        TurnWTSDCarryover = positions.Utg.TurnWTSDCarryover + positions.Hj.TurnWTSDCarryover + positions.Co.TurnWTSDCarryover + positions.Btn.TurnWTSDCarryover + positions.Sb.TurnWTSDCarryover + positions.Bb.TurnWTSDCarryover
    };

    private static RiverStats SumRiver(PositionRiverStatsByPosition positions) => new()
    {
        SawRiver = positions.Utg.SawRiver + positions.Hj.SawRiver + positions.Co.SawRiver + positions.Btn.SawRiver + positions.Sb.SawRiver + positions.Bb.SawRiver,
        WentToShowdown = positions.Utg.WentToShowdown + positions.Hj.WentToShowdown + positions.Co.WentToShowdown + positions.Btn.WentToShowdown + positions.Sb.WentToShowdown + positions.Bb.WentToShowdown,
        WonAtShowdown = positions.Utg.WonAtShowdown + positions.Hj.WonAtShowdown + positions.Co.WonAtShowdown + positions.Btn.WonAtShowdown + positions.Sb.WonAtShowdown + positions.Bb.WonAtShowdown,
        RiverBetOpportunities = positions.Utg.RiverBetOpportunities + positions.Hj.RiverBetOpportunities + positions.Co.RiverBetOpportunities + positions.Btn.RiverBetOpportunities + positions.Sb.RiverBetOpportunities + positions.Bb.RiverBetOpportunities,
        RiverBetsWhenCheckedTo = positions.Utg.RiverBetsWhenCheckedTo + positions.Hj.RiverBetsWhenCheckedTo + positions.Co.RiverBetsWhenCheckedTo + positions.Btn.RiverBetsWhenCheckedTo + positions.Sb.RiverBetsWhenCheckedTo + positions.Bb.RiverBetsWhenCheckedTo,
        RiverFacedBet = positions.Utg.RiverFacedBet + positions.Hj.RiverFacedBet + positions.Co.RiverFacedBet + positions.Btn.RiverFacedBet + positions.Sb.RiverFacedBet + positions.Bb.RiverFacedBet,
        RiverCallsVsBet = positions.Utg.RiverCallsVsBet + positions.Hj.RiverCallsVsBet + positions.Co.RiverCallsVsBet + positions.Btn.RiverCallsVsBet + positions.Sb.RiverCallsVsBet + positions.Bb.RiverCallsVsBet,
        RiverFoldToBet = positions.Utg.RiverFoldToBet + positions.Hj.RiverFoldToBet + positions.Co.RiverFoldToBet + positions.Btn.RiverFoldToBet + positions.Sb.RiverFoldToBet + positions.Bb.RiverFoldToBet,
        RiverRaiseVsBet = positions.Utg.RiverRaiseVsBet + positions.Hj.RiverRaiseVsBet + positions.Co.RiverRaiseVsBet + positions.Btn.RiverRaiseVsBet + positions.Sb.RiverRaiseVsBet + positions.Bb.RiverRaiseVsBet,
        RiverAggressionFactor = positions.Utg.RiverAggressionFactor + positions.Hj.RiverAggressionFactor + positions.Co.RiverAggressionFactor + positions.Btn.RiverAggressionFactor + positions.Sb.RiverAggressionFactor + positions.Bb.RiverAggressionFactor,
        RiverBetSizePercentPot = positions.Utg.RiverBetSizePercentPot + positions.Hj.RiverBetSizePercentPot + positions.Co.RiverBetSizePercentPot + positions.Btn.RiverBetSizePercentPot + positions.Sb.RiverBetSizePercentPot + positions.Bb.RiverBetSizePercentPot
    };

    private static decimal Percent(int numerator, int denominator)
        => denominator <= 0 ? 0m : Math.Round((decimal)numerator / denominator * 100m, 2);

    private static decimal Percent(decimal numerator, int denominator)
        => denominator <= 0 ? 0m : Math.Round(numerator / denominator * 100m, 2);
}

public sealed record PlayerStatPercent(string Name, decimal Percent);

public sealed record PlayerStatsResponse(string Player, int Hands, IReadOnlyList<PlayerStatPercent> Stats);
