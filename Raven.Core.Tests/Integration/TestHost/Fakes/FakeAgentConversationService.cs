using ArkaneSystems.Raven.Core.AgentRuntime;
using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;

public sealed class FakeAgentConversationService : IAgentConversationService
{
  private readonly ConcurrentDictionary<string, bool> _conversations = new();
  private int _streamChunkDelayMilliseconds = 1;

  public void ClearConversations () => this._conversations.Clear();

  public void SetStreamChunkDelay (TimeSpan delay)
  {
    if (delay < TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative.");
    }

    this._streamChunkDelayMilliseconds = (int)delay.TotalMilliseconds;
  }

  public void ResetStreamChunkDelay () => this._streamChunkDelayMilliseconds = 1;

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

    var delayMs = this._streamChunkDelayMilliseconds;
    if (delayMs > 0)
    {
      await Task.Delay (delayMs, cancellationToken);
    }

    yield return "line one\nline two";
  }
}