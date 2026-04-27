using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using ArkaneSystems.Raven.Core.Infrastructure.Persistence;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class FileSessionSnapshotStoreTests
{
  [Fact]
  public async Task SaveAndLoadSnapshot_RoundTrips_Correctly ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      var original = new SessionSnapshot(
          SessionId: "test-session-1",
          ConversationId: "conv-1",
          CreatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
          LastActivityAt: new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero),
          SnapshotAt: new DateTimeOffset(2026, 1, 2, 12, 30, 0, TimeSpan.Zero),
          EventLogSequence: 42);

      await sut.SaveSnapshotAsync(original, TestContext.Current.CancellationToken);

      var loaded = await sut.LoadSnapshotAsync("test-session-1", TestContext.Current.CancellationToken);

      Assert.NotNull(loaded);
      Assert.Equal(original.SessionId, loaded.SessionId);
      Assert.Equal(original.ConversationId, loaded.ConversationId);
      Assert.Equal(original.CreatedAt, loaded.CreatedAt);
      Assert.Equal(original.LastActivityAt, loaded.LastActivityAt);
      Assert.Equal(original.SnapshotAt, loaded.SnapshotAt);
      Assert.Equal(original.EventLogSequence, loaded.EventLogSequence);
      Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public async Task LoadSnapshotAsync_ReturnsNull_WhenNoSnapshotExists ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      var result = await sut.LoadSnapshotAsync("nonexistent-session", TestContext.Current.CancellationToken);
      Assert.Null(result);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public async Task SaveSnapshotAsync_OverwritesPreviousSnapshot ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      const string sessionId = "overwrite-session";

      var first = new SessionSnapshot(
          SessionId: sessionId,
          ConversationId: "conv-1",
          CreatedAt: DateTimeOffset.UtcNow,
          LastActivityAt: null,
          SnapshotAt: DateTimeOffset.UtcNow,
          EventLogSequence: 1);

      var second = new SessionSnapshot(
          SessionId: sessionId,
          ConversationId: "conv-1",
          CreatedAt: first.CreatedAt,
          LastActivityAt: DateTimeOffset.UtcNow,
          SnapshotAt: DateTimeOffset.UtcNow.AddMinutes(1),
          EventLogSequence: 5);

      await sut.SaveSnapshotAsync(first, TestContext.Current.CancellationToken);
      await sut.SaveSnapshotAsync(second, TestContext.Current.CancellationToken);

      var loaded = await sut.LoadSnapshotAsync(sessionId, TestContext.Current.CancellationToken);

      Assert.NotNull(loaded);
      Assert.Equal(5, loaded.EventLogSequence);
      Assert.NotNull(loaded.LastActivityAt);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public async Task InvalidateSnapshotAsync_RemovesSnapshot_AndReturnsFalse_WhenCalledAgain ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      var snapshot = new SessionSnapshot(
          SessionId: "to-invalidate",
          ConversationId: "conv-1",
          CreatedAt: DateTimeOffset.UtcNow,
          LastActivityAt: null,
          SnapshotAt: DateTimeOffset.UtcNow,
          EventLogSequence: 1);

      await sut.SaveSnapshotAsync(snapshot, TestContext.Current.CancellationToken);

      // First invalidation should succeed.
      var firstResult = await sut.InvalidateSnapshotAsync("to-invalidate", TestContext.Current.CancellationToken);
      Assert.True(firstResult);

      // Snapshot should be gone.
      var loaded = await sut.LoadSnapshotAsync("to-invalidate", TestContext.Current.CancellationToken);
      Assert.Null(loaded);

      // Second invalidation is idempotent — no error, returns false.
      var secondResult = await sut.InvalidateSnapshotAsync("to-invalidate", TestContext.Current.CancellationToken);
      Assert.False(secondResult);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public async Task ListSnapshotsAsync_ReturnsAllSavedSnapshots ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      var s1 = new SessionSnapshot("session-a", "conv-a", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, 1);
      var s2 = new SessionSnapshot("session-b", "conv-b", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, 1);
      var s3 = new SessionSnapshot("session-c", "conv-c", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, 1);

      await sut.SaveSnapshotAsync(s1, TestContext.Current.CancellationToken);
      await sut.SaveSnapshotAsync(s2, TestContext.Current.CancellationToken);
      await sut.SaveSnapshotAsync(s3, TestContext.Current.CancellationToken);

      var listed = await sut.ListSnapshotsAsync(TestContext.Current.CancellationToken)
                             .ToListAsync(TestContext.Current.CancellationToken);

      Assert.Equal(3, listed.Count);
      var ids = listed.Select(static s => s.SessionId).ToHashSet();
      Assert.Contains("session-a", ids);
      Assert.Contains("session-b", ids);
      Assert.Contains("session-c", ids);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public async Task ListSnapshotsAsync_ExcludesInvalidatedSnapshots ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      var s1 = new SessionSnapshot("keep-session", "conv-1", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, 1);
      var s2 = new SessionSnapshot("delete-session", "conv-2", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, 1);

      await sut.SaveSnapshotAsync(s1, TestContext.Current.CancellationToken);
      await sut.SaveSnapshotAsync(s2, TestContext.Current.CancellationToken);
      await sut.InvalidateSnapshotAsync("delete-session", TestContext.Current.CancellationToken);

      var listed = await sut.ListSnapshotsAsync(TestContext.Current.CancellationToken)
                             .ToListAsync(TestContext.Current.CancellationToken);

      Assert.Single(listed);
      Assert.Equal("keep-session", listed[0].SessionId);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }

  [Fact]
  public async Task ListSnapshotsAsync_ReturnsEmpty_WhenNoSnapshotsExist ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionSnapshotStore(workspacePaths);

    try
    {
      var listed = await sut.ListSnapshotsAsync(TestContext.Current.CancellationToken)
                             .ToListAsync(TestContext.Current.CancellationToken);

      Assert.Empty(listed);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
        Directory.Delete(workspaceRoot, recursive: true);
    }
  }
}
