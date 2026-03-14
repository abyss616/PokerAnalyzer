using Microsoft.AspNetCore.Mvc;
using PokerAnalyzer.Application.PreflopAnalysis;

namespace PokerAnalyzer.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class PreflopAnalysisController : ControllerBase
{
    private readonly IPreflopHandAnalysisService _service;

    public PreflopAnalysisController(IPreflopHandAnalysisService service)
    {
        _service = service;
    }

    [HttpGet("preflop-analysis/hand-number/{handNumber:long}")]
    public async Task<ActionResult<PreflopNodeQueryResultDto>> AnalyzeByHandNumber(long handNumber, [FromQuery] string? populationProfile, CancellationToken ct)
    {
        var result = await _service.QueryPreflopNodeByHandNumberAsync(handNumber, ct, populationProfile);
        if (result is null)
            return NotFound();

        return Ok(result);
    }

    [HttpPost("solver/preflop/node")]
    public async Task<ActionResult<PreflopNodeQueryResultDto>> QueryPreflopNode([FromBody] PreflopNodeQueryRequestDto request, CancellationToken ct)
    {
        var result = await _service.QueryPreflopNodeAsync(request, ct);
        return Ok(result);
    }
}
