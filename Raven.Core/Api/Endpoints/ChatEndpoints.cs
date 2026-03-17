using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;

namespace ArkaneSystems.Raven.Core.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat");

        group.MapPost("/sessions", async (ISessionStore sessions) =>
        {
            var sessionId = await sessions.CreateSessionAsync();
            return Results.Ok(new CreateSessionResponse(sessionId));
        });

        group.MapPost("/sessions/{sessionId}/messages", async (
            string sessionId,
            SendMessageRequest request,
            ISessionStore sessions) =>
        {
            if (!await sessions.SessionExistsAsync(sessionId))
                return Results.NotFound();

            return Results.Ok(new SendMessageResponse(sessionId, $"Echo: {request.Content}"));
        });

        return app;
    }
}
