using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Application.Chat;

// Bridges chat message streaming into structured response events.
public interface IChatStreamBroker
{
  // Emits ordered response events for a session message stream.
  // Returns false when the session is unknown.
  Task<bool> StreamResponseEventsAsync (
      string sessionId,
      string content,
      Func<ResponseStreamEventEnvelope, CancellationToken, Task> onEventAsync,
      string? userId = null,
      CancellationToken cancellationToken = default);
}
