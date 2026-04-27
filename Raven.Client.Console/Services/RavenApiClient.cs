using System.Text.Json;
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
  // GET /api/chat/sessions — list all resumable sessions with snapshots.
  // Returns an empty list if no sessions exist or the request fails.
  public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync ()
  {
    var response = await http.GetAsync("/api/chat/sessions");
    if (!response.IsSuccessStatusCode)
      return [];

    var result = await response.Content.ReadFromJsonAsync<ListSessionsResponse> ();
    return result?.Sessions ?? [];
  }

  // GET /api/chat/sessions/{sessionId} — check a session exists before resuming.
  // Returns the session info if found, or null if not.
  // (Alias exposed for clarity in resume workflows; backed by the existing GetSessionAsync.)

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

    var eventName = "delta"; // default keeps compatibility with legacy data-only SSE frames.
    var data = new System.Text.StringBuilder();
    var hasDataLine = false;

    async IAsyncEnumerable<string> FlushFrameAsync ([EnumeratorCancellation] CancellationToken ct)
    {
      if (!hasDataLine)
      {
        yield break;
      }

      var payload = data.ToString();

      if (string.Equals(eventName, "delta", StringComparison.OrdinalIgnoreCase))
      {
        if (!string.IsNullOrEmpty(payload))
          yield return payload;
      }
      else if (string.Equals(eventName, "failed", StringComparison.OrdinalIgnoreCase))
      {
        if (TryParseFailurePayload(payload, out var failureData))
        {
          var message = string.IsNullOrWhiteSpace(failureData.Message)
            ? "Streaming failed."
            : failureData.Message;

          throw new StreamEventFailedException(message, failureData.Code, failureData.IsRetryable);
        }

        throw new StreamEventFailedException(
            string.IsNullOrWhiteSpace(payload) ? "Streaming failed." : payload,
            code: null,
            isRetryable: false);
      }
      else if (string.Equals(eventName, "server_shutdown", StringComparison.OrdinalIgnoreCase))
      {
        // The server is about to shut down or restart. Stop consuming the stream
        // and surface the event to the caller so they can display a warning.
        var isRestart = string.Equals(payload, "restart", StringComparison.OrdinalIgnoreCase);
        throw new ServerShuttingDownException(isRestart);
      }

      await Task.CompletedTask;
    }

    while (!cancellationToken.IsCancellationRequested)
    {
      var line = await reader.ReadLineAsync(cancellationToken);
      if (line is null)
        break;

      // Blank line terminates one SSE event frame.
      if (line.Length == 0)
      {
        await foreach (var chunk in FlushFrameAsync(cancellationToken))
          yield return chunk;

        eventName = "delta";
        _ = data.Clear();
        hasDataLine = false;
        continue;
      }

      if (line.StartsWith(":", StringComparison.Ordinal))
        continue;

      if (line.StartsWith("event:", StringComparison.Ordinal))
      {
        eventName = line["event:".Length..].Trim();
        continue;
      }

      if (line.StartsWith("data:", StringComparison.Ordinal))
      {
        var segment = line["data:".Length..];
        if (segment.StartsWith(" ", StringComparison.Ordinal))
        {
          segment = segment[1..];
        }

        if (hasDataLine)
          _ = data.Append('\n');

        _ = data.Append(segment);
        hasDataLine = true;
      }
    }

    // Handle a final frame if the stream ends without a trailing blank line.
    await foreach (var chunk in FlushFrameAsync(cancellationToken))
      yield return chunk;
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

  // POST /api/admin/shutdown — request a graceful server shutdown.
  // The server notifies all active sessions and stops after a short grace period.
  public async Task RequestShutdownAsync ()
  {
    var response = await http.PostAsync ("/api/admin/shutdown", content: null);
    _ = response.EnsureSuccessStatusCode ();
  }

  // POST /api/admin/restart — request a graceful server restart.
  // The server notifies all active sessions, stops, and the container runner
  // is expected to restart the process based on the exit code.
  public async Task RequestRestartAsync ()
  {
    var response = await http.PostAsync ("/api/admin/restart", content: null);
    _ = response.EnsureSuccessStatusCode ();
  }

  // GET /api/chat/sessions/{sessionId}/notifications — long-lived SSE endpoint.
  // Yields ServerNotification values as the server pushes them. The connection
  // is kept open until the CancellationToken fires or the server closes it.
  // Callers should treat connection errors as graceful end-of-stream (the server
  // may have restarted) rather than propagating them as exceptions.
  public async IAsyncEnumerable<ServerNotification> SubscribeToNotificationsAsync (
      string sessionId,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    using var response = await http.GetAsync(
        $"/api/chat/sessions/{sessionId}/notifications",
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    // Non-2xx (e.g. 404/409/503) — yield nothing; caller decides how to handle.
    if (!response.IsSuccessStatusCode)
      yield break;

    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var reader = new StreamReader(stream);

    var eventName = string.Empty;
    var data = new System.Text.StringBuilder();
    var hasDataLine = false;

    async IAsyncEnumerable<ServerNotification> FlushFrameAsync (
        [EnumeratorCancellation] CancellationToken ct)
    {
      if (!hasDataLine || string.IsNullOrEmpty(eventName))
        yield break;

      yield return new ServerNotification(eventName, data.ToString());
      await Task.CompletedTask;
    }

    while (!cancellationToken.IsCancellationRequested)
    {
      var line = await reader.ReadLineAsync(cancellationToken);
      if (line is null)
        break;

      if (line.Length == 0)
      {
        await foreach (var n in FlushFrameAsync(cancellationToken))
          yield return n;

        eventName = string.Empty;
        _ = data.Clear();
        hasDataLine = false;
        continue;
      }

      if (line.StartsWith(":", StringComparison.Ordinal))
        continue;

      if (line.StartsWith("event:", StringComparison.Ordinal))
      {
        eventName = line["event:".Length..].Trim();
        continue;
      }

      if (line.StartsWith("data:", StringComparison.Ordinal))
      {
        var segment = line["data:".Length..];
        if (segment.StartsWith(" ", StringComparison.Ordinal))
          segment = segment[1..];

        if (hasDataLine)
          _ = data.Append('\n');

        _ = data.Append(segment);
        hasDataLine = true;
      }
    }

    // Handle a final frame if the stream ends without a trailing blank line.
    await foreach (var n in FlushFrameAsync(cancellationToken))
      yield return n;
  }

  private static bool TryParseFailurePayload (string payload, out StreamFailureEventData failureData)
  {
    failureData = new StreamFailureEventData("", null, false);

    if (string.IsNullOrWhiteSpace(payload))
    {
      return false;
    }

    try
    {
      var parsed = JsonSerializer.Deserialize<StreamFailureEventData>(payload);
      if (parsed is null)
      {
        return false;
      }

      failureData = parsed;
      return true;
    }
    catch (JsonException)
    {
      return false;
    }
  }
}