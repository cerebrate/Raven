namespace ArkaneSystems.Raven.Contracts.Chat;

// Response body returned from GET /api/chat/sessions/{sessionId}.
// Carries the metadata Raven.Core holds about a session — when it was
// created and when it last saw activity. LastActivityAt is nullable
// because a brand-new session will not have processed any messages yet.
// Title is populated after the first message and gives the session a
// human-readable label.
public record SessionInfoResponse (
    string SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt,
    string? Title = null);