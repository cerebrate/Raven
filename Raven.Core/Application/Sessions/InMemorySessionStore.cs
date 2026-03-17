using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Application.Sessions;

public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();

    public Task<string> CreateSessionAsync()
    {
        var sessionId = Guid.NewGuid().ToString();
        _sessions[sessionId] = DateTimeOffset.UtcNow;
        return Task.FromResult(sessionId);
    }

    public Task<bool> SessionExistsAsync(string sessionId) =>
        Task.FromResult(_sessions.ContainsKey(sessionId));
}
