namespace ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

public interface IWorkspacePaths
{
    string GetWorkspaceRoot();

    string GetSessionsPath();

    string GetSessionDatabasePath();

    string GetConfigPath();

    string ResolveScopedPath(string relativePath);

    void EnsureWorkspaceStructure();

    void EnsureDirectory(string path);

    WorkspaceIntegrityReport CheckIntegrity();
}

public sealed record WorkspaceIntegrityReport(IReadOnlyList<string> Issues)
{
    public bool IsHealthy => Issues.Count == 0;
}
