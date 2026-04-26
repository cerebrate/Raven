using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// In-memory pub/sub hub for response-stream events keyed by ResponseId.
public interface IResponseStreamEventHub
{
  bool TryCreateStream (string responseId);

  IAsyncEnumerable<ResponseStreamEventEnvelope> ReadAllAsync (string responseId, CancellationToken cancellationToken);

  ValueTask PublishAsync (ResponseStreamEventEnvelope envelope, CancellationToken cancellationToken);

  void Complete (string responseId);

  // Returns the IDs of all response streams that are currently open.
  // Used by ShutdownCoordinator to broadcast a shutdown notification to all active clients.
  IReadOnlyCollection<string> GetActiveStreamIds ();
}
