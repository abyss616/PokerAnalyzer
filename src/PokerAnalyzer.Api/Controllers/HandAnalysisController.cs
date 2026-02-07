using Microsoft.AspNetCore.Mvc;
using PokerAnalyzer.Application.Analysis;

[ApiController]
[Route("api/analysis")]
public sealed class HandAnalysisController : ControllerBase
{
    [HttpGet("hand/{handId:guid}")]
    public Task<ActionResult<HandAnalysisResult>> AnalyzeHand(
        Guid handId,
        CancellationToken ct)
    {
        return null!;
    }


    [HttpGet("session/{sessionId:guid}")]
    public Task<ActionResult<IReadOnlyList<HandAnalysisResult>>> AnalyzeSession(
    Guid sessionId,
    CancellationToken ct)
    {
        return null!;
    }

}
