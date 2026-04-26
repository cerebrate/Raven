using ArkaneSystems.Raven.Client.Console.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ArkaneSystems.Raven.Client.Console.Test.Services;

public sealed class RavenApiClientTests
{
  [Fact]
  public async Task StreamMessageAsync_PreservesLeadingLineBreak_InDeltaFrame ()
  {
    const string ssePayload = "event: delta\ndata:\ndata: line one\n\n";
    var client = CreateClient(ssePayload);

    var chunks = await ReadChunksAsync(client);

    var chunk = Assert.Single(chunks);
    Assert.Equal("\nline one", chunk);
  }

  [Fact]
  public async Task StreamMessageAsync_PreservesEmptyDataLine_BetweenSegments ()
  {
    const string ssePayload = "event: delta\ndata: alpha\ndata:\ndata: omega\n\n";
    var client = CreateClient(ssePayload);

    var chunks = await ReadChunksAsync(client);

    var chunk = Assert.Single(chunks);
    Assert.Equal("alpha\n\nomega", chunk);
  }

  // ── SubscribeToNotificationsAsync ─────────────────────────────────────────

  [Fact]
  public async Task SubscribeToNotificationsAsync_ParsesServerShutdownEvent ()
  {
    const string ssePayload = "event: server_shutdown\ndata: shutdown\n\n";
    var client = CreateClient(ssePayload);

    var notifications = await ReadNotificationsAsync(client);

    var notification = Assert.Single(notifications);
    Assert.Equal("server_shutdown", notification.EventType);
    Assert.Equal("shutdown", notification.Data);
  }

  [Fact]
  public async Task SubscribeToNotificationsAsync_ParsesServerRestartEvent ()
  {
    const string ssePayload = "event: server_shutdown\ndata: restart\n\n";
    var client = CreateClient(ssePayload);

    var notifications = await ReadNotificationsAsync(client);

    var notification = Assert.Single(notifications);
    Assert.Equal("server_shutdown", notification.EventType);
    Assert.Equal("restart", notification.Data);
  }

  [Fact]
  public async Task SubscribeToNotificationsAsync_ReturnsEmpty_ForNonSuccessResponse ()
  {
    var handler = new StubSseHttpMessageHandler(string.Empty, HttpStatusCode.ServiceUnavailable);
    var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    var client = new RavenApiClient(http);

    var notifications = await ReadNotificationsAsync(client);

    Assert.Empty(notifications);
  }

  [Fact]
  public async Task SubscribeToNotificationsAsync_IgnoresUnknownEventTypes ()
  {
    const string ssePayload = "event: future_event\ndata: some-data\n\nevent: server_shutdown\ndata: shutdown\n\n";
    var client = CreateClient(ssePayload);

    var notifications = await ReadNotificationsAsync(client);

    // Both events are surfaced; the caller is responsible for ignoring unknowns.
    Assert.Equal(2, notifications.Count);
    Assert.Equal("future_event", notifications[0].EventType);
    Assert.Equal("server_shutdown", notifications[1].EventType);
  }

  [Fact]
  public async Task SubscribeToNotificationsAsync_IgnoresCommentLines ()
  {
    const string ssePayload = ": keep-alive\nevent: server_shutdown\ndata: shutdown\n\n";
    var client = CreateClient(ssePayload);

    var notifications = await ReadNotificationsAsync(client);

    var notification = Assert.Single(notifications);
    Assert.Equal("server_shutdown", notification.EventType);
  }

  private static RavenApiClient CreateClient (string payload, HttpStatusCode status = HttpStatusCode.OK)
  {
    var handler = new StubSseHttpMessageHandler(payload, status);
    var http = new HttpClient(handler)
    {
      BaseAddress = new Uri("http://localhost")
    };

    return new RavenApiClient(http);
  }

  private static async Task<List<string>> ReadChunksAsync (RavenApiClient client)
  {
    var chunks = new List<string>();

    await foreach (var chunk in client.StreamMessageAsync("session-1", "hello", TestContext.Current.CancellationToken))
    {
      chunks.Add(chunk);
    }

    return chunks;
  }

  private static async Task<List<ServerNotification>> ReadNotificationsAsync (RavenApiClient client)
  {
    var notifications = new List<ServerNotification>();

    await foreach (var n in client.SubscribeToNotificationsAsync("session-1", TestContext.Current.CancellationToken))
    {
      notifications.Add(n);
    }

    return notifications;
  }

  private sealed class StubSseHttpMessageHandler (
      string payload,
      HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (statusCode != HttpStatusCode.OK)
      {
        return Task.FromResult(new HttpResponseMessage(statusCode));
      }

      var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(payload)));
      content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

      var response = new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = content
      };

      return Task.FromResult(response);
    }
  }
}
