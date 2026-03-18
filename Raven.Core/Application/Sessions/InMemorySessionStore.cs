using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Application.Sessions;

// In-memory implementation of ISessionStore. Kept for use in tests and as a
// fallback during development — not registered in production (SqliteSessionStore
// is used instead). All data is lost when the process restarts.
public class InMemorySessionStore : ISessionStore
{
  // ConcurrentDictionary makes individual read/write operations thread-safe
  // without needing explicit locks. The tuple value stores both the Foundry
  // conversation ID and the creation timestamp.
  private readonly ConcurrentDictionary<string, (string ConversationId, DateTimeOffset CreatedAt)> _sessions = new();

  public Task<string> CreateSessionAsync (string conversationId)
  {
    var sessionId = Guid.NewGuid().ToString();
    _sessions[sessionId] = (conversationId, DateTimeOffset.UtcNow);
    return Task.FromResult (sessionId);
  }

  public Task<bool> SessionExistsAsync (string sessionId) =>
      Task.FromResult (_sessions.ContainsKey (sessionId));

  public Task<string?> GetConversationIdAsync (string sessionId)
  {
    _sessions.TryGetValue (sessionId, out var entry);
    return Task.FromResult<string?> (entry == default ? null : entry.ConversationId);
  }

  public Task<SessionInfo?> GetSessionAsync (string sessionId)
  {
    _sessions.TryGetValue (sessionId, out var entry);
    if (entry == default)
      return Task.FromResult<SessionInfo?> (null);

    // LastActivityAt is not tracked in-memory (no write-through on lookup),
    // so it is always returned as null here.
    return Task.FromResult<SessionInfo?> (new SessionInfo (sessionId, entry.CreatedAt, null));
  }

  public Task<bool> DeleteSessionAsync (string sessionId) =>
      Task.FromResult (_sessions.TryRemove (sessionId, out _));
}