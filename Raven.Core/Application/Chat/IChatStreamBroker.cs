using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Application.Chat;

// Bridges chat message streaming into structured response events.
public interface IChatStreamBroker
{
  // Starts publishing ordered response events for a session message stream.
  // Returns null when the session is unknown.
  Task<ChatStreamStartResult?> StartResponseStreamAsync (
      string sessionId,
      string content,
      string? correlationId = null,
      string? userId = null,
      CancellationToken cancellationToken = default);
}

// Metadata returned when a stream starts successfully.
public sealed record ChatStreamStartResult(
    string ResponseId,
    Task Completion);
