using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using ArkaneSystems.Raven.Core.Infrastructure.Persistence;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class FileSessionEventLogTests
{
  [Fact]
  public async Task AppendAsync_AssignsMonotonicSequence_PerSession ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionEventLog(workspacePaths);

    try
    {
      const string sessionId = "session-seq";

      var first = await sut.AppendAsync(sessionId, "test.event.v1", new { Value = 1 }, cancellationToken: TestContext.Current.CancellationToken);
      var second = await sut.AppendAsync(sessionId, "test.event.v1", new { Value = 2 }, cancellationToken: TestContext.Current.CancellationToken);
      var third = await sut.AppendAsync(sessionId, "test.event.v1", new { Value = 3 }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(1, first.Sequence);
      Assert.Equal(2, second.Sequence);
      Assert.Equal(3, third.Sequence);

      var events = await sut.ReadAllAsync(sessionId, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
      Assert.Equal(3, events.Count);
      Assert.Equal([1L, 2L, 3L], events.Select(static e => e.Sequence).ToArray());
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
      {
        Directory.Delete(workspaceRoot, recursive: true);
      }
    }
  }

  [Fact]
  public async Task AppendAsync_AppendsWithoutMutatingExistingEvents ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure();
    var sut = new FileSessionEventLog(workspacePaths);

    try
    {
      const string sessionId = "session-growth";
      var logPath = Path.Combine(workspacePaths.ResolveScopedPath(Path.Combine("sessions", "logs")), $"{sessionId}.events.ndjson");

      _ = await sut.AppendAsync(sessionId, "test.event.v1", new { Message = "one" }, cancellationToken: TestContext.Current.CancellationToken);
      var firstLength = new FileInfo(logPath).Length;

      _ = await sut.AppendAsync(sessionId, "test.event.v1", new { Message = "two" }, cancellationToken: TestContext.Current.CancellationToken);
      var secondLength = new FileInfo(logPath).Length;

      Assert.True(secondLength > firstLength);

      var events = await sut.ReadAllAsync(sessionId, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
      Assert.Equal(2, events.Count);
      Assert.Equal("test.event.v1", events[0].EventType);
      Assert.Equal("test.event.v1", events[1].EventType);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
      {
        Directory.Delete(workspaceRoot, recursive: true);
      }
    }
  }
}
