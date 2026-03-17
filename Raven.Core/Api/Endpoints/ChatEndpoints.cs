using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Sessions;

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

        return app;
    }
}
