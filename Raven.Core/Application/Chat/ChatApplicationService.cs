#region header

// Raven.Core - ChatApplicationService.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-27 12:05 PM

#endregion

#region using

using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Sessions;
using JetBrains.Annotations;
using System.Text;

#endregion

namespace ArkaneSystems.Raven.Core.Application.Chat;

public sealed class ChatApplicationService (
  IAgentConversationService       conversations,
  ISessionStore                   sessions,
  ISessionEventLog                sessionEventLog,
  ISessionSnapshotStore           snapshotStore,
  IConversationTitleService       titleService,
  ILogger<ChatApplicationService> logger) : IChatApplicationService
{
  // Creates both sides of the session mapping so clients only work with Raven session IDs.
  // A snapshot is written immediately after creation so the session appears in the
  // resumable-sessions list from the moment it is created.
  public async Task<string> CreateSessionAsync (CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested ();

    string conversationId = await conversations.CreateConversationAsync ();
    string sessionId      = await sessions.CreateSessionAsync (conversationId);

    SessionEventEnvelope envelope = await sessionEventLog.AppendAsync (sessionId: sessionId,
                                                                       eventType: "session.created.v1",
                                                                       payload: new { ConversationId = conversationId },
                                                                       cancellationToken: cancellationToken);

    // Write an initial snapshot so the session is immediately resumable
    // even before any messages are sent.
    await snapshotStore.SaveSnapshotAsync (snapshot: new SessionSnapshot (SessionId: sessionId,
                                                                          ConversationId: conversationId,
                                                                          CreatedAt: DateTimeOffset.UtcNow,
                                                                          LastActivityAt: null,
                                                                          SnapshotAt: DateTimeOffset.UtcNow,
                                                                          EventLogSequence: envelope.Sequence),
                                           cancellationToken: cancellationToken);

    return sessionId;
  }

  // Resolves the session before sending to the agent so unknown sessions map to a null result.
  public async Task<string?> SendMessageAsync (string              sessionId,
                                               string              content,
                                               ChatRequestContext? requestContext    = null,
                                               CancellationToken   cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace (sessionId))
    {
      throw new ArgumentException (message: "Session ID cannot be empty.", paramName: nameof (sessionId));
    }

    if (string.IsNullOrWhiteSpace (content))
    {
      throw new ArgumentException (message: "Message content cannot be empty.", paramName: nameof (content));
    }

    cancellationToken.ThrowIfCancellationRequested ();

    string? conversationId = await sessions.GetConversationIdAsync (sessionId);

    if (conversationId is null)
    {
      return null;
    }

    ChatRequestContext context = requestContext ?? ChatRequestContext.Empty;

    // Load the current snapshot once. It gives us the existing title (so we
    // don't derive a new one on every message) and CreatedAt (so we don't
    // need a separate session store query).  Loaded before the agent call
    // so we do not pay the I/O overhead after the response arrives.
    SessionSnapshot? existingSnapshot =
      await snapshotStore.LoadSnapshotAsync (sessionId: sessionId, cancellationToken: cancellationToken);

    try
    {
      string reply = await conversations.SendMessageAsync (conversationId: conversationId, content: content);

      SessionEventEnvelope envelope = await sessionEventLog.AppendAsync (sessionId: sessionId,
                                                                         eventType: "chat.message.sent.v1",
                                                                         payload: new
                                                                                  {
                                                                                    RequestContent  = content,
                                                                                    ResponseContent = reply
                                                                                  },
                                                                         correlationId: context.CorrelationId,
                                                                         userId: context.UserId,
                                                                         cancellationToken: cancellationToken);

      // Update snapshot so the latest activity is reflected and the session
      // can be quickly resumed without replaying the full event log.
      // On the first exchange (no title yet) ask the model to generate one
      // from the message and reply.  On subsequent exchanges the existing
      // title is preserved; periodic re-generation will be hooked into
      // context-window consolidation (Epic 3).
      string? title = existingSnapshot?.Title;

      if (title is null)
      {
        title = await titleService.GenerateTitleAsync (userMessage: content,
                                                       agentReply: reply,
                                                       cancellationToken: cancellationToken);
      }

      // existingSnapshot is always present for a valid, non-stale session: CreateSessionAsync
      // writes an initial snapshot before returning the session ID.  The fallback to
      // sessions.GetSessionAsync (and ultimately UtcNow) is a defensive safety net for
      // any edge case where the initial snapshot was not written (e.g. a crash between
      // CreateConversationAsync and snapshotStore.SaveSnapshotAsync).
      DateTimeOffset createdAt = existingSnapshot?.CreatedAt ??
                                 (await sessions.GetSessionAsync (sessionId))?.CreatedAt ?? DateTimeOffset.UtcNow;
      await snapshotStore.SaveSnapshotAsync (snapshot: new SessionSnapshot (SessionId: sessionId,
                                                                            ConversationId: conversationId,
                                                                            CreatedAt: createdAt,
                                                                            LastActivityAt: DateTimeOffset.UtcNow,
                                                                            SnapshotAt: DateTimeOffset.UtcNow,
                                                                            EventLogSequence: envelope.Sequence,
                                                                            Title: title),
                                             cancellationToken: cancellationToken);

      return reply;
    }
    catch (ConversationNotFoundException ex)
    {
      bool invalidated = await sessions.DeleteSessionAsync (sessionId);

      // Remove the snapshot so the session no longer appears as resumable.
      _ = await snapshotStore.InvalidateSnapshotAsync (sessionId: sessionId, cancellationToken: cancellationToken);

      _ = await sessionEventLog.AppendAsync (sessionId: sessionId,
                                             eventType: "chat.message.failed.v1",
                                             payload: new
                                                      {
                                                        ErrorCode    = "session_stale",
                                                        ErrorMessage = ex.Message,
                                                        Invalidated  = invalidated
                                                      },
                                             correlationId: context.CorrelationId,
                                             userId: context.UserId,
                                             cancellationToken: cancellationToken);

      logger.LogWarning (message:
                         "Stale session detected during {Operation}. SessionId: {SessionId}, ConversationId: {ConversationId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Invalidated: {Invalidated}",
                         "SendMessage",
                         sessionId,
                         ex.ConversationId,
                         context.CorrelationId,
                         context.UserId,
                         invalidated);

      throw new SessionStaleException (sessionId);
    }
  }

  // Streams response chunks to the supplied callback to keep transport concerns outside this service.
  public async Task<bool> StreamMessageAsync (string                                sessionId,
                                              string                                content,
                                              Func<string, CancellationToken, Task> onChunkAsync,
                                              ChatRequestContext?                   requestContext    = null,
                                              CancellationToken                     cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace (sessionId))
    {
      throw new ArgumentException (message: "Session ID cannot be empty.", paramName: nameof (sessionId));
    }

    if (string.IsNullOrWhiteSpace (content))
    {
      throw new ArgumentException (message: "Message content cannot be empty.", paramName: nameof (content));
    }

    ArgumentNullException.ThrowIfNull (onChunkAsync);
    cancellationToken.ThrowIfCancellationRequested ();

    string? conversationId = await sessions.GetConversationIdAsync (sessionId);

    if (conversationId is null)
    {
      return false;
    }

    ChatRequestContext context = requestContext ?? ChatRequestContext.Empty;

    // Load the current snapshot once before starting the stream so we have
    // the existing title and CreatedAt without extra I/O after the response arrives.
    SessionSnapshot? existingSnapshot =
      await snapshotStore.LoadSnapshotAsync (sessionId: sessionId, cancellationToken: cancellationToken);

    try
    {
      // Accumulate the full reply while streaming so it is available for
      // title generation after the stream completes.
      StringBuilder replyBuilder = new StringBuilder ();

      await foreach (string chunk in conversations.StreamMessageAsync (conversationId: conversationId,
                                                                       content: content,
                                                                       cancellationToken: cancellationToken))
      {
        replyBuilder.Append (chunk);
        await onChunkAsync (arg1: chunk, arg2: cancellationToken);
      }

      // Update snapshot after successful stream completion so the session
      // is resumable from the latest state.
      // On the first exchange (no title yet) ask the model to generate one
      // from the message and accumulated reply.  On subsequent exchanges the
      // existing title is preserved; periodic re-generation will be hooked
      // into context-window consolidation (Epic 3).
      SessionEventEnvelope envelope = await sessionEventLog.AppendAsync (sessionId: sessionId,
                                                                         eventType: "chat.message.streamed.v1",
                                                                         payload: new { RequestContent = content },
                                                                         correlationId: context.CorrelationId,
                                                                         userId: context.UserId,
                                                                         cancellationToken: cancellationToken);

      string? title = existingSnapshot?.Title;

      if (title is null)
      {
        title = await titleService.GenerateTitleAsync (userMessage: content,
                                                       agentReply: replyBuilder.ToString (),
                                                       cancellationToken: cancellationToken);
      }

      // See SendMessageAsync for the rationale behind the existingSnapshot fallback chain.
      DateTimeOffset createdAt = existingSnapshot?.CreatedAt ??
                                 (await sessions.GetSessionAsync (sessionId))?.CreatedAt ?? DateTimeOffset.UtcNow;
      await snapshotStore.SaveSnapshotAsync (snapshot: new SessionSnapshot (SessionId: sessionId,
                                                                            ConversationId: conversationId,
                                                                            CreatedAt: createdAt,
                                                                            LastActivityAt: DateTimeOffset.UtcNow,
                                                                            SnapshotAt: DateTimeOffset.UtcNow,
                                                                            EventLogSequence: envelope.Sequence,
                                                                            Title: title),
                                             cancellationToken: cancellationToken);

      return true;
    }
    catch (ConversationNotFoundException ex)
    {
      bool invalidated = await sessions.DeleteSessionAsync (sessionId);

      // Remove the snapshot so the session no longer appears as resumable.
      _ = await snapshotStore.InvalidateSnapshotAsync (sessionId: sessionId, cancellationToken: cancellationToken);

      logger.LogWarning (message:
                         "Stale session detected during {Operation}. SessionId: {SessionId}, ConversationId: {ConversationId}, CorrelationId: {CorrelationId}, UserId: {UserId}, Invalidated: {Invalidated}",
                         "StreamMessage",
                         sessionId,
                         ex.ConversationId,
                         context.CorrelationId,
                         context.UserId,
                         invalidated);

      throw new SessionStaleException (sessionId);
    }
  }

  // Retrieves session metadata for session inspection APIs.
  // Also loads the snapshot title so callers get the full picture in one call.
  public async Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace (sessionId))
    {
      throw new ArgumentException (message: "Session ID cannot be empty.", paramName: nameof (sessionId));
    }

    cancellationToken.ThrowIfCancellationRequested ();

    SessionInfo? info = await sessions.GetSessionAsync (sessionId);

    if (info is null)
    {
      return null;
    }

    // Enrich with the title from the snapshot so callers don't need
    // to load the snapshot separately.
    SessionSnapshot? snapshot = await snapshotStore.LoadSnapshotAsync (sessionId: sessionId, cancellationToken: cancellationToken);

    return info with { Title = snapshot?.Title };
  }

  // Deletes session records through the session store and removes the snapshot
  // so the session no longer appears in the resumable-sessions list.
  public async Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace (sessionId))
    {
      throw new ArgumentException (message: "Session ID cannot be empty.", paramName: nameof (sessionId));
    }

    cancellationToken.ThrowIfCancellationRequested ();

    bool deleted = await sessions.DeleteSessionAsync (sessionId);

    if (!deleted)
    {
      return false;
    }

    // Remove the snapshot so the session no longer appears as resumable.
    _ = await snapshotStore.InvalidateSnapshotAsync (sessionId: sessionId, cancellationToken: cancellationToken);

    _ = await sessionEventLog.AppendAsync (sessionId: sessionId,
                                           eventType: "session.deleted.v1",
                                           payload: new { Deleted = true },
                                           cancellationToken: cancellationToken);

    return true;
  }

  // Returns all sessions that have a valid snapshot, ordered newest-first.
  // A snapshot exists for any session that was successfully created and has
  // not been explicitly deleted or invalidated by a stale-session event.
  public async Task<IReadOnlyList<SessionSnapshot>> ListSessionsAsync (CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested ();

    List<SessionSnapshot> snapshots = new List<SessionSnapshot> ();

    await foreach (SessionSnapshot snapshot in snapshotStore.ListSnapshotsAsync (cancellationToken))
    {
      snapshots.Add (snapshot);
    }

    // Most-recently-snapshotted sessions first.
    snapshots.Sort (static (a, b) => b.SnapshotAt.CompareTo (a.SnapshotAt));

    return snapshots;
  }
}