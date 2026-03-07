using Microsoft.AspNetCore.Mvc;
using PokerAnalyzer.Application.PreflopAnalysis;

namespace PokerAnalyzer.Api.Controllers;

[ApiController]
[Route("api/preflop-analysis")]
public sealed class PreflopAnalysisController : ControllerBase
{
    private readonly IPreflopHandAnalysisService _service;

    public PreflopAnalysisController(IPreflopHandAnalysisService service)
    {
        _service = service;
    }

    [HttpGet("hand-number/{handNumber:long}")]
    public async Task<ActionResult<PreflopHandAnalysisResultDto>> AnalyzeByHandNumber(long handNumber, CancellationToken ct)
    {
        var result = await _service.AnalyzePreflopByHandNumberAsync(handNumber, ct);
        if (result is null)
            return NotFound();

        return Ok(result);
    }
}
