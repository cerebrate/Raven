using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class WorkspacePathsTests
{
  [Fact]
  public void ResolveScopedPath_Throws_WhenPathEscapesWorkspaceRoot ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var sut = new WorkspacePaths(workspaceRoot);

    try
    {
      Assert.Throws<InvalidOperationException> (() => sut.ResolveScopedPath (Path.Combine ("..", "outside.txt")));
    }
    finally
    {
      if (Directory.Exists (workspaceRoot))
      {
        Directory.Delete (workspaceRoot, recursive: true);
      }
    }
  }

  [Fact]
  public void EnsureWorkspaceStructure_CreatesExpectedDirectories ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var sut = new WorkspacePaths(workspaceRoot);

    try
    {
      sut.EnsureWorkspaceStructure ();

      Assert.True (Directory.Exists (workspaceRoot));
      Assert.True (Directory.Exists (Path.Combine (workspaceRoot, "sessions", "db")));
      Assert.True (Directory.Exists (Path.Combine (workspaceRoot, "sessions", "logs")));
      Assert.True (Directory.Exists (Path.Combine (workspaceRoot, "sessions", "snapshots")));
      Assert.True (Directory.Exists (Path.Combine (workspaceRoot, "config")));
      Assert.True (Directory.Exists (Path.Combine (workspaceRoot, "tmp")));

    }
    finally
    {
      if (Directory.Exists (workspaceRoot))
      {
        Directory.Delete (workspaceRoot, recursive: true);
      }
    }
  }

  [Fact]
  public void CheckIntegrity_ReturnsHealthyReport_WhenWorkspaceIsInitialized ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var sut = new WorkspacePaths(workspaceRoot);

    try
    {
      sut.EnsureWorkspaceStructure ();

      var report = sut.CheckIntegrity();

      Assert.True (report.IsHealthy);
      Assert.Empty (report.Issues);
    }
    finally
    {
      if (Directory.Exists (workspaceRoot))
      {
        Directory.Delete (workspaceRoot, recursive: true);
      }
    }
  }
}