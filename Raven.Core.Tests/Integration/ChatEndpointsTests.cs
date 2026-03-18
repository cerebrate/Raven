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
    var response = await _client.PostAsJsonAsync("/api/chat/sessions", new { });

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
    Assert.NotNull (payload);
    Assert.False (string.IsNullOrWhiteSpace (payload.SessionId));
  }

  [Fact]
  public async Task SendMessage_ReturnsReply_ForExistingSession ()
  {
    var sessionId = await CreateSessionAsync();

    var response = await _client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("hello"));

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<SendMessageResponse>();
    Assert.NotNull (payload);
    Assert.Equal (sessionId, payload.SessionId);
    Assert.Equal ("echo:hello", payload.Content);
  }

  [Fact]
  public async Task SendMessage_ReturnsNotFound_ForMissingSession ()
  {
    var response = await _client.PostAsJsonAsync(
            $"/api/chat/sessions/{Guid.NewGuid()}/messages",
            new SendMessageRequest("hello"));

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task StreamMessage_ReturnsSsePayload_ForExistingSession ()
  {
    var sessionId = await CreateSessionAsync();

    var response = await _client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages/stream",
            new SendMessageRequest("hello"));

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);
    Assert.StartsWith ("text/event-stream", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);

    var streamPayload = await response.Content.ReadAsStringAsync();
    Assert.Contains ("data: echo:hello", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("data: line one", streamPayload, StringComparison.Ordinal);
    Assert.Contains ("data: line two", streamPayload, StringComparison.Ordinal);
  }

  [Fact]
  public async Task StreamMessage_ReturnsNotFound_ForMissingSession ()
  {
    var response = await _client.PostAsJsonAsync(
            $"/api/chat/sessions/{Guid.NewGuid()}/messages/stream",
            new SendMessageRequest("hello"));

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task GetSession_ReturnsSessionInfo_ForExistingSession ()
  {
    var sessionId = await CreateSessionAsync();

    var response = await _client.GetAsync($"/api/chat/sessions/{sessionId}");

    Assert.Equal (HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<SessionInfoResponse>();
    Assert.NotNull (payload);
    Assert.Equal (sessionId, payload.SessionId);
    Assert.NotEqual (default, payload.CreatedAt);
  }

  [Fact]
  public async Task DeleteSession_ReturnsNoContent_ForExistingSession ()
  {
    var sessionId = await CreateSessionAsync();

    var response = await _client.DeleteAsync($"/api/chat/sessions/{sessionId}");

    Assert.Equal (HttpStatusCode.NoContent, response.StatusCode);
  }

  [Fact]
  public async Task DeleteSession_ReturnsNotFound_ForMissingSession ()
  {
    var response = await _client.DeleteAsync($"/api/chat/sessions/{Guid.NewGuid()}");

    Assert.Equal (HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task SessionLifecycle_CompletesAcrossEndpoints ()
  {
    var sessionId = await CreateSessionAsync();

    var sendResponse = await _client.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest("workflow"));
    Assert.Equal (HttpStatusCode.OK, sendResponse.StatusCode);

    var getResponse = await _client.GetAsync($"/api/chat/sessions/{sessionId}");
    Assert.Equal (HttpStatusCode.OK, getResponse.StatusCode);

    var deleteResponse = await _client.DeleteAsync($"/api/chat/sessions/{sessionId}");
    Assert.Equal (HttpStatusCode.NoContent, deleteResponse.StatusCode);

    var getDeletedResponse = await _client.GetAsync($"/api/chat/sessions/{sessionId}");
    Assert.Equal (HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
  }

  private async Task<string> CreateSessionAsync ()
  {
    var response = await _client.PostAsJsonAsync("/api/chat/sessions", new { });
    response.EnsureSuccessStatusCode ();

    var payload = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
    Assert.NotNull (payload);

    return payload.SessionId;
  }
}