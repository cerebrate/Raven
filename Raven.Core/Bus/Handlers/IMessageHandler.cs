using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Bus.Handlers;

// Handles a typed bus payload wrapped in a message envelope.
public interface IMessageHandler<TPayload>
    where TPayload : notnull
{
  Task HandleAsync (MessageEnvelope<TPayload> message, CancellationToken cancellationToken);
}
