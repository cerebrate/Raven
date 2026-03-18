using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost;
using System.Net;
using System.Net.Http.Json;

namespace ArkaneSystems.Raven.Core.Tests.Integration;

public sealed class ChatEndpointsTests (RavenCoreWebAppFactory factory) : IClassFixture<RavenCoreWebAppFactory>
{
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

  private async Task<string> CreateSessionAsync ()
  {
    var response = await this._client.PostAsJsonAsync("/api/chat/sessions", new { });
    _ = response.EnsureSuccessStatusCode ();

    var payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
    Assert.NotNull (payload);

    return payload.SessionId;
  }
}