using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ArkaneSystems.Raven.Core.AgentRuntime.Foundry;

public class FoundryAgentConversationService : IAgentConversationService
{
    private readonly AIAgent _agent;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    public FoundryAgentConversationService(IOptions<FoundryOptions> options)
    {
        var opts = options.Value;

        _agent = new AzureOpenAIClient(new Uri(opts.Endpoint), new DefaultAzureCredential())
            .GetChatClient(opts.DeploymentName)
            .AsAIAgent(
                instructions: opts.SystemPrompt,
                name: opts.AgentName);
    }

    public async Task<string> CreateConversationAsync()
    {
        var session = await _agent.CreateSessionAsync();
        var conversationId = Guid.NewGuid().ToString();
        _sessions[conversationId] = session;
        return conversationId;
    }

    public async Task<string> SendMessageAsync(string conversationId, string content)
    {
        if (!_sessions.TryGetValue(conversationId, out var session))
            throw new InvalidOperationException($"Conversation '{conversationId}' not found.");

        return (await _agent.RunAsync(content, session)).Text;
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string conversationId,
        string content,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(conversationId, out var session))
            throw new InvalidOperationException($"Conversation '{conversationId}' not found.");

        await foreach (var update in _agent.RunStreamingAsync(content, session).WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
}
