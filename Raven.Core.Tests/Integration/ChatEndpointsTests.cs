#region header

// Raven.Core.Tests - ChatEndpointsTests.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-26 9:45 AM

#endregion

#region using

using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

#endregion

namespace ArkaneSystems.Raven.Core.Tests.Integration;

[Collection (IntegrationTestCollection.Name)]
public sealed class ChatEndpointsTests (RavenCoreWebAppFactory factory)
{
  #region Nested type: StreamExecutionOutcome

  private enum StreamExecutionOutcome
  {
    Completed,
    Canceled
  }

  #endregion

  private readonly HttpClient             _client  = factory.CreateClient ();
  private readonly RavenCoreWebAppFactory _factory = factory;

  [Fact]
  public async Task CreateSession_ReturnsSessionId ()
  {
    HttpResponseMessage response = await this._client.PostAsJsonAsync (requestUri: "/api/chat/sessions",
                                                                       value: new { },
                                                                       cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    CreateSessionResponse? payload =
      await response.Content.ReadFromJsonAsync<CreateSessionResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (payload);
    Assert.False (string.IsNullOrWhiteSpace (payload.SessionId));
  }

  [Fact]
  public async Task SendMessage_ReturnsReply_ForExistingSession ()
  {
    string sessionId = await this.CreateSessionAsync ();

    HttpResponseMessage response = await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages",
                                                                       value: new SendMessageRequest ("hello"),
                                                                       cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    SendMessageResponse? payload =
      await response.Content.ReadFromJsonAsync<SendMessageResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (payload);
    Assert.Equal (expected: sessionId,    actual: payload.SessionId);
    Assert.Equal (expected: "echo:hello", actual: payload.Content);
  }

  [Fact]
  public async Task SendMessage_ReturnsNotFound_ForMissingSession ()
  {
    HttpResponseMessage response = await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{Guid.NewGuid ()}/messages",
                                                                       value: new SendMessageRequest ("hello"),
                                                                       cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: response.StatusCode);
  }

  [Fact]
  public async Task StreamMessage_ReturnsSsePayload_ForExistingSession ()
  {
    string sessionId = await this.CreateSessionAsync ();

    HttpResponseMessage response =
      await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages/stream",
                                          value: new SendMessageRequest ("hello"),
                                          cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.StartsWith (expectedStartString: "text/event-stream",
                       actualString: response.Content.Headers.ContentType?.MediaType,
                       comparisonType: StringComparison.Ordinal);

    string streamPayload = await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);
    Assert.Contains (expectedSubstring: "data: echo:hello", actualString: streamPayload, comparisonType: StringComparison.Ordinal);
    Assert.Contains (expectedSubstring: "data: line one",   actualString: streamPayload, comparisonType: StringComparison.Ordinal);
    Assert.Contains (expectedSubstring: "data: line two",   actualString: streamPayload, comparisonType: StringComparison.Ordinal);
  }

  [Fact]
  public async Task StreamMessage_ReturnsNotFound_ForMissingSession ()
  {
    HttpResponseMessage response =
      await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{Guid.NewGuid ()}/messages/stream",
                                          value: new SendMessageRequest ("hello"),
                                          cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: response.StatusCode);
  }

  [Fact]
  public async Task GetSession_ReturnsSessionInfo_ForExistingSession ()
  {
    string sessionId = await this.CreateSessionAsync ();

    HttpResponseMessage response = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    SessionInfoResponse? payload =
      await response.Content.ReadFromJsonAsync<SessionInfoResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (payload);
    Assert.Equal (expected: sessionId, actual: payload.SessionId);
    Assert.NotEqual (expected: default, actual: payload.CreatedAt);
  }

  [Fact]
  public async Task DeleteSession_ReturnsNoContent_ForExistingSession ()
  {
    string sessionId = await this.CreateSessionAsync ();

    HttpResponseMessage response = await this._client.DeleteAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                                                   cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NoContent, actual: response.StatusCode);
  }

  [Fact]
  public async Task DeleteSession_ReturnsNotFound_ForMissingSession ()
  {
    HttpResponseMessage response = await this._client.DeleteAsync (requestUri: $"/api/chat/sessions/{Guid.NewGuid ()}",
                                                                   cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: response.StatusCode);
  }

  [Fact]
  public async Task SessionLifecycle_CompletesAcrossEndpoints ()
  {
    string sessionId = await this.CreateSessionAsync ();

    HttpResponseMessage sendResponse = await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages",
                                                                           value: new SendMessageRequest ("workflow"),
                                                                           cancellationToken: TestContext.Current
                                                                                                         .CancellationToken);
    Assert.Equal (expected: HttpStatusCode.OK, actual: sendResponse.StatusCode);

    HttpResponseMessage getResponse = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                                                   cancellationToken: TestContext.Current.CancellationToken);
    Assert.Equal (expected: HttpStatusCode.OK, actual: getResponse.StatusCode);

    HttpResponseMessage deleteResponse = await this._client.DeleteAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                                                         cancellationToken: TestContext.Current.CancellationToken);
    Assert.Equal (expected: HttpStatusCode.NoContent, actual: deleteResponse.StatusCode);

    HttpResponseMessage getDeletedResponse = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                                                          cancellationToken: TestContext.Current.CancellationToken);
    Assert.Equal (expected: HttpStatusCode.NotFound, actual: getDeletedResponse.StatusCode);
  }

  [Fact]
  public async Task SendMessage_ReturnsConflict_AndInvalidatesSession_ForStaleSession ()
  {
    string sessionId = await this.CreateSessionAsync ();
    this.ClearAgentConversations ();

    HttpResponseMessage sendResponse = await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages",
                                                                           value: new SendMessageRequest ("hello"),
                                                                           cancellationToken: TestContext.Current
                                                                                                         .CancellationToken);

    Assert.Equal (expected: HttpStatusCode.Conflict, actual: sendResponse.StatusCode);

    ChatErrorResponse? error =
      await sendResponse.Content.ReadFromJsonAsync<ChatErrorResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (error);
    Assert.Equal (expected: "session_stale", actual: error.Code);

    HttpResponseMessage getResponse = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                                                   cancellationToken: TestContext.Current.CancellationToken);
    Assert.Equal (expected: HttpStatusCode.NotFound, actual: getResponse.StatusCode);
  }

  [Fact]
  public async Task StreamMessage_ReturnsFailedEventPayload_WithSessionStaleCode_ForStaleSession ()
  {
    string sessionId = await this.CreateSessionAsync ();
    this.ClearAgentConversations ();

    HttpResponseMessage response =
      await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages/stream",
                                          value: new SendMessageRequest ("hello"),
                                          cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    string streamPayload = await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);
    Assert.Contains (expectedSubstring: "event: failed", actualString: streamPayload, comparisonType: StringComparison.Ordinal);
    Assert.Contains (expectedSubstring: "\"Code\":\"session_stale\"",
                     actualString: streamPayload,
                     comparisonType: StringComparison.Ordinal);
    Assert.Contains (expectedSubstring: "\"Message\":", actualString: streamPayload, comparisonType: StringComparison.Ordinal);
    Assert.Contains (expectedSubstring: "\"IsRetryable\":false",
                     actualString: streamPayload,
                     comparisonType: StringComparison.Ordinal);
  }

  [Fact]
  public async Task CreateSession_EchoesProvidedCorrelationId_Header ()
  {
    const string correlationId = "test-correlation-id";

    using HttpRequestMessage request = new HttpRequestMessage (method: HttpMethod.Post, requestUri: "/api/chat/sessions")
                                       {
                                         Content = JsonContent.Create (new { })
                                       };

    _ = request.Headers.TryAddWithoutValidation (name: "X-Correlation-Id", value: correlationId);

    HttpResponseMessage response =
      await this._client.SendAsync (request: request, cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.True (response.Headers.TryGetValues (name: "X-Correlation-Id", values: out IEnumerable<string>? values));
    Assert.Equal (expected: correlationId, actual: Assert.Single (values));
  }

  [Fact]
  public async Task StreamMessage_ConcurrentRequestsRemainSessionIsolated ()
  {
    string[]     prompts  = Enumerable.Range (start: 1, count: 6).Select (i => $"concurrent-{i}").ToArray ();
    List<string> sessions = new List<string> ();

    foreach (string _ in prompts)
    {
      sessions.Add (await this.CreateSessionAsync ());
    }

    Task<(string prompt, string payload)>[] tasks = sessions
                                                   .Zip (second: prompts,
                                                         resultSelector: static (sessionId, prompt) => (sessionId, prompt))
                                                   .Select (async x =>
                                                            {
                                                              string payload =
                                                                await this.StreamSessionMessageAsync (sessionId: x.sessionId,
                                                                                                      prompt: x.prompt,
                                                                                                      cancellationToken:
                                                                                                      CancellationToken.None);

                                                              return (x.prompt, payload);
                                                            })
                                                   .ToArray ();

    (string prompt, string payload)[] results = await Task.WhenAll (tasks);

    foreach ((string prompt, string payload) result in results)
    {
      Assert.Contains (expectedSubstring: $"data: echo:{result.prompt}",
                       actualString: result.payload,
                       comparisonType: StringComparison.Ordinal);

      foreach (string otherPrompt in prompts.Where (p => !string.Equals (a: p,
                                                                         b: result.prompt,
                                                                         comparisonType: StringComparison.Ordinal)))
      {
        Assert.DoesNotContain (expectedSubstring: $"data: echo:{otherPrompt}",
                               actualString: result.payload,
                               comparisonType: StringComparison.Ordinal);
      }
    }
  }

  [Fact]
  public async Task StreamMessage_MixedCancellationAndCompletion_BehavesAsExpected ()
  {
    this.ConfigureFakeStreamChunkDelay (TimeSpan.FromMilliseconds (250));

    try
    {
      string[]     prompts  = Enumerable.Range (start: 1, count: 6).Select (i => $"cancel-mix-{i}").ToArray ();
      List<string> sessions = new List<string> ();

      foreach (string _ in prompts)
      {
        sessions.Add (await this.CreateSessionAsync ());
      }

      Task<StreamExecutionOutcome>[] tasks = sessions
                                            .Zip (second: prompts,
                                                  resultSelector: static (sessionId, prompt) => (sessionId, prompt))
                                            .Select ((x, index)
                                                       => this.RunStreamWithOptionalCancellationAsync (sessionId: x.sessionId,
                                                                                                       prompt: x.prompt,
                                                                                                       cancelAfter: index % 2 == 0
                                                                                                                      ? TimeSpan
                                                                                                                       .FromMilliseconds (80)
                                                                                                                      : null))
                                            .ToArray ();

      StreamExecutionOutcome[] outcomes = await Task.WhenAll (tasks);

      Assert.Contains (collection: outcomes, filter: static o => o == StreamExecutionOutcome.Canceled);
      Assert.Contains (collection: outcomes, filter: static o => o == StreamExecutionOutcome.Completed);
    }
    finally
    {
      this.ResetFakeStreamChunkDelay ();
    }
  }

  [Fact]
  public async Task StreamMessage_SseEventOrder_IsStartedThenDeltaThenCompleted ()
  {
    string sessionId = await this.CreateSessionAsync ();

    string payload = await this.StreamSessionMessageAsync (sessionId: sessionId,
                                                           prompt: "ordering-check",
                                                           cancellationToken: TestContext.Current.CancellationToken);

    int startedIndex   = payload.IndexOf (value: "event: started",   comparisonType: StringComparison.Ordinal);
    int deltaIndex     = payload.IndexOf (value: "event: delta",     comparisonType: StringComparison.Ordinal);
    int completedIndex = payload.IndexOf (value: "event: completed", comparisonType: StringComparison.Ordinal);

    Assert.True (condition: startedIndex   >= 0,           userMessage: "Expected started event in stream payload.");
    Assert.True (condition: deltaIndex     > startedIndex, userMessage: "Expected delta event to appear after started event.");
    Assert.True (condition: completedIndex > deltaIndex,   userMessage: "Expected completed event to appear after delta event.");
  }

  [Fact]
  public async Task SessionLifecycle_WritesAppendOnlyEventLogEntries ()
  {
    string sessionId = await this.CreateSessionAsync ();

    HttpResponseMessage sendResponse = await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages",
                                                                           value: new SendMessageRequest ("event-log"),
                                                                           cancellationToken: TestContext.Current
                                                                                                         .CancellationToken);
    Assert.Equal (expected: HttpStatusCode.OK, actual: sendResponse.StatusCode);

    HttpResponseMessage deleteResponse =
      await this._client.DeleteAsync (requestUri: $"/api/chat/sessions/{sessionId}",
                                      cancellationToken: TestContext.Current.CancellationToken);
    Assert.Equal (expected: HttpStatusCode.NoContent, actual: deleteResponse.StatusCode);

    string logPath = Path.Combine (path1: this._factory.WorkspaceRoot,
                                   path2: "sessions",
                                   path3: "logs",
                                   path4: $"{sessionId}.events.ndjson");
    Assert.True (File.Exists (logPath));

    string[] lines = await File.ReadAllLinesAsync (path: logPath, cancellationToken: TestContext.Current.CancellationToken);
    Assert.True (lines.Length >= 3);
    Assert.Contains (collection: lines,
                     filter: static line => line.Contains (value: "\"eventType\":\"session.created.v1\"",
                                                           comparisonType: StringComparison.Ordinal));
    Assert.Contains (collection: lines,
                     filter: static line => line.Contains (value: "\"eventType\":\"chat.message.sent.v1\"",
                                                           comparisonType: StringComparison.Ordinal));
    Assert.Contains (collection: lines,
                     filter: static line => line.Contains (value: "\"eventType\":\"session.deleted.v1\"",
                                                           comparisonType: StringComparison.Ordinal));
  }

  private void ClearAgentConversations ()
  {
    FakeAgentConversationService? fake =
      this._factory.Services.GetRequiredService<IAgentConversationService> () as FakeAgentConversationService;
    Assert.NotNull (fake);
    fake.ClearConversations ();
  }

  private void ConfigureFakeStreamChunkDelay (TimeSpan delay)
  {
    FakeAgentConversationService? fake =
      this._factory.Services.GetRequiredService<IAgentConversationService> () as FakeAgentConversationService;
    Assert.NotNull (fake);
    fake.SetStreamChunkDelay (delay);
  }

  private void ResetFakeStreamChunkDelay ()
  {
    FakeAgentConversationService? fake =
      this._factory.Services.GetRequiredService<IAgentConversationService> () as FakeAgentConversationService;
    Assert.NotNull (fake);
    fake.ResetStreamChunkDelay ();
  }

  private async Task<string> StreamSessionMessageAsync (string sessionId, string prompt, CancellationToken cancellationToken)
  {
    HttpResponseMessage response =
      await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionId}/messages/stream",
                                          value: new SendMessageRequest (prompt),
                                          cancellationToken: cancellationToken);

    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);

    return await response.Content.ReadAsStringAsync (cancellationToken);
  }

  private async Task<StreamExecutionOutcome> RunStreamWithOptionalCancellationAsync (string    sessionId,
                                                                                     string    prompt,
                                                                                     TimeSpan? cancelAfter)
  {
    using CancellationTokenSource cts = cancelAfter.HasValue
                                          ? new CancellationTokenSource (cancelAfter.Value)
                                          : new CancellationTokenSource (TimeSpan.FromSeconds (5));

    try
    {
      _ = await this.StreamSessionMessageAsync (sessionId: sessionId, prompt: prompt, cancellationToken: cts.Token);

      return StreamExecutionOutcome.Completed;
    }
    catch (OperationCanceledException)
    {
      return StreamExecutionOutcome.Canceled;
    }
  }

  private async Task<string> CreateSessionAsync ()
  {
    HttpResponseMessage response = await this._client.PostAsJsonAsync (requestUri: "/api/chat/sessions", value: new { });
    _ = response.EnsureSuccessStatusCode ();

    CreateSessionResponse? payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse> ();
    Assert.NotNull (payload);

    return payload.SessionId;
  }
}

// Isolated tests for the session notification SSE endpoint.
[Collection (IntegrationTestCollection.Name)]
public sealed class NotificationEndpointTests (RavenCoreWebAppFactory factory)
{
  private readonly HttpClient             _client  = factory.CreateClient ();
  private readonly RavenCoreWebAppFactory _factory = factory;

  [Fact]
  public async Task Notifications_ReturnsNotFound_ForUnknownSession ()
  {
    HttpResponseMessage response = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{Guid.NewGuid ()}/notifications",
                                                                cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.NotFound, actual: response.StatusCode);
  }

  [Fact]
  public async Task Notifications_ReturnsConflict_WhenAlreadySubscribed ()
  {
    ISessionNotificationHub hub       = this._factory.Services.GetRequiredService<ISessionNotificationHub> ();
    string                  sessionId = await this.CreateSessionAsync ();

    // Hold the subscription slot directly via the hub — equivalent to having
    // a live HTTP connection open for this session. The hub is a singleton so
    // the same instance is used by both the test and the server endpoint.
    Assert.True (hub.TrySubscribe (sessionId));

    try
    {
      // The HTTP endpoint should find the slot is already taken and return 409.
      HttpResponseMessage response = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}/notifications",
                                                                  cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal (expected: HttpStatusCode.Conflict, actual: response.StatusCode);
    }
    finally
    {
      hub.Complete (sessionId);
    }
  }

  [Fact]
  public async Task Notifications_DeliversServerShutdownEvent_ToSubscribedSession ()
  {
    FakeShutdownCoordinator fakeShutdown = this._factory.Services.GetRequiredService<FakeShutdownCoordinator> ();
    fakeShutdown.Reset ();

    string                  sessionId = await this.CreateSessionAsync ();
    ISessionNotificationHub hub       = this._factory.Services.GetRequiredService<ISessionNotificationHub> ();

    // Start the GET request WITHOUT awaiting — this sends the HTTP request and
    // lets the server endpoint run concurrently.
    using CancellationTokenSource subCts = new CancellationTokenSource (TimeSpan.FromSeconds (10));
    Task<HttpResponseMessage> getTask = this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}/notifications",
                                                               cancellationToken: subCts.Token);

    // Poll the hub until the session appears as a subscriber. This is
    // deterministic: the endpoint calls TrySubscribe synchronously in its
    // request body, so once the session ID appears in the hub we know the
    // server is inside the await foreach and ready to receive notifications.
    DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds (5);

    while (!hub.GetSubscribedSessionIds ().Contains (sessionId))
    {
      if (DateTimeOffset.UtcNow > deadline)
      {
        throw new TimeoutException ("Session never appeared in notification hub within 5 seconds.");
      }

      await Task.Delay (delay: TimeSpan.FromMilliseconds (10), cancellationToken: TestContext.Current.CancellationToken);
    }

    // Push a shutdown notification and immediately complete the channel so the
    // endpoint's await foreach exits and the HTTP response is finalised.
    ServerNotificationEnvelope envelope = new ServerNotificationEnvelope (Metadata: MessageMetadata.Create ("server.shutdown.v1"),
                                                                          Notification: new
                                                                            ServerShutdownNotification (IsRestart: false));

    await hub.BroadcastAsync (envelope: envelope, cancellationToken: TestContext.Current.CancellationToken);
    hub.Complete (sessionId);

    // Now await the HTTP response (which should be available immediately).
    using HttpResponseMessage response = await getTask;
    Assert.Equal (expected: HttpStatusCode.OK, actual: response.StatusCode);
    Assert.StartsWith (expectedStartString: "text/event-stream",
                       actualString: response.Content.Headers.ContentType?.MediaType,
                       comparisonType: StringComparison.Ordinal);

    string ssePayload = await response.Content.ReadAsStringAsync (TestContext.Current.CancellationToken);

    Assert.Contains (expectedSubstring: "event: server_shutdown",
                     actualString: ssePayload,
                     comparisonType: StringComparison.Ordinal);
    Assert.Contains (expectedSubstring: "data: shutdown", actualString: ssePayload, comparisonType: StringComparison.Ordinal);
  }

  [Fact]
  public async Task Notifications_Returns503_WhenShutdownInProgress ()
  {
    FakeShutdownCoordinator fakeShutdown = this._factory.Services.GetRequiredService<FakeShutdownCoordinator> ();
    fakeShutdown.Reset ();

    await fakeShutdown.RequestShutdownAsync (restart: false, cancellationToken: TestContext.Current.CancellationToken);

    try
    {
      string sessionId = await this.CreateSessionAsync ();

      HttpResponseMessage response = await this._client.GetAsync (requestUri: $"/api/chat/sessions/{sessionId}/notifications",
                                                                  cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal (expected: HttpStatusCode.ServiceUnavailable, actual: response.StatusCode);
    }
    finally
    {
      fakeShutdown.Reset ();
    }
  }

  private async Task<string> CreateSessionAsync ()
  {
    HttpResponseMessage response = await this._client.PostAsJsonAsync (requestUri: "/api/chat/sessions", value: new { });
    _ = response.EnsureSuccessStatusCode ();
    CreateSessionResponse? payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse> ();
    Assert.NotNull (payload);

    return payload.SessionId;
  }
}