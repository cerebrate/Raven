using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace ArkaneSystems.Raven.Core.Application.Chat;

public sealed class ChatApplicationService (
    IAgentConversationService conversations,
    ISessionStore sessions,
    ISessionEventLog sessionEventLog,
    ISessionSnapshotStore snapshotStore,
    ILogger<ChatApplicationService> logger) : IChatApplicationService
{
  // Creates both sides of the session mapping so clients only work with Raven session IDs.
  // A snapshot is written immediately after creation so the session appears in the
  // resumable-sessions list from the moment it is created.
  public async Task<string> CreateSessionAsync (CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var conversationId = await conversations.CreateConversationAsync();
    var sessionId = await sessions.CreateSessionAsync(conversationId);

    var envelope = await sessionEventLog.AppendAsync(
        sessionId,
        eventType: "session.created.v1",
        payload: new
        {
          ConversationId = conversationId
        },
        cancellationToken: cancellationToken);

    // Write an initial snapshot so the session is immediately resumable
    // even before any messages are sent.
    await snapshotStore.SaveSnapshotAsync (new SessionSnapshot (
        SessionId:        sessionId,
        ConversationId:   conversationId,
        CreatedAt:        DateTimeOffset.UtcNow,
        LastActivityAt:   null,
        SnapshotAt:       DateTimeOffset.UtcNow,
        EventLogSequence: envelope.Sequence), cancellationToken);

    return sessionId;
  }

  // Resolves the session before sending to the agent so unknown sessions map to a null result.
  public async Task<string?> SendMessageAsync (
      string sessionId,
      string content,
      ChatRequestContext? requestContext = null,
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

    cancellationToken.ThrowIfCancellationRequested();

    var conversationId = await sessions.GetConversationIdAsync(sessionId);
    if (conversationId is null)
    {
      return null;
    }

    var context = requestContext ?? ChatRequestContext.Empty;

    try
    {
      var reply = await conversations.SendMessageAsync(conversationId, content);

      var envelope = await sessionEventLog.AppendAsync(
          sessionId,
          eventType: "chat.message.sent.v1",
          payload: new
          {
            RequestContent = content,
            ResponseContent = reply
          },
          correlationId: context.CorrelationId,
          userId: context.UserId,
          cancellationToken: cancellationToken);

      // Update snapshot so the latest activity is reflected and the session
      // can be quickly resumed without replaying the full event log.
      var sessionInfo = await sessions.GetSessionAsync (sessionId);
      await snapshotStore.SaveSnapshotAsync (new SessionSnapshot (
          SessionId:        sessionId,
          ConversationId:   conversationId,
          CreatedAt:        sessionInfo?.CreatedAt ?? DateTimeOffset.UtcNow,
          LastActivityAt:   DateTimeOffset.UtcNow,
          SnapshotAt:       DateTimeOffset.UtcNow,
          EventLogSequence: envelope.Sequence), cancellationToken);

      return reply;
    }
    catch (ConversationNotFoundException ex)
    {
      var invalidated = await sessions.DeleteSessionAsync(sessionId);

      // Remove the snapshot so the session no longer appears as resumable.
      _ = await snapshotStore.InvalidateSnapshotAsync (sessionId, cancellationToken);

      _ = await sessionEventLog.AppendAsync(
          sessionId,
          eventType: "chat.message.failed.v1",
          payload: new
          {
            ErrorCode = "session_stale",
            ErrorMessage = ex.Message,
            Invalidated = invalidated
          },
          correlationId: context.CorrelationId,
          userId: context.UserId,
          cancellationToken: cancellationToken);

      logger.LogWarning(
          "Stale session detected during {Operation}. SessionId: {SessionId}, ConversationId: {ConversationId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Invalidated: {Invalidated}",
          "SendMessage",
          sessionId,
          ex.ConversationId,
          context.CorrelationId,
          context.UserId,
          invalidated);

      throw new SessionStaleException(sessionId);
    }
  }

  // Streams response chunks to the supplied callback to keep transport concerns outside this service.
  public async Task<bool> StreamMessageAsync (
      string sessionId,
      string content,
      Func<string, CancellationToken, Task> onChunkAsync,
      ChatRequestContext? requestContext = null,
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

    var context = requestContext ?? ChatRequestContext.Empty;

    try
    {
      await foreach (var chunk in conversations.StreamMessageAsync(conversationId, content, cancellationToken))
      {
        await onChunkAsync(chunk, cancellationToken);
      }

      // Update snapshot after successful stream completion so the session
      // is resumable from the latest state.
      var envelope = await sessionEventLog.AppendAsync(
          sessionId,
          eventType: "chat.message.streamed.v1",
          payload: new
          {
            RequestContent = content
          },
          correlationId: context.CorrelationId,
          userId: context.UserId,
          cancellationToken: cancellationToken);

      var sessionInfo = await sessions.GetSessionAsync (sessionId);
      await snapshotStore.SaveSnapshotAsync (new SessionSnapshot (
          SessionId:        sessionId,
          ConversationId:   conversationId,
          CreatedAt:        sessionInfo?.CreatedAt ?? DateTimeOffset.UtcNow,
          LastActivityAt:   DateTimeOffset.UtcNow,
          SnapshotAt:       DateTimeOffset.UtcNow,
          EventLogSequence: envelope.Sequence), cancellationToken);

      return true;
    }
    catch (ConversationNotFoundException ex)
    {
      var invalidated = await sessions.DeleteSessionAsync(sessionId);

      // Remove the snapshot so the session no longer appears as resumable.
      _ = await snapshotStore.InvalidateSnapshotAsync (sessionId, cancellationToken);

      logger.LogWarning(
          "Stale session detected during {Operation}. SessionId: {SessionId}, ConversationId: {ConversationId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Invalidated: {Invalidated}",
          "StreamMessage",
          sessionId,
          ex.ConversationId,
          context.CorrelationId,
          context.UserId,
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

  // Deletes session records through the session store and removes the snapshot
  // so the session no longer appears in the resumable-sessions list.
  public async Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    cancellationToken.ThrowIfCancellationRequested();

    var deleted = await sessions.DeleteSessionAsync(sessionId);
    if (!deleted)
    {
      return false;
    }

    // Remove the snapshot so the session no longer appears as resumable.
    _ = await snapshotStore.InvalidateSnapshotAsync (sessionId, cancellationToken);

    _ = await sessionEventLog.AppendAsync(
        sessionId,
        eventType: "session.deleted.v1",
        payload: new
        {
          Deleted = true
        },
        cancellationToken: cancellationToken);

    return true;
  }

  // Returns all sessions that have a valid snapshot, ordered newest-first.
  // A snapshot exists for any session that was successfully created and has
  // not been explicitly deleted or invalidated by a stale-session event.
  public async Task<IReadOnlyList<SessionSnapshot>> ListSessionsAsync (CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var snapshots = new List<SessionSnapshot>();
    await foreach (var snapshot in snapshotStore.ListSnapshotsAsync (cancellationToken))
    {
      snapshots.Add (snapshot);
    }

    // Most-recently-snapshotted sessions first.
    snapshots.Sort (static (a, b) => b.SnapshotAt.CompareTo (a.SnapshotAt));
    return snapshots;
  }
}
