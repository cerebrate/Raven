namespace ArkaneSystems.Raven.Core.AgentRuntime;

public interface IAgentConversationService
{
    Task<string> CreateConversationAsync();
    Task<string> SendMessageAsync(string conversationId, string content);
    IAsyncEnumerable<string> StreamMessageAsync(string conversationId, string content, CancellationToken cancellationToken = default);
}
