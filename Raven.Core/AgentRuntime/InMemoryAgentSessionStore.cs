using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.AgentRuntime;

// In-memory implementation of IAgentSessionStore for use in unit tests.
// Not registered in production (FileAgentSessionStore is used instead).
// All entries are lost when the process restarts.
public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
  private readonly ConcurrentDictionary<string, string> _sessions =
      new (StringComparer.Ordinal);

  public Task SaveAsync (string conversationId, string serializedState, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (conversationId);
    ArgumentException.ThrowIfNullOrWhiteSpace (serializedState);
    _sessions[conversationId] = serializedState;
    return Task.CompletedTask;
  }

  public Task<string?> LoadAsync (string conversationId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (conversationId);
    _sessions.TryGetValue (conversationId, out var state);
    return Task.FromResult<string?> (state);
  }

  public Task<bool> DeleteAsync (string conversationId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (conversationId);
    return Task.FromResult (_sessions.TryRemove (conversationId, out _));
  }
}
