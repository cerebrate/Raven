#region header

// Raven.Core - IWorkspacePaths.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-25 1:38 PM

#endregion

#region using

using JetBrains.Annotations;

#endregion

namespace ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

public interface IWorkspacePaths
{
  string GetWorkspaceRoot ();

  string GetSessionsPath ();

  string GetSessionDatabasePath ();

  string GetConfigPath ();

  string ResolveScopedPath (string relativePath);

  WorkspaceInitializationReport EnsureWorkspaceStructure ();

  void EnsureDirectory (string path);

  WorkspaceIntegrityReport CheckIntegrity ();
}

public sealed record WorkspaceInitializationReport (
  IReadOnlyList<string> CreatedDirectories,
  IReadOnlyList<string> ExistingDirectories)
{
  public int TotalDirectories => this.CreatedDirectories.Count + this.ExistingDirectories.Count;
}

public sealed record WorkspaceIntegrityReport (
  IReadOnlyList<string> MissingDirectories,
  bool                  WriteProbeSucceeded,
  string?               WriteProbeError)
{
  public bool IsHealthy => (this.MissingDirectories.Count == 0) && this.WriteProbeSucceeded;
}