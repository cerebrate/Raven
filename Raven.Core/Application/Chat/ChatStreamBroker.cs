using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using System.Text;

namespace ArkaneSystems.Raven.Core.Application.Chat;

public sealed class ChatStreamBroker (
    IChatApplicationService chat,
    IMessageBus messageBus,
    IResponseStreamEventHub streamHub,
    ILogger<ChatStreamBroker> logger) : IChatStreamBroker
{
  public async Task<ChatStreamStartResult?> StartResponseStreamAsync (
      string sessionId,
      string content,
      string? userId = null,
      CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace (sessionId))
    {
      throw new ArgumentException ("Session ID cannot be empty.", nameof (sessionId));
    }

    if (string.IsNullOrWhiteSpace (content))
    {
      throw new ArgumentException ("Message content cannot be empty.", nameof (content));
    }

    cancellationToken.ThrowIfCancellationRequested ();

    var session = await chat.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
      return null;
    }

    var responseId = Guid.NewGuid().ToString();
    if (!streamHub.TryCreateStream (responseId))
    {
      throw new InvalidOperationException ($"Response stream '{responseId}' already exists.");
    }

    var completion = this.PublishStreamAsync(responseId, sessionId, content, userId, cancellationToken);
    return new ChatStreamStartResult (responseId, completion);
  }

  private async Task PublishStreamAsync (
      string responseId,
      string sessionId,
      string content,
      string? userId,
      CancellationToken cancellationToken)
  {
    var correlationId = Guid.NewGuid().ToString();
    var responseContent = new StringBuilder();
    var sequence = 0;
    var terminalPublished = false;

    try
    {
      await this.PublishEventAsync (
          CreateEnvelope (
              type: "chat.response.started.v1",
              sessionId: sessionId,
              userId: userId,
              correlationId: correlationId,
              causationId: null,
              streamEvent: new ResponseStarted (responseId, DateTimeOffset.UtcNow)),
          cancellationToken);

      var streamed = await chat.StreamMessageAsync(
          sessionId,
          content,
          async (chunk, ct) =>
          {
            sequence++;
            _ = responseContent.Append(chunk);

            await this.PublishEventAsync(
                CreateEnvelope(
                    type: "chat.response.delta.v1",
                    sessionId: sessionId,
                    userId: userId,
                    correlationId: correlationId,
                    causationId: null,
                    streamEvent: new ResponseDelta(responseId, sequence, chunk, DateTimeOffset.UtcNow)),
                ct);
          },
          cancellationToken);

      if (!streamed)
      {
        await this.PublishEventAsync (
            CreateEnvelope (
                type: "chat.response.failed.v1",
                sessionId: sessionId,
                userId: userId,
                correlationId: correlationId,
                causationId: null,
                streamEvent: new ResponseFailed (responseId, "Session became unavailable before streaming completed.", DateTimeOffset.UtcNow, ErrorCode: "session_not_found", IsRetryable: false)),
            cancellationToken);

        terminalPublished = true;
        return;
      }

      await this.PublishEventAsync (
          CreateEnvelope (
              type: "chat.response.completed.v1",
              sessionId: sessionId,
              userId: userId,
              correlationId: correlationId,
              causationId: null,
              streamEvent: new ResponseCompleted (responseId, DateTimeOffset.UtcNow, responseContent.ToString ())),
          cancellationToken);

      terminalPublished = true;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (SessionStaleException ex)
    {
#pragma warning disable CA1873 // Avoid potentially expensive logging
      logger.LogInformation ("Session {SessionId} is stale during streaming and will require recovery.", ex.SessionId);
#pragma warning restore CA1873 // Avoid potentially expensive logging

      await this.PublishEventAsync (
          CreateEnvelope (
              type: "chat.response.failed.v1",
              sessionId: sessionId,
              userId: userId,
              correlationId: correlationId,
              causationId: null,
              streamEvent: new ResponseFailed (responseId, ex.Message, DateTimeOffset.UtcNow, ErrorCode: "session_stale", IsRetryable: false)),
          cancellationToken);

      terminalPublished = true;
    }
    catch (InvalidOperationException ex)
    {
      // If the backing conversation mapping is missing, surface as stream failure
      // event without failing the HTTP pipeline after streaming has started.
      logger.LogWarning (ex, "Streaming failed for session {SessionId}", sessionId);

      await this.PublishEventAsync (
          CreateEnvelope (
              type: "chat.response.failed.v1",
              sessionId: sessionId,
              userId: userId,
              correlationId: correlationId,
              causationId: null,
              streamEvent: new ResponseFailed (responseId, ex.Message, DateTimeOffset.UtcNow, ErrorCode: "conversation_not_found", IsRetryable: false)),
          cancellationToken);

      terminalPublished = true;
    }
    finally
    {
      // Only force-complete when no terminal event was published (for example,
      // cancellation before completed/failed could be enqueued). When a terminal
      // event is published, ResponseStreamEventForwardingHandler completes the stream
      // after forwarding that terminal event to subscribers.
      if (!terminalPublished)
      {
        streamHub.Complete (responseId);
      }
    }
  }

  private Task PublishEventAsync (ResponseStreamEventEnvelope eventEnvelope, CancellationToken cancellationToken) =>
      messageBus.PublishAsync (new MessageEnvelope<ResponseStreamEventEnvelope> (eventEnvelope.Metadata, eventEnvelope), cancellationToken);

  private static ResponseStreamEventEnvelope CreateEnvelope (
      string type,
      string sessionId,
      string? userId,
      string correlationId,
      string? causationId,
      IResponseStreamEvent streamEvent)
  {
    var metadata = MessageMetadata.Create(
        type: type,
        correlationId: correlationId,
        causationId: causationId,
        sessionId: sessionId,
        userId: userId,
        priority: MessagePriority.Normal,
        createdAtUtc: DateTimeOffset.UtcNow);

    return new ResponseStreamEventEnvelope (metadata, streamEvent);
  }
}
