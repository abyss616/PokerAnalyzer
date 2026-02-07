using Microsoft.AspNetCore.Mvc;
using PokerAnalyzer.Infrastructure;

[ApiController]
[Route("api/hand-histories")]
public sealed class HandHistoriesController : ControllerBase
{
    private readonly IHandHistoryIngestService _ingest;

    public HandHistoriesController(IHandHistoryIngestService ingest) => _ingest = ingest;

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
}
