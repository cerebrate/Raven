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

    // Load the current snapshot once. It gives us the existing title (so we
    // don't derive a new one on every message) and CreatedAt (so we don't
    // need a separate session store query).  Loaded before the agent call
    // so we do not pay the I/O overhead after the response arrives.
    var existingSnapshot = await snapshotStore.LoadSnapshotAsync (sessionId, cancellationToken);

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
      // Preserve any title already set on the snapshot; derive one from the first
      // user message if the session doesn't have one yet.
      var title = existingSnapshot?.Title ?? DeriveTitle (content);
      var createdAt = existingSnapshot?.CreatedAt ?? (await sessions.GetSessionAsync (sessionId))?.CreatedAt ?? DateTimeOffset.UtcNow;
      await snapshotStore.SaveSnapshotAsync (new SessionSnapshot (
          SessionId:        sessionId,
          ConversationId:   conversationId,
          CreatedAt:        createdAt,
          LastActivityAt:   DateTimeOffset.UtcNow,
          SnapshotAt:       DateTimeOffset.UtcNow,
          EventLogSequence: envelope.Sequence,
          Title:            title), cancellationToken);

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

    // Load the current snapshot once before starting the stream so we have
    // the existing title and CreatedAt without extra I/O after the response arrives.
    var existingSnapshot = await snapshotStore.LoadSnapshotAsync (sessionId, cancellationToken);

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

      var title = existingSnapshot?.Title ?? DeriveTitle (content);
      var createdAt = existingSnapshot?.CreatedAt ?? (await sessions.GetSessionAsync (sessionId))?.CreatedAt ?? DateTimeOffset.UtcNow;
      await snapshotStore.SaveSnapshotAsync (new SessionSnapshot (
          SessionId:        sessionId,
          ConversationId:   conversationId,
          CreatedAt:        createdAt,
          LastActivityAt:   DateTimeOffset.UtcNow,
          SnapshotAt:       DateTimeOffset.UtcNow,
          EventLogSequence: envelope.Sequence,
          Title:            title), cancellationToken);

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
  // Also loads the snapshot title so callers get the full picture in one call.
  public async Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
    }

    cancellationToken.ThrowIfCancellationRequested();

    var info = await sessions.GetSessionAsync(sessionId);
    if (info is null)
      return null;

    // Enrich with the title from the snapshot so callers don't need
    // to load the snapshot separately.
    var snapshot = await snapshotStore.LoadSnapshotAsync (sessionId, cancellationToken);
    return info with { Title = snapshot?.Title };
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

  // Derives a human-readable title from the first user message so that the
  // session list can show something meaningful without an extra AI call.
  //
  // Strategy: take the first line of the message (up to TitleMaxLength
  // characters), strip excess whitespace, and append "…" if truncated.
  // This is deterministic, fast, and requires no round-trip to the agent.
  private const int TitleMaxLength = 60;

  internal static string DeriveTitle (string firstUserMessage)
  {
    if (string.IsNullOrWhiteSpace (firstUserMessage))
      return "New conversation";

    // Use the first non-blank line as the basis for the title.
    var firstLine = firstUserMessage
        .Split (['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault (static l => !string.IsNullOrWhiteSpace (l))
        ?.Trim ()
        ?? firstUserMessage.Trim ();

    if (firstLine.Length <= TitleMaxLength)
      return firstLine;

    // Truncate at a word boundary when possible so the title doesn't cut mid-word.
    var truncated = firstLine[..TitleMaxLength];
    var lastSpace = truncated.LastIndexOf (' ');
    if (lastSpace > TitleMaxLength / 2)
      truncated = truncated[..lastSpace];

    return truncated + "…";
  }
}
