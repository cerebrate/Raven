using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;

namespace ArkaneSystems.Raven.Core.Bus.Handlers;

// Forwards dispatched response-stream events into the in-memory subscription hub.
public sealed class ResponseStreamEventForwardingHandler (
    IResponseStreamEventHub streamHub,
    ILogger<ResponseStreamEventForwardingHandler> logger) : IMessageHandler<ResponseStreamEventEnvelope>
{
  public async Task HandleAsync (MessageEnvelope<ResponseStreamEventEnvelope> message, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(message);

    await streamHub.PublishAsync(message.Payload, cancellationToken);

    if (message.Payload.Event is ResponseCompleted or ResponseFailed)
    {
      streamHub.Complete(message.Payload.Event.ResponseId);
      logger.LogDebug(
          "Completed response stream {ResponseId} for message type {MessageType}",
          message.Payload.Event.ResponseId,
          message.Metadata.Type);
    }
  }
}
