using System.Text;
using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Application.Chat;

public sealed class ChatStreamBroker (
    IChatApplicationService chat,
    ILogger<ChatStreamBroker> logger) : IChatStreamBroker
{
  public async Task<bool> StreamResponseEventsAsync (
      string sessionId,
      string content,
      Func<ResponseStreamEventEnvelope, CancellationToken, Task> onEventAsync,
      string? userId = null,
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

    ArgumentNullException.ThrowIfNull(onEventAsync);
    cancellationToken.ThrowIfCancellationRequested();

    var session = await chat.GetSessionAsync(sessionId, cancellationToken);
    if (session is null)
    {
      return false;
    }

    var responseId = Guid.NewGuid().ToString();
    var correlationId = Guid.NewGuid().ToString();
    var responseContent = new StringBuilder();
    var sequence = 0;

    await onEventAsync(
        CreateEnvelope(
            type: "chat.response.started.v1",
            sessionId: sessionId,
            userId: userId,
            correlationId: correlationId,
            causationId: null,
            streamEvent: new ResponseStarted(responseId, DateTimeOffset.UtcNow)),
        cancellationToken);

    try
    {
      var streamed = await chat.StreamMessageAsync(
          sessionId,
          content,
          async (chunk, ct) =>
          {
            sequence++;
            _ = responseContent.Append(chunk);

            await onEventAsync(
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
        await onEventAsync(
            CreateEnvelope(
                type: "chat.response.failed.v1",
                sessionId: sessionId,
                userId: userId,
                correlationId: correlationId,
                causationId: null,
                streamEvent: new ResponseFailed(responseId, "Session became unavailable before streaming completed.", DateTimeOffset.UtcNow, ErrorCode: "session_not_found", IsRetryable: false)),
            cancellationToken);

        return true;
      }

      await onEventAsync(
          CreateEnvelope(
              type: "chat.response.completed.v1",
              sessionId: sessionId,
              userId: userId,
              correlationId: correlationId,
              causationId: null,
              streamEvent: new ResponseCompleted(responseId, DateTimeOffset.UtcNow, responseContent.ToString())),
          cancellationToken);

      return true;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (InvalidOperationException ex)
    {
      // If the backing conversation mapping is missing, surface as stream failure
      // event without failing the HTTP pipeline after streaming has started.
      logger.LogWarning(ex, "Streaming failed for session {SessionId}", sessionId);

      await onEventAsync(
          CreateEnvelope(
              type: "chat.response.failed.v1",
              sessionId: sessionId,
              userId: userId,
              correlationId: correlationId,
              causationId: null,
              streamEvent: new ResponseFailed(responseId, ex.Message, DateTimeOffset.UtcNow, ErrorCode: "conversation_not_found", IsRetryable: false)),
          cancellationToken);

      return true;
    }
  }

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

    return new ResponseStreamEventEnvelope(metadata, streamEvent);
  }
}
