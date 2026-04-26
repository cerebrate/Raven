using System.Text.Json;
using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.Application.Admin;
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
  private const string CorrelationItemKey = "Raven.CorrelationId";

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
        HttpContext http,
        CancellationToken cancellationToken) =>
    {
      var requestContext = BuildRequestContext(http);

      try
      {
        var reply = await chat.SendMessageAsync(
            sessionId,
            request.Content,
            requestContext,
            cancellationToken);

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
    // Returns 503 Service Unavailable if a shutdown or restart is in progress.
    _ = group.MapPost ("/sessions/{sessionId}/messages/stream", async (
        string sessionId,
        SendMessageRequest request,
        IChatStreamBroker streamBroker,
        IResponseStreamEventHub streamHub,
        IShutdownCoordinator shutdownCoordinator,
        HttpContext http,
        CancellationToken cancellationToken) =>
    {
      // Reject new streaming requests once shutdown/restart has been initiated.
      // Active streams have already been notified via ServerShuttingDown events;
      // this guards against new requests racing in during the grace period.
      if (shutdownCoordinator.IsShutdownRequested)
      {
        http.Response.StatusCode = 503;
        return;
      }
      var requestContext = BuildRequestContext(http);

      var stream = await streamBroker.StartResponseStreamAsync(
          sessionId,
          request.Content,
          requestContext,
          cancellationToken);

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

    // GET /api/chat/sessions/{sessionId}/notifications
    // Long-lived SSE endpoint for server-initiated push notifications.
    // The client subscribes once per session and keeps the connection open
    // indefinitely. The server pushes typed events through this channel at any
    // time — not only in response to a chat request. Current event types:
    //
    //   server_shutdown  data: "restart" | "shutdown"
    //     Emitted when an admin issues /shutdown or /restart. Idle clients
    //     (those not currently streaming a chat response) receive this event
    //     here; mid-stream clients receive it on their response stream instead.
    //
    // Future event types (examples):
    //   memory_updated, heartbeat, background_task_result, …
    //
    // Returns 404 if the sessionId is not recognised.
    // Returns 409 if a notification subscription already exists for this session
    //   (the client should close the old connection before opening a new one).
    // Returns 503 during an in-progress shutdown so new subscribers are not
    //   added after the broadcast has already fired.
    _ = group.MapGet ("/sessions/{sessionId}/notifications", async (
        string sessionId,
        IChatApplicationService chat,
        ISessionNotificationHub notificationHub,
        IShutdownCoordinator shutdownCoordinator,
        HttpContext http,
        CancellationToken cancellationToken) =>
    {
      if (shutdownCoordinator.IsShutdownRequested)
      {
        http.Response.StatusCode = 503;
        return;
      }

      var session = await chat.GetSessionAsync(sessionId, cancellationToken);
      if (session is null)
      {
        http.Response.StatusCode = 404;
        return;
      }

      if (!notificationHub.TrySubscribe(sessionId))
      {
        // A notification channel is already open for this session.
        // Return 409 so the client knows it must close the old connection first.
        http.Response.StatusCode = 409;
        return;
      }

      http.Features.Get<IHttpResponseBodyFeature> ()?.DisableBuffering ();
      http.Response.ContentType = "text/event-stream";
      http.Response.Headers.CacheControl = "no-cache";

      // Send an initial SSE comment to flush the response headers immediately.
      // This lets the client distinguish "subscribed and waiting" from "waiting
      // for headers", and makes the connection reliably detectable by tests and
      // health-check tooling. SSE comment lines (starting with ':') are ignored
      // by compliant parsers.
      await http.Response.WriteAsync(": connected\n\n", cancellationToken);
      await http.Response.Body.FlushAsync(cancellationToken);

      try
      {
        await foreach (var envelope in notificationHub.ReadAllAsync(sessionId, cancellationToken))
        {
          await WriteNotificationSseEventAsync(http.Response, envelope.Notification, cancellationToken);
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // Client disconnected — expected for long-lived SSE connections.
      }
    });

    return app;
  }

  private static async Task WriteNotificationSseEventAsync (
      HttpResponse response,
      IServerNotification notification,
      CancellationToken cancellationToken)
  {
    var (eventName, data) = notification switch
    {
      // Shutdown/restart broadcast to idle clients (clients currently streaming
      // a chat response receive this via WriteSseEventAsync instead).
      ServerShutdownNotification shutdown => ("server_shutdown", shutdown.IsRestart ? "restart" : "shutdown"),
      _ => ("unknown", string.Empty)
    };

    var normalizedData = data.Replace("\n", "\ndata: ");
    await response.WriteAsync($"event: {eventName}\ndata: {normalizedData}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
  }

  private static string ResolveCorrelationId (HttpContext http)
  {
    if (http.Items.TryGetValue(CorrelationItemKey, out var value) && value is string correlationId && !string.IsNullOrWhiteSpace(correlationId))
    {
      return correlationId;
    }

    var headerValue = http.Request.Headers[CorrelationHeaderName].ToString();
    return string.IsNullOrWhiteSpace(headerValue) ? Guid.NewGuid().ToString() : headerValue;
  }

  private static ChatRequestContext BuildRequestContext (HttpContext http) =>
      new(ResolveCorrelationId(http), UserId: null);

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
      // Broadcast when the server is preparing to shut down or restart.
      // The data payload indicates whether a restart will follow so the client
      // can display an appropriate message.
      ServerShuttingDown shutdown => ("server_shutdown", shutdown.IsRestart ? "restart" : "shutdown"),
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
      http.Items[CorrelationItemKey] = correlationId;

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