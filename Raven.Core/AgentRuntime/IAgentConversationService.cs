namespace ArkaneSystems.Raven.Core.AgentRuntime;

// Abstracts all interaction with the underlying AI agent infrastructure.
// Nothing outside of the AgentRuntime folder should reference Foundry or
// Azure OpenAI SDK types directly — those are hidden behind this interface.
// This makes it straightforward to swap the backing provider in the future.
public interface IAgentConversationService
{
    // Creates a new conversation with the agent and returns an opaque
    // conversationId that must be passed to every subsequent call.
    Task<string> CreateConversationAsync();

    // Sends a user message and waits for the complete agent reply.
    // Use this when the full response is needed before rendering (e.g. tests).
    Task<string> SendMessageAsync(string conversationId, string content);

    // Sends a user message and streams the agent reply back as a sequence of
    // text chunks. Each chunk is a small fragment of the final response.
    // Use this for the console client's real-time display.
    IAsyncEnumerable<string> StreamMessageAsync(string conversationId, string content, CancellationToken cancellationToken = default);
}
