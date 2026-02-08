using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Headers;

namespace PokerAnalyzer.Web.Services;


public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public sealed record UploadHandHistoryResult(Guid SessionId);
    public sealed record PlayerStatPercent(string Name, decimal Percent);
    public sealed record PlayerStatsResult(string Player, int Hands, IReadOnlyList<PlayerStatPercent> Stats);

    public async Task<UploadHandHistoryResult> UploadHandHistoryXmlAsync(
        IBrowserFile file,
        CancellationToken ct = default)
    {
        if (file is null) throw new ArgumentNullException(nameof(file));
        if (!file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .xml files are supported.");

        // match API limit; keep it consistent with [RequestSizeLimit] on the controller
        const long maxBytes = 20_000_000;

        using var content = new MultipartFormDataContent();

        await using var stream = file.OpenReadStream(maxAllowedSize: maxBytes, cancellationToken: ct);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/xml");

        content.Add(fileContent, name: "file", fileName: file.Name);

        using var resp = await _http.PostAsync("api/hand-histories/upload-xml", content, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Upload failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        var result = await resp.Content.ReadFromJsonAsync<UploadHandHistoryResult>(cancellationToken: ct);
        if (result is null || result.SessionId == Guid.Empty)
            throw new InvalidOperationException("Upload succeeded but response was invalid.");

        return result;
    }

    public async Task<IReadOnlyList<PlayerStatsResult>> GetPlayerStatsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        using var resp = await _http.GetAsync($"api/hand-histories/{sessionId}/player-stats", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Player stats request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        var result = await resp.Content.ReadFromJsonAsync<IReadOnlyList<PlayerStatsResult>>(cancellationToken: ct);
        return result ?? Array.Empty<PlayerStatsResult>();
    }

    public async Task<IReadOnlyList<PlayerStatsResult>> GetLatestPlayerStatsAsync(
        CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("api/hand-histories/latest/player-stats", ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Latest player stats request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
        }

        var result = await resp.Content.ReadFromJsonAsync<IReadOnlyList<PlayerStatsResult>>(cancellationToken: ct);
        return result ?? Array.Empty<PlayerStatsResult>();
    }
}
