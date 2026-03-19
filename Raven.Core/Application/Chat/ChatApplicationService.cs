using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace ArkaneSystems.Raven.Core.Application.Chat;

public sealed class ChatApplicationService (
    IAgentConversationService conversations,
    ISessionStore sessions,
    ILogger<ChatApplicationService> logger) : IChatApplicationService
{
  // Creates both sides of the session mapping so clients only work with Raven session IDs.
  public async Task<string> CreateSessionAsync (CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var conversationId = await conversations.CreateConversationAsync();
    return await sessions.CreateSessionAsync(conversationId);
  }

  // Resolves the session before sending to the agent so unknown sessions map to a null result.
  public async Task<string?> SendMessageAsync (string sessionId, string content, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    if (string.IsNullOrWhiteSpace(content))
    {
      throw new ArgumentException("Message content cannot be empty.", nameof(content));
    }

    cancellationToken.ThrowIfCancellationRequested();

    var conversationId = await sessions.GetConversationIdAsync(sessionId);
    if (conversationId is null)
    {
      return null;
    }

    try
    {
      return await conversations.SendMessageAsync(conversationId, content);
    }
    catch (ConversationNotFoundException ex)
    {
      var invalidated = await sessions.DeleteSessionAsync(sessionId);

      logger.LogWarning(
          "Stale session detected during {Operation}. SessionId: {SessionId}, ConversationId: {ConversationId}, Invalidated: {Invalidated}",
          "SendMessage",
          sessionId,
          ex.ConversationId,
          invalidated);

      throw new SessionStaleException(sessionId);
    }
  }

  // Streams response chunks to the supplied callback to keep transport concerns outside this service.
  public async Task<bool> StreamMessageAsync (
      string sessionId,
      string content,
      Func<string, CancellationToken, Task> onChunkAsync,
      CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    if (string.IsNullOrWhiteSpace(content))
    {
      throw new ArgumentException("Message content cannot be empty.", nameof(content));
    }

    ArgumentNullException.ThrowIfNull(onChunkAsync);
    cancellationToken.ThrowIfCancellationRequested();

    var conversationId = await sessions.GetConversationIdAsync(sessionId);
    if (conversationId is null)
    {
      return false;
    }

    try
    {
      await foreach (var chunk in conversations.StreamMessageAsync(conversationId, content, cancellationToken))
      {
        await onChunkAsync(chunk, cancellationToken);
      }

      return true;
    }
    catch (ConversationNotFoundException ex)
    {
      var invalidated = await sessions.DeleteSessionAsync(sessionId);

      logger.LogWarning(
          "Stale session detected during {Operation}. SessionId: {SessionId}, ConversationId: {ConversationId}, Invalidated: {Invalidated}",
          "StreamMessage",
          sessionId,
          ex.ConversationId,
          invalidated);

      throw new SessionStaleException(sessionId);
    }
  }

  // Retrieves session metadata for session inspection APIs.
  public Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    cancellationToken.ThrowIfCancellationRequested();
    return sessions.GetSessionAsync(sessionId);
  }

  // Deletes session records through the session store.
  public Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    cancellationToken.ThrowIfCancellationRequested();
    return sessions.DeleteSessionAsync(sessionId);
  }
}
