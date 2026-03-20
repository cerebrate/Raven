namespace ArkaneSystems.Raven.Core.Application.Chat;

// Thrown when a persisted session maps to a conversation that is no longer
// present in the agent runtime and must be recreated by the client.
public sealed class SessionStaleException (string sessionId)
    : InvalidOperationException($"Session '{sessionId}' is stale and must be recreated.")
{
  public string SessionId { get; } = sessionId;
}
