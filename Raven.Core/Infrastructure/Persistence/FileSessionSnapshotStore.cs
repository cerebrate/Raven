#region header

// Raven.Core - FileSessionSnapshotStore.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-27

#endregion

#region using

using System.Runtime.CompilerServices;
using System.Text.Json;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

#endregion

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

// Filesystem-backed snapshot store.
// Each session snapshot is a single JSON file written atomically via
// AtomicFileWriter so a crash during a write never leaves a corrupt file.
//
// File location: {workspace}/sessions/snapshots/{sessionId}.snapshot.json
//
// A missing file means "no valid snapshot" — callers must fall back to
// event-log replay or create a fresh session.  Invalidation simply
// deletes the file; it is safe to call even if no snapshot exists.
public sealed class FileSessionSnapshotStore (IWorkspacePaths workspacePaths) : ISessionSnapshotStore
{
  private static readonly JsonSerializerOptions SerializerOptions =
      new (JsonSerializerDefaults.Web) { WriteIndented = false };

  // Atomically writes (or replaces) the snapshot for the given session.
  public async Task SaveSnapshotAsync (SessionSnapshot snapshot, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull (snapshot);
    cancellationToken.ThrowIfCancellationRequested ();

    string json = JsonSerializer.Serialize (snapshot, SerializerOptions);
    await AtomicFileWriter.WriteAllTextAsync (this.GetSnapshotFilePath (snapshot.SessionId), json, cancellationToken);
  }

  // Reads and deserializes the snapshot file; returns null if no file exists.
  public async Task<SessionSnapshot?> LoadSnapshotAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (sessionId);
    cancellationToken.ThrowIfCancellationRequested ();

    string filePath = this.GetSnapshotFilePath (sessionId);
    if (!File.Exists (filePath))
      return null;

    string json = await File.ReadAllTextAsync (filePath, cancellationToken);
    if (string.IsNullOrWhiteSpace (json))
      return null;

    return JsonSerializer.Deserialize<SessionSnapshot> (json, SerializerOptions);
  }

  // Deletes the snapshot file. Idempotent — returns false if no file was present.
  public Task<bool> InvalidateSnapshotAsync (string sessionId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (sessionId);
    cancellationToken.ThrowIfCancellationRequested ();

    string filePath = this.GetSnapshotFilePath (sessionId);
    if (!File.Exists (filePath))
      return Task.FromResult (false);

    File.Delete (filePath);
    return Task.FromResult (true);
  }

  // Enumerates all *.snapshot.json files in the snapshots directory and
  // yields the successfully deserialized snapshots in arbitrary order.
  // Files that fail to parse are silently skipped so a single corrupt
  // snapshot does not prevent the others from being listed.
  public async IAsyncEnumerable<SessionSnapshot> ListSnapshotsAsync (
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested ();

    string snapshotsDir = this.GetSnapshotsDirectory ();

    if (!Directory.Exists (snapshotsDir))
      yield break;

    foreach (string filePath in Directory.EnumerateFiles (snapshotsDir, "*.snapshot.json"))
    {
      cancellationToken.ThrowIfCancellationRequested ();

      string json;
      try
      {
        json = await File.ReadAllTextAsync (filePath, cancellationToken);
      }
      catch (IOException)
      {
        // Skip files that cannot be read (e.g. being written concurrently).
        continue;
      }

      SessionSnapshot? snapshot;
      try
      {
        snapshot = JsonSerializer.Deserialize<SessionSnapshot> (json, SerializerOptions);
      }
      catch (JsonException)
      {
        // Skip files that fail to deserialize (e.g. written by an older schema).
        continue;
      }

      if (snapshot is not null)
        yield return snapshot;
    }
  }

  private string GetSnapshotsDirectory () =>
      workspacePaths.ResolveScopedPath (Path.Combine ("sessions", "snapshots"));

  private string GetSnapshotFilePath (string sessionId) =>
      Path.Combine (this.GetSnapshotsDirectory (), $"{sessionId}.snapshot.json");
}
