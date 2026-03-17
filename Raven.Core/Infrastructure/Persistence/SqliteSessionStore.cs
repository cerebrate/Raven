using ArkaneSystems.Raven.Core.Application.Sessions;
using Microsoft.EntityFrameworkCore;

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

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
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (record is null)
            return null;

        // Touch last activity
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
        var deleted = await db.Sessions
            .Where(s => s.SessionId == sessionId)
            .ExecuteDeleteAsync();

        return deleted > 0;
    }
}
