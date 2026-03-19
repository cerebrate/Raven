namespace ArkaneSystems.Raven.Core.AgentRuntime;

// Thrown when a previously issued conversationId no longer maps to an active
// agent session in the runtime (for example, after process restart).
public sealed class ConversationNotFoundException (string conversationId)
    : InvalidOperationException($"Conversation '{conversationId}' not found.")
{
  public string ConversationId { get; } = conversationId;
}
