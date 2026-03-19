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

  private static RavenApiClient CreateClient (string payload)
  {
    var handler = new StubSseHttpMessageHandler(payload);
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

  private sealed class StubSseHttpMessageHandler (string payload) : HttpMessageHandler
  {
    private readonly string _payload = payload;

    protected override Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(_payload)));
      content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

      var response = new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = content
      };

      return Task.FromResult(response);
    }
  }
}
