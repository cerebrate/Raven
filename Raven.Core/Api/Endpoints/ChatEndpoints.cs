using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Sessions;
using Microsoft.AspNetCore.Http.Features;

namespace ArkaneSystems.Raven.Core.Api.Endpoints;

// Extension class that registers all chat-related HTTP endpoints onto the
// minimal API route builder. Called once from Program.cs via MapChatEndpoints().
// Grouping under "/api/chat" means that prefix is not repeated on each route.
public static class ChatEndpoints
{
  public static IEndpointRouteBuilder MapChatEndpoints (this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/chat");

    // POST /api/chat/sessions
    // Creates a new conversation with the agent and a matching session record.
    // Returns the sessionId the client should use in all subsequent calls.
    group.MapPost ("/sessions", async (
        IAgentConversationService conversations,
        ISessionStore sessions) =>
    {
      var conversationId = await conversations.CreateConversationAsync();
      var sessionId = await sessions.CreateSessionAsync(conversationId);
      return Results.Ok (new CreateSessionResponse (sessionId));
    });

    // POST /api/chat/sessions/{sessionId}/messages
    // Non-streaming variant: waits for the full agent reply before responding.
    // Returns 404 if the sessionId is not recognised.
    group.MapPost ("/sessions/{sessionId}/messages", async (
        string sessionId,
        SendMessageRequest request,
        IAgentConversationService conversations,
        ISessionStore sessions) =>
    {
      var conversationId = await sessions.GetConversationIdAsync(sessionId);
      if (conversationId is null)
        return Results.NotFound ();

      var reply = await conversations.SendMessageAsync(conversationId, request.Content);
      return Results.Ok (new SendMessageResponse (sessionId, reply));
    });

    // POST /api/chat/sessions/{sessionId}/messages/stream
    // Streaming variant: sends the agent reply as a Server-Sent Events (SSE) stream.
    // Each token or small chunk is written as a "data: ..." line and flushed
    // immediately so the client sees text appearing in real time.
    // Response buffering is disabled so bytes are not held by ASP.NET Core's
    // output buffer before being sent to the client.
    group.MapPost ("/sessions/{sessionId}/messages/stream", async (
        string sessionId,
        SendMessageRequest request,
        IAgentConversationService conversations,
        ISessionStore sessions,
        HttpContext http,
        CancellationToken cancellationToken) =>
    {
      var conversationId = await sessions.GetConversationIdAsync(sessionId);
      if (conversationId is null)
      {
        http.Response.StatusCode = 404;
        return;
      }

      http.Features.Get<IHttpResponseBodyFeature> ()?.DisableBuffering ();
      http.Response.ContentType = "text/event-stream";
      http.Response.Headers["Cache-Control"] = "no-cache";

      await foreach (var chunk in conversations.StreamMessageAsync (conversationId, request.Content, cancellationToken))
      {
        // SSE format requires each message on its own "data: " line, followed
        // by a blank line. Multi-line chunks have each line individually prefixed.
        await http.Response.WriteAsync ($"data: {chunk.Replace ("\n", "\ndata: ")}\n\n", cancellationToken);
        await http.Response.Body.FlushAsync (cancellationToken);
      }
    });

    // GET /api/chat/sessions/{sessionId}
    // Returns metadata about an existing session (timestamps).
    // Used by the console client's /history command.
    group.MapGet ("/sessions/{sessionId}", async (
        string sessionId,
        ISessionStore sessions) =>
    {
      var info = await sessions.GetSessionAsync(sessionId);
      if (info is null)
        return Results.NotFound ();

      return Results.Ok (new SessionInfoResponse (info.SessionId, info.CreatedAt, info.LastActivityAt));
    });

    // DELETE /api/chat/sessions/{sessionId}
    // Removes the session record. Returns 204 No Content on success, 404 if not found.
    // Used by the console client's /new command to clean up the old session before
    // creating a new one.
    group.MapDelete ("/sessions/{sessionId}", async (
        string sessionId,
        ISessionStore sessions) =>
    {
      var deleted = await sessions.DeleteSessionAsync(sessionId);
      return deleted ? Results.NoContent () : Results.NotFound ();
    });

    return app;
  }
}