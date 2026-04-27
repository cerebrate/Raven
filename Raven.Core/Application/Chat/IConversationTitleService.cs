namespace ArkaneSystems.Raven.Core.Application.Chat;

// Generates a concise, human-readable title for a session from the first
// user message and the agent's reply.
//
// Title generation is intentionally best-effort: implementations must catch
// and log non-cancellation failures rather than propagating them, and return
// null to signal that no title could be generated.
public interface IConversationTitleService
{
  // Generate a session title from the first message exchange.
  // Returns null if title generation fails or produces an empty result.
  Task<string?> GenerateTitleAsync (
      string userMessage,
      string agentReply,
      CancellationToken cancellationToken = default);
}
