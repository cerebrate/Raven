using ArkaneSystems.Raven.Core.Application.Sessions;

namespace ArkaneSystems.Raven.Core.Application.Chat;

// Application boundary for chat/session use-cases consumed by HTTP endpoints.
// This keeps transport concerns separate from agent runtime and persistence details.
public interface IChatApplicationService
{
  // Creates a new conversation and linked session. Returns the client-facing session ID.
  Task<string> CreateSessionAsync (CancellationToken cancellationToken = default);

  // Sends a message for a session and returns the full reply, or null when the session is unknown.
  Task<string?> SendMessageAsync (
      string sessionId,
      string content,
      ChatRequestContext? requestContext = null,
      CancellationToken cancellationToken = default);

  // Streams message chunks for a session by invoking onChunkAsync for each chunk.
  // Returns false when the session is unknown; otherwise true.
  Task<bool> StreamMessageAsync (
      string sessionId,
      string content,
      Func<string, CancellationToken, Task> onChunkAsync,
      ChatRequestContext? requestContext = null,
      CancellationToken cancellationToken = default);

  // Returns session metadata, or null when the session is unknown.
  Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default);

  // Deletes a session. Returns false when the session is unknown.
  Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default);
}
