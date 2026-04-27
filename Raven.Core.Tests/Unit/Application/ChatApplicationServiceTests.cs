using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Tests.Unit.Application.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Application;

public sealed class ChatApplicationServiceTests
{
  [Fact]
  public async Task SendMessageAsync_ThrowsSessionStaleException_AndDeletesSession_WhenConversationIsMissing ()
  {
    var conversations = new StubAgentConversationService();
    var sessions = new InMemorySessionStore();
    var eventLog = new InMemorySessionEventLog();
    var snapshotStore = new InMemorySessionSnapshotStore();
    var sut = new ChatApplicationService(conversations, sessions, eventLog, snapshotStore, NullLogger<ChatApplicationService>.Instance);

    const string missingConversationId = "missing-conversation";
    var sessionId = await sessions.CreateSessionAsync(missingConversationId);

    var error = await Assert.ThrowsAsync<SessionStaleException>(
        () => sut.SendMessageAsync(
            sessionId,
            "hello",
            cancellationToken: TestContext.Current.CancellationToken));

    Assert.Equal (sessionId, error.SessionId);
    Assert.False (await sessions.SessionExistsAsync (sessionId));
  }

  [Fact]
  public async Task StreamMessageAsync_ThrowsSessionStaleException_AndDeletesSession_WhenConversationIsMissing ()
  {
    var conversations = new StubAgentConversationService();
    var sessions = new InMemorySessionStore();
    var eventLog = new InMemorySessionEventLog();
    var snapshotStore = new InMemorySessionSnapshotStore();
    var sut = new ChatApplicationService(conversations, sessions, eventLog, snapshotStore, NullLogger<ChatApplicationService>.Instance);

    const string missingConversationId = "missing-conversation";
    var sessionId = await sessions.CreateSessionAsync(missingConversationId);

    var error = await Assert.ThrowsAsync<SessionStaleException>(() => sut.StreamMessageAsync(
        sessionId,
        "hello",
        static (_, _) => Task.CompletedTask,
        cancellationToken: TestContext.Current.CancellationToken));

    Assert.Equal (sessionId, error.SessionId);
    Assert.False (await sessions.SessionExistsAsync (sessionId));
  }

  private sealed class StubAgentConversationService : IAgentConversationService
  {
    private readonly ConcurrentDictionary<string, bool> _conversations = new();

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
      yield return !this._conversations.ContainsKey (conversationId) ? throw new ConversationNotFoundException (conversationId) : $"echo:{content}";
      await Task.CompletedTask;
    }
  }
}

public sealed class DeriveTitleTests
{
  [Fact]
  public void DeriveTitle_ReturnsFirstLine_WhenShortSingleLine ()
  {
    var title = ChatApplicationService.DeriveTitle ("What is the capital of France?");
    Assert.Equal ("What is the capital of France?", title);
  }

  [Fact]
  public void DeriveTitle_TruncatesAtWordBoundary_WhenInputExceeds60Chars ()
  {
    var longMessage = "This is a very long message that definitely exceeds sixty characters when measured carefully";
    var title = ChatApplicationService.DeriveTitle (longMessage);

    Assert.True (title.Length <= 63, $"Title too long: '{title}' ({title.Length} chars)"); // 60 + "…"
    Assert.EndsWith ("…", title);
  }

  [Fact]
  public void DeriveTitle_UsesFirstNonBlankLine_WhenMultilineInput ()
  {
    var multiLine = "\n\nHello, Raven!\nThis is the rest of the message.";
    var title = ChatApplicationService.DeriveTitle (multiLine);
    Assert.Equal ("Hello, Raven!", title);
  }

  [Fact]
  public void DeriveTitle_ReturnsDefaultTitle_WhenInputIsEmpty ()
  {
    Assert.Equal ("New conversation", ChatApplicationService.DeriveTitle (""));
    Assert.Equal ("New conversation", ChatApplicationService.DeriveTitle ("   "));
    Assert.Equal ("New conversation", ChatApplicationService.DeriveTitle ("\n\n"));
  }

  [Theory]
  [InlineData ("Hello")]
  [InlineData ("What is AI?")]
  public void DeriveTitle_NeverReturnsEmptyString (string input)
  {
    var title = ChatApplicationService.DeriveTitle (input);
    Assert.False (string.IsNullOrWhiteSpace (title));
  }
}
