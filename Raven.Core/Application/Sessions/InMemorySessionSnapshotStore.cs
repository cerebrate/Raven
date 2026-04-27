using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ArkaneSystems.Raven.Core.Application.Sessions;

// In-memory implementation of ISessionSnapshotStore for use in unit tests.
// Not registered in production (FileSessionSnapshotStore is used instead).
// All snapshots are lost when the process restarts.
public sealed class InMemorySessionSnapshotStore : ISessionSnapshotStore
{
  private readonly ConcurrentDictionary<string, SessionSnapshot> _snapshots =
      new (StringComparer.Ordinal);

  public Task SaveSnapshotAsync (SessionSnapshot snapshot, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull (snapshot);
    _snapshots[snapshot.SessionId] = snapshot;
    return Task.CompletedTask;
  }

  public Task<SessionSnapshot?> LoadSnapshotAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (sessionId);
    _snapshots.TryGetValue (sessionId, out var snapshot);
    return Task.FromResult<SessionSnapshot?> (snapshot);
  }

  public Task<bool> InvalidateSnapshotAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (sessionId);
    return Task.FromResult (_snapshots.TryRemove (sessionId, out _));
  }

  public async IAsyncEnumerable<SessionSnapshot> ListSnapshotsAsync (
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    foreach (var snapshot in _snapshots.Values)
    {
      cancellationToken.ThrowIfCancellationRequested ();
      yield return snapshot;
    }
  }
}
