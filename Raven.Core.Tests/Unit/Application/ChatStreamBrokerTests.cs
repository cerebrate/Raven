using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Application;

public sealed class ChatStreamBrokerTests
{
  [Fact]
  public async Task StreamResponseEventsAsync_ReturnsFalse_WhenSessionDoesNotExist ()
  {
    var chat = new StubChatApplicationService { Session = null };
    var broker = new ChatStreamBroker(chat, NullLogger<ChatStreamBroker>.Instance);
    var events = new List<ResponseStreamEventEnvelope>();

    var exists = await broker.StreamResponseEventsAsync(
        "missing-session",
        "hello",
        (evt, _) =>
        {
          events.Add(evt);
          return Task.CompletedTask;
        });

    Assert.False(exists);
    Assert.Empty(events);
  }

  [Fact]
  public async Task StreamResponseEventsAsync_EmitsOrderedStartedDeltaCompletedEvents ()
  {
    var chat = new StubChatApplicationService
    {
      Session = new SessionInfo("session-1", DateTimeOffset.UtcNow, null),
      StreamChunks = ["echo:hello", "line one\nline two"]
    };

    var broker = new ChatStreamBroker(chat, NullLogger<ChatStreamBroker>.Instance);
    var events = new List<ResponseStreamEventEnvelope>();

    var exists = await broker.StreamResponseEventsAsync(
        "session-1",
        "hello",
        (evt, _) =>
        {
          events.Add(evt);
          return Task.CompletedTask;
        });

    Assert.True(exists);
    Assert.Equal(4, events.Count);
    _ = Assert.IsType<ResponseStarted>(events[0].Event);

    var firstDelta = Assert.IsType<ResponseDelta>(events[1].Event);
    Assert.Equal(1, firstDelta.Sequence);
    Assert.Equal("echo:hello", firstDelta.Content);

    var secondDelta = Assert.IsType<ResponseDelta>(events[2].Event);
    Assert.Equal(2, secondDelta.Sequence);
    Assert.Equal("line one\nline two", secondDelta.Content);

    var completed = Assert.IsType<ResponseCompleted>(events[3].Event);
    Assert.Equal("echo:helloline one\nline two", completed.FinalContent);

    Assert.Equal("chat.response.started.v1", events[0].Metadata.Type);
    Assert.Equal("chat.response.delta.v1", events[1].Metadata.Type);
    Assert.Equal("chat.response.delta.v1", events[2].Metadata.Type);
    Assert.Equal("chat.response.completed.v1", events[3].Metadata.Type);
  }

  [Fact]
  public async Task StreamResponseEventsAsync_EmitsFailedEvent_WhenStreamingThrowsInvalidOperationException ()
  {
    var chat = new StubChatApplicationService
    {
      Session = new SessionInfo("session-1", DateTimeOffset.UtcNow, null),
      ThrowOnStream = true
    };

    var broker = new ChatStreamBroker(chat, NullLogger<ChatStreamBroker>.Instance);
    var events = new List<ResponseStreamEventEnvelope>();

    var exists = await broker.StreamResponseEventsAsync(
        "session-1",
        "hello",
        (evt, _) =>
        {
          events.Add(evt);
          return Task.CompletedTask;
        });

    Assert.True(exists);
    Assert.Equal(2, events.Count);
    _ = Assert.IsType<ResponseStarted>(events[0].Event);

    var failed = Assert.IsType<ResponseFailed>(events[1].Event);
    Assert.Contains("conversation", failed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Assert.Equal("chat.response.failed.v1", events[1].Metadata.Type);
  }

  private sealed class StubChatApplicationService : IChatApplicationService
  {
    public SessionInfo? Session { get; set; }

    public List<string> StreamChunks { get; set; } = [];

    public bool ThrowOnStream { get; set; }

    public Task<string> CreateSessionAsync (CancellationToken cancellationToken = default) =>
        Task.FromResult("unused");

    public Task<string?> SendMessageAsync (string sessionId, string content, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("unused");

    public async Task<bool> StreamMessageAsync (
        string sessionId,
        string content,
        Func<string, CancellationToken, Task> onChunkAsync,
        CancellationToken cancellationToken = default)
    {
      if (Session is null)
      {
        return false;
      }

      if (ThrowOnStream)
      {
        throw new InvalidOperationException("Backing conversation was not found.");
      }

      foreach (var chunk in StreamChunks)
      {
        await onChunkAsync(chunk, cancellationToken);
      }

      return true;
    }

    public Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Session);

    public Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
  }
}
