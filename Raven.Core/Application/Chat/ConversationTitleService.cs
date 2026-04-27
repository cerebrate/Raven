using ArkaneSystems.Raven.Core.AgentRuntime;
using Microsoft.Extensions.Logging;

namespace ArkaneSystems.Raven.Core.Application.Chat;

// Generates session titles by making a one-off call to IAgentService with a
// purpose-specific system prompt.  The call runs outside the user's conversation
// thread so it never affects the user's context window.
//
// Title generation is best-effort: any non-cancellation exception is caught,
// logged at Warning, and null is returned so the caller can proceed without a title.
//
// Context sent to the model is capped at MaxContextChars per side so a very long
// first message or reply does not blow the token budget of the title call.
//
// Lifetime: Singleton (IAgentService is Singleton; ILogger is thread-safe).
public sealed class ConversationTitleService (
    IAgentService agentService,
    ILogger<ConversationTitleService> logger) : IConversationTitleService
{
  // The system prompt instructs the model to act only as a title generator.
  // Keeping it tightly scoped avoids the model being "helpful" in unexpected ways.
  private const string TitleSystemPrompt =
      """
      You generate concise session titles for an AI assistant conversation.
      When given a conversation exchange, reply with a short, descriptive title of at most six words.
      Respond with only the title — no quotes, no trailing punctuation, no explanation.
      """;

  // Caps per-side to avoid sending enormous first messages to the title model.
  private const int MaxContextChars = 500;

  public async Task<string?> GenerateTitleAsync (
      string userMessage,
      string agentReply,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (userMessage);
    ArgumentException.ThrowIfNullOrWhiteSpace (agentReply);

    var userContext  = Truncate (userMessage, MaxContextChars);
    var replyContext = Truncate (agentReply,  MaxContextChars);
    var userPrompt   = $"User: {userContext}\n\nAssistant: {replyContext}";

    try
    {
      var title = await agentService.CompleteAsync (TitleSystemPrompt, userPrompt, cancellationToken);
      return string.IsNullOrWhiteSpace (title) ? null : title;
    }
    catch (OperationCanceledException)
    {
      // Propagate cancellation so the caller can honour it.
      throw;
    }
    catch (Exception ex)
    {
      // Title generation is best-effort; log and return null so the session
      // list simply shows no title rather than failing the whole request.
      logger.LogWarning (
          ex,
          "Title generation failed; session will have no title until the next successful attempt.");
      return null;
    }
  }

  private static string Truncate (string text, int maxLength) =>
      text.Length <= maxLength ? text : text[..maxLength] + "…";
}
