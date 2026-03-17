using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Application.Sessions;

public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, (string ConversationId, DateTimeOffset CreatedAt)> _sessions = new();

    public Task<string> CreateSessionAsync(string conversationId)
    {
        var sessionId = Guid.NewGuid().ToString();
        _sessions[sessionId] = (conversationId, DateTimeOffset.UtcNow);
        return Task.FromResult(sessionId);
    }

    public Task<bool> SessionExistsAsync(string sessionId) =>
        Task.FromResult(_sessions.ContainsKey(sessionId));

    public Task<string?> GetConversationIdAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var entry);
        return Task.FromResult<string?>(entry == default ? null : entry.ConversationId);
    }
}
