using ArkaneSystems.Raven.Core.AgentRuntime;
using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;

public sealed class FakeAgentConversationService : IAgentConversationService
{
  private readonly ConcurrentDictionary<string, bool> _conversations = new();

  public void ClearConversations () => this._conversations.Clear ();

  public Task<string> CreateConversationAsync ()
  {
    var conversationId = Guid.NewGuid().ToString();
    this._conversations[conversationId] = true;
    return Task.FromResult (conversationId);
  }

  public Task<string> SendMessageAsync (string conversationId, string content) =>
    !this._conversations.ContainsKey (conversationId)
      ? throw new ConversationNotFoundException (conversationId)
      : Task.FromResult ($"echo:{content}");

  public async IAsyncEnumerable<string> StreamMessageAsync (
      string conversationId,
      string content,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    yield return !this._conversations.ContainsKey (conversationId)
      ? throw new ConversationNotFoundException (conversationId)
      : $"echo:{content}";
    await Task.Delay (1, cancellationToken);
    yield return "line one\nline two";
  }
}