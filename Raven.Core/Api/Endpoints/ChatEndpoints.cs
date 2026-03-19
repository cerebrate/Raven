using System.Text.Json;
using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using Microsoft.AspNetCore.Http.Features;

namespace ArkaneSystems.Raven.Core.Api.Endpoints;

// Extension class that registers all chat-related HTTP endpoints onto the
// minimal API route builder. Called once from Program.cs via MapChatEndpoints().
// Grouping under "/api/chat" means that prefix is not repeated on each route.
public static class ChatEndpoints
{
  private const string CorrelationHeaderName = "X-Correlation-Id";

  public static IEndpointRouteBuilder MapChatEndpoints (this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/chat");
    _ = group.AddEndpointFilter(new CorrelationScopeEndpointFilter());

    // POST /api/chat/sessions
    // Creates a new conversation with the agent and a matching session record.
    // Returns the sessionId the client should use in all subsequent calls.
    _ = group.MapPost ("/sessions", async (
        IChatApplicationService chat,
        CancellationToken cancellationToken) =>
    {
      var sessionId = await chat.CreateSessionAsync(cancellationToken);
      return Results.Ok (new CreateSessionResponse (sessionId));
    });

    // POST /api/chat/sessions/{sessionId}/messages
    // Non-streaming variant: waits for the full agent reply before responding.
    // Returns 404 if the sessionId is not recognised.
    _ = group.MapPost ("/sessions/{sessionId}/messages", async (
        string sessionId,
        SendMessageRequest request,
        IChatApplicationService chat,
        CancellationToken cancellationToken) =>
    {
      try
      {
        var reply = await chat.SendMessageAsync(sessionId, request.Content, cancellationToken);
        return reply is null ? Results.NotFound () : Results.Ok (new SendMessageResponse (sessionId, reply));
      }
      catch (SessionStaleException)
      {
        return Results.Conflict (new ChatErrorResponse ("session_stale"));
      }
    });

    // POST /api/chat/sessions/{sessionId}/messages/stream
    // Streaming variant: sends the agent reply as a Server-Sent Events (SSE) stream.
    // Each token or small chunk is written as a "data: ..." line and flushed
    // immediately so the client sees text appearing in real time.
    // Response buffering is disabled so bytes are not held by ASP.NET Core's
    // output buffer before being sent to the client.
    _ = group.MapPost ("/sessions/{sessionId}/messages/stream", async (
        string sessionId,
        SendMessageRequest request,
        IChatStreamBroker streamBroker,
        IResponseStreamEventHub streamHub,
        HttpContext http,
        CancellationToken cancellationToken) =>
    {
      var stream = await streamBroker.StartResponseStreamAsync(
          sessionId,
          request.Content,
          cancellationToken: cancellationToken);

      if (stream is null)
      {
        http.Response.StatusCode = 404;
        return;
      }

      http.Features.Get<IHttpResponseBodyFeature> ()?.DisableBuffering ();
      http.Response.ContentType = "text/event-stream";
      http.Response.Headers.CacheControl = "no-cache";

      try
      {
        await foreach (var streamEventEnvelope in streamHub.ReadAllAsync (stream.ResponseId, cancellationToken))
        {
          await WriteSseEventAsync (http.Response, streamEventEnvelope.Event, cancellationToken);
        }

        await stream.Completion;
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // Client disconnects/timeouts are expected for long-lived SSE requests.
        // Treat request-abort cancellation as a graceful stream shutdown.
      }
    });

    // GET /api/chat/sessions/{sessionId}
    // Returns metadata about an existing session (timestamps).
    // Used by the console client's /history command.
    _ = group.MapGet ("/sessions/{sessionId}", async (
        string sessionId,
        IChatApplicationService chat,
        CancellationToken cancellationToken) =>
    {
      var info = await chat.GetSessionAsync(sessionId, cancellationToken);
      return info is null
        ? Results.NotFound ()
        : Results.Ok (new SessionInfoResponse (info.SessionId, info.CreatedAt, info.LastActivityAt));
    });

    // DELETE /api/chat/sessions/{sessionId}
    // Removes the session record. Returns 204 No Content on success, 404 if not found.
    // Used by the console client's /new command to clean up the old session before
    // creating a new one.
    _ = group.MapDelete ("/sessions/{sessionId}", async (
        string sessionId,
        IChatApplicationService chat,
        CancellationToken cancellationToken) =>
    {
      var deleted = await chat.DeleteSessionAsync(sessionId, cancellationToken);
      return deleted ? Results.NoContent () : Results.NotFound ();
    });

    return app;
  }

  private static async Task WriteSseEventAsync (
      HttpResponse response,
      IResponseStreamEvent streamEvent,
      CancellationToken cancellationToken)
  {
    var (eventName, data) = streamEvent switch
    {
      ResponseStarted started => ("started", started.ResponseId),
      ResponseDelta delta => ("delta", delta.Content),
      ResponseCompleted completed => ("completed", completed.FinalContent ?? string.Empty),
      ResponseFailed failed =>
      (
        "failed",
        JsonSerializer.Serialize(new StreamFailureEventData(failed.ErrorMessage, failed.ErrorCode, failed.IsRetryable))
      ),
      _ => ("unknown", string.Empty)
    };

    // SSE format requires each message on its own "data: " line, followed
    // by a blank line. Multi-line chunks have each line individually prefixed.
    var normalizedData = data.Replace("\n", "\ndata: ");
    await response.WriteAsync ($"event: {eventName}\ndata: {normalizedData}\n\n", cancellationToken);
    await response.Body.FlushAsync (cancellationToken);
  }

  private sealed class CorrelationScopeEndpointFilter : IEndpointFilter
  {
    public async ValueTask<object?> InvokeAsync (EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
      var http = context.HttpContext;
      var loggerFactory = http.RequestServices.GetRequiredService<ILoggerFactory>();
      var logger = loggerFactory.CreateLogger("Raven.Core.Api.Correlation");

      var incomingCorrelationId = http.Request.Headers[CorrelationHeaderName].ToString();
      var correlationId = string.IsNullOrWhiteSpace(incomingCorrelationId)
        ? Guid.NewGuid().ToString()
        : incomingCorrelationId;

      http.Response.Headers[CorrelationHeaderName] = correlationId;

      using var _ = logger.BeginScope(new Dictionary<string, object>
      {
        ["CorrelationId"] = correlationId,
        ["RequestPath"] = http.Request.Path.Value ?? string.Empty,
        ["RequestMethod"] = http.Request.Method
      });

      return await next(context);
    }
  }
}