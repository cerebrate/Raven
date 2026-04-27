namespace ArkaneSystems.Raven.Core.AgentRuntime;

// Persists serialized AIAgent session state (chat history) keyed by the
// internal conversationId.  This is the mechanism that lets the server resume
// an existing conversation after a process restart without needing the
// Assistants API or any server-side storage.
//
// Each entry is the raw JSON produced by AIAgent.SerializeSessionAsync.
// The format is opaque — callers must treat it as an unstructured blob and
// feed it back to AIAgent.DeserializeSessionAsync without modification.
//
// File location (FileAgentSessionStore):
//   {workspace}/sessions/agent-sessions/{conversationId}.agent.json
//
// Orphaned files (from deleted sessions) accumulate harmlessly — they cannot
// be reached via any live session mapping and will be swept by a future
// workspace maintenance task.
public interface IAgentSessionStore
{
  // Persist the serialized session JSON for the given conversationId.
  // Creates or atomically replaces the existing entry.
  Task SaveAsync (string conversationId, string serializedState, CancellationToken cancellationToken = default);

  // Return the previously saved session JSON, or null if no entry exists.
  Task<string?> LoadAsync (string conversationId, CancellationToken cancellationToken = default);

  // Remove the entry for the given conversationId.
  // Idempotent — returns false if no entry was found.
  Task<bool> DeleteAsync (string conversationId, CancellationToken cancellationToken = default);
}
