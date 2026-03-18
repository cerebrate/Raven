using ArkaneSystems.Raven.Core.Application.Sessions;
using Microsoft.EntityFrameworkCore;

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

// SQLite-backed implementation of ISessionStore using EF Core.
// Uses IDbContextFactory so each method creates and disposes its own DbContext,
// which is the correct pattern when the store itself is registered as Scoped and
// the factory is registered as Singleton.
public class SqliteSessionStore(IDbContextFactory<RavenDbContext> contextFactory) : ISessionStore
{
    public async Task<string> CreateSessionAsync(string conversationId)
    {
        var sessionId = Guid.NewGuid().ToString();
        await using var db = await contextFactory.CreateDbContextAsync();

        db.Sessions.Add(new SessionRecord
        {
            SessionId = sessionId,
            ConversationId = conversationId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return sessionId;
    }

    public async Task<bool> SessionExistsAsync(string sessionId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Sessions.AnyAsync(s => s.SessionId == sessionId);
    }

    public async Task<string?> GetConversationIdAsync(string sessionId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var record = await db.Sessions
            .AsNoTracking()   // read-only query; no change tracking needed
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (record is null)
            return null;

        // Fire-and-forget style update: stamp LastActivityAt in the same call
        // so callers don't have to remember to do it separately.
        await db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastActivityAt, DateTimeOffset.UtcNow));

        return record.ConversationId;
    }

    public async Task<SessionInfo?> GetSessionAsync(string sessionId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var record = await db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        return record is null
            ? null
            : new SessionInfo(record.SessionId, record.CreatedAt, record.LastActivityAt);
    }

    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        // ExecuteDeleteAsync issues a single DELETE statement without loading
        // the entity into memory first. Returns the number of rows affected.
        var deleted = await db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteDeleteAsync();

        return deleted > 0;
    }
}
