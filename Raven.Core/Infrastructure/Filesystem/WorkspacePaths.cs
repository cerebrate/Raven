namespace ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

public sealed class WorkspacePaths(string workspaceRoot) : IWorkspacePaths
{
    private readonly string _workspaceRoot = Path.GetFullPath(workspaceRoot);

    public string GetWorkspaceRoot() => _workspaceRoot;

    public string GetSessionsPath() => Path.Combine(_workspaceRoot, "sessions");

    public string GetSessionDatabasePath() => Path.Combine(GetSessionsPath(), "db", "raven.db");

    public string GetConfigPath() => Path.Combine(_workspaceRoot, "config");

    public string ResolveScopedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(relativePath));
        }

        var combined = Path.Combine(_workspaceRoot, relativePath);
        var fullPath = Path.GetFullPath(combined);

        if (!IsSubPathOf(_workspaceRoot, fullPath))
        {
            throw new InvalidOperationException($"Path '{relativePath}' resolves outside workspace root '{_workspaceRoot}'.");
        }

        return fullPath;
    }

    public void EnsureWorkspaceStructure()
    {
        EnsureDirectory(_workspaceRoot);
        EnsureDirectory(GetSessionsPath());
        EnsureDirectory(Path.Combine(GetSessionsPath(), "db"));
        EnsureDirectory(Path.Combine(GetSessionsPath(), "logs"));
        EnsureDirectory(Path.Combine(GetSessionsPath(), "snapshots"));
        EnsureDirectory(Path.Combine(_workspaceRoot, "memory"));
        EnsureDirectory(Path.Combine(_workspaceRoot, "heartbeat"));
        EnsureDirectory(Path.Combine(_workspaceRoot, "artifacts"));
        EnsureDirectory(Path.Combine(_workspaceRoot, "audit"));
        EnsureDirectory(GetConfigPath());
        EnsureDirectory(Path.Combine(_workspaceRoot, "tmp"));
    }

    public void EnsureDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsSubPathOf(_workspaceRoot, fullPath))
        {
            throw new InvalidOperationException($"Directory '{path}' is outside workspace root '{_workspaceRoot}'.");
        }

        Directory.CreateDirectory(fullPath);
    }

    public WorkspaceIntegrityReport CheckIntegrity()
    {
        var issues = new List<string>();

        var expectedDirectories = new[]
        {
            _workspaceRoot,
            GetSessionsPath(),
            Path.Combine(GetSessionsPath(), "db"),
            Path.Combine(GetSessionsPath(), "logs"),
            Path.Combine(GetSessionsPath(), "snapshots"),
            Path.Combine(_workspaceRoot, "memory"),
            Path.Combine(_workspaceRoot, "heartbeat"),
            Path.Combine(_workspaceRoot, "artifacts"),
            Path.Combine(_workspaceRoot, "audit"),
            GetConfigPath(),
            Path.Combine(_workspaceRoot, "tmp")
        };

        foreach (var directory in expectedDirectories)
        {
            if (!Directory.Exists(directory))
            {
                issues.Add($"Missing directory: {directory}");
            }
        }

        var tmpPath = Path.Combine(_workspaceRoot, "tmp");
        var probePath = Path.Combine(tmpPath, $"integrity-{Guid.NewGuid():N}.probe");

        try
        {
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
        }
        catch (Exception ex)
        {
            issues.Add($"Workspace write probe failed under '{tmpPath}': {ex.Message}");
        }

        return new WorkspaceIntegrityReport(issues);
    }

    private static bool IsSubPathOf(string parentPath, string candidatePath)
    {
        var parentFullPath = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var candidateFullPath = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidateFullPath.StartsWith(parentFullPath, comparison);
    }
}
