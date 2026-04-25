using ArkaneSystems.Raven.Core.Application.Sessions;
using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Application.Fakes;

internal sealed class InMemorySessionEventLog : ISessionEventLog
{
  private readonly ConcurrentDictionary<string, List<SessionEventEnvelope>> _events = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<string, long> _sequences = new(StringComparer.Ordinal);

  public Task<SessionEventEnvelope> AppendAsync (
      string sessionId,
      string eventType,
      object payload,
      string? correlationId = null,
      string? userId = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
    ArgumentNullException.ThrowIfNull(payload);

    cancellationToken.ThrowIfCancellationRequested();

    var next = _sequences.AddOrUpdate(sessionId, 1, static (_, current) => current + 1);

    var envelope = new SessionEventEnvelope(
        EventId: Guid.NewGuid().ToString(),
        SessionId: sessionId,
        Sequence: next,
        EventType: eventType,
        OccurredAtUtc: DateTimeOffset.UtcNow,
        CorrelationId: correlationId,
        UserId: userId,
        SchemaVersion: 1,
        Payload: payload);

    var list = _events.GetOrAdd(sessionId, static _ => []);
    lock (list)
    {
      list.Add(envelope);
    }

    return Task.FromResult(envelope);
  }

  public async IAsyncEnumerable<SessionEventEnvelope> ReadAllAsync (
      string sessionId,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (!_events.TryGetValue(sessionId, out var list))
    {
      yield break;
    }

    List<SessionEventEnvelope> snapshot;
    lock (list)
    {
      snapshot = [.. list];
    }

    foreach (var item in snapshot)
    {
      cancellationToken.ThrowIfCancellationRequested();
      yield return item;
      await Task.CompletedTask;
    }
  }
}
