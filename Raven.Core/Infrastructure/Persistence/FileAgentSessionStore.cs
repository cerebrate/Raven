using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

// Filesystem-backed agent session store.
// Each entry is a single JSON file written atomically via AtomicFileWriter so
// a crash during a write never leaves a corrupt file.
//
// File location: {workspace}/sessions/agent-sessions/{conversationId}.agent.json
//
// A missing file means "no valid session" — callers must fall back to
// creating a new conversation.  Deletion simply removes the file; it is
// safe to call even if no entry exists.
public sealed class FileAgentSessionStore (IWorkspacePaths workspacePaths) : IAgentSessionStore
{
  // Atomically writes (or replaces) the agent session state for the given
  // conversationId.
  public async Task SaveAsync (string conversationId, string serializedState, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (conversationId);
    ArgumentException.ThrowIfNullOrWhiteSpace (serializedState);
    cancellationToken.ThrowIfCancellationRequested ();

    await AtomicFileWriter.WriteAllTextAsync (this.GetFilePath (conversationId), serializedState, cancellationToken);
  }

  // Reads and returns the raw session JSON, or null if no file exists.
  public async Task<string?> LoadAsync (string conversationId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (conversationId);
    cancellationToken.ThrowIfCancellationRequested ();

    string filePath = this.GetFilePath (conversationId);
    if (!File.Exists (filePath))
      return null;

    var json = await File.ReadAllTextAsync (filePath, cancellationToken);
    return string.IsNullOrWhiteSpace (json) ? null : json;
  }

  // Deletes the entry file. Idempotent — returns false if no file was present.
  public Task<bool> DeleteAsync (string conversationId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (conversationId);
    cancellationToken.ThrowIfCancellationRequested ();

    string filePath = this.GetFilePath (conversationId);
    if (!File.Exists (filePath))
      return Task.FromResult (false);

    File.Delete (filePath);
    return Task.FromResult (true);
  }

  private string GetAgentSessionsDirectory () =>
      workspacePaths.ResolveScopedPath (Path.Combine ("sessions", "agent-sessions"));

  private string GetFilePath (string conversationId) =>
      Path.Combine (this.GetAgentSessionsDirectory (), $"{conversationId}.agent.json");
}
