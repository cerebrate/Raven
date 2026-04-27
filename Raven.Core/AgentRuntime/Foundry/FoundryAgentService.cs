using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ArkaneSystems.Raven.Core.AgentRuntime.Foundry;

// Foundry/Azure OpenAI implementation of IAgentService.
//
// Each call creates a minimal two-message chat completion (system + user) using the
// same model deployment as the main conversation service, but without any session
// state.  The ChatClient is created once and reused across calls — it is thread-safe
// and designed to be long-lived.
//
// Lifetime: Singleton.
public sealed class FoundryAgentService : IAgentService
{
  private readonly ChatClient _chatClient;

  public FoundryAgentService (IOptions<FoundryOptions> options)
  {
    var opts = options.Value;

    // Reuse the same credential strategy as FoundryAgentConversationService:
    // DefaultAzureCredential selects CLI login in development and managed
    // identity in production — no hard-coded secrets anywhere.
    this._chatClient = new AzureOpenAIClient (new Uri (opts.Endpoint), new DefaultAzureCredential ())
        .GetChatClient (opts.DeploymentName);
  }

  public async Task<string> CompleteAsync (
      string systemPrompt,
      string userMessage,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (systemPrompt);
    ArgumentException.ThrowIfNullOrWhiteSpace (userMessage);

    ChatMessage[] messages =
    [
      new SystemChatMessage (systemPrompt),
      new UserChatMessage (userMessage)
    ];

    var result = await this._chatClient.CompleteChatAsync (messages, cancellationToken: cancellationToken);
    return result.Value.Content[0].Text?.Trim () ?? string.Empty;
  }
}
