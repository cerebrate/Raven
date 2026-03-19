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
      ChatRequestContext? requestContext = null,
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

    var context = requestContext ?? ChatRequestContext.Empty;

    var resolvedCorrelationId = string.IsNullOrWhiteSpace(context.CorrelationId)
      ? Guid.NewGuid().ToString()
      : context.CorrelationId;

    var completion = this.PublishStreamAsync(responseId, sessionId, content, resolvedCorrelationId, context.UserId, cancellationToken);
    return new ChatStreamStartResult (responseId, completion);
  }

  private async Task PublishStreamAsync (
      string responseId,
      string sessionId,
      string content,
      string correlationId,
      string? userId,
      CancellationToken cancellationToken)
  {
    var responseContent = new StringBuilder();
    var sequence = 0;
    var terminalPublished = false;
    string? causationId = null;

    try
    {
      var startedEnvelope = CreateEnvelope(
          type: "chat.response.started.v1",
          sessionId: sessionId,
          userId: userId,
          correlationId: correlationId,
          causationId: causationId,
          streamEvent: new ResponseStarted(responseId, DateTimeOffset.UtcNow));

      await this.PublishEventAsync(startedEnvelope, cancellationToken);
      causationId = startedEnvelope.Metadata.MessageId;

      var streamed = await chat.StreamMessageAsync(
          sessionId,
          content,
          async (chunk, ct) =>
          {
            sequence++;
            _ = responseContent.Append(chunk);

            var deltaEnvelope = CreateEnvelope(
                type: "chat.response.delta.v1",
                sessionId: sessionId,
                userId: userId,
                correlationId: correlationId,
                causationId: causationId,
                streamEvent: new ResponseDelta(responseId, sequence, chunk, DateTimeOffset.UtcNow));

            await this.PublishEventAsync(deltaEnvelope, ct);
            causationId = deltaEnvelope.Metadata.MessageId;
          },
          requestContext: new ChatRequestContext(correlationId, userId),
          cancellationToken: cancellationToken);

      if (!streamed)
      {
        var failedEnvelope = CreateEnvelope(
            type: "chat.response.failed.v1",
            sessionId: sessionId,
            userId: userId,
            correlationId: correlationId,
            causationId: causationId,
            streamEvent: new ResponseFailed(responseId, "Session became unavailable before streaming completed.", DateTimeOffset.UtcNow, ErrorCode: "session_not_found", IsRetryable: false));

        await this.PublishEventAsync(failedEnvelope, cancellationToken);
        terminalPublished = true;
        return;
      }

      var completedEnvelope = CreateEnvelope(
          type: "chat.response.completed.v1",
          sessionId: sessionId,
          userId: userId,
          correlationId: correlationId,
          causationId: causationId,
          streamEvent: new ResponseCompleted(responseId, DateTimeOffset.UtcNow, responseContent.ToString()));

      await this.PublishEventAsync(completedEnvelope, cancellationToken);
      terminalPublished = true;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (SessionStaleException ex)
    {
      logger.LogInformation("Session {SessionId} is stale during streaming and will require recovery. CorrelationId: {CorrelationId}, UserId: {UserId}", ex.SessionId, correlationId, userId);

      var failedEnvelope = CreateEnvelope(
          type: "chat.response.failed.v1",
          sessionId: sessionId,
          userId: userId,
          correlationId: correlationId,
          causationId: causationId,
          streamEvent: new ResponseFailed(responseId, ex.Message, DateTimeOffset.UtcNow, ErrorCode: "session_stale", IsRetryable: false));

      await this.PublishEventAsync(failedEnvelope, cancellationToken);
      terminalPublished = true;
    }
    catch (InvalidOperationException ex)
    {
      // If the backing conversation mapping is missing, surface as stream failure
      // event without failing the HTTP pipeline after streaming has started.
      logger.LogWarning(ex, "Streaming failed for session {SessionId}. CorrelationId: {CorrelationId}, UserId: {UserId}", sessionId, correlationId, userId);

      var failedEnvelope = CreateEnvelope(
          type: "chat.response.failed.v1",
          sessionId: sessionId,
          userId: userId,
          correlationId: correlationId,
          causationId: causationId,
          streamEvent: new ResponseFailed(responseId, ex.Message, DateTimeOffset.UtcNow, ErrorCode: "conversation_not_found", IsRetryable: false));

      await this.PublishEventAsync(failedEnvelope, cancellationToken);
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
