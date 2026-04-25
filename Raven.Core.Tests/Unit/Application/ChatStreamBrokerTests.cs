using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Tests.Unit.Application.Fakes;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using ArkaneSystems.Raven.Core.Bus.Handlers;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Application;

public sealed class ChatStreamBrokerTests
{
  [Fact]
  public async Task StartResponseStreamAsync_ReturnsNull_WhenSessionDoesNotExist ()
  {
    var chat = new StubChatApplicationService { Session = null };
    var streamHub = new InMemoryResponseStreamEventHub();
    var eventLog = new InMemorySessionEventLog();
    var handler = new ResponseStreamEventForwardingHandler(streamHub, NullLogger<ResponseStreamEventForwardingHandler>.Instance);
    var messageBus = new ForwardingMessageBus(handler);
    var broker = new ChatStreamBroker(chat, eventLog, messageBus, streamHub, NullLogger<ChatStreamBroker>.Instance);

    var stream = await broker.StartResponseStreamAsync ("missing-session", "hello", cancellationToken: TestContext.Current.CancellationToken);

    Assert.Null (stream);
    Assert.Empty (messageBus.Published);
  }

  [Fact]
  public async Task StartResponseStreamAsync_PublishesOrderedStartedDeltaCompletedEvents ()
  {
    var chat = new StubChatApplicationService
    {
      Session = new SessionInfo("session-1", DateTimeOffset.UtcNow, null),
      StreamChunks = ["echo:hello", "line one\nline two"]
    };

    var streamHub = new InMemoryResponseStreamEventHub();
    var eventLog = new InMemorySessionEventLog();
    var handler = new ResponseStreamEventForwardingHandler(streamHub, NullLogger<ResponseStreamEventForwardingHandler>.Instance);
    var messageBus = new ForwardingMessageBus(handler);
    var broker = new ChatStreamBroker(chat, eventLog, messageBus, streamHub, NullLogger<ChatStreamBroker>.Instance);

    const string correlationId = "corr-123";
    var stream = await broker.StartResponseStreamAsync(
        "session-1",
        "hello",
        requestContext: new ChatRequestContext(correlationId, "user-1"),
        cancellationToken: TestContext.Current.CancellationToken);

    Assert.NotNull (stream);

    var readTask = ReadAllAsync(streamHub, stream.ResponseId, TimeSpan.FromSeconds(2));
    await stream.Completion;
    var events = await readTask;

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

    Assert.All(messageBus.Published, envelope => Assert.Equal(correlationId, envelope.Metadata.CorrelationId));
    Assert.Null(messageBus.Published[0].Metadata.CausationId);
    Assert.Equal(messageBus.Published[0].Metadata.MessageId, messageBus.Published[1].Metadata.CausationId);
    Assert.Equal(messageBus.Published[1].Metadata.MessageId, messageBus.Published[2].Metadata.CausationId);
    Assert.Equal(messageBus.Published[2].Metadata.MessageId, messageBus.Published[3].Metadata.CausationId);
  }

  [Fact]
  public async Task StartResponseStreamAsync_PublishesFailedEvent_WhenStreamingThrowsInvalidOperationException ()
  {
    var chat = new StubChatApplicationService
    {
      Session = new SessionInfo("session-1", DateTimeOffset.UtcNow, null),
      ThrowOnStream = true
    };

    var streamHub = new InMemoryResponseStreamEventHub();
    var eventLog = new InMemorySessionEventLog();
    var handler = new ResponseStreamEventForwardingHandler(streamHub, NullLogger<ResponseStreamEventForwardingHandler>.Instance);
    var messageBus = new ForwardingMessageBus(handler);
    var broker = new ChatStreamBroker(chat, eventLog, messageBus, streamHub, NullLogger<ChatStreamBroker>.Instance);

    var stream = await broker.StartResponseStreamAsync("session-1", "hello", cancellationToken: TestContext.Current.CancellationToken);

    Assert.NotNull (stream);

    var readTask = ReadAllAsync(streamHub, stream.ResponseId, TimeSpan.FromSeconds(2));
    await stream.Completion;
    var events = await readTask;

    Assert.Equal (2, events.Count);
    Assert.Equal (2, messageBus.Published.Count);
    _ = Assert.IsType<ResponseStarted> (events[0].Event);

    var failed = Assert.IsType<ResponseFailed>(events[1].Event);
    Assert.Contains ("conversation", failed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    Assert.Equal ("chat.response.failed.v1", events[1].Metadata.Type);
  }

  private static async Task<List<ResponseStreamEventEnvelope>> ReadAllAsync (
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
      IResponseStreamEventHub streamHub,
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
      string responseId,
      TimeSpan timeout)
  {
    var events = new List<ResponseStreamEventEnvelope>();
    using var cts = new CancellationTokenSource(timeout);

    await foreach (var envelope in streamHub.ReadAllAsync (responseId, cts.Token))
    {
      events.Add (envelope);
    }

    return events;
  }

  private sealed class StubChatApplicationService : IChatApplicationService
  {
    public SessionInfo? Session { get; set; }

    public List<string> StreamChunks { get; set; } = [];

    public bool ThrowOnStream { get; set; }

    public Task<string> CreateSessionAsync (CancellationToken cancellationToken = default) =>
        Task.FromResult ("unused");

    public Task<string?> SendMessageAsync (
        string sessionId,
        string content,
        ChatRequestContext? requestContext = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?> ("unused");

    public async Task<bool> StreamMessageAsync (
        string sessionId,
        string content,
        Func<string, CancellationToken, Task> onChunkAsync,
        ChatRequestContext? requestContext = null,
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

  private sealed class ForwardingMessageBus (IMessageHandler<ResponseStreamEventEnvelope> handler) : IMessageBus
  {
    public List<MessageEnvelope<ResponseStreamEventEnvelope>> Published { get; } = [];

    public async Task PublishAsync<TPayload> (MessageEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
        where TPayload : notnull
    {
      if (envelope.Payload is not ResponseStreamEventEnvelope payload)
      {
        throw new InvalidOperationException ($"Unexpected payload type: {typeof (TPayload).FullName}");
      }

      var typedEnvelope = new MessageEnvelope<ResponseStreamEventEnvelope>(envelope.Metadata, payload);
      this.Published.Add (typedEnvelope);
      await handler.HandleAsync (typedEnvelope, cancellationToken);
    }
  }
}
