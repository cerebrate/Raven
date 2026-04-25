using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace ArkaneSystems.Raven.Core.Tests.Integration;

public sealed class ChatEndpointsTests (RavenCoreWebAppFactory factory) : IClassFixture<RavenCoreWebAppFactory>
{
  private readonly RavenCoreWebAppFactory _factory = factory;
  private readonly HttpClient _client = factory.CreateClient();

  [Fact]
  public async Task CreateSession_ReturnsSessionId ()
  {
    var response = await this._client.PostAsJsonAsync("/api/chat/sessions", new { }, TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull (payload);
    Assert.False (string.IsNullOrWhiteSpace (payload.SessionId));
  }

  [Fact]
  public async Task SendMessage_ReturnsReply_ForExistingSession ()
  {
    var sessionId = await this.CreateSessionAsync();

    var response = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("hello"),
            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<SendMessageResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull (payload);
    Assert.Equal (sessionId, payload.SessionId);
    Assert.Equal ("echo:hello", payload.Content);
  }

  [Fact]
  public async Task SendMessage_ReturnsNotFound_ForMissingSession ()
  {
    var response = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{Guid.NewGuid()}/messages",
            new SendMessageRequest("hello"),
            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task StreamMessage_ReturnsSsePayload_ForExistingSession ()
  {
    var sessionId = await this.CreateSessionAsync();

    var response = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages/stream",
            new SendMessageRequest("hello"),
            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);
    Assert.StartsWith ("text/event-stream", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);

    var streamPayload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    Assert.Contains ("data: echo:hello", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("data: line one", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("data: line two", streamPayload, StringComparison.Ordinal);
  }

  [Fact]
  public async Task StreamMessage_ReturnsNotFound_ForMissingSession ()
  {
    var response = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{Guid.NewGuid()}/messages/stream",
            new SendMessageRequest("hello"),
            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task GetSession_ReturnsSessionInfo_ForExistingSession ()
  {
    var sessionId = await this.CreateSessionAsync();

    var response = await this._client.GetAsync($"/api/chat/sessions/{sessionId}", TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<SessionInfoResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull (payload);
    Assert.Equal (sessionId, payload.SessionId);
    Assert.NotEqual (default, payload.CreatedAt);
  }

  [Fact]
  public async Task DeleteSession_ReturnsNoContent_ForExistingSession ()
  {
    var sessionId = await this.CreateSessionAsync();

    var response = await this._client.DeleteAsync($"/api/chat/sessions/{sessionId}",
      TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.NoContent, response.StatusCode);
  }

  [Fact]
  public async Task DeleteSession_ReturnsNotFound_ForMissingSession ()
  {
    var response = await this._client.DeleteAsync($"/api/chat/sessions/{Guid.NewGuid()}",
      TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task SessionLifecycle_CompletesAcrossEndpoints ()
  {
    var sessionId = await this.CreateSessionAsync();

    var sendResponse = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("workflow"),
            TestContext.Current.CancellationToken);
    Assert.Equal (HttpStatusCode.OK, sendResponse.StatusCode);

    var getResponse = await this._client.GetAsync($"/api/chat/sessions/{sessionId}",
      TestContext.Current.CancellationToken);
    Assert.Equal (HttpStatusCode.OK, getResponse.StatusCode);

    var deleteResponse = await this._client.DeleteAsync($"/api/chat/sessions/{sessionId}",
      TestContext.Current.CancellationToken);
    Assert.Equal (HttpStatusCode.NoContent, deleteResponse.StatusCode);

    var getDeletedResponse = await this._client.GetAsync($"/api/chat/sessions/{sessionId}",
      TestContext.Current.CancellationToken);
    Assert.Equal (HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
  }

  [Fact]
  public async Task SendMessage_ReturnsConflict_AndInvalidatesSession_ForStaleSession ()
  {
    var sessionId = await this.CreateSessionAsync();
    this.ClearAgentConversations();

    var sendResponse = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("hello"),
            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.Conflict, sendResponse.StatusCode);

    var error = await sendResponse.Content.ReadFromJsonAsync<ChatErrorResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull (error);
    Assert.Equal ("session_stale", error.Code);

    var getResponse = await this._client.GetAsync($"/api/chat/sessions/{sessionId}", TestContext.Current.CancellationToken);
    Assert.Equal (HttpStatusCode.NotFound, getResponse.StatusCode);
  }

  [Fact]
  public async Task StreamMessage_ReturnsFailedEventPayload_WithSessionStaleCode_ForStaleSession ()
  {
    var sessionId = await this.CreateSessionAsync();
    this.ClearAgentConversations();

    var response = await this._client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages/stream",
            new SendMessageRequest("hello"),
            TestContext.Current.CancellationToken);

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var streamPayload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    Assert.Contains ("event: failed", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("\"Code\":\"session_stale\"", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("\"Message\":", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("\"IsRetryable\":false", streamPayload, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CreateSession_EchoesProvidedCorrelationId_Header ()
  {
    const string correlationId = "test-correlation-id";

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/sessions")
    {
      Content = JsonContent.Create(new { })
    };

    _ = request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

    var response = await this._client.SendAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
    Assert.Equal(correlationId, Assert.Single(values));
  }

  [Fact]
  public async Task StreamMessage_ConcurrentRequestsRemainSessionIsolated ()
  {
    var prompts = Enumerable.Range(1, 6).Select(i => $"concurrent-{i}").ToArray();
    var sessions = new List<string>();

    foreach (var _ in prompts)
    {
      sessions.Add(await this.CreateSessionAsync());
    }

    var tasks = sessions
        .Zip(prompts, static (sessionId, prompt) => (sessionId, prompt))
        .Select(async x =>
        {
          var payload = await this.StreamSessionMessageAsync(x.sessionId, x.prompt, CancellationToken.None);
          return (x.prompt, payload);
        })
        .ToArray();

    var results = await Task.WhenAll(tasks);

    foreach (var result in results)
    {
      Assert.Contains($"data: echo:{result.prompt}", result.payload, StringComparison.Ordinal);

      foreach (var otherPrompt in prompts.Where(p => !string.Equals(p, result.prompt, StringComparison.Ordinal)))
      {
        Assert.DoesNotContain($"data: echo:{otherPrompt}", result.payload, StringComparison.Ordinal);
      }
    }
  }

  [Fact]
  public async Task StreamMessage_MixedCancellationAndCompletion_BehavesAsExpected ()
  {
    this.ConfigureFakeStreamChunkDelay(TimeSpan.FromMilliseconds(250));

    try
    {
      var prompts = Enumerable.Range(1, 6).Select(i => $"cancel-mix-{i}").ToArray();
      var sessions = new List<string>();

      foreach (var _ in prompts)
      {
        sessions.Add(await this.CreateSessionAsync());
      }

      var tasks = sessions
          .Zip(prompts, static (sessionId, prompt) => (sessionId, prompt))
          .Select((x, index) => this.RunStreamWithOptionalCancellationAsync(
              x.sessionId,
              x.prompt,
              cancelAfter: index % 2 == 0 ? TimeSpan.FromMilliseconds(80) : null))
          .ToArray();

      var outcomes = await Task.WhenAll(tasks);

      Assert.Contains(outcomes, static o => o == StreamExecutionOutcome.Canceled);
      Assert.Contains(outcomes, static o => o == StreamExecutionOutcome.Completed);
    }
    finally
    {
      this.ResetFakeStreamChunkDelay();
    }
  }

  [Fact]
  public async Task StreamMessage_SseEventOrder_IsStartedThenDeltaThenCompleted ()
  {
    var sessionId = await this.CreateSessionAsync();

    var payload = await this.StreamSessionMessageAsync(sessionId, "ordering-check", TestContext.Current.CancellationToken);

    var startedIndex = payload.IndexOf("event: started", StringComparison.Ordinal);
    var deltaIndex = payload.IndexOf("event: delta", StringComparison.Ordinal);
    var completedIndex = payload.IndexOf("event: completed", StringComparison.Ordinal);

    Assert.True(startedIndex >= 0, "Expected started event in stream payload.");
    Assert.True(deltaIndex > startedIndex, "Expected delta event to appear after started event.");
    Assert.True(completedIndex > deltaIndex, "Expected completed event to appear after delta event.");
  }

  [Fact]
  public async Task SessionLifecycle_WritesAppendOnlyEventLogEntries ()
  {
    var sessionId = await this.CreateSessionAsync();

    var sendResponse = await this._client.PostAsJsonAsync(
        $"/api/chat/sessions/{sessionId}/messages",
        new SendMessageRequest("event-log"),
        TestContext.Current.CancellationToken);
    Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

    var deleteResponse = await this._client.DeleteAsync($"/api/chat/sessions/{sessionId}", TestContext.Current.CancellationToken);
    Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

    var logPath = Path.Combine(this._factory.WorkspaceRoot, "sessions", "logs", $"{sessionId}.events.ndjson");
    Assert.True(File.Exists(logPath));

    var lines = await File.ReadAllLinesAsync(logPath, TestContext.Current.CancellationToken);
    Assert.True(lines.Length >= 3);
    Assert.Contains(lines, static line => line.Contains("\"eventType\":\"session.created.v1\"", StringComparison.Ordinal));
    Assert.Contains(lines, static line => line.Contains("\"eventType\":\"chat.message.sent.v1\"", StringComparison.Ordinal));
    Assert.Contains(lines, static line => line.Contains("\"eventType\":\"session.deleted.v1\"", StringComparison.Ordinal));
  }

  private void ClearAgentConversations ()
  {
    var fake = this._factory.Services.GetRequiredService<IAgentConversationService>() as FakeAgentConversationService;
    Assert.NotNull (fake);
    fake.ClearConversations ();
  }

  private void ConfigureFakeStreamChunkDelay (TimeSpan delay)
  {
    var fake = this._factory.Services.GetRequiredService<IAgentConversationService>() as FakeAgentConversationService;
    Assert.NotNull(fake);
    fake.SetStreamChunkDelay(delay);
  }

  private void ResetFakeStreamChunkDelay ()
  {
    var fake = this._factory.Services.GetRequiredService<IAgentConversationService>() as FakeAgentConversationService;
    Assert.NotNull(fake);
    fake.ResetStreamChunkDelay();
  }

  private async Task<string> StreamSessionMessageAsync (string sessionId, string prompt, CancellationToken cancellationToken)
  {
    var response = await this._client.PostAsJsonAsync(
        $"/api/chat/sessions/{sessionId}/messages/stream",
        new SendMessageRequest(prompt),
        cancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return await response.Content.ReadAsStringAsync(cancellationToken);
  }

  private async Task<StreamExecutionOutcome> RunStreamWithOptionalCancellationAsync (
      string sessionId,
      string prompt,
      TimeSpan? cancelAfter)
  {
    using var cts = cancelAfter.HasValue
      ? new CancellationTokenSource(cancelAfter.Value)
      : new CancellationTokenSource(TimeSpan.FromSeconds(5));

    try
    {
      _ = await this.StreamSessionMessageAsync(sessionId, prompt, cts.Token);
      return StreamExecutionOutcome.Completed;
    }
    catch (OperationCanceledException)
    {
      return StreamExecutionOutcome.Canceled;
    }
  }

  private enum StreamExecutionOutcome
  {
    Completed,
    Canceled
  }

  private async Task<string> CreateSessionAsync ()
  {
    var response = await this._client.PostAsJsonAsync("/api/chat/sessions", new { });
    _ = response.EnsureSuccessStatusCode ();

    var payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
    Assert.NotNull (payload);

    return payload.SessionId;
  }
}