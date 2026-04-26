using ArkaneSystems.Raven.Core.Bus.Contracts;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Thread-safe in-memory stream hub keyed by ResponseId.
public sealed class InMemoryResponseStreamEventHub : IResponseStreamEventHub
{
  private readonly ConcurrentDictionary<string, Channel<ResponseStreamEventEnvelope>> _streams = new(StringComparer.Ordinal);

  public bool TryCreateStream (string responseId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(responseId);

    var channel = Channel.CreateUnbounded<ResponseStreamEventEnvelope>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false
    });

    return _streams.TryAdd(responseId, channel);
  }

  public async IAsyncEnumerable<ResponseStreamEventEnvelope> ReadAllAsync (
      string responseId,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(responseId);

    if (!_streams.TryGetValue(responseId, out var channel))
    {
      throw new InvalidOperationException($"Response stream '{responseId}' is not registered.");
    }

    try
    {
      await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken))
      {
        yield return envelope;
      }
    }
    finally
    {
      _ = _streams.TryRemove(responseId, out _);
    }
  }

  public async ValueTask PublishAsync (ResponseStreamEventEnvelope envelope, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(envelope);

    var responseId = envelope.Event.ResponseId;
    if (!_streams.TryGetValue(responseId, out var channel))
    {
      // Stream not found — the client disconnected and ReadAllAsync already removed it.
      // Silently discard instead of throwing so normal disconnects don't dead-letter.
      return;
    }

    await channel.Writer.WriteAsync(envelope, cancellationToken);
  }

  public void Complete (string responseId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(responseId);

    if (_streams.TryGetValue(responseId, out var channel))
    {
      channel.Writer.TryComplete();
    }
  }

  // Returns a snapshot of all currently open stream IDs. Used by ShutdownCoordinator
  // to broadcast a shutdown notification to every active SSE client.
  public IReadOnlyCollection<string> GetActiveStreamIds () => _streams.Keys.ToArray();
}
