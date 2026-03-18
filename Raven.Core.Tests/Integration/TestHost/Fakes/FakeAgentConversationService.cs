using System.Collections.Concurrent;
using ArkaneSystems.Raven.Core.AgentRuntime;

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;

public sealed class FakeAgentConversationService : IAgentConversationService
{
    private readonly ConcurrentDictionary<string, bool> _conversations = new();

    public Task<string> CreateConversationAsync()
    {
        var conversationId = Guid.NewGuid().ToString();
        _conversations[conversationId] = true;
        return Task.FromResult(conversationId);
    }

    public Task<string> SendMessageAsync(string conversationId, string content)
    {
        if (!_conversations.ContainsKey(conversationId))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found.");
        }

        return Task.FromResult($"echo:{content}");
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string conversationId,
        string content,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_conversations.ContainsKey(conversationId))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found.");
        }

        yield return $"echo:{content}";
        await Task.Delay(1, cancellationToken);
        yield return "line one\nline two";
    }
}
