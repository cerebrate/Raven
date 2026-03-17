using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Sessions;
using Microsoft.AspNetCore.Http.Features;

namespace ArkaneSystems.Raven.Core.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat");

        group.MapPost("/sessions", async (
            IAgentConversationService conversations,
            ISessionStore sessions) =>
        {
            var conversationId = await conversations.CreateConversationAsync();
            var sessionId = await sessions.CreateSessionAsync(conversationId);
            return Results.Ok(new CreateSessionResponse(sessionId));
        });

        group.MapPost("/sessions/{sessionId}/messages", async (
            string sessionId,
            SendMessageRequest request,
            IAgentConversationService conversations,
            ISessionStore sessions) =>
        {
            var conversationId = await sessions.GetConversationIdAsync(sessionId);
            if (conversationId is null)
                return Results.NotFound();

            var reply = await conversations.SendMessageAsync(conversationId, request.Content);
            return Results.Ok(new SendMessageResponse(sessionId, reply));
        });

        group.MapPost("/sessions/{sessionId}/messages/stream", async (
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

            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers["Cache-Control"] = "no-cache";

            await foreach (var chunk in conversations.StreamMessageAsync(conversationId, request.Content, cancellationToken))
            {
                await http.Response.WriteAsync($"data: {chunk.Replace("\n", "\ndata: ")}\n\n", cancellationToken);
                await http.Response.Body.FlushAsync(cancellationToken);
            }
        });

        return app;
    }
}
