namespace ArkaneSystems.Raven.Contracts.Chat;

// Response body returned from POST /api/chat/sessions.
// The client must hold on to SessionId and pass it in every subsequent request
// to identify which conversation it is talking to.
public record CreateSessionResponse(string SessionId);
