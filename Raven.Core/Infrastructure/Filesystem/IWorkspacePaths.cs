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
  public int TotalDirectories => CreatedDirectories.Count + ExistingDirectories.Count;
}

public sealed record WorkspaceIntegrityReport (
    IReadOnlyList<string> MissingDirectories,
    bool WriteProbeSucceeded,
    string? WriteProbeError)
{
  public bool IsHealthy => MissingDirectories.Count == 0 && WriteProbeSucceeded;
}
