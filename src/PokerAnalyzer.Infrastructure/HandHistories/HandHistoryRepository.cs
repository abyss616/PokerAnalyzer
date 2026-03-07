using Microsoft.EntityFrameworkCore;
using PokerAnalyzer.Infrastructure.Persistence;

namespace PokerAnalyzer.Infrastructure.HandHistories;

public sealed class HandHistoryRepository : IHandHistoryRepository
{
    private readonly PokerDbContext _db;

    public HandHistoryRepository(PokerDbContext db)
    {
        _db = db;
    }

    public Task<HandHistorySession?> GetSessionAsync(Guid sessionId, CancellationToken ct) =>
        _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId, ct);

    public Task<Hand?> GetHandAsync(Guid handId, CancellationToken ct) =>
        _db.HandHistoryHands
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == handId, ct);

    public Task<Hand?> GetHandByGameCodeAsync(long handNumber, CancellationToken ct) =>
        _db.HandHistoryHands
            .AsNoTracking()
            .Include(x => x.Actions)
            .Include(x => x.Players)
            .OrderBy(x => x.StartedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((int)Math.Max(0, handNumber - 1))
            .FirstOrDefaultAsync(ct);

}
