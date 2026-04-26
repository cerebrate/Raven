using ArkaneSystems.Raven.Core.Bus.Contracts;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Thread-safe in-memory notification hub keyed by SessionId.
// Mirrors the design of InMemoryResponseStreamEventHub but for the persistent
// per-session notification channel rather than per-response SSE streams.
public sealed class InMemorySessionNotificationHub : ISessionNotificationHub
{
  private readonly ConcurrentDictionary<string, Channel<ServerNotificationEnvelope>> _channels =
      new (StringComparer.Ordinal);

  public bool TrySubscribe (string sessionId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    var channel = Channel.CreateUnbounded<ServerNotificationEnvelope>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false
    });

    return _channels.TryAdd(sessionId, channel);
  }

  public async IAsyncEnumerable<ServerNotificationEnvelope> ReadAllAsync (
      string sessionId,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    if (!_channels.TryGetValue(sessionId, out var channel))
    {
      throw new InvalidOperationException($"Notification channel for session '{sessionId}' is not registered.");
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
      // Remove the subscription when the reader exits (client disconnected or
      // Complete was called). This ensures the slot is free for reconnects.
      _ = _channels.TryRemove(sessionId, out _);
    }
  }

  public async ValueTask PublishToSessionAsync (
      string sessionId,
      ServerNotificationEnvelope envelope,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    ArgumentNullException.ThrowIfNull(envelope);

    if (!_channels.TryGetValue(sessionId, out var channel))
    {
      // Session not subscribed or already disconnected — discard silently.
      return;
    }

    try
    {
      await channel.Writer.WriteAsync(envelope, cancellationToken);
    }
    catch (ChannelClosedException)
    {
      // Channel was completed (e.g. by Complete()) before we could write.
      // Discard the notification — the subscriber is no longer active.
    }
  }

  public async ValueTask BroadcastAsync (
      ServerNotificationEnvelope envelope,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(envelope);

    // Snapshot the keys so we don't hold the dictionary lock while awaiting.
    var sessionIds = _channels.Keys.ToArray();

    foreach (var sessionId in sessionIds)
    {
      if (!_channels.TryGetValue(sessionId, out var channel))
        continue;

      try
      {
        await channel.Writer.WriteAsync(envelope, cancellationToken);
      }
      catch (ChannelClosedException)
      {
        // Channel was completed before we could write (e.g. the subscriber
        // disconnected mid-broadcast). Skip and continue so other subscribers
        // still receive the notification.
      }
    }
  }

  public void Complete (string sessionId)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    // Mark the writer as complete so ReadAllAsync's foreach exits cleanly.
    // Do NOT remove from _channels here — ReadAllAsync's finally block handles
    // removal. Removing here would create a race: if TrySubscribe and ReadAllAsync
    // are called in sequence, Complete() could delete the channel between them,
    // causing ReadAllAsync to throw "channel not registered".
    if (_channels.TryGetValue(sessionId, out var channel))
    {
      channel.Writer.TryComplete();
    }
  }

  // Returns a snapshot of all currently subscribed session IDs.
  public IReadOnlyCollection<string> GetSubscribedSessionIds () => _channels.Keys.ToArray();
}
