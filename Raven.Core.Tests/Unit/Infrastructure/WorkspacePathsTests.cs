#region header

// Raven.Core.Tests - WorkspacePathsTests.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-25 1:38 PM

#endregion

#region using

using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using JetBrains.Annotations;

#endregion

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class WorkspacePathsTests
{
  [Fact]
  public void ResolveScopedPath_Throws_WhenPathEscapesWorkspaceRoot ()
  {
    string workspaceRoot =
      Path.Combine (path1: Path.GetTempPath (), path2: "Raven.Core.Tests", path3: Guid.NewGuid ().ToString ("N"));
    WorkspacePaths sut = new WorkspacePaths (workspaceRoot);

    try
    {
      _ = Assert.Throws<InvalidOperationException> (() => sut.ResolveScopedPath (Path.Combine (path1: "..", path2: "outside.txt")));
    }
    finally
    {
      if (Directory.Exists (workspaceRoot))
      {
        Directory.Delete (path: workspaceRoot, recursive: true);
      }
    }
  }

  [Fact]
  public void EnsureWorkspaceStructure_CreatesExpectedDirectories ()
  {
    string workspaceRoot =
      Path.Combine (path1: Path.GetTempPath (), path2: "Raven.Core.Tests", path3: Guid.NewGuid ().ToString ("N"));
    WorkspacePaths sut = new WorkspacePaths (workspaceRoot);

    try
    {
      WorkspaceInitializationReport report = sut.EnsureWorkspaceStructure ();

      Assert.True (Directory.Exists (workspaceRoot));
      Assert.True (Directory.Exists (Path.Combine (path1: workspaceRoot, path2: "sessions", path3: "db")));
      Assert.True (Directory.Exists (Path.Combine (path1: workspaceRoot, path2: "sessions", path3: "logs")));
      Assert.True (Directory.Exists (Path.Combine (path1: workspaceRoot, path2: "sessions", path3: "snapshots")));
      Assert.True (Directory.Exists (Path.Combine (path1: workspaceRoot, path2: "sessions", path3: "agent-sessions")));
      Assert.True (Directory.Exists (Path.Combine (path1: workspaceRoot, path2: "config")));
      Assert.True (Directory.Exists (Path.Combine (path1: workspaceRoot, path2: "tmp")));
      Assert.Equal (expected: 12, actual: report.TotalDirectories);
      Assert.Equal (expected: 12, actual: report.CreatedDirectories.Count);
      Assert.Empty (report.ExistingDirectories);
    }
    finally
    {
      if (Directory.Exists (workspaceRoot))
      {
        Directory.Delete (path: workspaceRoot, recursive: true);
      }
    }
  }

  [Fact]
  public void CheckIntegrity_ReturnsHealthyReport_WhenWorkspaceIsInitialized ()
  {
    string workspaceRoot =
      Path.Combine (path1: Path.GetTempPath (), path2: "Raven.Core.Tests", path3: Guid.NewGuid ().ToString ("N"));
    WorkspacePaths sut = new WorkspacePaths (workspaceRoot);

    try
    {
      _ = sut.EnsureWorkspaceStructure ();

      WorkspaceIntegrityReport report = sut.CheckIntegrity ();

      Assert.True (report.IsHealthy);
      Assert.Empty (report.MissingDirectories);
      Assert.True (report.WriteProbeSucceeded);
      Assert.Null (report.WriteProbeError);
    }
    finally
    {
      if (Directory.Exists (workspaceRoot))
      {
        Directory.Delete (path: workspaceRoot, recursive: true);
      }
    }
  }
}