using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ArkaneSystems.Raven.Core.AgentRuntime.Foundry;

// Concrete implementation of IAgentConversationService that talks to a model
// deployed in Microsoft Foundry via the Azure OpenAI SDK + Microsoft.Agents.AI.
//
// Lifetime: Singleton. The AIAgent and the session dictionary are long-lived
// objects that should be shared for the lifetime of the process.
public class FoundryAgentConversationService : IAgentConversationService
{
  // The AIAgent wraps the Azure OpenAI chat client and holds the configured
  // system prompt and agent name. It is stateless with respect to individual
  // conversations — session state lives in AgentSession objects below.
  private readonly AIAgent _agent;

  // Maps our internal conversationId (a Guid string we generate) to the
  // Foundry AgentSession object, which holds the conversation thread state.
  // ConcurrentDictionary is used because requests can arrive concurrently.
  private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

  public FoundryAgentConversationService (IOptions<FoundryOptions> options)
  {
    var opts = options.Value;

    // Build the agent from the configured Azure OpenAI endpoint using
    // DefaultAzureCredential, which will use the logged-in Azure CLI
    // account in development and managed identity in production.
    this._agent = new AzureOpenAIClient (new Uri (opts.Endpoint), new DefaultAzureCredential ())
        .GetChatClient (opts.DeploymentName)
        .AsAIAgent (
            instructions: opts.SystemPrompt,
            name: opts.AgentName);
  }

  public async Task<string> CreateConversationAsync ()
  {
    // Ask the agent to create a new conversation thread (AgentSession).
    // We then generate our own conversationId to use as the key so we
    // are not coupled to whatever internal ID Foundry uses.
    var session = await this._agent.CreateSessionAsync();
    var conversationId = Guid.NewGuid().ToString();
    this._sessions[conversationId] = session;
    return conversationId;
  }

  public async Task<string> SendMessageAsync (string conversationId, string content)
  {
    if (!this._sessions.TryGetValue (conversationId, out var session))
      throw new ConversationNotFoundException (conversationId);

    // RunAsync sends the message to Foundry and waits for the full response.
    // .Text extracts the plain-text content from the AgentResponse.
    return (await this._agent.RunAsync (content, session)).Text;
  }

  public async IAsyncEnumerable<string> StreamMessageAsync (
      string conversationId,
      string content,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    if (!this._sessions.TryGetValue (conversationId, out var session))
      throw new ConversationNotFoundException (conversationId);

    // RunStreamingAsync returns an IAsyncEnumerable of incremental update objects.
    // We yield only updates that carry text — some updates are metadata/control frames
    // with an empty Text property, which we skip to avoid writing blank SSE lines.
    // [EnumeratorCancellation] ensures the CancellationToken is wired through
    // correctly when the caller cancels iteration.
    await foreach (var update in this._agent.RunStreamingAsync (content, session, cancellationToken: cancellationToken).WithCancellation (cancellationToken))
    {
      if (!string.IsNullOrEmpty (update.Text))
        yield return update.Text;
    }
  }
}