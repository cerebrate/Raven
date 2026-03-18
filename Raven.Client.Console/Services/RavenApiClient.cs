using ArkaneSystems.Raven.Contracts.Chat;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace ArkaneSystems.Raven.Client.Console.Services;

// Thin HTTP wrapper around the Raven.Core chat API.
// Each method corresponds to one endpoint. Error handling (non-2xx responses)
// is done by EnsureSuccessStatusCode(), which throws HttpRequestException —
// caught and displayed by ConsoleLoop via IConsoleRenderer.ShowError.
public class RavenApiClient (HttpClient http)
{
  // POST /api/chat/sessions — creates a new session and returns its ID.
  public async Task<string> CreateSessionAsync ()
  {
    var response = await http.PostAsJsonAsync("/api/chat/sessions", new CreateSessionRequest());
    _ = response.EnsureSuccessStatusCode ();
    var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
    return result!.SessionId;
  }

  // POST /api/chat/sessions/{sessionId}/messages — non-streaming send.
  // Waits for the complete reply. Kept for potential non-streaming use cases.
  public async Task<string> SendMessageAsync (string sessionId, string content)
  {
    var response = await http.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendMessageRequest(content));
    _ = response.EnsureSuccessStatusCode ();
    var result = await response.Content.ReadFromJsonAsync<SendMessageResponse>();
    return result!.Content;
  }

  // POST /api/chat/sessions/{sessionId}/messages/stream — SSE streaming send.
  // Uses HttpCompletionOption.ResponseHeadersRead so the body is not buffered:
  // the connection stays open and we read lines incrementally as they arrive.
  // Each yielded string is one text chunk from the agent's response.
  public async IAsyncEnumerable<string> StreamMessageAsync (
      string sessionId,
      string content,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/chat/sessions/{sessionId}/messages/stream")
    {
      Content = JsonContent.Create(new SendMessageRequest(content))
    };

    // ResponseHeadersRead: return as soon as headers are received, leaving
    // the body stream open for incremental reading.
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    _ = response.EnsureSuccessStatusCode ();

    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var reader = new StreamReader(stream);

    while (!cancellationToken.IsCancellationRequested)
    {
      var line = await reader.ReadLineAsync(cancellationToken);

      if (line is null)
        break;

      // SSE lines look like: "data: <text>"
      // Skip blank lines, comment lines, and any other non-data frames.
      if (!line.StartsWith ("data: ", StringComparison.Ordinal))
        continue;

      // Strip the "data: " prefix and yield the payload to the caller.
      var chunk = line["data: ".Length..];
      if (!string.IsNullOrEmpty (chunk))
        yield return chunk;
    }
  }

  // GET /api/chat/sessions/{sessionId} — fetch session metadata.
  // Returns null rather than throwing if the session is not found (404),
  // so the caller can decide how to handle a missing session gracefully.
  public async Task<SessionInfoResponse?> GetSessionAsync (string sessionId)
  {
    var response = await http.GetAsync($"/api/chat/sessions/{sessionId}");
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
      return null;

    _ = response.EnsureSuccessStatusCode ();
    return await response.Content.ReadFromJsonAsync<SessionInfoResponse> ();
  }

  // DELETE /api/chat/sessions/{sessionId} — remove the session record on the server.
  // Called by /new before creating a replacement session.
  public async Task DeleteSessionAsync (string sessionId)
  {
    var response = await http.DeleteAsync($"/api/chat/sessions/{sessionId}");
    _ = response.EnsureSuccessStatusCode ();
  }
}