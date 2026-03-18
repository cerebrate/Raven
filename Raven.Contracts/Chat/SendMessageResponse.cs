namespace ArkaneSystems.Raven.Contracts.Chat;

// Response body returned from POST /api/chat/sessions/{sessionId}/messages
// (the non-streaming variant). Content is the complete agent reply.
public record SendMessageResponse(string SessionId, string Content);
