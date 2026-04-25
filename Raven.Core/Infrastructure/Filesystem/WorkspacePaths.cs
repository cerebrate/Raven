#region header

// Raven.Core - WorkspacePaths.cs
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

public sealed class WorkspacePaths (string workspaceRoot) : IWorkspacePaths
{
  private readonly string _workspaceRoot = Path.GetFullPath (workspaceRoot);

  public string GetWorkspaceRoot () => this._workspaceRoot;

  public string GetSessionsPath () => Path.Combine (path1: this._workspaceRoot, path2: "sessions");

  public string GetSessionDatabasePath () => Path.Combine (path1: this.GetSessionsPath (), path2: "db", path3: "raven.db");

  public string GetConfigPath () => Path.Combine (path1: this._workspaceRoot, path2: "config");

  public string ResolveScopedPath (string relativePath)
  {
    if (string.IsNullOrWhiteSpace (relativePath))
    {
      throw new ArgumentException (message: "Path cannot be empty.", paramName: nameof (relativePath));
    }

    string combined = Path.Combine (path1: this._workspaceRoot, path2: relativePath);
    string fullPath = Path.GetFullPath (combined);

    if (!IsSubPathOf (parentPath: this._workspaceRoot, candidatePath: fullPath))
    {
      throw new InvalidOperationException ($"Path '{relativePath}' resolves outside workspace root '{this._workspaceRoot}'.");
    }

    return fullPath;
  }

  public WorkspaceInitializationReport EnsureWorkspaceStructure ()
  {
    List<string> createdDirectories  = new List<string> ();
    List<string> existingDirectories = new List<string> ();

    string[] expectedDirectories = new[]
                                   {
                                     this._workspaceRoot,
                                     this.GetSessionsPath (),
                                     Path.Combine (path1: this.GetSessionsPath (), path2: "db"),
                                     Path.Combine (path1: this.GetSessionsPath (), path2: "logs"),
                                     Path.Combine (path1: this.GetSessionsPath (), path2: "snapshots"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "memory"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "heartbeat"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "artifacts"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "audit"),
                                     this.GetConfigPath (),
                                     Path.Combine (path1: this._workspaceRoot, path2: "tmp")
                                   };

    foreach (string directory in expectedDirectories)
    {
      this.EnsureDirectory (path: directory, createdDirectories: createdDirectories, existingDirectories: existingDirectories);
    }

    return new WorkspaceInitializationReport (CreatedDirectories: createdDirectories, ExistingDirectories: existingDirectories);
  }

  public void EnsureDirectory (string path)
    => this.EnsureDirectory (path: path, createdDirectories: null, existingDirectories: null);

  public WorkspaceIntegrityReport CheckIntegrity ()
  {
    List<string> missingDirectories = new List<string> ();

    string[] expectedDirectories = new[]
                                   {
                                     this._workspaceRoot,
                                     this.GetSessionsPath (),
                                     Path.Combine (path1: this.GetSessionsPath (), path2: "db"),
                                     Path.Combine (path1: this.GetSessionsPath (), path2: "logs"),
                                     Path.Combine (path1: this.GetSessionsPath (), path2: "snapshots"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "memory"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "heartbeat"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "artifacts"),
                                     Path.Combine (path1: this._workspaceRoot,     path2: "audit"),
                                     this.GetConfigPath (),
                                     Path.Combine (path1: this._workspaceRoot, path2: "tmp")
                                   };

    foreach (string directory in expectedDirectories)
    {
      if (!Directory.Exists (directory))
      {
        missingDirectories.Add (directory);
      }
    }

    string tmpPath   = Path.Combine (path1: this._workspaceRoot, path2: "tmp");
    string probePath = Path.Combine (path1: tmpPath,             path2: $"integrity-{Guid.NewGuid ():N}.probe");

    bool    writeProbeSucceeded = true;
    string? writeProbeError     = null;

    try
    {
      File.WriteAllText (path: probePath, contents: "ok");
      File.Delete (probePath);
    }
    catch (Exception ex)
    {
      writeProbeSucceeded = false;
      writeProbeError     = ex.Message;
    }

    return new WorkspaceIntegrityReport (MissingDirectories: missingDirectories,
                                         WriteProbeSucceeded: writeProbeSucceeded,
                                         WriteProbeError: writeProbeError);
  }

  private void EnsureDirectory (string path, List<string>? createdDirectories, List<string>? existingDirectories)
  {
    string fullPath = Path.GetFullPath (path);

    if (!IsSubPathOf (parentPath: this._workspaceRoot, candidatePath: fullPath))
    {
      throw new InvalidOperationException ($"Directory '{path}' is outside workspace root '{this._workspaceRoot}'.");
    }

    bool existed = Directory.Exists (fullPath);
    _ = Directory.CreateDirectory (fullPath);

    if (existed)
    {
      existingDirectories?.Add (fullPath);
    }
    else
    {
      createdDirectories?.Add (fullPath);
    }
  }

  private static bool IsSubPathOf (string parentPath, string candidatePath)
  {
    string parentFullPath = Path.GetFullPath (parentPath)
                                .TrimEnd (Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;

    string candidateFullPath = Path.GetFullPath (candidatePath)
                                   .TrimEnd (Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                               Path.DirectorySeparatorChar;

    StringComparison comparison = OperatingSystem.IsWindows ()
                                    ? StringComparison.OrdinalIgnoreCase
                                    : StringComparison.Ordinal;

    return candidateFullPath.StartsWith (value: parentFullPath, comparisonType: comparison);
  }
}