#region header

// Raven.Core.Tests - ChatStreamBrokerTests.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-27 12:05 PM

#endregion

#region using

using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using ArkaneSystems.Raven.Core.Bus.Handlers;
using ArkaneSystems.Raven.Core.Tests.Unit.Application.Fakes;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace ArkaneSystems.Raven.Core.Tests.Unit.Application;

public sealed class ChatStreamBrokerTests
{
  #region Nested type: ForwardingMessageBus

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

      MessageEnvelope<ResponseStreamEventEnvelope> typedEnvelope =
        new MessageEnvelope<ResponseStreamEventEnvelope> (Metadata: envelope.Metadata, Payload: payload);
      this.Published.Add (typedEnvelope);
      await handler.HandleAsync (message: typedEnvelope, cancellationToken: cancellationToken);
    }
  }

  #endregion

  #region Nested type: StubChatApplicationService

  private sealed class StubChatApplicationService : IChatApplicationService
  {
    public SessionInfo? Session { get; set; }

    public List<string> StreamChunks { get; set; } = [];

    public bool ThrowOnStream { get; set; }

    public Task<string> CreateSessionAsync (CancellationToken cancellationToken = default) => Task.FromResult ("unused");

    public Task<string?> SendMessageAsync (string              sessionId,
                                           string              content,
                                           ChatRequestContext? requestContext    = null,
                                           CancellationToken   cancellationToken = default)
      => Task.FromResult<string?> ("unused");

    public async Task<bool> StreamMessageAsync (string                                sessionId,
                                                string                                content,
                                                Func<string, CancellationToken, Task> onChunkAsync,
                                                ChatRequestContext?                   requestContext    = null,
                                                CancellationToken                     cancellationToken = default)
    {
      if (this.Session is null)
      {
        return false;
      }

      if (this.ThrowOnStream)
      {
        throw new InvalidOperationException ("Backing conversation was not found.");
      }

      foreach (string chunk in this.StreamChunks)
      {
        await onChunkAsync (arg1: chunk, arg2: cancellationToken);
      }

      return true;
    }

    public Task<SessionInfo?> GetSessionAsync (string sessionId, CancellationToken cancellationToken = default)
      => Task.FromResult (this.Session);

    public Task<bool> DeleteSessionAsync (string sessionId, CancellationToken cancellationToken = default)
      => Task.FromResult (true);

    public Task<IReadOnlyList<SessionSnapshot>> ListSessionsAsync (CancellationToken cancellationToken = default)
      => Task.FromResult<IReadOnlyList<SessionSnapshot>> ([]);
  }

  #endregion

  [Fact]
  public async Task StartResponseStreamAsync_ReturnsNull_WhenSessionDoesNotExist ()
  {
    StubChatApplicationService     chat      = new StubChatApplicationService { Session = null };
    InMemoryResponseStreamEventHub streamHub = new InMemoryResponseStreamEventHub ();
    InMemorySessionEventLog        eventLog  = new InMemorySessionEventLog ();
    ResponseStreamEventForwardingHandler handler =
      new ResponseStreamEventForwardingHandler (streamHub: streamHub,
                                                logger: NullLogger<ResponseStreamEventForwardingHandler>.Instance);
    ForwardingMessageBus messageBus = new ForwardingMessageBus (handler);
    ChatStreamBroker broker = new ChatStreamBroker (chat: chat,
                                                    sessionEventLog: eventLog,
                                                    messageBus: messageBus,
                                                    streamHub: streamHub,
                                                    logger: NullLogger<ChatStreamBroker>.Instance);

    ChatStreamStartResult? stream = await broker.StartResponseStreamAsync (sessionId: "missing-session",
                                                                           content: "hello",
                                                                           cancellationToken: TestContext.Current
                                                                                                         .CancellationToken);

    Assert.Null (stream);
    Assert.Empty (messageBus.Published);
  }

  [Fact]
  public async Task StartResponseStreamAsync_PublishesOrderedStartedDeltaCompletedEvents ()
  {
    StubChatApplicationService chat = new StubChatApplicationService
                                      {
                                        Session = new SessionInfo (SessionId: "session-1",
                                                                   CreatedAt: DateTimeOffset.UtcNow,
                                                                   LastActivityAt: null),
                                        StreamChunks = ["echo:hello", "line one\nline two"]
                                      };

    InMemoryResponseStreamEventHub streamHub = new InMemoryResponseStreamEventHub ();
    InMemorySessionEventLog        eventLog  = new InMemorySessionEventLog ();
    ResponseStreamEventForwardingHandler handler =
      new ResponseStreamEventForwardingHandler (streamHub: streamHub,
                                                logger: NullLogger<ResponseStreamEventForwardingHandler>.Instance);
    ForwardingMessageBus messageBus = new ForwardingMessageBus (handler);
    ChatStreamBroker broker = new ChatStreamBroker (chat: chat,
                                                    sessionEventLog: eventLog,
                                                    messageBus: messageBus,
                                                    streamHub: streamHub,
                                                    logger: NullLogger<ChatStreamBroker>.Instance);

    const string correlationId = "corr-123";
    ChatStreamStartResult? stream = await broker.StartResponseStreamAsync (sessionId: "session-1",
                                                                           content: "hello",
                                                                           requestContext:
                                                                           new ChatRequestContext (CorrelationId: correlationId,
                                                                                                   UserId: "user-1"),
                                                                           cancellationToken: TestContext.Current
                                                                                                         .CancellationToken);

    Assert.NotNull (stream);

    Task<List<ResponseStreamEventEnvelope>> readTask =
      ChatStreamBrokerTests.ReadAllAsync (streamHub: streamHub, responseId: stream.ResponseId, timeout: TimeSpan.FromSeconds (2));
    await stream.Completion;
    List<ResponseStreamEventEnvelope> events = await readTask;

    Assert.Equal (expected: 4, actual: events.Count);
    Assert.Equal (expected: 4, actual: messageBus.Published.Count);

    _ = Assert.IsType<ResponseStarted> (events[0].Event);

    ResponseDelta firstDelta = Assert.IsType<ResponseDelta> (events[1].Event);
    Assert.Equal (expected: 1,            actual: firstDelta.Sequence);
    Assert.Equal (expected: "echo:hello", actual: firstDelta.Content);

    ResponseDelta secondDelta = Assert.IsType<ResponseDelta> (events[2].Event);
    Assert.Equal (expected: 2,                    actual: secondDelta.Sequence);
    Assert.Equal (expected: "line one\nline two", actual: secondDelta.Content);

    ResponseCompleted completed = Assert.IsType<ResponseCompleted> (events[3].Event);
    Assert.Equal (expected: "echo:helloline one\nline two", actual: completed.FinalContent);

    Assert.Equal (expected: "chat.response.started.v1",   actual: events[0].Metadata.Type);
    Assert.Equal (expected: "chat.response.delta.v1",     actual: events[1].Metadata.Type);
    Assert.Equal (expected: "chat.response.delta.v1",     actual: events[2].Metadata.Type);
    Assert.Equal (expected: "chat.response.completed.v1", actual: events[3].Metadata.Type);

    Assert.All (collection: messageBus.Published,
                action: envelope => Assert.Equal (expected: correlationId, actual: envelope.Metadata.CorrelationId));
    Assert.Null (messageBus.Published[0].Metadata.CausationId);
    Assert.Equal (expected: messageBus.Published[0].Metadata.MessageId, actual: messageBus.Published[1].Metadata.CausationId);
    Assert.Equal (expected: messageBus.Published[1].Metadata.MessageId, actual: messageBus.Published[2].Metadata.CausationId);
    Assert.Equal (expected: messageBus.Published[2].Metadata.MessageId, actual: messageBus.Published[3].Metadata.CausationId);
  }

  [Fact]
  public async Task StartResponseStreamAsync_PublishesFailedEvent_WhenStreamingThrowsInvalidOperationException ()
  {
    StubChatApplicationService chat = new StubChatApplicationService
                                      {
                                        Session = new SessionInfo (SessionId: "session-1",
                                                                   CreatedAt: DateTimeOffset.UtcNow,
                                                                   LastActivityAt: null),
                                        ThrowOnStream = true
                                      };

    InMemoryResponseStreamEventHub streamHub = new InMemoryResponseStreamEventHub ();
    InMemorySessionEventLog        eventLog  = new InMemorySessionEventLog ();
    ResponseStreamEventForwardingHandler handler =
      new ResponseStreamEventForwardingHandler (streamHub: streamHub,
                                                logger: NullLogger<ResponseStreamEventForwardingHandler>.Instance);
    ForwardingMessageBus messageBus = new ForwardingMessageBus (handler);
    ChatStreamBroker broker = new ChatStreamBroker (chat: chat,
                                                    sessionEventLog: eventLog,
                                                    messageBus: messageBus,
                                                    streamHub: streamHub,
                                                    logger: NullLogger<ChatStreamBroker>.Instance);

    ChatStreamStartResult? stream =
      await broker.StartResponseStreamAsync (sessionId: "session-1",
                                             content: "hello",
                                             cancellationToken: TestContext.Current.CancellationToken);

    Assert.NotNull (stream);

    Task<List<ResponseStreamEventEnvelope>> readTask =
      ChatStreamBrokerTests.ReadAllAsync (streamHub: streamHub, responseId: stream.ResponseId, timeout: TimeSpan.FromSeconds (2));
    await stream.Completion;
    List<ResponseStreamEventEnvelope> events = await readTask;

    Assert.Equal (expected: 2, actual: events.Count);
    Assert.Equal (expected: 2, actual: messageBus.Published.Count);
    _ = Assert.IsType<ResponseStarted> (events[0].Event);

    ResponseFailed failed = Assert.IsType<ResponseFailed> (events[1].Event);
    Assert.Contains (expectedSubstring: "conversation",
                     actualString: failed.ErrorMessage,
                     comparisonType: StringComparison.OrdinalIgnoreCase);
    Assert.Equal (expected: "chat.response.failed.v1", actual: events[1].Metadata.Type);
  }

  private static async Task<List<ResponseStreamEventEnvelope>> ReadAllAsync (
    #pragma warning disable CA1859 // Use concrete types when possible for improved performance
    IResponseStreamEventHub streamHub,
    #pragma warning restore CA1859 // Use concrete types when possible for improved performance
    string   responseId,
    TimeSpan timeout)
  {
    List<ResponseStreamEventEnvelope> events = new List<ResponseStreamEventEnvelope> ();
    using CancellationTokenSource     cts    = new CancellationTokenSource (timeout);

    await foreach (ResponseStreamEventEnvelope envelope in streamHub.ReadAllAsync (responseId: responseId,
                                                                                   cancellationToken: cts.Token))
    {
      events.Add (envelope);
    }

    return events;
  }
}