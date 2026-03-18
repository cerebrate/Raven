using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Publisher abstraction for enqueueing typed envelopes into the in-process bus.
public interface IMessageBus
{
  Task PublishAsync<TPayload> (MessageEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
      where TPayload : notnull;
}
