namespace ArkaneSystems.Raven.Core.AgentRuntime;

// Provides a single, stateless completion call to the backing AI model
// without creating or reusing a persistent conversation session.
//
// Use this for one-off server-internal tasks (title generation, classification,
// formatting, etc.) that must not pollute any user conversation thread.
//
// Lifetime: Singleton — implementations hold a shared HTTP client / credential.
public interface IAgentService
{
  // Sends a single completion request with a given system prompt and user message.
  // Returns the model's plain-text reply, trimmed of surrounding whitespace.
  // Throws on unrecoverable transport or model errors; callers should handle
  // OperationCanceledException and log/swallow other exceptions for best-effort tasks.
  Task<string> CompleteAsync (string systemPrompt, string userMessage, CancellationToken cancellationToken = default);
}
