namespace ArkaneSystems.Raven.Contracts.Chat;

// Request body for POST /api/chat/sessions/{sessionId}/messages (and /stream).
// Content is the raw text the user typed.
public record SendMessageRequest (string Content);