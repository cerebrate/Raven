using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Application;

public sealed class ChatStreamBrokerTests
{
  [Fact]
  public async Task StreamResponseEventsAsync_ReturnsFalse_WhenSessionDoesNotExist ()
  {
    var chat = new StubChatApplicationService { Session = null };
    var messageBus = new RecordingMessageBus();
    var broker = new ChatStreamBroker(chat, messageBus, NullLogger<ChatStreamBroker>.Instance);
    var events = new List<ResponseStreamEventEnvelope>();

    var exists = await broker.StreamResponseEventsAsync ("missing-session", "hello", (evt, _) =>
        {
          events.Add(evt);
          return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

    Assert.False (exists);
    Assert.Empty (events);
    Assert.Empty (messageBus.Published);
  }

  [Fact]
  public async Task StreamResponseEventsAsync_EmitsOrderedStartedDeltaCompletedEvents ()
  {
    var chat = new StubChatApplicationService
    {
      Session = new SessionInfo("session-1", DateTimeOffset.UtcNow, null),
      StreamChunks = ["echo:hello", "line one\nline two"]
    };

    var messageBus = new RecordingMessageBus();
    var broker = new ChatStreamBroker(chat, messageBus, NullLogger<ChatStreamBroker>.Instance);
    var events = new List<ResponseStreamEventEnvelope>();

    var exists = await broker.StreamResponseEventsAsync ("session-1", "hello", (evt, _) =>
        {
          events.Add(evt);
          return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

    Assert.True (exists);
    Assert.Equal (4, events.Count);
    Assert.Equal (4, messageBus.Published.Count);
    _ = Assert.IsType<ResponseStarted> (events[0].Event);

    var firstDelta = Assert.IsType<ResponseDelta>(events[1].Event);
    Assert.Equal (1, firstDelta.Sequence);
    Assert.Equal ("echo:hello", firstDelta.Content);

    var secondDelta = Assert.IsType<ResponseDelta>(events[2].Event);
    Assert.Equal (2, secondDelta.Sequence);
    Assert.Equal ("line one\nline two", secondDelta.Content);

    var completed = Assert.IsType<ResponseCompleted>(events[3].Event);
    Assert.Equal ("echo:helloline one\nline two", completed.FinalContent);

    Assert.Equal ("chat.response.started.v1", events[0].Metadata.Type);
    Assert.Equal ("chat.response.delta.v1", events[1].Metadata.Type);
    Assert.Equal ("chat.response.delta.v1", events[2].Metadata.Type);
    Assert.Equal ("chat.response.completed.v1", events[3].Metadata.Type);

    Assert.All (messageBus.Published, published => Assert.IsType<ResponseStreamEventEnvelope> (published.Payload));
    Assert.Equal (events.Select (e => e.Metadata.MessageId), messageBus.Published.Select (p => p.Metadata.MessageId));
  }

  [Fact]
  public async Task StreamResponseEventsAsync_EmitsFailedEvent_WhenStreamingThrowsInvalidOperationException ()
  {
    var chat = new StubChatApplicationService
    {
      Session = new SessionInfo("session-1", DateTimeOffset.UtcNow, null),
      ThrowOnStream = true
    };

    var messageBus = new RecordingMessageBus();
    var broker = new ChatStreamBroker(chat, messageBus, NullLogger<ChatStreamBroker>.Instance);
    var events = new List<ResponseStreamEventEnvelope>();

    var exists = await broker.StreamResponseEventsAsync ("session-1", "hello", (evt, _) =>
        {
          events.Add(evt);
          return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

    Assert.True (exists);
    Assert.Equal (2, events.Count);
    Assert.Equal (2, messageBus.Published.Count);
    _ = Assert.IsType<ResponseStarted> (events[0].Event);

    var failed = Assert.IsType<ResponseFailed>(events[1].Event);
    Assert.Contains ("conversation", failed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Assert.Equal ("chat.response.failed.v1", events[1].Metadata.Type);
  }

  private sealed class StubChatApplicationService : IChatApplicationService
  {
    public SessionInfo? Session { get; set; }

    public List<string> StreamChunks { get; set; } = [];

    public bool ThrowOnStream { get; set; }

    public Task<string> CreateSessionAsync (CancellationToken cancellationToken = default) =>
        Task.FromResult ("unused");

    public Task<string?> SendMessageAsync (string sessionId, string content, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?> ("unused");

    public async Task<bool> StreamMessageAsync (
        string sessionId,
        string content,
        Func<string, CancellationToken, Task> onChunkAsync,
        CancellationToken cancellationToken = default)
    {
      if (this.Session is null)
      {
        return false;
      }

      if (this.ThrowOnStream)
      {
        throw new InvalidOperationException ("Backing conversation was not found.");
      }

      foreach (var chunk in this.StreamChunks)
      {
        await onChunkAsync (chunk, cancellationToken);
      }

      return true;
    }

    public Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult (this.Session);

    public Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult (true);
  }

  private sealed class RecordingMessageBus : IMessageBus
  {
    public List<MessageEnvelope<ResponseStreamEventEnvelope>> Published { get; } = [];

    public Task PublishAsync<TPayload> (MessageEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
        where TPayload : notnull
    {
      if (envelope.Payload is not ResponseStreamEventEnvelope payload)
      {
        throw new InvalidOperationException ($"Unexpected payload type: {typeof (TPayload).FullName}");
      }

      this.Published.Add (new MessageEnvelope<ResponseStreamEventEnvelope> (envelope.Metadata, payload));
      return Task.CompletedTask;
    }
  }
}
