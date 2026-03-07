namespace PokerAnalyzer.Infrastructure.HandHistories
{
    public interface IHandHistoryRepository
    {
        Task<HandHistorySession?> GetSessionAsync(Guid sessionId, CancellationToken ct);
        Task<Hand?> GetHandAsync(Guid handId, CancellationToken ct);
        Task<Hand?> GetHandByGameCodeAsync(long gameCode, CancellationToken ct);
    }
}
